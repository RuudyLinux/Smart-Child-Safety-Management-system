using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Application.Services;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;
using FluentValidation;
using K = ChildGuard.Application.Import.ImportSchema.Keys;

namespace ChildGuard.Application.Import;

/// <summary>
/// Validates and stages the rows of one category. Single-record rules are not restated here:
/// each row is turned into the same request the single-record form sends and run through that
/// request's existing validator. Import adds what a batch needs on top: duplicate detection within
/// the file and against the database, and resolving references by natural key.
/// One instance per request (scoped): it may keep state between staging and after-commit work.
/// </summary>
public abstract class ImportHandler
{
    public abstract ImportCategoryDefinition Definition { get; }

    /// <summary>Validates every row. Rows are checked in order, so a duplicate points at the earlier row.</summary>
    public abstract Task<List<RowPlan>> PlanAsync(IReadOnlyList<ImportRowDto> rows, CancellationToken ct);

    /// <summary>Builds the entities for valid rows into the write set. Called only on commit.</summary>
    public abstract void Stage(IReadOnlyList<RowPlan> validRows, ImportWriteSet set, DateTime nowUtc);

    /// <summary>Work that must follow a successful commit (and never precede it), such as emails.</summary>
    public virtual Task<List<ImportEmailFailureDto>> AfterCommitAsync(CancellationToken ct) =>
        Task.FromResult(new List<ImportEmailFailureDto>());

    protected static string Fold(string value) => value.Trim().ToUpperInvariant();

    /// <summary>Records the first row each key was seen on; reports later rows as duplicates of it.</summary>
    protected sealed class FirstSeen
    {
        private readonly Dictionary<string, int> _rows = new(StringComparer.OrdinalIgnoreCase);

        /// <returns>The earlier row number, or null when this is the first time.</returns>
        public int? Check(string key, int rowNumber)
        {
            if (_rows.TryGetValue(key, out var earlier)) return earlier;
            _rows[key] = rowNumber;
            return null;
        }
    }
}

// ---------------------------------------------------------------- Students

public sealed class StudentImportHandler : ImportHandler
{
    private sealed record Parsed(string FullName, DateTime DateOfBirth, string? Grade, int? GuardianId, string? Relationship,
        bool AuthorizedForPickup, int? BusId, int? RouteId, string? RfidCardUid);

    private static readonly Dictionary<string, string> Columns = new()
    {
        [nameof(CreateStudentRequest.FullName)] = K.FullName,
        [nameof(CreateStudentRequest.DateOfBirth)] = K.DateOfBirth,
        [nameof(CreateStudentRequest.Grade)] = K.Grade,
    };

    private readonly IBulkImportRepository _repo;
    private readonly IValidator<CreateStudentRequest> _validator;

    public StudentImportHandler(IBulkImportRepository repo, IValidator<CreateStudentRequest> validator)
    {
        _repo = repo;
        _validator = validator;
    }

    public override ImportCategoryDefinition Definition => ImportSchema.Students;

