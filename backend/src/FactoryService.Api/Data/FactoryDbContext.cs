using FactoryService.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FactoryService.Api.Data;

public sealed class FactoryDbContext(DbContextOptions<FactoryDbContext> options) : DbContext(options)
{
    public DbSet<Plant> Plants => Set<Plant>();
    public DbSet<Workstation> Workstations => Set<Workstation>();
    public DbSet<FactoryOperator> Operators => Set<FactoryOperator>();
    public DbSet<QrSession> QrSessions => Set<QrSession>();
    public DbSet<RespondentDevice> RespondentDevices => Set<RespondentDevice>();
    public DbSet<SurveyResponse> SurveyResponses => Set<SurveyResponse>();
    public DbSet<Rating> Ratings => Set<Rating>();
    public DbSet<PortalUser> PortalUsers => Set<PortalUser>();
    public DbSet<MfaChallenge> MfaChallenges => Set<MfaChallenge>();
    public DbSet<PortalSession> PortalSessions => Set<PortalSession>();
    public DbSet<RateLimitDecision> RateLimitDecisions => Set<RateLimitDecision>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ApplicationSetting> Settings => Set<ApplicationSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        if (Database.IsSqlite())
        {
            var dateTimeOffsetConverter = new ValueConverter<DateTimeOffset, long>(
                value => value.ToUnixTimeMilliseconds(),
                value => DateTimeOffset.FromUnixTimeMilliseconds(value));
            var nullableDateTimeOffsetConverter = new ValueConverter<DateTimeOffset?, long?>(
                value => value.HasValue ? value.Value.ToUnixTimeMilliseconds() : null,
                value => value.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(value.Value) : null);
            foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(x => x.GetProperties()))
            {
                if (property.ClrType == typeof(DateTimeOffset)) property.SetValueConverter(dateTimeOffsetConverter);
                else if (property.ClrType == typeof(DateTimeOffset?)) property.SetValueConverter(nullableDateTimeOffsetConverter);
            }
        }

        modelBuilder.Entity<Plant>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<Workstation>().HasIndex(x => x.DeviceId).IsUnique();
        modelBuilder.Entity<FactoryOperator>().HasIndex(x => x.AdUsername).IsUnique();
        modelBuilder.Entity<QrSession>().HasIndex(x => x.TokenHash).IsUnique();
        modelBuilder.Entity<RespondentDevice>().HasIndex(x => x.DeviceTokenHash).IsUnique();
        modelBuilder.Entity<SurveyResponse>().HasIndex(x => x.IdempotencyKey).IsUnique();
        modelBuilder.Entity<PortalUser>().HasIndex(x => x.Username).IsUnique();
        modelBuilder.Entity<PortalSession>().HasIndex(x => x.TokenHash).IsUnique();
        modelBuilder.Entity<Rating>().HasIndex(x => new { x.SurveyResponseId, x.ServiceType }).IsUnique();

        modelBuilder.Entity<Workstation>().Property(x => x.Status).HasConversion<string>();
        modelBuilder.Entity<QrSession>().Property(x => x.Status).HasConversion<string>();
        modelBuilder.Entity<Rating>().Property(x => x.ServiceType).HasConversion<string>();
        modelBuilder.Entity<PortalUser>().Property(x => x.Role).HasConversion<string>();

        modelBuilder.Entity<FactoryOperator>()
            .HasOne(x => x.Workstation).WithMany().HasForeignKey(x => x.WorkstationId)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<SurveyResponse>()
            .HasMany(x => x.Ratings).WithOne(x => x.SurveyResponse).HasForeignKey(x => x.SurveyResponseId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
