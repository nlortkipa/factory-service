using FactoryService.Api.Domain;
using FactoryService.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace FactoryService.Api.Data;

public static class SeedData
{
    public static async Task InitializeAsync(FactoryDbContext db)
    {
        if (db.Database.IsSqlite()) await db.Database.EnsureCreatedAsync();
        else await db.Database.MigrateAsync();

        string? configuredAdminPhone = Environment.GetEnvironmentVariable("ADMIN_PHONE")?.Trim();
        string adminPhone = string.IsNullOrWhiteSpace(configuredAdminPhone) ? "+995555000001" : configuredAdminPhone;

        // Keep an initial administrator available, but leave operational data
        // (plants, operators, and responses) for the administrator to create.
        PortalUser? existingAdministrator = await db.PortalUsers.SingleOrDefaultAsync(x => x.Username == "admin");
        if (existingAdministrator is not null)
        {
            if (!string.IsNullOrWhiteSpace(configuredAdminPhone) && existingAdministrator.Phone != configuredAdminPhone)
            {
                existingAdministrator.Phone = configuredAdminPhone;
                await db.SaveChangesAsync();
            }
            return;
        }

        (string hash, string salt) = SecureHash.HashPassword("Admin123!");
        db.PortalUsers.Add(new PortalUser
        {
            Username = "admin",
            DisplayName = "System Administrator",
            PasswordHash = hash,
            PasswordSalt = salt,
            Phone = adminPhone,
            Role = PortalRole.SystemAdministrator
        });
        await db.SaveChangesAsync();
    }
}