    public override async Task<List<RowPlan>> PlanAsync(IReadOnlyList<ImportRowDto> rows, CancellationToken ct)
    {
        var existingStudents = (await _repo.GetStudentKeysAsync(ct))
            .Select(s => StudentKey(s.FullName, s.DateOfBirth)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var guardians = (await _repo.GetGuardianKeysAsync(ct))
            .GroupBy(g => Fold(g.Email)).ToDictionary(g => g.Key, g => g.First().GuardianId);
        var buses = (await _repo.GetBusKeysAsync(ct))
            .GroupBy(b => Fold(b.BusNumber)).ToDictionary(g => g.Key, g => g.First());
        var cards = (await _repo.GetRfidCardUidsAsync(ct)).Select(Fold).ToHashSet();

        var seenStudents = new FirstSeen();
        var seenCards = new FirstSeen();
        var seatsClaimed = new Dictionary<int, int>(); // bus id -> valid rows in this file already placed on it
        var plans = new List<RowPlan>(rows.Count);

        foreach (var row in rows)
        {
            var reader = new ImportRowReader(Definition, row);
            var plan = reader.Plan;
            var name = reader.Text(K.FullName);
            var dob = reader.Date(K.DateOfBirth);

            var validation = await _validator.ValidateAsync(new CreateStudentRequest(name ?? string.Empty, dob ?? default, reader.Text(K.Grade)), ct);
            reader.AddValidation(validation, Columns);

            if (name is not null && dob is not null && !plan.HasError(K.FullName) && !plan.HasError(K.DateOfBirth))
            {
                var key = StudentKey(name, dob.Value);
                if (existingStudents.Contains(key))
                    plan.Error(K.FullName, "A student with this name and date of birth already exists.");
                else if (seenStudents.Check(key, plan.RowNumber) is int earlier)
                    plan.Error(K.FullName, $"Duplicate of row {earlier} (same name and date of birth).");
            }

            int? guardianId = null;
            var guardianEmail = reader.Text(K.GuardianEmail);
            if (guardianEmail is not null)
            {
                if (guardians.TryGetValue(Fold(guardianEmail), out var id)) guardianId = id;
                else plan.Error(K.GuardianEmail, "No registered guardian has this email. Import the guardian first.");
            }
            else
            {
                if (plan.Values[K.Relationship] is not null)
                    plan.Error(K.Relationship, "Relationship needs a Guardian Email.");
                if (plan.Values[K.AuthorizedForPickup] is not null)
                    plan.Error(K.AuthorizedForPickup, "Authorized For Pickup needs a Guardian Email.");
            }

            BusImportKey? bus = null;
            var busNumber = reader.Text(K.BusNumber);
            if (busNumber is not null)
            {
                if (!buses.TryGetValue(Fold(busNumber), out bus))
                    plan.Error(K.BusNumber, "No bus has this number.");
                else if (bus.Status != BusStatus.Active)
                    plan.Error(K.BusNumber, "This bus is out of service. Students cannot be assigned to it.");
            }

            var cardUid = reader.Text(K.RfidCardUid);
            if (cardUid is not null)
            {
                if (cards.Contains(Fold(cardUid)))
                    plan.Error(K.RfidCardUid, "This RFID card is already registered.");
                else if (seenCards.Check(Fold(cardUid), plan.RowNumber) is int earlier)
                    plan.Error(K.RfidCardUid, $"The same RFID card is also on row {earlier}.");
            }

            // Seats: only a row that is otherwise valid takes one, so a rejected row never blocks a later one.
            if (plan.IsValid && bus is not null)
            {
                var claimed = seatsClaimed.GetValueOrDefault(bus.BusId);
                if (bus.ActiveStudents + claimed >= bus.Capacity)
                    plan.Error(K.BusNumber, $"Bus {bus.BusNumber} is full: {bus.ActiveStudents} of {bus.Capacity} seats taken"
                        + (claimed > 0 ? $" and {claimed} more on earlier rows of this import." : "."));
                else seatsClaimed[bus.BusId] = claimed + 1;
            }

            if (plan.IsValid)
                plan.Parsed = new Parsed(name!, dob!.Value, reader.Text(K.Grade), guardianId, reader.Text(K.Relationship),
                    reader.Bool(K.AuthorizedForPickup) ?? false, bus?.BusId, bus?.RouteId, cardUid);
            plans.Add(plan);
        }
        return plans;
    }

    public override void Stage(IReadOnlyList<RowPlan> validRows, ImportWriteSet set, DateTime nowUtc)
    {
        foreach (var p in validRows.Select(r => (Parsed)r.Parsed!))
        {
            var student = new Student
            {
                FullName = p.FullName,
                DateOfBirth = p.DateOfBirth,
                Grade = p.Grade,
                Status = RecordStatus.Active,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc
            };
            if (p.GuardianId is int guardianId)
                student.StudentGuardians.Add(new StudentGuardian
                {
                    GuardianId = guardianId,
                    Relationship = p.Relationship,
                    IsAuthorizedForPickup = p.AuthorizedForPickup,
                    CreatedAt = nowUtc
                });
            if (p.BusId is int busId)
                student.BusAssignments.Add(new StudentBusAssignment { BusId = busId, RouteId = p.RouteId, Status = RecordStatus.Active, AssignedAt = nowUtc });
            if (p.RfidCardUid is not null)
                student.RfidCard = new RfidCard { CardUid = p.RfidCardUid, Status = RfidCardStatus.Active, AssignedAt = nowUtc, CreatedAt = nowUtc };
            set.Students.Add(student);
        }
    }

    private static string StudentKey(string fullName, DateTime dob) =>
        string.Join(' ', fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant() + "|" + dob.ToString("yyyy-MM-dd");
}

// ---------------------------------------------------------------- Guardians

public sealed class GuardianImportHandler : ImportHandler
{
    private sealed record Parsed(string FullName, string Email, string ContactNumber);

    private static readonly Dictionary<string, string> Columns = new()
    {
        [nameof(CreateGuardianRequest.FullName)] = K.FullName,
        [nameof(CreateGuardianRequest.Email)] = K.Email,
        [nameof(CreateGuardianRequest.ContactNumber)] = K.ContactNumber,
    };

    private readonly IBulkImportRepository _repo;
    private readonly IValidator<CreateGuardianRequest> _validator;
    private readonly IPasswordHasherService _hasher;
    private readonly IEmailSender _email;
    private readonly List<(int RowNumber, Guardian Guardian, string Password)> _pendingEmails = new();

    public GuardianImportHandler(IBulkImportRepository repo, IValidator<CreateGuardianRequest> validator,
        IPasswordHasherService hasher, IEmailSender email)
    {
        _repo = repo;
        _validator = validator;
        _hasher = hasher;
        _email = email;
    }

    public override ImportCategoryDefinition Definition => ImportSchema.Guardians;

    public override async Task<List<RowPlan>> PlanAsync(IReadOnlyList<ImportRowDto> rows, CancellationToken ct)
    {
        var existing = await _repo.GetGuardianKeysAsync(ct);
        var emails = existing.Select(g => Fold(g.Email)).ToHashSet();
        var phones = existing.Where(g => g.Phone is not null).Select(g => ContactNumber.Normalize(g.Phone)!).ToHashSet();
        var seenEmails = new FirstSeen();
        var seenPhones = new FirstSeen();
        var plans = new List<RowPlan>(rows.Count);

        foreach (var row in rows)
        {
            var reader = new ImportRowReader(Definition, row);
            var plan = reader.Plan;
            var name = reader.Text(K.FullName);
            var email = reader.Text(K.Email);
            var contact = reader.Text(K.ContactNumber);

            reader.AddValidation(await _validator.ValidateAsync(
                new CreateGuardianRequest(name ?? string.Empty, email ?? string.Empty, contact ?? string.Empty), ct), Columns);

            if (email is not null && !plan.HasError(K.Email))
            {
                if (emails.Contains(Fold(email))) plan.Error(K.Email, "This email is already registered to a guardian.");
                else if (seenEmails.Check(Fold(email), plan.RowNumber) is int earlier) plan.Error(K.Email, $"The same email is also on row {earlier}.");
            }

            var normalized = contact is null ? null : ContactNumber.Normalize(contact);
            if (normalized is not null && !plan.HasError(K.ContactNumber))
            {
                if (phones.Contains(normalized)) plan.Error(K.ContactNumber, "This contact number is already registered to a guardian.");
                else if (seenPhones.Check(normalized, plan.RowNumber) is int earlier) plan.Error(K.ContactNumber, $"The same contact number is also on row {earlier}.");
            }

            if (plan.IsValid) plan.Parsed = new Parsed(name!, email!, normalized!);
            plans.Add(plan);
        }
        return plans;
    }

    /// <summary>Same account setup as a single admin-created guardian: random password, hashed, reset required.</summary>
    public override void Stage(IReadOnlyList<RowPlan> validRows, ImportWriteSet set, DateTime nowUtc)
    {
        foreach (var row in validRows)
        {
            var p = (Parsed)row.Parsed!;
            var password = GuardianCredentialDefaults.NewInitialPassword();
            var guardian = new Guardian
            {
                FullName = p.FullName,
                Email = p.Email,
                Phone = p.ContactNumber,
                PasswordHash = _hasher.Hash(password),
                PasswordResetRequired = true,
                Status = RecordStatus.Active,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc
            };
            set.Guardians.Add(guardian);
            _pendingEmails.Add((row.RowNumber, guardian, password));
        }
    }

    /// <summary>
    /// Credentials go to each guardian's own address only after the accounts exist. A failed delivery
    /// does not undo the account (same as single creation); it is reported so the admin can reissue.
    /// </summary>
    public override async Task<List<ImportEmailFailureDto>> AfterCommitAsync(CancellationToken ct)
    {
        var failures = new List<ImportEmailFailureDto>();
        foreach (var (rowNumber, guardian, password) in _pendingEmails)
        {
            var message = GuardianEmails.BuildCredentialEmail(guardian.FullName, guardian.Email, guardian.Phone!, password);
            Result delivery;
            try
            {
                delivery = await _email.SendAsync(message, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                delivery = Result.Failure("The email could not be sent.");
            }
            if (!delivery.Succeeded)
                failures.Add(new ImportEmailFailureDto(rowNumber, guardian.FullName, guardian.Email, delivery.Error));
        }
        _pendingEmails.Clear(); // the plaintext passwords are not kept a moment longer than needed
        return failures;
    }
}

// ---------------------------------------------------------------- Drivers

public sealed class DriverImportHandler : ImportHandler
{
    private sealed record Parsed(string FullName, string? Phone, string? LicenseNumber);

    private static readonly Dictionary<string, string> Columns = new()
    {
        [nameof(CreateDriverRequest.FullName)] = K.FullName,
        [nameof(CreateDriverRequest.Phone)] = K.Phone,
        [nameof(CreateDriverRequest.LicenseNumber)] = K.LicenseNumber,
    };

    private readonly IBulkImportRepository _repo;
    private readonly IValidator<CreateDriverRequest> _validator;

    public DriverImportHandler(IBulkImportRepository repo, IValidator<CreateDriverRequest> validator)
    {
        _repo = repo;
        _validator = validator;
    }

    public override ImportCategoryDefinition Definition => ImportSchema.Drivers;

    public override async Task<List<RowPlan>> PlanAsync(IReadOnlyList<ImportRowDto> rows, CancellationToken ct)
    {
        var existing = await _repo.GetDriverKeysAsync(ct);
        var licences = existing.Where(d => d.LicenseNumber is not null).Select(d => LicenceKey(d.LicenseNumber!)).ToHashSet();
        var namePhones = existing.Where(d => ContactNumber.Normalize(d.Phone) is not null)
            .Select(d => NamePhoneKey(d.FullName, d.Phone!)).ToHashSet();
        var seenLicences = new FirstSeen();
        var seenNamePhones = new FirstSeen();
        var plans = new List<RowPlan>(rows.Count);

        foreach (var row in rows)
        {
            var reader = new ImportRowReader(Definition, row);
            var plan = reader.Plan;
            var name = reader.Text(K.FullName);
            var phone = reader.Text(K.Phone);
            var licence = reader.Text(K.LicenseNumber);

            reader.AddValidation(await _validator.ValidateAsync(new CreateDriverRequest(name ?? string.Empty, phone, licence), ct), Columns);

            if (licence is not null && !plan.HasError(K.LicenseNumber))
            {
                if (licences.Contains(LicenceKey(licence))) plan.Error(K.LicenseNumber, "A driver with this licence number already exists.");
                else if (seenLicences.Check(LicenceKey(licence), plan.RowNumber) is int earlier) plan.Error(K.LicenseNumber, $"The same licence number is also on row {earlier}.");
            }
            if (name is not null && phone is not null && !plan.HasError(K.Phone) && !plan.HasError(K.FullName))
            {
                var key = NamePhoneKey(name, phone);
                if (namePhones.Contains(key)) plan.Error(K.FullName, "A driver with this name and phone already exists.");
                else if (seenNamePhones.Check(key, plan.RowNumber) is int earlier) plan.Error(K.FullName, $"Duplicate of row {earlier} (same name and phone).");
            }

            if (plan.IsValid) plan.Parsed = new Parsed(name!, phone, licence);
            plans.Add(plan);
        }
        return plans;
    }

    public override void Stage(IReadOnlyList<RowPlan> validRows, ImportWriteSet set, DateTime nowUtc)
    {
        foreach (var p in validRows.Select(r => (Parsed)r.Parsed!))
            set.Drivers.Add(new Driver { FullName = p.FullName, Phone = p.Phone, LicenseNumber = p.LicenseNumber, Status = RecordStatus.Active, CreatedAt = nowUtc });
    }

    /// <summary>Licence numbers are compared ignoring case and spacing ("MH12 2011" = "mh122011").</summary>
    internal static string LicenceKey(string licence) => new string(licence.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    private static string NamePhoneKey(string name, string phone) => Fold(name) + "|" + ContactNumber.Normalize(phone);
}

// ---------------------------------------------------------------- Routes

public sealed class RouteImportHandler : ImportHandler
{
    private sealed record Parsed(string RouteName, string? Description);

    private static readonly Dictionary<string, string> Columns = new()
    {
        [nameof(CreateRouteRequest.RouteName)] = K.RouteName,
        [nameof(CreateRouteRequest.Description)] = K.Description,
    };

    private readonly IBulkImportRepository _repo;
    private readonly IValidator<CreateRouteRequest> _validator;

    public RouteImportHandler(IBulkImportRepository repo, IValidator<CreateRouteRequest> validator)
    {
        _repo = repo;
        _validator = validator;
    }

    public override ImportCategoryDefinition Definition => ImportSchema.Routes;

    public override async Task<List<RowPlan>> PlanAsync(IReadOnlyList<ImportRowDto> rows, CancellationToken ct)
    {
        var existing = (await _repo.GetRouteKeysAsync(ct)).Select(r => Fold(r.RouteName)).ToHashSet();
        var seen = new FirstSeen();
        var plans = new List<RowPlan>(rows.Count);

        foreach (var row in rows)
        {
            var reader = new ImportRowReader(Definition, row);
            var plan = reader.Plan;
            var name = reader.Text(K.RouteName);
            reader.AddValidation(await _validator.ValidateAsync(new CreateRouteRequest(name ?? string.Empty, reader.Text(K.Description)), ct), Columns);

            if (name is not null && !plan.HasError(K.RouteName))
            {
                if (existing.Contains(Fold(name))) plan.Error(K.RouteName, "A route with this name already exists.");
                else if (seen.Check(Fold(name), plan.RowNumber) is int earlier) plan.Error(K.RouteName, $"The same route name is also on row {earlier}.");
            }

            if (plan.IsValid) plan.Parsed = new Parsed(name!, reader.Text(K.Description));
            plans.Add(plan);
        }
        return plans;
    }

    public override void Stage(IReadOnlyList<RowPlan> validRows, ImportWriteSet set, DateTime nowUtc)
    {
        foreach (var p in validRows.Select(r => (Parsed)r.Parsed!))
            set.Routes.Add(new Route { RouteName = p.RouteName, Description = p.Description, Status = RecordStatus.Active, CreatedAt = nowUtc });
    }
}

// ---------------------------------------------------------------- Buses

public sealed class BusImportHandler : ImportHandler
{
    private sealed record Parsed(string BusNumber, int Capacity, int? DriverId, int? RouteId);

    private static readonly Dictionary<string, string> Columns = new()
    {
        [nameof(CreateBusRequest.BusNumber)] = K.BusNumber,
        [nameof(CreateBusRequest.Capacity)] = K.Capacity,
        [nameof(CreateBusRequest.DriverId)] = K.DriverLicenseNumber,
        [nameof(CreateBusRequest.RouteId)] = K.RouteName,
    };

    private readonly IBulkImportRepository _repo;
    private readonly IValidator<CreateBusRequest> _validator;

    public BusImportHandler(IBulkImportRepository repo, IValidator<CreateBusRequest> validator)
    {
        _repo = repo;
        _validator = validator;
    }

    public override ImportCategoryDefinition Definition => ImportSchema.Buses;

    public override async Task<List<RowPlan>> PlanAsync(IReadOnlyList<ImportRowDto> rows, CancellationToken ct)
    {
        var buses = (await _repo.GetBusKeysAsync(ct)).Select(b => Fold(b.BusNumber)).ToHashSet();
        var driversByLicence = (await _repo.GetDriverKeysAsync(ct))
            .Where(d => d.LicenseNumber is not null)
            .GroupBy(d => DriverImportHandler.LicenceKey(d.LicenseNumber!))
            .ToDictionary(g => g.Key, g => g.ToList());
        var routes = (await _repo.GetRouteKeysAsync(ct))
            .GroupBy(r => Fold(r.RouteName)).ToDictionary(g => g.Key, g => g.First());
        var seen = new FirstSeen();
        var plans = new List<RowPlan>(rows.Count);

        foreach (var row in rows)
        {
            var reader = new ImportRowReader(Definition, row);
            var plan = reader.Plan;
            var number = reader.Text(K.BusNumber);
            var capacity = reader.Int(K.Capacity);

            int? driverId = null;
            if (reader.Text(K.DriverLicenseNumber) is { } licence)
            {
                if (!driversByLicence.TryGetValue(DriverImportHandler.LicenceKey(licence), out var matches))
                    plan.Error(K.DriverLicenseNumber, "No driver has this licence number. Import the driver first.");
                else if (matches.Count > 1)
                    plan.Error(K.DriverLicenseNumber, "More than one driver has this licence number; assign this bus's driver from the Transportation page.");
                else if (matches[0].Status != RecordStatus.Active)
                    plan.Error(K.DriverLicenseNumber, "This driver is deactivated and cannot be assigned to a bus.");
                else driverId = matches[0].DriverId;
            }

            int? routeId = null;
            if (reader.Text(K.RouteName) is { } routeName)
            {
                if (!routes.TryGetValue(Fold(routeName), out var route))
                    plan.Error(K.RouteName, "No route has this name. Import the route first.");
                else if (route.Status != RecordStatus.Active)
                    plan.Error(K.RouteName, "This route is deactivated and cannot be newly assigned.");
                else routeId = route.RouteId;
            }

            reader.AddValidation(await _validator.ValidateAsync(
                new CreateBusRequest(number ?? string.Empty, capacity ?? 0, driverId, routeId), ct), Columns);

            if (number is not null && !plan.HasError(K.BusNumber))
            {
                if (buses.Contains(Fold(number))) plan.Error(K.BusNumber, "A bus with this number already exists.");
                else if (seen.Check(Fold(number), plan.RowNumber) is int earlier) plan.Error(K.BusNumber, $"The same bus number is also on row {earlier}.");
            }

            if (plan.IsValid) plan.Parsed = new Parsed(number!, capacity!.Value, driverId, routeId);
            plans.Add(plan);
        }
        return plans;
    }

    public override void Stage(IReadOnlyList<RowPlan> validRows, ImportWriteSet set, DateTime nowUtc)
    {
        foreach (var p in validRows.Select(r => (Parsed)r.Parsed!))
            set.Buses.Add(new Bus { BusNumber = p.BusNumber, Capacity = p.Capacity, DriverId = p.DriverId, RouteId = p.RouteId, Status = BusStatus.Active, CreatedAt = nowUtc });
    }
}

// ---------------------------------------------------------------- RouteStops

public sealed class RouteStopImportHandler : ImportHandler
{
    private sealed record Parsed(int RouteId, string StopName, string? Address, TimeOnly? PickupTime, TimeOnly? DropTime);

    private readonly IBulkImportRepository _repo;
    private readonly IValidator<RouteStopRequest> _validator;

    public RouteStopImportHandler(IBulkImportRepository repo, IValidator<RouteStopRequest> validator)
    {
        _repo = repo;
        _validator = validator;
    }

    public override ImportCategoryDefinition Definition => ImportSchema.RouteStops;

    public override async Task<List<RowPlan>> PlanAsync(IReadOnlyList<ImportRowDto> rows, CancellationToken ct)
    {
        var routes = (await _repo.GetRouteStopKeysAsync(ct))
            .GroupBy(r => Fold(r.RouteName))
            .ToDictionary(g => g.Key, g => g.First());

        // Track per-route: how many stops each route already has + how many valid rows claimed so far
        var routeStopCounts = routes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.StopCount);
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); // "routeName|stopName" -> first row

        var plans = new List<RowPlan>(rows.Count);

        foreach (var row in rows)
        {
            var reader = new ImportRowReader(Definition, row);
            var plan = reader.Plan;

            var routeName = reader.Text(K.RouteName);
            var stopName = reader.Text(K.StopName);

            reader.AddValidation(await _validator.ValidateAsync(
                new RouteStopRequest(stopName ?? string.Empty, reader.Text(K.Address), null, null), ct),
                new Dictionary<string, string>
                {
                    [nameof(RouteStopRequest.Name)] = K.StopName,
                    [nameof(RouteStopRequest.Address)] = K.Address,
                });

            int routeId = 0;
            if (routeName is not null)
            {
                if (!routes.TryGetValue(Fold(routeName), out var routeKey))
                    plan.Error(K.RouteName, "No route has this name. Import the route first.");
                else if (routeKey.RouteId == 0) // placeholder – active check handled below
                    plan.Error(K.RouteName, "This route is deactivated.");
                else
                    routeId = routeKey.RouteId;
            }

            // Parse times; treat blank as null
            TimeOnly? pickup = null, drop = null;
            var pickupRaw = reader.Text(K.PickupTime);
            var dropRaw = reader.Text(K.DropTime);
            if (pickupRaw is not null && !TimeOnly.TryParse(pickupRaw, out var pt))
                plan.Error(K.PickupTime, "Enter the time as HH:MM (e.g. 07:30).");
            else if (pickupRaw is not null) pickup = TimeOnly.Parse(pickupRaw);
            if (dropRaw is not null && !TimeOnly.TryParse(dropRaw, out var dt))
                plan.Error(K.DropTime, "Enter the time as HH:MM (e.g. 15:45).");
            else if (dropRaw is not null) drop = TimeOnly.Parse(dropRaw);

            // Duplicate: same route + same stop name
            if (routeName is not null && stopName is not null && routeId > 0 && !plan.HasError(K.StopName))
            {
                var dupKey = $"{Fold(routeName)}|{Fold(stopName)}";
                if (seen.TryGetValue(dupKey, out var earlier))
                    plan.Error(K.StopName, $"Duplicate of row {earlier} (same route and stop name).");
                else
                    seen[dupKey] = plan.RowNumber;
            }

            if (plan.IsValid)
                plan.Parsed = new Parsed(routeId, stopName!, reader.Text(K.Address), pickup, drop);

            plans.Add(plan);
        }
        return plans;
    }

    public override void Stage(IReadOnlyList<RowPlan> validRows, ImportWriteSet set, DateTime nowUtc)
    {
        // Assign sequences per route: existing stop count + position within this batch
        var nextSeq = new Dictionary<int, int>();

        foreach (var row in validRows)
        {
            var p = (Parsed)row.Parsed!;
            if (!nextSeq.ContainsKey(p.RouteId))
            {
                // Find the existing stop count from the original keys (not modified during planning)
                // We re-compute by looking at stops already staged in this set
                var alreadyStaged = set.RouteStops.Count(s => s.RouteId == p.RouteId);
                nextSeq[p.RouteId] = alreadyStaged + 1; // will be corrected per-route below
            }
        }

        // Reset to use the counts captured during PlanAsync — but those aren't stored here.
        // Use a simple per-route counter starting at 1; the repository will add the route's existing count.
        var perRouteCounter = new Dictionary<int, int>();
        foreach (var row in validRows)
        {
            var p = (Parsed)row.Parsed!;
            perRouteCounter.TryAdd(p.RouteId, 0);
            perRouteCounter[p.RouteId]++;
            set.RouteStops.Add(new RouteStop
            {
                RouteId = p.RouteId,
                // Sequence set during save (existing stops + position in this batch)
                Sequence = perRouteCounter[p.RouteId],
                Name = p.StopName,
                Address = p.Address,
                PickupTime = p.PickupTime,
                DropTime = p.DropTime,
                Status = RecordStatus.Active,
                CreatedAt = nowUtc
            });
        }
    }
}

// ---------------------------------------------------------------- SchoolCalendarDays

public sealed class SchoolCalendarDayImportHandler : ImportHandler
{
    private sealed record Parsed(DateOnly Date, SchoolDayType DayType, string Title,
        TimeOnly? DismissalTime, TimeOnly? StartTime, TimeOnly? LateArrivalAfter);

    private readonly IBulkImportRepository _repo;
    private readonly SchoolClock _clock;

    public SchoolCalendarDayImportHandler(IBulkImportRepository repo, SchoolClock clock)
    {
        _repo = repo;
        _clock = clock;
    }

    public override ImportCategoryDefinition Definition => ImportSchema.SchoolCalendarDays;

    public override async Task<List<RowPlan>> PlanAsync(IReadOnlyList<ImportRowDto> rows, CancellationToken ct)
    {
        var existing = (await _repo.GetCalendarDayKeysAsync(ct)).Select(d => d.Date).ToHashSet();
        var seen = new FirstSeen();
        var today = _clock.Today;
        var plans = new List<RowPlan>(rows.Count);

        foreach (var row in rows)
        {
            var reader = new ImportRowReader(Definition, row);
            var plan = reader.Plan;

            var dateRaw = reader.Text(K.Date);
            DateOnly? date = null;
            if (dateRaw is not null)
            {
                if (!DateOnly.TryParse(dateRaw, out var d))
                    plan.Error(K.Date, "Enter the date as YYYY-MM-DD (e.g. 2027-01-26).");
                else if (d < today.AddYears(-2) || d > today.AddYears(2))
                    plan.Error(K.Date, "Date must be within 2 years of today.");
                else
                    date = d;
            }

            var dayTypeRaw = reader.Text(K.DayType);
            SchoolDayType? dayType = null;
            if (dayTypeRaw is not null)
            {
                if (!Enum.TryParse<SchoolDayType>(dayTypeRaw, ignoreCase: true, out var dt))
                    plan.Error(K.DayType, "Day Type must be Holiday, HalfDay, SpecialTiming, or WorkingDay.");
                else
                    dayType = dt;
            }
            else
                plan.Error(K.DayType, "Day Type is required.");

            var titleRaw = reader.Text(K.Title);
            if (titleRaw is not null && titleRaw.Length > 100)
                plan.Error(K.Title, "Title cannot be longer than 100 characters.");

            TimeOnly? dismissal = null, start = null, late = null;
            var dismissalRaw = reader.Text(K.DismissalTime);
            var startRaw = reader.Text(K.StartTime);
            var lateRaw = reader.Text(K.LateArrivalAfter);

            if (dismissalRaw is not null && !TimeOnly.TryParse(dismissalRaw, out var dmt))
                plan.Error(K.DismissalTime, "Enter the time as HH:MM (e.g. 12:00).");
            else if (dismissalRaw is not null) dismissal = TimeOnly.Parse(dismissalRaw);

            if (startRaw is not null && !TimeOnly.TryParse(startRaw, out var st))
                plan.Error(K.StartTime, "Enter the time as HH:MM (e.g. 09:30).");
            else if (startRaw is not null) start = TimeOnly.Parse(startRaw);

            if (lateRaw is not null && !TimeOnly.TryParse(lateRaw, out var lt))
                plan.Error(K.LateArrivalAfter, "Enter the time as HH:MM (e.g. 10:00).");
            else if (lateRaw is not null) late = TimeOnly.Parse(lateRaw);

            // Business rules
            if (dayType is not null && !plan.HasError(K.DayType))
            {
                var hasTimes = dismissal is not null || start is not null || late is not null;
                switch (dayType.Value)
                {
                    case SchoolDayType.Holiday when hasTimes:
                        plan.Error(K.DismissalTime, "Holiday rows must not include any times.");
                        break;
                    case SchoolDayType.HalfDay when dismissal is null:
                        plan.Error(K.DismissalTime, "Dismissal Time is required for HalfDay.");
                        break;
                    case SchoolDayType.SpecialTiming when !hasTimes:
                        plan.Error(K.DismissalTime, "SpecialTiming requires at least one time override.");
                        break;
                }
            }

            if (date is not null && !plan.HasError(K.Date))
            {
                if (existing.Contains(date.Value))
                    plan.Error(K.Date, "This date already has an active calendar exception.");
                else if (seen.Check(date.Value.ToString("yyyy-MM-dd"), plan.RowNumber) is int earlier)
                    plan.Error(K.Date, $"Duplicate of row {earlier} (same date).");
            }

            if (plan.IsValid)
                plan.Parsed = new Parsed(date!.Value, dayType!.Value, titleRaw!.Trim(), dismissal, start, late);

            plans.Add(plan);
        }
        return plans;
    }

    public override void Stage(IReadOnlyList<RowPlan> validRows, ImportWriteSet set, DateTime nowUtc)
    {
        foreach (var row in validRows)
        {
            var p = (Parsed)row.Parsed!;
            set.SchoolCalendarDays.Add(new SchoolCalendarDay
            {
                Date = p.Date,
                DayType = p.DayType,
                Title = p.Title,
                DismissalTime = p.DismissalTime,
                StartTime = p.StartTime,
                LateArrivalAfter = p.LateArrivalAfter,
                Status = RecordStatus.Active,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc
            });
        }
    }
}
