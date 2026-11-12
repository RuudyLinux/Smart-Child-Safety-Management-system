using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using ChildGuard.Api.Security;
using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Enums;
using ChildGuard.Infrastructure;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---- Logging ----
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console()
    .WriteTo.File("logs/childguard-api-.log", rollingInterval: RollingInterval.Day));

// ---- Services ----
builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddOpenApi();

builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.IsDevelopment());
builder.Services.AddValidatorsFromAssembly(
    typeof(ChildGuard.Application.Validators.LoginRequestValidator).Assembly);

var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Missing Jwt:Secret configuration.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "ChildGuard";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "ChildGuardClients";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        ClockSkew = TimeSpan.FromMinutes(1)
    };

    // A valid-looking token means nothing once the account behind it is
    // deactivated — re-check status on every request so a deactivated
    // admin/guardian loses access immediately, not just after the token expires.
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            // JwtBearer maps inbound "sub" to ClaimTypes.NameIdentifier by default,
            // so reading only "sub" here failed every token. Same lookup as GetUserId().
            var sub = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub)
                ?? context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var role = context.Principal?.FindFirstValue(ClaimTypes.Role);
            if (!int.TryParse(sub, out var userId) || role is null)
            {
                context.Fail("Invalid token.");
                return;
            }

            var services = context.HttpContext.RequestServices;
            var status = role switch
            {
                nameof(AppRole.Guardian) => (await services.GetRequiredService<IGuardianRepository>()
                    .GetByIdAsync(userId, context.HttpContext.RequestAborted))?.Status,
                nameof(AppRole.Admin) => (await services.GetRequiredService<IAdminRepository>()
                    .GetByIdAsync(userId, context.HttpContext.RequestAborted))?.Status,
                _ => null
            };

            if (status != RecordStatus.Active)
                context.Fail("Account is no longer active.");
        }
    };
})
// Lets a physical RFID scanner authenticate with a shared device key (X-Device-Key)
// instead of an Admin JWT — see ApiKeyAuthenticationHandler. Additive only; the
// Admin-JWT path used by the browser demo page is unchanged.
.AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
    ApiKeyAuthenticationHandler.SchemeName, _ => { });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AdminOnly", p => p.RequireRole("Admin"))
    .AddPolicy("GuardianOnly", p => p.RequireRole("Guardian"))
    .AddPolicy("AdminOrDevice", p => p
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme, ApiKeyAuthenticationHandler.SchemeName)
        .RequireAssertion(ctx => ctx.User.IsInRole("Admin") || ctx.User.HasClaim("scanner", "device")));

// ---- Reverse proxy ----
// Behind a proxy/load balancer every request arrives from the proxy's address, which
// would put all callers in one rate-limit bucket. X-Forwarded-For is only trusted
// from the proxies listed here — never from arbitrary clients, who could forge it.
var knownProxies = builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [];
var knownNetworks = builder.Configuration.GetSection("ReverseProxy:KnownNetworks").Get<string[]>() ?? [];
var behindProxy = knownProxies.Length > 0 || knownNetworks.Length > 0;
if (behindProxy)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var proxy in knownProxies) options.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
        foreach (var network in knownNetworks) options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
    });
}

// ---- Rate limiting (brute-force / scan-flood protection) ----
var limits = builder.Configuration.GetSection("RateLimiting");
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Login/OTP are anonymous, so they partition per client IP (the real client IP
    // when ReverseProxy is configured) — one attacker cannot exhaust everyone's window.
    options.AddPolicy(RateLimitPolicies.Auth, PerCallerFixedWindow(limits.GetValue("AuthPerMinute", 10), TimeSpan.FromMinutes(1)));
    options.AddPolicy(RateLimitPolicies.Otp, PerCallerFixedWindow(limits.GetValue("OtpPerMinute", 20), TimeSpan.FromMinutes(1)));
    // Scans partition per authenticated scanner (or admin), not per IP: several
    // scanners behind one NAT/proxy must not share a bucket. 60/min = a sustained
    // tap per second per reader.
    options.AddPolicy(RateLimitPolicies.RfidScan, PerIdentityFixedWindow(limits.GetValue("RfidScanPerMinute", 60), TimeSpan.FromMinutes(1)));
    // Image analysis runs a detector per call, so it is metered per signed-in caller.
    options.AddPolicy(RateLimitPolicies.FaceAnalyze, PerIdentityFixedWindow(limits.GetValue("FaceAnalyzePerMinute", 60), TimeSpan.FromMinutes(1)));
    // A live check is a heavy multi-frame analysis, so it is metered tighter than single-image checks.
    options.AddPolicy(RateLimitPolicies.LiveVerify, PerIdentityFixedWindow(limits.GetValue("LiveVerifyPerMinute", 20), TimeSpan.FromMinutes(1)));
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AdminWebAndApp", policy =>
    {
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();

        // `flutter run` picks a random localhost port unless --web-port is given, so
        // in development any loopback origin is accepted as well. Loopback only —
        // never a wildcard, and never outside Development, where the configured
        // list above stays the only thing allowed.
        if (builder.Environment.IsDevelopment())
            policy.SetIsOriginAllowed(IsLoopbackOrigin);
    });
});

