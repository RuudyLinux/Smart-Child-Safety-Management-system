using System.Security.Cryptography;
using System.Text.Json;
using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Services;

public class EnrollmentService : IEnrollmentService
{
    public const string DuplicateAdmissionNumber = "A student with this admission number already exists.";
    public const string GuardianListedTwice = "The same guardian is listed more than once.";
    public const string GuardianNotFound = "The selected guardian no longer exists. Search again and select an account.";
    public const string InvalidContactNumber = "A valid contact number is required for every new guardian.";
    public const string ConcurrentConflict = "Some of these details (an admission number, guardian email or contact number, or the RFID card) were registered by someone else at the same moment. Review and try again.";
    public const string KeyReused = "This enrollment form was already submitted and completed with different details. Reload the page to start a new enrollment.";
    public const string ConfirmationNeeded = "Please confirm before the student is enrolled.";

    public static string EmailRegistered(string email) =>
        $"A guardian with the email {email} already exists. Choose 'Link existing guardian' and select that account instead.";
    public static string ContactRegistered(string contact) =>
        $"A guardian with the contact number {contact} already exists. Choose 'Link existing guardian' and select that account instead.";

    private readonly IEnrollmentRepository _repo;
    private readonly IStudentRepository _students;
    private readonly IGuardianRepository _guardians;
    private readonly IRfidRepository _rfid;
    private readonly IRfidService _rfidService;
    private readonly ITransportationService _transport;
    private readonly ITransportationRepository _transportRepo;
    private readonly IPasswordHasherService _hasher;
    private readonly IEmailSender _email;
    private readonly SchoolClock _clock;
    private readonly IAuditTrail _audit;

    public EnrollmentService(
        IEnrollmentRepository repo,
        IStudentRepository students,
        IGuardianRepository guardians,
        IRfidRepository rfid,
        IRfidService rfidService,
        ITransportationService transport,
        ITransportationRepository transportRepo,
        IPasswordHasherService hasher,
        IEmailSender email,
        SchoolClock clock,
        IAuditTrail? audit = null)
    {
        _audit = audit ?? NullAuditTrail.Instance;
        _repo = repo;
        _students = students;
        _guardians = guardians;
        _rfid = rfid;
        _rfidService = rfidService;
        _transport = transport;
        _transportRepo = transportRepo;
        _hasher = hasher;
        _email = email;
        _clock = clock;
    }

    private sealed record GuardianPlan(
        Guardian? Existing, string? FullName, string? Email, string? ContactNumber, PostalAddress? Address,
        string? Relationship, bool IsAuthorizedForPickup);

    private sealed class Plan
    {
        public List<GuardianPlan> Guardians { get; } = new();
        public string? DuplicateOverrideReason { get; set; }
        public List<int> ConfirmedInactiveGuardianIds { get; } = new();
    }

    public async Task<EnrollmentStudentCheckDto> CheckStudentAsync(EnrollmentStudentCheckRequest request, CancellationToken ct = default)
    {
        var admission = Blank(request.AdmissionNumber);
        var taken = admission is not null && await _repo.AdmissionNumberExistsAsync(admission, ct);
        var matches = await _repo.FindSameNameAndBirthDateAsync(request.FullName.Trim(), request.DateOfBirth, ct);
        return new EnrollmentStudentCheckDto(taken, matches.Select(ToDuplicateDto).ToList());
    }

