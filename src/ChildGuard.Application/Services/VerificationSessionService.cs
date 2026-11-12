using System.Security.Cryptography;
using System.Text;
using ChildGuard.Application.Common;
using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Application.Services.FaceVerification;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;

namespace ChildGuard.Application.Services;

/// <summary>
/// Desk verification in layers (see <see cref="VerificationDecisionEngine"/>). Everything is judged here from the
/// raw images; the client supplies only bytes, a source label and the challenge token it was given.
///
///   Start      student + admin -> session (expires if left open)
///   Identify   quality, detection, identity among THIS student's guardians, authorisation. Never liveness.
///   Challenge  random head-turn sequence, token stored hashed, single use, short lived, bound to the session
///   LiveVerify burst vs challenge -> quality, identity, authorisation, liveness; only a full pass makes a pending
///              verification, which an administrator still approves or rejects.
///
/// A gallery image can identify but never satisfy liveness, and a live capture must show the same guardian a
/// gallery image identified. Only guardians linked to the selected student are ever compared, so a guardian of
/// another child cannot verify here and no unrelated guardian is loaded or named.
/// </summary>
public class VerificationSessionService : IVerificationSessionService
{
    private readonly IVerificationRepository _verifications;
    private readonly IStudentRepository _students;
    private readonly IGuardianFaceReferenceService _references;
    private readonly IFaceServiceClient _faces;
    private readonly IFaceAuditLog _audit;
    private readonly ILiveChallengeGenerator _challenges;
    private readonly FaceVerificationOptions _options;
    private readonly VerificationOptions _pickupOptions;
    private readonly TimeProvider _time;

    public VerificationSessionService(
        IVerificationRepository verifications, IStudentRepository students, IGuardianFaceReferenceService references,
        IFaceServiceClient faces, IFaceAuditLog audit, ILiveChallengeGenerator challenges,
        FaceVerificationOptions options, VerificationOptions pickupOptions, TimeProvider time)
    {
        _verifications = verifications;
        _students = students;
        _references = references;
        _faces = faces;
        _audit = audit;
        _challenges = challenges;
        _options = options;
        _pickupOptions = pickupOptions;
        _time = time;
    }

    public const string SessionGoneMessage = "This verification session is no longer open. Start again.";
    public const string ChallengeUsedMessage = "That liveness challenge is not valid any more (already used, replaced or wrong). Start the live check again.";
    public const string ChallengeExpiredMessage = "The liveness challenge expired. Start the live check again.";

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    // ------------------------------------------------------------------ Start

    public async Task<Result<VerificationSessionDto>> StartAsync(int studentId, int adminId, CancellationToken ct = default)
    {
        var student = await _students.GetByIdAsync(studentId, ct);
        if (student is null) return Result<VerificationSessionDto>.Failure("Student not found.");
        if (student.Status != RecordStatus.Active) return Result<VerificationSessionDto>.Failure("This student's record is no longer active.");

        await _verifications.CloseOpenSessionsAsync(studentId, adminId, ct);
        var now = Now;
        var session = new VerificationSession
        {
            StudentId = studentId, CreatedByAdminId = adminId, CreatedAt = now, ExpiresAt = now + _options.SessionLifetime,
            State = VerificationSessionState.Open
        };
        await _verifications.AddSessionAsync(session, ct);
        await _verifications.SaveChangesAsync(ct);

        var guardians = new List<SessionGuardianDto>();
        foreach (var link in await _students.GetAuthorizedGuardiansAsync(studentId, ct))
        {
            if (link.Guardian.Status != RecordStatus.Active) continue;
            var refs = await _references.ListAsync(link.GuardianId, ct);
            var count = refs.Succeeded ? refs.Value!.References.Count : 0;
            guardians.Add(new SessionGuardianDto(link.GuardianId, link.Guardian.FullName, link.Relationship, count > 0, count));
        }

        await Log(FaceAuditLayer.Decision, "StartSession", "Opened", adminId, session, detail: $"guardians={guardians.Count}");
        return Result<VerificationSessionDto>.Success(new VerificationSessionDto(
            session.Id, studentId, student.FullName, session.ExpiresAt, _options.MaxLiveAttempts, guardians));
    }