/// Accepts http(s)://localhost:* and http(s)://127.0.0.1:* (and the IPv6 loopback).
static bool IsLoopbackOrigin(string origin) =>
    Uri.TryCreate(origin, UriKind.Absolute, out var uri)
    && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
    && uri.IsLoopback;

static Func<HttpContext, RateLimitPartition<string>> PerCallerFixedWindow(int permitLimit, TimeSpan window) =>
    httpContext => RateLimitPartition.GetFixedWindowLimiter(
        "ip:" + (httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = permitLimit, Window = window, QueueLimit = 0 });

static Func<HttpContext, RateLimitPartition<string>> PerIdentityFixedWindow(int permitLimit, TimeSpan window) =>
    httpContext =>
    {
        var user = httpContext.User;
        var device = user.FindFirst(ApiKeyAuthenticationHandler.DeviceIdClaim)?.Value;
        var subject = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var key = device is not null ? "device:" + device
            : subject is not null ? "user:" + subject
            : "ip:" + (httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        return RateLimitPartition.GetFixedWindowLimiter(key,
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permitLimit, Window = window, QueueLimit = 0 });
    };

var app = builder.Build();

// ---- Pipeline ----
if (behindProxy)
    app.UseForwardedHeaders(); // first, so logging, HTTPS redirect and rate limits see the real client

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseHsts();
    WarnOnInsecureProductionSettings(app.Configuration, app.Logger);
}

app.UseSerilogRequestLogging();
app.UseMiddleware<ChildGuard.Api.Middleware.ExceptionHandlingMiddleware>();

app.UseHttpsRedirection();
app.UseCors("AdminWebAndApp");
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

await ChildGuard.Infrastructure.Persistence.DbSeeder.SeedAsync(app.Services);
await ChildGuard.Infrastructure.Persistence.AttendanceTimeProjection.SyncAsync(app.Services);
if (app.Configuration.GetValue<bool>("SeedDemoData"))
{
    // Demo guardians share a documented default password: never seed them outside Development.
    if (app.Environment.IsDevelopment())
        await ChildGuard.Infrastructure.Persistence.DemoDataSeeder.SeedAsync(app.Services);
    else
        app.Logger.LogWarning("SeedDemoData is ignored outside Development.");
}

app.Run();

/// Startup warnings for settings that are acceptable in development but not in production.
/// Logged (never the values themselves), not fatal, so an operator sees them on first boot.
static void WarnOnInsecureProductionSettings(IConfiguration config, Microsoft.Extensions.Logging.ILogger logger)
{
    var cs = config.GetConnectionString("ChildGuardDb") ?? "";
    if (ChildGuard.Infrastructure.Persistence.DatabaseProviders.Parse(config["Database:Provider"])
        == ChildGuard.Infrastructure.Persistence.DatabaseProvider.Postgres)
    {
        // Public CAs (Neon, most managed hosts) need no SslCa; the certificate just has to be verified.
        if (!ChildGuard.Infrastructure.Persistence.PostgresConnectionString.UsesVerifiedTls(cs))
            logger.LogWarning("Database connection is not using SSL Mode=VerifyFull (or VerifyCA): the server certificate is not verified.");
    }
    else
    {
        var sslMode = System.Text.RegularExpressions.Regex.Match(cs, @"(?i)ssl\s*mode\s*=\s*([a-z]+)");
        var verified = sslMode.Success && sslMode.Groups[1].Value.ToLowerInvariant() is "verifyca" or "verifyfull";
        var hasCa = System.Text.RegularExpressions.Regex.IsMatch(cs, @"(?i)ssl\s*ca\s*=");
        if (!verified || !hasCa)
            logger.LogWarning("Database connection is not using SslMode=VerifyFull (or VerifyCA) with SslCa: the server certificate is not verified.");
    }
    if (config.GetValue<bool>("Otp:LogCodeOnDeliveryFailure"))
        logger.LogWarning("Otp:LogCodeOnDeliveryFailure is set. It is ignored outside Development, but remove it: a logged login code can be used by anyone with log access.");
    if (string.IsNullOrWhiteSpace(config["Biometrics:EncryptionKey"]))
        logger.LogWarning("Biometrics:EncryptionKey is not set: face embeddings are stored unencrypted. Set a 32-byte base64 key.");
    if (!string.IsNullOrWhiteSpace(config["Rfid:DeviceApiKey"]))
        logger.LogWarning("Rfid:DeviceApiKey (shared scanner key) is set. Issue per-scanner keys and remove it.");
    if ((config["AllowedHosts"] ?? "*") == "*")
        logger.LogWarning("AllowedHosts is '*'. Set it to the API's public host name(s).");
}

/// Exposed for WebApplicationFactory-based integration tests.
public partial class Program;