    public async Task<EnrollmentOutcome> EnrollAsync(EnrollStudentRequest request, int? adminId, CancellationToken ct = default)
    {
        var key = request.IdempotencyKey.Trim();
        var hash = Hash(request);
        var replay = await ReplayAsync(key, hash, ct);
        if (replay is not null) return replay;

        var s = request.Student;
        var name = s.FullName.Trim();
        var admission = Blank(s.AdmissionNumber);
        var cardUid = Blank(request.CardUid);
        var plan = new Plan();
        var (error, confirmation) = await PrecheckAsync(request, name, admission, cardUid, plan, ct);
        if (error is not null || confirmation is not null)
        {
            // A resubmission racing the first request can see the first one's rows here before its key.
            return await ReplayAsync(key, hash, ct)
                   ?? (error is not null ? EnrollmentOutcome.Failed(error) : EnrollmentOutcome.NeedsConfirmation(confirmation!));
        }

        // Initial passwords exist only in memory until the emails below are sent; only hashes are stored.
        var credentials = new List<(Guardian Guardian, string Password)>();
        var studentId = 0;
        var (result, conflict) = await _repo.RunInTransactionAsync(async tx =>
        {
            var now = _clock.UtcNow;
            var student = new Student
            {
                // A calendar date, not an instant (see ChildGuardDbContext.MarkInstantsAsUtc).
                FullName = name, DateOfBirth = DateTime.SpecifyKind(s.DateOfBirth.Date, DateTimeKind.Unspecified), Grade = Blank(s.Grade),
                Section = Blank(s.Section), AdmissionNumber = admission, Gender = s.Gender, Address = s.Address?.ToEntity(),
                CreatedAt = now, UpdatedAt = now
            };
            await _students.AddAsync(student, tx);
            await _students.SaveChangesAsync(tx);
            studentId = student.StudentId;

            // Saved straight away: a concurrent request with the same key waits on this row and then fails as a duplicate.
            await _repo.AddAsync(new EnrollmentRequest
            {
                IdempotencyKey = key, RequestHash = hash, StudentId = student.StudentId, AdminId = adminId, CreatedAt = now,
                DuplicateOverrideReason = plan.DuplicateOverrideReason,
                ConfirmedInactiveGuardianIds = plan.ConfirmedInactiveGuardianIds.Count == 0 ? null : string.Join(",", plan.ConfirmedInactiveGuardianIds)
            }, tx);
            await _students.SaveChangesAsync(tx);

            var linked = new List<(Guardian Guardian, GuardianPlan Plan)>();
            foreach (var g in plan.Guardians)
            {
                var guardian = g.Existing;
                if (guardian is null)
                {
                    var password = GuardianCredentialDefaults.NewInitialPassword();
                    guardian = new Guardian
                    {
                        FullName = g.FullName!, Email = g.Email!, Phone = g.ContactNumber,
                        PasswordHash = _hasher.Hash(password), PasswordResetRequired = true,
                        Status = RecordStatus.Active, Address = g.Address, CreatedAt = now, UpdatedAt = now
                    };
                    await _guardians.AddAsync(guardian, tx);
                    credentials.Add((guardian, password));
                }
                await _students.AddStudentGuardianAsync(new StudentGuardian
                {
                    Student = student, Guardian = guardian, Relationship = g.Relationship,
                    IsAuthorizedForPickup = g.IsAuthorizedForPickup, CreatedAt = now
                }, tx);
                linked.Add((guardian, g));
            }

            RfidCard? card = null;
            var cardWasNew = false;
            if (cardUid is not null)
            {
                card = await _rfid.GetByCardUidAsync(cardUid, tx);
                // A registered spare is locked and re-read, so two enrollments (or an admin) cannot both take it.
                if (card is not null) await _rfid.LockCardsAsync(new[] { card.RfidCardId }, tx);
                if (card is null)
                {
                    card = new RfidCard { CardUid = cardUid, Status = RfidCardStatus.Active, CreatedAt = now };
                    cardWasNew = true;
                    await _rfid.AddAsync(card, tx);
                }
                else if (card.StudentId is not null || card.Status != RfidCardStatus.Active)
                {
                    return Result.Failure((await _rfidService.CheckCardAsync(cardUid, tx)).Message);
                }
                card.Student = student;
                card.AssignedAt = now;
                await _rfid.AddAssignmentHistoryAsync(new RfidCardAssignment
                {
                    RfidCard = card, Student = student, AssignedAt = now, RecordedAt = now, AssignedByAdminId = adminId
                }, tx);
            }
            await _students.SaveChangesAsync(tx);

            if (request.Transport is { } t)
            {
                // The existing assignment rules (bus in service, capacity, route, one active assignment) run inside this transaction.
                var assigned = await _transport.AddAssignmentAsync(
                    new CreateStudentBusAssignmentRequest(student.StudentId, t.BusId, t.RouteId, t.PickupStopId, t.DropStopId), tx, adminId);
                if (!assigned.Succeeded) return Result.Failure($"Transport could not be assigned: {assigned.Error}");
            }

            // Ids exist now; the trail is written in the same transaction, so it commits or rolls back with the enrollment.
            RecordEnrollment(student, linked, card, cardWasNew, request.Transport, plan);
            await _students.SaveChangesAsync(tx);
            return Result.Success();
        }, ct);

        if (!result.Succeeded)
        {
            credentials.Clear();
            if (conflict) return await ReplayAsync(key, hash, ct) ?? EnrollmentOutcome.Failed(ConcurrentConflict);
            return EnrollmentOutcome.Failed(result.Error!);
        }

        // Committed. Emails go to each new guardian's own address. A failed delivery does not undo the
        // enrollment; it is reported so the admin can follow up. Not cancelled with the request: the
        // accounts already exist and must get their credentials.
        var delivery = new Dictionary<int, Result>();
        foreach (var (guardian, password) in credentials)
        {
            var message = GuardianEmails.BuildCredentialEmail(guardian.FullName, guardian.Email, guardian.Phone!, password);
            Result sent;
            try { sent = await _email.SendAsync(message, CancellationToken.None); }
            catch (Exception ex) { sent = Result.Failure($"Email delivery failed: {ex.GetType().Name}."); }
            delivery[guardian.GuardianId] = sent;
            if (!sent.Succeeded)
                await _audit.RecordNowAsync(new AuditEvent("CredentialEmailFailed", "Guardian", guardian.GuardianId,
                    $"Sign-in details could not be emailed to {guardian.FullName}")
                { GuardianId = guardian.GuardianId, StudentId = studentId, Details = new { error = sent.Error } }, CancellationToken.None);
        }
        credentials.Clear();

        return EnrollmentOutcome.Done(await DescribeAsync(studentId, false, delivery, ct));
    }