    // --------------------------------------------------------------- Identify

    public async Task<Result<VerificationAttemptResultDto>> IdentifyAsync(
        int sessionId, int adminId, SessionIdentifyRequest request, CancellationToken ct = default)
    {
        var (session, error) = await LoadOpenAsync(sessionId, adminId, ct);
        if (session is null) return Result<VerificationAttemptResultDto>.Failure(error!);

        var analysis = await _faces.AnalyzeImageAsync(request.ImageBase64, FaceImageAnalysisService.VerificationPurpose, ct);
        if (!analysis.Succeeded) return Result<VerificationAttemptResultDto>.Failure(analysis.Error ?? "Face service error.");
        var quality = FaceQualityPolicy.Assess(analysis.Value!);
        await LogQuality(adminId, session, "Identify", quality);

        if (!quality.Passed)
            return Result<VerificationAttemptResultDto>.Success(ToDto(
                VerificationDecisionEngine.Decide(new(request.Source, quality.Quality, quality.FaceDetection, null, AuthorisationResult.NotEvaluated, LivenessResult.NotPerformed)),
                quality, null, AuthorisationResult.NotEvaluated, LivenessResult.NotPerformed, null, null, quality.Messages, session));

        var candidates = await BuildCandidatesAsync(session.StudentId, ct);
        if (candidates.Count == 0)
            return Result<VerificationAttemptResultDto>.Failure("No guardian linked to this student has a registered face. Register a face first.");

        var identified = await _faces.IdentifyAsync(request.ImageBase64, candidates.Select(c => c.ForFaceService).ToList(), ct);
        if (!identified.Succeeded) return Result<VerificationAttemptResultDto>.Failure(identified.Error ?? "Face service error.");

        var identity = IdentityPolicy.Assess(identified.Value!, _options.AmbiguityMargin);
        var (authorisation, match) = Authorise(identity, candidates);
        var decision = VerificationDecisionEngine.Decide(new(request.Source, quality.Quality, quality.FaceDetection,
            identity.Result, authorisation, LivenessResult.NotPerformed, _pickupOptions.RequireLiveness));

        await LogIdentity(adminId, session, "Identify", identity, authorisation, match);
        await Log(FaceAuditLayer.Liveness, "Identify", LivenessResult.NotPerformed.ToString(), adminId, session, match?.GuardianId,
            $"source={request.Source}");

        int? verificationId = null;
        DateTime? expiresAt = null;
        if (decision.PickupEligible)
        {
            // Live-person check not required (Verification:RequireLiveness off): the photo pass is a pending verification an administrator decides.
            var record = NewRecord(session, match!, identity, quality, LivenessResult.NotPerformed, null, request.Source, authorisation, true, identity.Reasons);
            await _verifications.AddVerificationAsync(record, ct);
            await _verifications.SaveChangesAsync(ct);
            verificationId = record.Id;
            expiresAt = record.AttemptedAt + _pickupOptions.PendingExpiry;
            session.State = VerificationSessionState.Completed;
            session.VerificationId = record.Id;
            session.Version++;
            await SaveSessionAsync(ct);
        }
        else if (identity.Result == IdentityResult.Match && authorisation == AuthorisationResult.Authorised)
        {
            session.IdentifiedGuardianId = match!.GuardianId;
            session.IdentifiedSource = request.Source;
            session.IdentifiedScore = identity.Score;
            session.Version++;
            await SaveSessionAsync(ct);
        }
        await Log(FaceAuditLayer.Decision, "Identify", decision.Outcome.ToString(), adminId, session, match?.GuardianId,
            verificationId is null ? null : $"verification={verificationId}");

        return Result<VerificationAttemptResultDto>.Success(ToDto(decision, quality, identity, authorisation, LivenessResult.NotPerformed,
            match, identity.Reasons, quality.Messages, session, null, verificationId, expiresAt, LabelFor(match, identity)));
    }

    // -------------------------------------------------------------- Challenge

