using ChildGuard.Infrastructure.Persistence;
using Npgsql;

namespace ChildGuard.Api.IntegrationTests;

/// <summary>Neon (and most hosts) hand out postgresql:// URIs; the app and Npgsql need keyword form (no database needed).</summary>
public class PostgresConnectionStringTests
{
    private const string NeonUri =
        "postgresql://neondb_owner:p%40ss%2Fword@ep-example-pooler.c-9.us-east-2.aws.neon.tech/neondb?channel_binding=require&sslmode=require";

    [Fact]
    public void NeonUri_IsConvertedToKeywordForm_WithVerifiedTls()
    {
        var b = new NpgsqlConnectionStringBuilder(PostgresConnectionString.Normalize(NeonUri));

        Assert.Equal("ep-example-pooler.c-9.us-east-2.aws.neon.tech", b.Host);
        Assert.Equal(5432, b.Port);
        Assert.Equal("neondb", b.Database);
        Assert.Equal("neondb_owner", b.Username);
        Assert.Equal("p@ss/word", b.Password);
        // "require" would not check the certificate; Neon uses a public CA, so verify it.
        Assert.Equal(SslMode.VerifyFull, b.SslMode);
        Assert.Equal(ChannelBinding.Require, b.ChannelBinding);
        Assert.Equal(30, b.Timeout);
    }

    [Theory]
    [InlineData("disable", SslMode.Disable)]
    [InlineData("require", SslMode.VerifyFull)]
    [InlineData("verify-ca", SslMode.VerifyCA)]
    [InlineData("verify-full", SslMode.VerifyFull)]
    public void SslModeInUri_IsMapped(string uriMode, SslMode expected)
    {
        var b = new NpgsqlConnectionStringBuilder(
            PostgresConnectionString.Normalize($"postgres://u:p@db.example.com:6543/app?sslmode={uriMode}"));

        Assert.Equal(expected, b.SslMode);
        Assert.Equal(6543, b.Port);
    }

    [Fact]
    public void UriWithoutSslMode_DefaultsToVerifiedTls()
    {
        var b = new NpgsqlConnectionStringBuilder(PostgresConnectionString.Normalize("postgres://u:p@db.example.com/app"));
        Assert.Equal(SslMode.VerifyFull, b.SslMode);
    }

    [Fact]
    public void KeywordString_PassesThroughUnchanged()
    {
        const string keyword = "Host=127.0.0.1;Port=5433;Username=postgres;Database=x;SSL Mode=Disable";
        Assert.Equal(keyword, PostgresConnectionString.Normalize(keyword));
    }

    [Theory]
    [InlineData("postgresql:///neondb")]
    [InlineData("postgresql://u:p@host")]
    public void UriWithoutHostOrDatabase_IsRejected(string uri) =>
        Assert.Throws<ArgumentException>(() => PostgresConnectionString.Normalize(uri));

    [Theory]
    [InlineData("MySql", DatabaseProvider.MySql)]
    [InlineData("mysql", DatabaseProvider.MySql)]
    [InlineData("", DatabaseProvider.MySql)]
    [InlineData(null, DatabaseProvider.MySql)]
    [InlineData("Postgres", DatabaseProvider.Postgres)]
    [InlineData("postgresql", DatabaseProvider.Postgres)]
    public void Provider_IsParsed_AndDefaultsToMySql(string? value, DatabaseProvider expected) =>
        Assert.Equal(expected, DatabaseProviders.Parse(value));

    [Fact]
    public void UnknownProvider_StopsStartup() =>
        Assert.Throws<InvalidOperationException>(() => DatabaseProviders.Parse("sqlite"));

    [Theory]
    [InlineData(NeonUri, true)]
    [InlineData("postgres://u:p@db.example.com/app?sslmode=verify-ca", true)]
    [InlineData("postgres://u:p@db.example.com/app?sslmode=disable", false)]
    [InlineData("Host=db;Database=x;Username=u;SSL Mode=VerifyFull", true)]
    [InlineData("Host=db;Database=x;Username=u;SSL Mode=Require", false)]
    [InlineData("Host=db;Database=x;Username=u", false)]
    public void UsesVerifiedTls_IsTrueOnlyWhenTheServerCertificateIsChecked(string connection, bool expected) =>
        Assert.Equal(expected, PostgresConnectionString.UsesVerifiedTls(connection));
}
