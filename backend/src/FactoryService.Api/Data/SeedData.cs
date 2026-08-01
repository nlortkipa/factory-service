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

        // Keep an initial administrator available, but leave operational data
        // (plants, operators, workstations, and responses) for the administrator to create.
        if (await db.PortalUsers.AnyAsync(x => x.Username == "admin")) return;

        (string hash, string salt) = SecureHash.HashPassword("Admin123!");
        db.PortalUsers.Add(new PortalUser
        {
            Username = "admin",
            DisplayName = "System Administrator",
            PasswordHash = hash,
            PasswordSalt = salt,
            Phone = "+995555000001",
            Role = PortalRole.SystemAdministrator
        });
        await db.SaveChangesAsync();
    }
}
