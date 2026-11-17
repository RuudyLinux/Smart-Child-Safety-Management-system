using ChildGuard.Application.Common;
using ChildGuard.Application.Interfaces;
using ChildGuard.Application.Services;
using ChildGuard.Application.Services.FaceVerification;
using ChildGuard.Infrastructure.FaceService;
using ChildGuard.Infrastructure.Identity;
using ChildGuard.Infrastructure.Notifications;
using ChildGuard.Infrastructure.Persistence;
using ChildGuard.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChildGuard.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config, bool isDevelopment)
    {
        // appsettings.json ships an empty placeholder, so a null check alone would let
        // an unconfigured deployment through to an opaque driver error.
        var connectionString = config.GetConnectionString("ChildGuardDb");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Missing ConnectionStrings:ChildGuardDb configuration.");

        // Database:Provider picks the engine (default MySql, so existing deployments are unchanged).
        // Each provider has its own migration set: MySQL's live in this assembly, PostgreSQL's in
        // ChildGuard.Infrastructure.Postgres (loaded by name so this project does not reference it).
        var provider = DatabaseProviders.Parse(config["Database:Provider"]);
        if (provider == DatabaseProvider.Postgres)
        {
            var npgsqlConnectionString = PostgresConnectionString.Normalize(connectionString);
            services.AddDbContext<ChildGuardDbContext>(options =>
                options.UseNpgsql(npgsqlConnectionString,
                    npgsql => npgsql.MigrationsAssembly("ChildGuard.Infrastructure.Postgres")));
        }
        else
        {
            // Detect once at startup. Inside the options lambda AutoDetect runs for every
            // DbContext instance — i.e. an extra connection + SELECT VERSION() per request.
            var serverVersion = ServerVersion.AutoDetect(connectionString);
            services.AddDbContext<ChildGuardDbContext>(options =>
                options.UseMySql(connectionString, serverVersion));
        }

        services.Configure<JwtSettings>(config.GetSection("Jwt"));

        services.AddScoped<IAdminRepository, AdminRepository>();
        services.AddScoped<IGuardianRepository, GuardianRepository>();
        services.AddScoped<IPasswordHasherService, PasswordHasherService>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IPasswordResetNotifier, LoggingPasswordResetNotifier>();
        RegisterEmailSender(services, config, isDevelopment);
        services.AddSingleton(config.GetSection("Otp").Get<OtpOptions>() ?? new OtpOptions());
        // School-day policy (late/absent cutoffs) lives in configuration so the
        // rule is stated once and no screen invents its own threshold.
        services.AddSingleton(config.GetSection("School").Get<SchoolOptions>() ?? new SchoolOptions());
        services.AddSingleton(config.GetSection("Rfid").Get<RfidOptions>() ?? new RfidOptions());
        services.AddSingleton(config.GetSection("Verification").Get<VerificationOptions>() ?? new VerificationOptions());
        services.AddSingleton(TimeProvider.System);
        // School calendar (time zone, "today", day boundaries). Resolving it validates
        // School:TimeZone, so a bad id fails at startup rather than on the first scan.
        services.AddSingleton<SchoolClock>();
        // School days and per-date timings (regular week + holidays/half days). Every late,
        // absent and dismissal rule reads them through ISchoolScheduleProvider.
        services.AddScoped<ISchoolCalendarRepository, SchoolCalendarRepository>();
        services.AddScoped<ISchoolScheduleProvider, SchoolScheduleProvider>();
        services.AddScoped<ISchoolCalendarService, SchoolCalendarService>();
        services.AddScoped<IAuthService, AuthService>();

        services.AddScoped<IStudentRepository, StudentRepository>();
        services.AddScoped<IStudentService, StudentService>();

        services.AddScoped<IRfidRepository, RfidRepository>();
        services.AddScoped<IJourneyRepository, JourneyRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        RegisterPushNotificationSender(services, config);
        services.AddSingleton<JourneyValidationService>();
        services.AddScoped<IRfidService, RfidService>();

        services.AddScoped<IGuardianService, GuardianService>();
        services.AddScoped<ITransportationRepository, TransportationRepository>();
        services.AddScoped<ITransportationService, TransportationService>();
        services.AddSingleton(config.GetSection("Transport").Get<TransportOptions>() ?? new TransportOptions());
        // The API registers the request's actor over this default.
        services.TryAddScoped<ICurrentActor, SystemActor>();
        services.AddScoped<IAuditTrail, EfAuditTrail>();
        services.AddScoped<IDatabaseMaintenanceService, DatabaseMaintenanceService>();
        services.AddScoped<IAuditQueryService, AuditQueryService>();
        services.AddScoped<ILifecycleRepository, LifecycleRepository>();
        services.AddScoped<IRecordLifecycleService, RecordLifecycleService>();
        services.AddScoped<IEnrollmentRepository, EnrollmentRepository>();
        services.AddScoped<IEnrollmentService, EnrollmentService>();
        services.AddScoped<IAttendanceJourneyService, AttendanceJourneyService>();
        services.AddScoped<INotificationService, NotificationService>();

        services.AddScoped<IVerificationRepository, VerificationRepository>();
        services.AddScoped<IVerificationService, VerificationService>();
        // Embeddings are stored encrypted when a key is configured. An invalid key fails here, at
        // startup, rather than on the first registration.
        var biometrics = config.GetSection("Biometrics").Get<BiometricsOptions>() ?? new BiometricsOptions();
        services.AddSingleton(biometrics);
        if (string.IsNullOrWhiteSpace(biometrics.EncryptionKey))
            services.AddSingleton<IEmbeddingProtector, NullEmbeddingProtector>();
        else
            services.AddSingleton<IEmbeddingProtector>(new AesGcmEmbeddingProtector(biometrics));
        services.AddScoped<IGuardianFaceReferenceService, GuardianFaceReferenceService>();
        services.AddSingleton(config.GetSection("FaceVerification").Get<FaceVerificationOptions>() ?? new FaceVerificationOptions());
        services.AddSingleton<ILiveChallengeGenerator, SecureLiveChallengeGenerator>();
        services.AddScoped<IVerificationSessionService, VerificationSessionService>();
        services.AddScoped<IFaceAuditLog, EfFaceAuditLog>();
        services.AddScoped<IFaceImageAnalysisService, FaceImageAnalysisService>();
        services.AddScoped<ISafetyReadService, SafetyReadService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IGuardianPortalReadService, GuardianPortalReadService>();

        // Temporary pickup guardians: separate from permanent guardians, admin-approved, QR + face checked.
        services.AddSingleton(config.GetSection("TemporaryPickup").Get<TemporaryPickupOptions>() ?? new TemporaryPickupOptions());
        services.AddScoped<ITemporaryPickupRepository, TemporaryPickupRepository>();
        services.AddSingleton<ITemporaryPickupTokenService>(sp =>
            new TemporaryPickupTokenService(sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<JwtSettings>>().Value.Secret));
        services.AddScoped<ITemporaryPickupService, TemporaryPickupService>();

        services.AddScoped<IPhotoRepository, PhotoRepository>();
        services.AddScoped<IPhotoService, PhotoService>();

        // Bulk import: one handler per category, resolved by the service from the set.
        services.AddScoped<IBulkImportRepository, BulkImportRepository>();
        services.AddScoped<ChildGuard.Application.Import.ImportHandler, ChildGuard.Application.Import.StudentImportHandler>();
        services.AddScoped<ChildGuard.Application.Import.ImportHandler, ChildGuard.Application.Import.GuardianImportHandler>();
        services.AddScoped<ChildGuard.Application.Import.ImportHandler, ChildGuard.Application.Import.DriverImportHandler>();
        services.AddScoped<ChildGuard.Application.Import.ImportHandler, ChildGuard.Application.Import.RouteImportHandler>();
        services.AddScoped<ChildGuard.Application.Import.ImportHandler, ChildGuard.Application.Import.BusImportHandler>();
        services.AddScoped<IBulkImportService, BulkImportService>();

        // Any host, port or path prefix (e.g. a tunnel at https://pc.example.ts.net/face). The trailing "/"
        // keeps the prefix: the client's calls use relative paths ("identify", not "/identify").
        var faceServiceBaseUrl = config["FaceService:BaseUrl"] is { Length: > 0 } configuredFaceUrl
            ? configuredFaceUrl.Trim()
            : "http://localhost:8001";
        if (!faceServiceBaseUrl.EndsWith('/')) faceServiceBaseUrl += "/";
        var faceServiceApiKey = config["FaceService:ApiKey"];
        void ConfigureFaceClient(HttpClient client)
        {
            client.BaseAddress = new Uri(faceServiceBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
            // Internal shared secret — face-service rejects calls without it once
            // FACE_SERVICE_KEY is set on that side. Empty by default in dev.
            if (!string.IsNullOrWhiteSpace(faceServiceApiKey))
                client.DefaultRequestHeaders.Add("X-Internal-Key", faceServiceApiKey);
        }
        services.AddHttpClient<IFaceServiceClient, FaceServiceHttpClient>(ConfigureFaceClient);
        // Same address and key, so the diagnostics check follows the exact path real face calls take.
        services.AddHttpClient<IFaceServiceHealthProbe, FaceServiceHealthProbe>(ConfigureFaceClient);

        return services;
    }

    /// <summary>
    /// Picks the email provider from <c>Email:Provider</c>: "Resend", "Smtp" or "Log". Empty means Resend when
    /// <c>Email:Resend:ApiKey</c> is set, otherwise Log. Credentials come from user-secrets or environment
    /// variables only. The login OTP is sent by the same provider to the Guardian's registered email.
    /// "Log" writes email bodies (login codes, initial passwords) to the log, so it exists only in Development.
    /// Switching provider is a configuration change: nothing in the OTP flow knows which one is in use.
    /// </summary>
    private static void RegisterEmailSender(IServiceCollection services, IConfiguration config, bool isDevelopment)
    {
        var emailOptions = config.GetSection("Email").Get<EmailOptions>() ?? new EmailOptions();
        services.AddSingleton(emailOptions);

        var apiKey = emailOptions.Resend.ApiKey;
        var provider = string.IsNullOrWhiteSpace(emailOptions.Provider)
            ? (string.IsNullOrWhiteSpace(apiKey) ? "Log" : "Resend")
            : emailOptions.Provider.Trim();

        switch (provider.ToLowerInvariant())
        {
            case "resend":
                if (string.IsNullOrWhiteSpace(apiKey))
                    throw new InvalidOperationException("Email:Provider is Resend but Email:Resend:ApiKey is not configured.");
                services.AddHttpClient<IEmailSender, ResendEmailSender>(client =>
                {
                    client.BaseAddress = new Uri("https://api.resend.com");
                    client.Timeout = TimeSpan.FromSeconds(15);
                    client.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                });
                break;

            case "smtp":
                if (string.IsNullOrWhiteSpace(emailOptions.Smtp.Host))
                    throw new InvalidOperationException("Email:Provider is Smtp but Email:Smtp:Host is not configured.");
                services.AddScoped<IEmailSender, SmtpEmailSender>();
                break;

            case "log":
                if (!isDevelopment)
                    throw new InvalidOperationException(
                        "No email provider is configured. Set Email:Provider (Resend or Smtp) with its credentials: the Log provider writes live OTP codes and passwords to the log and is allowed only in Development.");
                services.AddScoped<IEmailSender, LoggingEmailSender>();
                services.AddScoped<IOtpSender, LoggingOtpSender>();
                return;

            default:
                throw new InvalidOperationException($"Email:Provider '{emailOptions.Provider}' is not supported. Use Resend, Smtp or Log.");
        }

        services.AddScoped<IOtpSender>(sp => new EmailOtpSender(
            sp.GetRequiredService<IEmailSender>(), sp.GetRequiredService<OtpOptions>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<EmailOtpSender>>(), allowDevCodeLogging: isDevelopment));
    }

    /// <summary>
    /// Uses real Firebase Cloud Messaging only when a service-account credential
    /// path is configured (Firebase:CredentialsPath, via user-secrets/env — never
    /// hardcoded). Without it, falls back to the logging sender so the app runs
    /// fully in dev/demo without a Firebase project.
    /// </summary>
    private static void RegisterPushNotificationSender(IServiceCollection services, IConfiguration config)
    {
        var credentialsPath = config["Firebase:CredentialsPath"];
        if (string.IsNullOrWhiteSpace(credentialsPath) || !File.Exists(credentialsPath))
        {
            services.AddScoped<IPushNotificationSender, LoggingPushNotificationSender>();
            return;
        }

        if (FirebaseAdmin.FirebaseApp.DefaultInstance is null)
        {
#pragma warning disable CS0618 // FromFile is flagged in favor of a newer CredentialFactory API; still the documented Firebase Admin SDK setup path.
            FirebaseAdmin.FirebaseApp.Create(new FirebaseAdmin.AppOptions
            {
                Credential = Google.Apis.Auth.OAuth2.GoogleCredential.FromFile(credentialsPath)
            });
#pragma warning restore CS0618
        }
        services.AddScoped<IPushNotificationSender, Notifications.FcmPushNotificationSender>();
    }
}