    public async Task<Result<LiveChallengeDto>> IssueChallengeAsync(int sessionId, int adminId, CancellationToken ct = default)
    {
        var (session, error) = await LoadOpenAsync(sessionId, adminId, ct);
        if (session is null) return Result<LiveChallengeDto>.Failure(error!);

        var created = _challenges.Create();
        var challenge = _options.RequireHeadTurn ? created : created with { Steps = [] };
        var now = Now;
        session.ChallengeTokenHash = Sha256Hex(challenge.Token);
        session.ChallengeSteps = string.Join(',', challenge.Steps);
        session.ChallengeIssuedAt = now;
        session.ChallengeExpiresAt = now + _options.ChallengeLifetime;
        session.ChallengeConsumedAt = null;
        session.Version++;
        try { await SaveSessionAsync(ct); }
        catch (ConcurrencyConflictException) { return Result<LiveChallengeDto>.Failure(SessionGoneMessage); }

        await Log(FaceAuditLayer.Liveness, "ChallengeIssued", "Issued", adminId, session, detail: $"steps={session.ChallengeSteps}");
        return Result<LiveChallengeDto>.Success(new LiveChallengeDto(
            challenge.Token, challenge.Steps, session.ChallengeExpiresAt.Value, _options.MinFrames, _options.MaxFrames, _options.MinLiveSeconds));
    }

    // ------------------------------------------------------------- LiveVerify

