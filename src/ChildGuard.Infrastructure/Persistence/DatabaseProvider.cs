using Npgsql;

namespace ChildGuard.Infrastructure.Persistence;

/// <summary>Which database engine the API runs on. Chosen by <c>Database:Provider</c>.</summary>
public enum DatabaseProvider
{
    MySql,
    Postgres
}

public static class DatabaseProviders
{
    /// <summary>
    /// Empty means MySql so existing deployments keep working without a new setting. An unknown value
    /// stops startup: silently picking an engine would point the app at the wrong database.
    /// </summary>
    public static DatabaseProvider Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "mysql" or "mariadb" => DatabaseProvider.MySql,
        "postgres" or "postgresql" or "npgsql" => DatabaseProvider.Postgres,
        _ => throw new InvalidOperationException("Unsupported Database:Provider. Use 'MySql' or 'Postgres'.")
    };
}

/// <summary>
/// Hosts such as Neon hand out <c>postgresql://user:pass@host/db?sslmode=require&amp;channel_binding=require</c>.
/// Npgsql wants keyword form, so URIs are converted here; keyword strings pass through unchanged.
/// </summary>
public static class PostgresConnectionString
{
    private const int DefaultPort = 5432;
    private const int DefaultTimeoutSeconds = 30;

    /// <summary>True when TLS is on and the server certificate is verified (VerifyFull or VerifyCA).</summary>
    public static bool UsesVerifiedTls(string connectionString) =>
        new NpgsqlConnectionStringBuilder(Normalize(connectionString)).SslMode is SslMode.VerifyFull or SslMode.VerifyCA;

    public static string Normalize(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("The PostgreSQL connection string is empty.", nameof(connectionString));

        var trimmed = connectionString.Trim();
        if (!trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
            return connectionString;

        // Exception messages never include the input: it carries the password.
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            throw new ArgumentException("The PostgreSQL connection URI has no host.", nameof(connectionString));

        var database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        if (string.IsNullOrEmpty(database))
            throw new ArgumentException("The PostgreSQL connection URI has no database name.", nameof(connectionString));

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : DefaultPort,
            Database = database,
            Timeout = DefaultTimeoutSeconds,
            SslMode = SslMode.VerifyFull
        };

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var separator = uri.UserInfo.IndexOf(':');
            builder.Username = Uri.UnescapeDataString(separator < 0 ? uri.UserInfo : uri.UserInfo[..separator]);
            if (separator >= 0)
                builder.Password = Uri.UnescapeDataString(uri.UserInfo[(separator + 1)..]);
        }

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var key = Uri.UnescapeDataString(eq < 0 ? pair : pair[..eq]).ToLowerInvariant();
            var value = eq < 0 ? "" : Uri.UnescapeDataString(pair[(eq + 1)..]).ToLowerInvariant();
            switch (key)
            {
                case "sslmode":
                    // Only an explicit "disable" or CA-only check changes the secure default. libpq's
                    // "require" does not verify the certificate; Neon uses a public CA, so verify it.
                    builder.SslMode = value switch
                    {
                        "disable" => SslMode.Disable,
                        "verify-ca" => SslMode.VerifyCA,
                        _ => SslMode.VerifyFull
                    };
                    break;
                case "channel_binding":
                    builder.ChannelBinding = value switch
                    {
                        "disable" => ChannelBinding.Disable,
                        "require" => ChannelBinding.Require,
                        _ => ChannelBinding.Prefer
                    };
                    break;
            }
        }

        return builder.ConnectionString;
    }
}