    private void RecordEnrollment(Student student, List<(Guardian Guardian, GuardianPlan Plan)> linked, RfidCard? card, bool cardWasNew,
        EnrollmentTransportDto? transport, Plan plan)
    {
        var masked = card is null ? null : RfidMasking.Mask(card.CardUid);
        _audit.Record(new AuditEvent("StudentEnrolled", "Student", student.StudentId, $"{student.FullName} enrolled")
        {
            StudentId = student.StudentId,
            Reason = plan.DuplicateOverrideReason,
            Details = new
            {
                student.FullName, dateOfBirth = DateOnly.FromDateTime(student.DateOfBirth), student.Grade, student.Section, student.AdmissionNumber,
                guardians = linked.Select(l => new
                {
                    l.Guardian.GuardianId, created = l.Plan.Existing is null, l.Plan.Relationship, l.Plan.IsAuthorizedForPickup
                }),
                card = masked, rfidPending = card is null,
                busId = transport?.BusId, routeId = transport?.RouteId,
                duplicateOverride = plan.DuplicateOverrideReason is not null,
                confirmedInactiveGuardianIds = plan.ConfirmedInactiveGuardianIds
            }
        });

        foreach (var (guardian, g) in linked)
        {
            if (g.Existing is null)
                _audit.Record(new AuditEvent("GuardianCreated", "Guardian", guardian.GuardianId,
                    $"Guardian {guardian.FullName} created during enrollment of {student.FullName}")
                { GuardianId = guardian.GuardianId, StudentId = student.StudentId, Details = new { guardian.FullName, guardian.Email, contactNumber = guardian.Phone } });
            _audit.Record(new AuditEvent("GuardianLinked", "Student", student.StudentId, $"{guardian.FullName} linked to {student.FullName}")
            {
                StudentId = student.StudentId, GuardianId = guardian.GuardianId,
                Details = new { relationship = g.Relationship, isAuthorizedForPickup = g.IsAuthorizedForPickup, deactivatedAccountConfirmed = guardian.Status != RecordStatus.Active }
            });
        }

        if (card is not null)
            _audit.Record(new AuditEvent("RfidCardAssigned", "RfidCard", card.RfidCardId, $"Card {masked} assigned to {student.FullName}")
            { StudentId = student.StudentId, Details = new { card = masked, newCard = cardWasNew } });
    }

    /// <summary>The completed enrollment for this key, or a refusal when the key was used for different content; null if unused.</summary>
    private async Task<EnrollmentOutcome?> ReplayAsync(string key, string hash, CancellationToken ct)
    {
        var prior = await _repo.GetByKeyAsync(key, ct);
        if (prior is null) return null;
        if (!string.Equals(prior.RequestHash, hash, StringComparison.OrdinalIgnoreCase))
            return EnrollmentOutcome.Failed(KeyReused) with { KeyReused = true };
        return EnrollmentOutcome.Done(await DescribeAsync(prior.StudentId, true, null, ct));
    }

