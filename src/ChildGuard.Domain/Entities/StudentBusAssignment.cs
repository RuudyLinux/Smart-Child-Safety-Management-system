using ChildGuard.Domain.Enums;

namespace ChildGuard.Domain.Entities;

public class StudentBusAssignment
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public Student Student { get; set; } = default!;
    public int BusId { get; set; }
    public Bus Bus { get; set; } = default!;
    public int? RouteId { get; set; }
    public Route? Route { get; set; }
    /// <summary>Where the student boards in the morning; a stop on the assignment's route.</summary>
    public int? PickupStopId { get; set; }
    public RouteStop? PickupStop { get; set; }
    /// <summary>Where the student leaves the bus in the afternoon; a stop on the assignment's route.</summary>
    public int? DropStopId { get; set; }
    public RouteStop? DropStop { get; set; }
    public RecordStatus Status { get; set; } = RecordStatus.Active;
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    /// <summary>When the assignment stopped being the student's current bus. Null while active (and on rows ended before this was recorded).</summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>
    /// Database-computed: StudentId while Active, otherwise null. A unique index on it lets the
    /// database itself refuse a second active assignment for a student. Never written by the app.
    /// </summary>
    public int? ActiveStudentKey { get; private set; }

    public ICollection<JourneyEvent> JourneyEvents { get; set; } = new List<JourneyEvent>();
}
