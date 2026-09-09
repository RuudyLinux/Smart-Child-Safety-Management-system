using System.Text;
using System.Threading.RateLimiting;
using ChildGuard.Api.Security;
using ChildGuard.Infrastructure;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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

builder.Services.AddInfrastructure(builder.Configuration);
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

// ---- Rate limiting (brute-force / scan-flood protection) ----
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Partitioned per caller IP so one attacker cannot exhaust the window for
    // every other user (a single shared window would lock legitimate users out).
    options.AddPolicy(RateLimitPolicies.Auth, PerCallerFixedWindow(permitLimit: 10, window: TimeSpan.FromMinutes(1)));
    options.AddPolicy(RateLimitPolicies.Otp, PerCallerFixedWindow(permitLimit: 20, window: TimeSpan.FromMinutes(1)));
    options.AddFixedWindowLimiter(RateLimitPolicies.RfidScan, o =>
    {
        o.PermitLimit = 30;
        o.Window = TimeSpan.FromMinutes(1);
        o.QueueLimit = 0;
    });
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
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = permitLimit, Window = window, QueueLimit = 0 });

var app = builder.Build();

// ---- Pipeline ----
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
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
if (app.Configuration.GetValue<bool>("SeedDemoData"))
{
    await ChildGuard.Infrastructure.Persistence.DemoDataSeeder.SeedAsync(app.Services);
}

app.Run();
