using Npgsql;

namespace ChildGuard.Infrastructure.Persistence;

/// <summary>Engine-neutral reading of database errors, so callers do not name MySQL or PostgreSQL.</summary>
public static class DbErrors
{
    /// <summary>True when a unique index or primary key refused the write (MySQL 1062, PostgreSQL 23505).</summary>
    public static bool IsUniqueViolation(Exception? exception)
    {
        for (var e = exception; e is not null; e = e.InnerException)
        {
            if (e is MySqlConnector.MySqlException { ErrorCode: MySqlConnector.MySqlErrorCode.DuplicateKeyEntry }) return true;
            if (e is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }) return true;
        }
        return false;
    }
}
