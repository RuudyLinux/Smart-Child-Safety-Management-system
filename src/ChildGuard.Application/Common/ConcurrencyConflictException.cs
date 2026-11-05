namespace ChildGuard.Application.Common;

/// <summary>
/// Thrown by a repository when a concurrent write already changed the row a
/// service was about to update. Keeps EF's own concurrency exception out of
/// the Application layer, which has no reference to EF Core.
/// </summary>
public class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(string message) : base(message) { }
}
