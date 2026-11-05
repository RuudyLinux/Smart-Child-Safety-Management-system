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
}
