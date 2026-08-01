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
        if (await db.Plants.AnyAsync()) return;

        var rustavi = new Plant { Code = "RST", Name = "რუსთავის ქარხანა", Address = "რუსთავი, საქართველო" };
        var kaspi = new Plant { Code = "KSP", Name = "კასპის ქარხანა", Address = "კასპი, საქართველო" };
        var poti = new Plant { Code = "PTI", Name = "ფოთის ტერმინალი", Address = "ფოთი, საქართველო" };
        db.Plants.AddRange(rustavi, kaspi, poti);

        var workstation = new Workstation
        {
            DeviceId = "demo-workstation",
            ComputerName = Environment.MachineName,
            Plant = rustavi,
            TerminalNumber = "ფანჯარა 1",
            AgentKeyHash = SecureHash.Sha256("demo-agent-key"),
            ScreenModel = "On-screen QR preview",
            Status = WorkstationStatus.Online,
            LastSeen = DateTimeOffset.UtcNow
        };
        db.Workstations.Add(workstation);

        var operators = new[]
        {
            new FactoryOperator { AdUsername = $"{Environment.UserDomainName}\\{Environment.UserName}", DisplayName = "ნინო ბერიძე", PublicDisplayName = "ნინო ბ.", Plant = rustavi, Workstation = workstation },
            new FactoryOperator { AdUsername = Environment.UserName, DisplayName = "ნინო ბერიძე", PublicDisplayName = "ნინო ბ.", Plant = rustavi, Workstation = workstation },
            new FactoryOperator { AdUsername = "KARTULI\\g.kapanadze", DisplayName = "გიორგი კაპანაძე", PublicDisplayName = "გიორგი კ.", Plant = kaspi }
        };
        db.Operators.AddRange(operators);

        (string hash, string salt) = SecureHash.HashPassword("Admin123!");
        db.PortalUsers.Add(new PortalUser
        {
            Username = "admin",
            DisplayName = "სისტემის ადმინისტრატორი",
            PasswordHash = hash,
            PasswordSalt = salt,
            Phone = "+995555000001",
            Role = PortalRole.SystemAdministrator
        });

        db.Settings.AddRange(
            new ApplicationSetting { Key = "repeatWindowDays", Value = "7", UpdatedAt = DateTimeOffset.UtcNow },
            new ApplicationSetting { Key = "commentMaxLength", Value = "1000", UpdatedAt = DateTimeOffset.UtcNow },
            new ApplicationSetting { Key = "lowScoreThreshold", Value = "2", UpdatedAt = DateTimeOffset.UtcNow },
            new ApplicationSetting { Key = "privacyNotice", Value = "შეფასების სიხშირის და უსაფრთხოების კონტროლისთვის ინახება ანონიმური ტექნიკური ნიშანი. ტელეფონის ნომერი არ გროვდება.", UpdatedAt = DateTimeOffset.UtcNow });

        await db.SaveChangesAsync();
        await SeedResponsesAsync(db, operators[0], workstation, rustavi, kaspi, poti);
    }

    private static async Task SeedResponsesAsync(FactoryDbContext db, FactoryOperator op, Workstation workstation, params Plant[] plants)
    {
        var random = new Random(7421);
        for (int day = 30; day >= 0; day--)
        {
            int count = random.Next(1, 5);
            for (int n = 0; n < count; n++)
            {
                Plant plant = plants[random.Next(plants.Length)];
                var qr = new QrSession
                {
                    TokenHash = SecureHash.Sha256(Guid.NewGuid().ToString()), Operator = op, Plant = plant,
                    Workstation = workstation, IssuedAt = DateTimeOffset.UtcNow.AddDays(-day),
                    ExpiresAt = DateTimeOffset.UtcNow.AddDays(-day).AddMinutes(3), Nonce = SecureHash.RandomToken(12), Status = QrSessionStatus.Completed, IsTest = true
                };
                var device = new RespondentDevice { DeviceTokenHash = SecureHash.Sha256(Guid.NewGuid().ToString()), FirstSeen = qr.IssuedAt, LastSeen = qr.IssuedAt };
                var response = new SurveyResponse
                {
                    QrSession = qr, RespondentDevice = device, SubmittedAt = qr.IssuedAt.AddMinutes(1), Plant = plant,
                    Operator = op, Workstation = workstation, IdempotencyKey = Guid.NewGuid().ToString(), IsTest = false
                };
                foreach (ServiceType type in Enum.GetValues<ServiceType>())
                {
                    int score = Math.Clamp((int)Math.Round(3.6 + random.NextDouble() * 1.6), 1, 5);
                    response.Ratings.Add(new Rating { ServiceType = type, Score = score, Comment = score <= 2 ? "მომსახურება უფრო სწრაფი უნდა იყოს" : "", AttributionType = type == ServiceType.Sales ? "Operator" : "Plant", OperatorId = type == ServiceType.Sales ? op.Id : null, PlantId = plant.Id });
                }
                db.SurveyResponses.Add(response);
            }
        }
        await db.SaveChangesAsync();
    }
}
