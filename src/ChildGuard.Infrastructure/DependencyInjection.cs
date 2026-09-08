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
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
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
        services.AddScoped<IDashboardService, DashboardService>();

        var faceServiceBaseUrl = config["FaceService:BaseUrl"] ?? "http://localhost:8001";
        services.AddHttpClient<IFaceServiceClient, FaceServiceHttpClient>(client =>
        {
            client.BaseAddress = new Uri(faceServiceBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        return services;
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
