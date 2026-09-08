using System.Text;
using ChildGuard.Infrastructure;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
});

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AdminOnly", p => p.RequireRole("Admin"))
    .AddPolicy("GuardianOnly", p => p.RequireRole("Guardian"));

builder.Services.AddCors(options =>
{
    options.AddPolicy("AdminWebAndApp", policy =>
    {
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
    });
});

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

app.MapControllers();

await ChildGuard.Infrastructure.Persistence.DbSeeder.SeedAsync(app.Services);
if (app.Configuration.GetValue<bool>("SeedDemoData"))
{
    await ChildGuard.Infrastructure.Persistence.DemoDataSeeder.SeedAsync(app.Services);
}

app.Run();
