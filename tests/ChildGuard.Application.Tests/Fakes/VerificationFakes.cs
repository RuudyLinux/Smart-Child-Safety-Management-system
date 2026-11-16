using ChildGuard.Application.Common;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;

namespace ChildGuard.Application.Tests.Fakes;

internal sealed class FakeStudentRepository : IStudentRepository
{
    public List<Student> Students { get; } = new();
    public List<StudentGuardian> Links { get; } = new();

    public Task<List<Student>> GetAllAsync(string? search, CancellationToken ct = default) =>
        Task.FromResult(Students.ToList());

    public Task<Student?> GetByIdAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Students.FirstOrDefault(s => s.StudentId == id));

    public Task<Student?> GetWithGuardiansAsync(int id, CancellationToken ct = default) => GetByIdAsync(id, ct);

    public Task AddAsync(Student student, CancellationToken ct = default)
    {
        student.StudentId = student.StudentId == 0 ? Students.Count + 1 : student.StudentId;
        Students.Add(student);
        return Task.CompletedTask;
    }

    public Task<StudentGuardian?> GetStudentGuardianAsync(int studentId, int guardianId, CancellationToken ct = default) =>
        Task.FromResult(Links.FirstOrDefault(l => l.StudentId == studentId && l.GuardianId == guardianId));

    public Task AddStudentGuardianAsync(StudentGuardian link, CancellationToken ct = default)
    {
        Links.Add(link);
        return Task.CompletedTask;
    }

    public void RemoveStudentGuardian(StudentGuardian link) => Links.Remove(link);

    public Task<List<StudentGuardian>> GetAuthorizedGuardiansAsync(int studentId, CancellationToken ct = default) =>
        Task.FromResult(Links.Where(l => l.StudentId == studentId && l.IsAuthorizedForPickup).ToList());

    public Task<List<StudentGuardian>> GetLinkedGuardiansAsync(int studentId, CancellationToken ct = default) =>
        Task.FromResult(Links.Where(l => l.StudentId == studentId).ToList());

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task<bool> AdmissionNumberTakenAsync(string admissionNumber, int exceptStudentId, CancellationToken ct = default) => Task.FromResult(false);
    public Task<bool> TrySaveChangesAsync(CancellationToken ct = default) => Task.FromResult(true);
}

internal sealed class FakeNotificationRepository : INotificationRepository
{
    public List<Notification> Notifications { get; } = new();

    public Task AddAsync(Notification notification, CancellationToken ct = default)
    {
        notification.NotificationId = Notifications.Count + 1;
        Notifications.Add(notification);
        return Task.CompletedTask;
    }

    public Task<List<Notification>> GetForGuardianAsync(int guardianId, CancellationToken ct = default) =>
        Task.FromResult(Notifications.Where(n => n.GuardianId == guardianId).ToList());

    public Task<Notification?> GetByIdAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Notifications.FirstOrDefault(n => n.NotificationId == id));

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class FakePushNotificationSender : IPushNotificationSender
{
    public List<(string Title, string Message, int GuardianId)> Sent { get; } = new();

    public Task SendAsync(string title, string message, int guardianId, CancellationToken ct = default)
    {
        Sent.Add((title, message, guardianId));
        return Task.CompletedTask;
    }
}

/// <summary>Always reports a match unless told not to — the scenarios that matter are decided by the service, not this stub.</summary>
internal sealed class FakeFaceServiceClient : IFaceServiceClient
{
    public bool NextMatch { get; set; } = true;
    public double NextScore { get; set; } = 0.95;

    public Task<Result<FaceEmbeddingResult>> RegisterFaceAsync(string imageBase64, CancellationToken ct = default) =>
        Task.FromResult(Result<FaceEmbeddingResult>.Success(new FaceEmbeddingResult("[0.1,0.2]", "test-model")));

    public Task<Result<FaceMatchResult>> VerifyFaceAsync(string storedEmbeddingJson, string imageBase64, CancellationToken ct = default) =>
        Task.FromResult(Result<FaceMatchResult>.Success(new FaceMatchResult(NextMatch, NextScore)));

    public Result<ImageAnalysis> NextAnalysis { get; set; } = Result<ImageAnalysis>.Success(CleanAnalysis);
    public string? LastPurpose { get; private set; }

    public static ImageAnalysis CleanAnalysis { get; } = new(
        "verification", 1, [], [], [], new ImageAnalysisMetrics(300, 150, 40, 200, 0, 0.99));

    public Task<Result<ImageAnalysis>> AnalyzeImageAsync(string imageBase64, string purpose, CancellationToken ct = default)
    {
        LastPurpose = purpose;
        return Task.FromResult(NextAnalysis);
    }