    public async Task<Result<VerificationAttemptResultDto>> LiveVerifyAsync(
        int sessionId, int adminId, LiveVerifyRequest request, CancellationToken ct = default)
    {
        var (session, error) = await LoadOpenAsync(sessionId, adminId, ct);
        if (session is null) return Result<VerificationAttemptResultDto>.Failure(error!);

        if (request.Frames.Count < _options.MinFrames || request.Frames.Count > _options.MaxFrames)
            return Result<VerificationAttemptResultDto>.Failure($"Send between {_options.MinFrames} and {_options.MaxFrames} camera frames.");

        // The token proves this capture answers the challenge this session was given: right token, not used
        // before, not expired. It is spent before anything else happens, so a replay cannot be processed twice.
        var now = Now;
        if (session.ChallengeTokenHash is null || session.ChallengeConsumedAt is not null
            || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Sha256Hex(request.ChallengeToken ?? "")), Encoding.ASCII.GetBytes(session.ChallengeTokenHash)))
        {
            await Log(FaceAuditLayer.Liveness, "LiveVerify", "Rejected", adminId, session,
                detail: session.ChallengeConsumedAt is not null ? "token reused" : "token not valid");
            return Result<VerificationAttemptResultDto>.Failure(ChallengeUsedMessage);
        }
        if (now > session.ChallengeExpiresAt)
        {
            await Log(FaceAuditLayer.Liveness, "LiveVerify", "Rejected", adminId, session, detail: "challenge expired");
            return Result<VerificationAttemptResultDto>.Failure(ChallengeExpiredMessage);
        }

        session.ChallengeConsumedAt = now;
        session.Version++;
        try { await SaveSessionAsync(ct); }
        catch (ConcurrencyConflictException)
        {
            await Log(FaceAuditLayer.Liveness, "LiveVerify", "Rejected", adminId, session, detail: "token used concurrently");
            return Result<VerificationAttemptResultDto>.Failure(ChallengeUsedMessage);
        }

        var tooFast = (now - session.ChallengeIssuedAt!.Value).TotalSeconds < _options.MinLiveSeconds;
        var candidates = await BuildCandidatesAsync(session.StudentId, ct);
        if (candidates.Count == 0)
            return Result<VerificationAttemptResultDto>.Failure("No guardian linked to this student has a registered face. Register a face first.");

        var analysed = await _faces.AnalyzeLivenessAsync(
            session.ChallengeSteps!.Split(',', StringSplitOptions.RemoveEmptyEntries),
            request.Frames.Select(f => new LiveFrameInput(f.ImageBase64, f.TMs)).ToList(),
            candidates.Select(c => c.ForFaceService).ToList(), ct);
        if (!analysed.Succeeded) return Result<VerificationAttemptResultDto>.Failure(analysed.Error ?? "Face service error.");
        var a = analysed.Value!;

        // From here the attempt counts, whatever the outcome (a pass gives it back).
        session.FailedLiveAttempts++;

        var replayed = false;
        if (a.NeutralFrameIndex is int idx && idx >= 0 && idx < request.Frames.Count)
        {
            var hash = FrameHash(request.Frames[idx].ImageBase64);
            replayed = await _verifications.IsNeutralFrameHashUsedAsync(hash, ct);
            if (!replayed) session.NeutralFrameHash = hash;
        }

        var quality = a.NeutralQuality is not null
            ? FaceQualityPolicy.Assess(a.NeutralQuality)
            : new QualityAssessment(CheckResult.Fail, CheckResult.Fail, 0, ["NO_NEUTRAL_FRAME"], [],
                FaceVerificationMessages.ForReasons(["NO_NEUTRAL_FRAME"]).ToList(), new ImageAnalysisMetrics(null, null, null, null, null, null));
        var liveness = LivenessPolicy.Assess(a, _options, replayed, tooFast);

        IdentityAssessment? identity = null;
        (AuthorisationResult Authorisation, Candidate? Match) authorised = (AuthorisationResult.NotEvaluated, null);
        if (quality.Passed)
        {
            identity = a.Identification is null
                ? new IdentityAssessment(IdentityResult.NoMatch, null, null, null, 0, ["IDENTITY_UNAVAILABLE"])
                : IdentityPolicy.Assess(a.Identification, _options.AmbiguityMargin);

            // A live capture after a gallery match has to show the same guardian the gallery photo identified.
            if (identity.Result == IdentityResult.Match && session.IdentifiedGuardianId is int expected && identity.GuardianId != expected)
                identity = identity with { Result = IdentityResult.NoMatch, GuardianId = null, Reasons = [.. identity.Reasons, "DIFFERS_FROM_PHOTO"] };

            authorised = Authorise(identity, candidates);
        }

        var decision = VerificationDecisionEngine.Decide(new(CaptureSource.Webcam, quality.Quality, quality.FaceDetection,
            identity?.Result, authorised.Authorisation, liveness.Result, _pickupOptions.RequireLiveness));

        await LogQuality(adminId, session, "LiveVerify", quality);
        if (identity is not null) await LogIdentity(adminId, session, "LiveVerify", identity, authorised.Authorisation, authorised.Match);
        await Log(FaceAuditLayer.Liveness, "LiveVerify", liveness.Result.ToString(), adminId, session, authorised.Match?.GuardianId,
            $"reasons={string.Join(',', liveness.Reasons)};passive={a.Passive.Mean:0.00};steps={session.ChallengeSteps}");

        int? verificationId = null;
        DateTime? expiresAt = null;
        var reasons = (identity?.Reasons ?? []).Concat(liveness.Reasons).Distinct().ToList();

        if (decision.PickupEligible)
        {
            var record = NewRecord(session, authorised.Match!, identity!, quality, liveness.Result, liveness.Score, CaptureSource.Webcam, authorised.Authorisation, true, reasons);
            await _verifications.AddVerificationAsync(record, ct);
            await _verifications.SaveChangesAsync(ct);
            verificationId = record.Id;
            expiresAt = record.AttemptedAt + _pickupOptions.PendingExpiry;
            session.State = VerificationSessionState.Completed;
            session.VerificationId = record.Id;
            session.FailedLiveAttempts--;
        }
        else
        {
            // A recognised guardian whose liveness failed, or who is not authorised, leaves a visible, non-approvable
            // record (it can only be rejected), so the log shows the attempt.
            if (identity?.Result == IdentityResult.Match
                && decision.Outcome is DecisionOutcome.LivenessFailed or DecisionOutcome.Unauthorised && authorised.Match is not null)
            {
                var record = NewRecord(session, authorised.Match, identity, quality, liveness.Result, liveness.Score, CaptureSource.Webcam, authorised.Authorisation, false, reasons);
                await _verifications.AddVerificationAsync(record, ct);
                await _verifications.SaveChangesAsync(ct);
                verificationId = record.Id;
            }
            if (session.FailedLiveAttempts >= _options.MaxLiveAttempts)
                session.State = VerificationSessionState.Closed;
        }

        session.Version++;
        try { await SaveSessionAsync(ct); }
        catch (ConcurrencyConflictException)
        {
            // Also raised for the unique neutral-frame hash: the backstop against a racing replay of the same capture.
            await Log(FaceAuditLayer.Liveness, "LiveVerify", "Rejected", adminId, session, detail: "capture reused");
            return Result<VerificationAttemptResultDto>.Failure(ChallengeUsedMessage);
        }
        await Log(FaceAuditLayer.Decision, "LiveVerify", decision.Outcome.ToString(), adminId, session, authorised.Match?.GuardianId,
            verificationId is null ? null : $"verification={verificationId}");

        var failureMessages = decision.Outcome == DecisionOutcome.LivenessFailed
            ? FaceVerificationMessages.ForReasons(liveness.Reasons).ToList()
            : quality.Messages.ToList();
        return Result<VerificationAttemptResultDto>.Success(ToDto(decision, quality, identity, authorised.Authorisation, liveness.Result,
            authorised.Match, reasons, failureMessages, session, liveness.Score, verificationId, expiresAt, LabelFor(authorised.Match, identity)));
    }

    // ---------------------------------------------------------------- helpers

    private sealed record Candidate(StudentGuardian Link, IReadOnlyList<ReferenceEmbedding> Embeddings, IReadOnlyList<FaceReferenceDto> Labels)
    {
        public int GuardianId => Link.GuardianId;
        public IdentifyCandidate ForFaceService => new(Link.GuardianId, Embeddings);
    }

    /// <summary>Guardians linked to the student that have a readable face on file. Nobody else is ever loaded.</summary>
    private async Task<List<Candidate>> BuildCandidatesAsync(int studentId, CancellationToken ct)
    {
        var result = new List<Candidate>();
        foreach (var link in await _students.GetLinkedGuardiansAsync(studentId, ct))
        {
            var embeddings = await _references.LoadEmbeddingsAsync(link.GuardianId, ct);
            if (!embeddings.Succeeded || embeddings.Value!.Count == 0) continue;
            var labels = await _references.ListAsync(link.GuardianId, ct);
            result.Add(new Candidate(link, embeddings.Value, labels.Succeeded ? labels.Value!.References : []));
        }
        return result;
    }

    private static (AuthorisationResult, Candidate?) Authorise(IdentityAssessment identity, IReadOnlyList<Candidate> candidates)
    {
        if (identity.Result != IdentityResult.Match || identity.GuardianId is not int id) return (AuthorisationResult.NotEvaluated, null);
        var match = candidates.FirstOrDefault(c => c.GuardianId == id);
        if (match is null) return (AuthorisationResult.NotEvaluated, null);
        var allowed = match.Link.IsAuthorizedForPickup && match.Link.Guardian.Status == RecordStatus.Active;
        return (allowed ? AuthorisationResult.Authorised : AuthorisationResult.Unauthorised, match);
    }

    private static string? LabelFor(Candidate? match, IdentityAssessment? identity)
    {
        if (match is null || identity?.BestReferenceId is not int id) return null;
        var reference = match.Labels.FirstOrDefault(r => r.Id == id);
        return reference is null ? null : reference.Label ?? (reference.Source == "Primary" ? "Primary face" : "Reference photo");
    }

    private GuardianVerification NewRecord(
        VerificationSession session, Candidate match, IdentityAssessment identity, QualityAssessment quality,
        LivenessResult liveness, double? livenessScore, CaptureSource source,
        AuthorisationResult authorisation, bool eligible, IReadOnlyList<string> reasons) => new()
    {
        StudentId = session.StudentId, GuardianId = match.GuardianId, AttemptedAt = Now,
        MatchScore = identity.Score ?? 0,
        // Match only when every mandatory layer passed, so the existing pending/approve flow can act on it.
        Result = eligible ? VerificationResult.Match : VerificationResult.NoMatch,
        PickupApproved = false,
        CaptureSource = source, QualityResult = quality.Quality, FaceDetectionResult = quality.FaceDetection,
        IdentityResult = identity.Result, AuthorisationResult = authorisation, LivenessResult = liveness,
        LivenessScore = livenessScore, BestReferenceId = identity.BestReferenceId, SessionId = session.Id,
        LayerNotes = reasons.Count == 0 ? null : string.Join(',', reasons)
    };

    private VerificationAttemptResultDto ToDto(
        VerificationDecision decision, QualityAssessment quality, IdentityAssessment? identity, AuthorisationResult authorisation,
        LivenessResult liveness, Candidate? match, IReadOnlyList<string>? reasons, IReadOnlyCollection<string> issueMessages,
        VerificationSession session, double? livenessScore = null, int? verificationId = null, DateTime? expiresAt = null, string? label = null)
    {
        var threshold = identity?.Threshold ?? 0;
        return new VerificationAttemptResultDto(
            decision.Outcome.ToString(), decision.PickupEligible, decision.NextStep,
            FaceVerificationMessages.ForOutcome(decision.Outcome, issueMessages.ToList()),
            quality.Quality, quality.FaceDetection, identity?.Result, authorisation, liveness,
            match is null ? null : new MatchedGuardianDto(match.GuardianId, match.Link.Guardian.FullName, match.Link.Relationship),
            identity?.Score, threshold,
            identity?.Score is double s && threshold > 0 ? IdentityPolicy.ConfidenceBand(s, threshold) : null,
            label, issueMessages.ToList(), quality.Warnings, reasons ?? [],
            verificationId, expiresAt, Math.Max(0, _options.MaxLiveAttempts - session.FailedLiveAttempts));
    }

    private async Task<(VerificationSession? Session, string? Error)> LoadOpenAsync(int sessionId, int adminId, CancellationToken ct)
    {
        var session = await _verifications.GetSessionAsync(sessionId, ct);
        // Another admin's session is reported exactly like a missing one.
        if (session is null || session.CreatedByAdminId != adminId)
        {
            await _audit.WriteAsync(new FaceAuditEntry(FaceAuditLayer.Access, "Session", "Denied", FaceActor.Admin(adminId), SessionId: sessionId));
            return (null, SessionGoneMessage);
        }
        if (session.State != VerificationSessionState.Open) return (null, SessionGoneMessage);
        if (Now > session.ExpiresAt)
        {
            session.State = VerificationSessionState.Expired;
            session.Version++;
            try { await SaveSessionAsync(ct); } catch (ConcurrencyConflictException) { /* someone else already ended it */ }
            await Log(FaceAuditLayer.Decision, "Session", "Expired", adminId, session);
            return (null, "This verification session expired. Start again.");
        }
        return (session, null);
    }

    private Task SaveSessionAsync(CancellationToken ct) => _verifications.SaveChangesAsync(ct);

    private Task Log(FaceAuditLayer layer, string action, string outcome, int adminId, VerificationSession session, int? guardianId = null, string? detail = null) =>
        _audit.WriteAsync(new FaceAuditEntry(layer, action, outcome, FaceActor.Admin(adminId), StudentId: session.StudentId,
            GuardianId: guardianId, SessionId: session.Id, Detail: detail));

    private async Task LogQuality(int adminId, VerificationSession session, string action, QualityAssessment quality)
    {
        var detail = $"issues={string.Join(',', quality.Issues)}";
        await Log(FaceAuditLayer.Quality, action, quality.Quality.ToString(), adminId, session, detail: detail);
        await Log(FaceAuditLayer.Detection, action, quality.FaceDetection.ToString(), adminId, session, detail: $"faces={quality.FaceCount}");
    }

    private async Task LogIdentity(int adminId, VerificationSession session, string action, IdentityAssessment identity,
        AuthorisationResult authorisation, Candidate? match)
    {
        await Log(FaceAuditLayer.Identity, action, identity.Result.ToString(), adminId, session, match?.GuardianId,
            $"score={identity.Score:0.000};threshold={identity.Threshold:0.00};reasons={string.Join(',', identity.Reasons)}");
        await Log(FaceAuditLayer.Authorisation, action, authorisation.ToString(), adminId, session, match?.GuardianId);
    }

    private static string Sha256Hex(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static string FrameHash(string base64) => Sha256Hex(string.Concat(base64.Where(c => !char.IsWhiteSpace(c))));
}
