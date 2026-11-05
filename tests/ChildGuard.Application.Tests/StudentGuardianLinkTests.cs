using ChildGuard.Application.DTOs;
using ChildGuard.Application.Interfaces;
using ChildGuard.Application.Services;
using ChildGuard.Application.Tests.Fakes;
using ChildGuard.Domain.Entities;

namespace ChildGuard.Application.Tests;

/// <summary>Admin management of the student-guardian link: assign, edit, unlink.</summary>
public class StudentGuardianLinkTests
{
    private sealed class FakeStudentRepository : IStudentRepository
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

    private sealed class Harness
    {
        public FakeStudentRepository Students { get; } = new();
        public FakeGuardianRepository Guardians { get; } = new();
        public StudentService Service { get; }

        public Harness()
        {
            Service = new StudentService(Students, Guardians, new FakeJourneyRepository(), new FakePhotoRepository(), new FakeSafetyReadService(), TestClock.Create().Clock);
            Students.Students.Add(new Student { StudentId = 1, FullName = "Rahul Patel", DateOfBirth = new DateTime(2016, 4, 2) });
            Guardians.Guardians.Add(new Guardian
            {
                GuardianId = 1,
                FullName = "Priya Patel",
                Email = "priya@example.test",
                Phone = "9820011111",
                PasswordHash = "hashed:x"
            });
        }

        public Task<Common.Result> AssignAsync(string? relationship = "Mother", bool pickup = true) =>
            Service.AssignGuardianAsync(1, new AssignGuardianRequest(1, relationship, pickup));
    }

    [Fact]
    public async Task UpdateGuardianLink_ChangesRelationshipAndPickupAuthorization()
    {
        var h = new Harness();
        await h.AssignAsync(relationship: "Mother", pickup: true);

        var result = await h.Service.UpdateGuardianLinkAsync(1, 1, new UpdateStudentGuardianRequest("Guardian", false));

        Assert.True(result.Succeeded);
        var link = Assert.Single(h.Students.Links);
        Assert.Equal("Guardian", link.Relationship);
        Assert.False(link.IsAuthorizedForPickup);
    }

    [Fact]
    public async Task UpdateGuardianLink_ForAnUnlinkedPair_IsRejected()
    {
        var h = new Harness();

        var result = await h.Service.UpdateGuardianLinkAsync(1, 1, new UpdateStudentGuardianRequest("Mother", true));

        Assert.False(result.Succeeded);
        Assert.Equal("Guardian is not linked to this student.", result.Error);
    }

    [Fact]
    public async Task RemoveGuardianLink_DropsTheLinkButKeepsBothRecords()
    {
        var h = new Harness();
        await h.AssignAsync();

        var result = await h.Service.RemoveGuardianLinkAsync(1, 1);

        Assert.True(result.Succeeded);
        Assert.Empty(h.Students.Links);
        Assert.Single(h.Students.Students);
        Assert.Single(h.Guardians.Guardians);
    }

    [Fact]
    public async Task RemoveGuardianLink_AlsoRevokesPickupAuthorization()
    {
        var h = new Harness();
        await h.AssignAsync(pickup: true);

        await h.Service.RemoveGuardianLinkAsync(1, 1);

        Assert.Empty(await h.Students.GetAuthorizedGuardiansAsync(1));
    }

    [Fact]
    public async Task RemoveGuardianLink_WhenNotLinked_IsRejected()
    {
        var h = new Harness();

        var result = await h.Service.RemoveGuardianLinkAsync(1, 1);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task UpdateStudent_ChangesEveryEditableDetail()
    {
        var h = new Harness();

        var result = await h.Service.UpdateAsync(1, new UpdateStudentRequest("Rahul S Patel", new DateTime(2015, 1, 9), "Grade 4"));

        Assert.True(result.Succeeded);
        var student = h.Students.Students[0];
        Assert.Equal("Rahul S Patel", student.FullName);
        Assert.Equal(new DateTime(2015, 1, 9), student.DateOfBirth);
        Assert.Equal("Grade 4", student.Grade);
    }
}
