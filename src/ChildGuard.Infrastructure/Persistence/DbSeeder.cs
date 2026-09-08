using ChildGuard.Application.Interfaces;
using ChildGuard.Domain.Entities;
using ChildGuard.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ChildGuard.Infrastructure.Persistence;

/// <summary>
/// Seeds the first Admin account from configuration (user-secrets/env), since the
/// spec has no admin self-registration flow — an Admin must be provisioned to log
/// in at all. Idempotent: only inserts when the Admins table is empty.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChildGuardDbContext>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasherService>();

        if (!await db.Admins.AnyAsync())
        {
            var email = config["SeedAdmin:Email"];
            var password = config["SeedAdmin:Password"];
            var fullName = config["SeedAdmin:FullName"] ?? "System Administrator";

            if (!string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(password))
            {
                db.Admins.Add(new Admin
                {
                    FullName = fullName,
                    Email = email,
                    PasswordHash = hasher.Hash(password),
                    Status = RecordStatus.Active
                });
            }
        }

        if (!await db.RfidDevices.AnyAsync())
        {
            db.RfidDevices.Add(new RfidDevice
            {
                DeviceLabel = "RFID-001",
                ConnectionStatus = RfidConnectionStatus.Disconnected
            });
        }

        await db.SaveChangesAsync();
    }
}
