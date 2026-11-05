using ChildGuard.Application.Common;
using ChildGuard.Application.Interfaces;
using ChildGuard.Application.Services;
using ChildGuard.Infrastructure.FaceService;
using ChildGuard.Infrastructure.Identity;
using ChildGuard.Infrastructure.Notifications;
using ChildGuard.Infrastructure.Persistence;
using ChildGuard.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ChildGuard.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config, bool isDevelopment)
    {
        var connectionString = config.GetConnectionString("ChildGuardDb")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:ChildGuardDb configuration.");

        services.AddDbContext<ChildGuardDbContext>(options =>
            options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

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
        services.AddSingleton(TimeProvider.System);
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
        services.AddScoped<IAttendanceJourneyService, AttendanceJourneyService>();
        services.AddScoped<INotificationService, NotificationService>();

        services.AddScoped<IVerificationRepository, VerificationRepository>();
        services.AddScoped<IVerificationService, VerificationService>();
        services.AddScoped<ISafetyReadService, SafetyReadService>();
        services.AddScoped<IDashboardService, DashboardService>();

        services.AddScoped<IPhotoRepository, PhotoRepository>();
        services.AddScoped<IPhotoService, PhotoService>();

        var faceServiceBaseUrl = config["FaceService:BaseUrl"] ?? "http://localhost:8001";
        var faceServiceApiKey = config["FaceService:ApiKey"];
        services.AddHttpClient<IFaceServiceClient, FaceServiceHttpClient>(client =>
        {
            client.BaseAddress = new Uri(faceServiceBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
            // Internal shared secret — face-service rejects calls without it once
            // FACE_SERVICE_KEY is set on that side. Empty by default in dev.
            if (!string.IsNullOrWhiteSpace(faceServiceApiKey))
                client.DefaultRequestHeaders.Add("X-Internal-Key", faceServiceApiKey);
        });

        return services;
    }

    /// <summary>
    /// Sends real email through Resend only when an API key is configured
    /// (Email:Resend:ApiKey, via user-secrets/env — never hardcoded). Without one,
    /// falls back to the logging sender so the app runs fully in dev — Development
    /// only, since that sender writes live OTP codes and credentials to the log.
    /// The login OTP rides the same channel: there is no SMS gateway in the
    /// approved stack, so it goes to the Guardian's registered email address, and
    /// only drops to a log line when no provider is configured at all.
    /// </summary>
    private static void RegisterEmailSender(IServiceCollection services, IConfiguration config, bool isDevelopment)
    {
        var emailOptions = config.GetSection("Email").Get<EmailOptions>() ?? new EmailOptions();
        services.AddSingleton(emailOptions);

        var apiKey = config["Email:Resend:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            if (!isDevelopment)
                throw new InvalidOperationException(
                    "Email:Resend:ApiKey must be configured outside Development — the fallback sender logs live OTP codes and passwords in plaintext.");

            services.AddScoped<IEmailSender, LoggingEmailSender>();
            services.AddScoped<IOtpSender, LoggingOtpSender>();
            return;
        }

        services.AddHttpClient<IEmailSender, ResendEmailSender>(client =>
        {
            client.BaseAddress = new Uri("https://api.resend.com");
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        });
        services.AddScoped<IOtpSender, EmailOtpSender>();
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