    /// <summary>Every call made to IdentifyAsync: the candidates it was given.</summary>
    public List<IReadOnlyList<IdentifyCandidate>> IdentifyCalls { get; } = new();
    public double IdentifyThreshold { get; set; } = 0.40;
    public Dictionary<int, double> ScoreByGuardian { get; } = new();
    public Result<FaceIdentification>? NextIdentification { get; set; }

    public Task<Result<FaceIdentification>> IdentifyAsync(string imageBase64, IReadOnlyList<IdentifyCandidate> candidates, CancellationToken ct = default)
    {
        IdentifyCalls.Add(candidates);
        if (NextIdentification is not null) return Task.FromResult(NextIdentification);

        var results = candidates.Select(c => new GuardianIdentification(
            c.GuardianId, ScoreByGuardian.GetValueOrDefault(c.GuardianId, NextScore), c.References.Count > 0 ? c.References[0].ReferenceId : null,
            c.References.Select(r => new ReferenceScore(r.ReferenceId, ScoreByGuardian.GetValueOrDefault(c.GuardianId, NextScore))).ToList(), [])).ToList();
        return Task.FromResult(Result<FaceIdentification>.Success(new FaceIdentification(IdentifyThreshold, "test-model", results)));
    }


    /// <summary>A liveness analysis that passes every signal for the first candidate; tests tweak it with <c>with</c>.</summary>
    public static LivenessAnalysis PassingLiveness(IReadOnlyList<string> steps, IReadOnlyList<IdentifyCandidate>? candidates, double score = 0.93, int? guardianId = null)
    {
        var identification = candidates is null ? null : new FaceIdentification(0.40, "test-model",
            candidates.Select(c => new GuardianIdentification(c.GuardianId,
                c.GuardianId == (guardianId ?? candidates[0].GuardianId) ? score : 0.05,
                c.References.Count > 0 ? c.References[0].ReferenceId : null,
                c.References.Select(r => new ReferenceScore(r.ReferenceId, score)).ToList(), [])).ToList());
        return new LivenessAnalysis(
            10, 1.0, 2,
            steps.Select((s, i) => new LivenessStepResult(s, true, 4 + i * 3, s == "turn_left" ? 0.35 : -0.35)).ToList(),
            true, new PassiveSignals(true, 5, [0.97, 0.98, 0.96, 0.97, 0.98], 1.0, 0.972, 0.96),
            new ContinuitySignals(2, 0.7, true), 0.0, [], identification,
            new ImageAnalysis("verification", 1, [], [], [], new ImageAnalysisMetrics(300, 150, 40, 200, 0, 0.99)));
    }

    public Func<IReadOnlyList<string>, IReadOnlyList<IdentifyCandidate>?, LivenessAnalysis>? LivenessFactory { get; set; }
    public Result<LivenessAnalysis>? NextLivenessFailure { get; set; }
    public List<(IReadOnlyList<string> Steps, IReadOnlyList<LiveFrameInput> Frames, IReadOnlyList<IdentifyCandidate>? Candidates)> LivenessCalls { get; } = new();

    public Task<Result<LivenessAnalysis>> AnalyzeLivenessAsync(
        IReadOnlyList<string> steps, IReadOnlyList<LiveFrameInput> frames, IReadOnlyList<IdentifyCandidate>? candidates, CancellationToken ct = default)
    {
        LivenessCalls.Add((steps, frames, candidates));
        if (NextLivenessFailure is not null) return Task.FromResult(NextLivenessFailure);
        return Task.FromResult(Result<LivenessAnalysis>.Success(
            LivenessFactory?.Invoke(steps, candidates) ?? PassingLiveness(steps, candidates, NextScore)));
    }

    public int? NextDuplicateOf { get; set; }
    public double? NextBestExistingScore { get; set; } = 0.5;
    public List<IReadOnlyList<ReferenceEmbedding>> RegisterReferenceCalls { get; } = new();
    public Result<ReferenceRegistration>? NextRegistration { get; set; }

    public Task<Result<ReferenceRegistration>> RegisterReferenceAsync(string imageBase64, IReadOnlyList<ReferenceEmbedding> existing, CancellationToken ct = default)
    {
        RegisterReferenceCalls.Add(existing);
        return Task.FromResult(NextRegistration ?? Result<ReferenceRegistration>.Success(
            new ReferenceRegistration("[0.5,0.5]", "test-model", NextDuplicateOf, NextBestExistingScore, 0.40)));
    }
}

internal sealed class FakeFaceAuditLog : IFaceAuditLog
{
    public List<FaceAuditEntry> Entries { get; } = new();

    public Task WriteAsync(FaceAuditEntry entry, CancellationToken ct = default)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }
}