    /// <summary>
    /// Checked before the transaction so the admin gets a precise message; the database's unique keys back every
    /// hard rule. Fills <paramref name="plan"/>. Returns a hard error, or the confirmations the admin still has to give.
    /// </summary>
    private async Task<(string? Error, EnrollmentConfirmationDto? Confirmation)> PrecheckAsync(
        EnrollStudentRequest request, string name, string? admission, string? cardUid, Plan plan, CancellationToken ct)
    {
        if (admission is not null && await _repo.AdmissionNumberExistsAsync(admission, ct)) return (DuplicateAdmissionNumber, null);

        var inactive = new List<InactiveGuardianDto>();
        var seenIds = new HashSet<int>();
        var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenContacts = new HashSet<string>();
        foreach (var g in request.Guardians)
        {
            var relationship = Blank(g.Relationship);
            if (g.ExistingGuardianId is int id)
            {
                if (!seenIds.Add(id)) return (GuardianListedTwice, null);
                var existing = await _guardians.GetByIdAsync(id, ct);
                if (existing is null) return (GuardianNotFound, null);
                if (existing.Status != RecordStatus.Active)
                {
                    if (g.ConfirmInactiveAccount) plan.ConfirmedInactiveGuardianIds.Add(id);
                    else inactive.Add(new InactiveGuardianDto(existing.GuardianId, existing.FullName, existing.Email, existing.Status));
                }
                plan.Guardians.Add(new GuardianPlan(existing, null, null, null, null, relationship, g.IsAuthorizedForPickup));
                continue;
            }

            var email = g.Email!.Trim();
            var contact = ContactNumber.Normalize(g.ContactNumber);
            if (contact is null) return (InvalidContactNumber, null);
            if (!seenEmails.Add(email) || !seenContacts.Add(contact)) return (GuardianListedTwice, null);
            if (await _guardians.EmailExistsAsync(email, ct)) return (EmailRegistered(email), null);
            if (await _guardians.ContactNumberExistsAsync(contact, ct)) return (ContactRegistered(contact), null);
            var address = g.SameAsStudentAddress ? request.Student.Address?.ToEntity() : g.Address?.ToEntity();
            plan.Guardians.Add(new GuardianPlan(null, g.FullName!.Trim(), email, contact, address, relationship, g.IsAuthorizedForPickup));
        }

        if (cardUid is not null)
        {
            var check = await _rfidService.CheckCardAsync(cardUid, ct);
            if (!check.CanAssign) return (check.Message, null);
        }

        // Same name and date of birth is a warning, not a rule: twins and namesakes exist. The admin must give a reason.
        var duplicates = await _repo.FindSameNameAndBirthDateAsync(name, request.Student.DateOfBirth, ct);
        var reason = Blank(request.DuplicateStudentOverrideReason);
        var unconfirmedDuplicates = duplicates.Count > 0 && reason is null ? duplicates : new List<Student>();
        if (duplicates.Count > 0) plan.DuplicateOverrideReason = reason;

        if (unconfirmedDuplicates.Count > 0 || inactive.Count > 0)
            return (null, new EnrollmentConfirmationDto(ConfirmationNeeded, unconfirmedDuplicates.Select(ToDuplicateDto).ToList(), inactive));
        return (null, null);
    }

    private async Task<EnrollmentResultDto> DescribeAsync(int studentId, bool alreadyEnrolled, IReadOnlyDictionary<int, Result>? delivery, CancellationToken ct)
    {
        var student = (await _students.GetWithGuardiansAsync(studentId, ct))!;
        var guardians = student.StudentGuardians.Select(sg =>
        {
            Result? sent = null;
            var created = delivery is not null && delivery.TryGetValue(sg.GuardianId, out sent);
            return new EnrolledGuardianDto(sg.GuardianId, sg.Guardian.FullName, sg.Relationship, sg.IsAuthorizedForPickup,
                created, created ? sent!.Succeeded : null, created && !sent!.Succeeded ? sent.Error : null);
        }).ToList();

        string? busNumber = null, routeName = null;
        var assignment = await _transportRepo.GetActiveAssignmentForStudentAsync(studentId, ct);
        if (assignment is not null)
        {
            busNumber = (await _transportRepo.GetBusByIdAsync(assignment.BusId, ct))?.BusNumber;
            if (assignment.RouteId is int routeId) routeName = (await _transportRepo.GetRouteByIdAsync(routeId, ct))?.RouteName;
        }

        return new EnrollmentResultDto(
            student.StudentId, student.FullName, alreadyEnrolled, guardians,
            student.RfidCard is null ? null : RfidMasking.Mask(student.RfidCard.CardUid),
            student.RfidCard is null, busNumber, routeName);
    }

    /// <summary>SHA-256 of the request without its idempotency key, so a retry matches and a different form does not.</summary>
    internal static string Hash(EnrollStudentRequest request)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(request with { IdempotencyKey = "" });
        return Convert.ToHexString(SHA256.HashData(json)).ToLowerInvariant();
    }

    private static PossibleDuplicateStudentDto ToDuplicateDto(Student s) =>
        new(s.StudentId, s.FullName, s.DateOfBirth, s.Grade, s.Section, s.AdmissionNumber, s.Status);

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
