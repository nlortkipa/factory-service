using FactoryService.Api.Data;
using Microsoft.EntityFrameworkCore;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: FactoryService.DataMigration <sqlite-file> <sql-server-connection-string>");
    return 2;
}

string sqliteFile = Path.GetFullPath(args[0]);
if (!File.Exists(sqliteFile))
{
    Console.Error.WriteLine($"SQLite source was not found: {sqliteFile}");
    return 3;
}

var sourceOptions = new DbContextOptionsBuilder<FactoryDbContext>()
    .UseSqlite($"Data Source={sqliteFile}")
    .Options;
var targetOptions = new DbContextOptionsBuilder<FactoryDbContext>()
    .UseSqlServer(args[1])
    .Options;

await using var source = new FactoryDbContext(sourceOptions);
await using var target = new FactoryDbContext(targetOptions);

await target.Database.MigrateAsync();
if (await target.Plants.AnyAsync() || await target.SurveyResponses.AnyAsync())
{
    Console.Error.WriteLine("The target database already contains application data; migration was cancelled.");
    return 4;
}

await using var transaction = await target.Database.BeginTransactionAsync();

await CopyAsync(source.Plants, target.Plants);
await CopyAsync(source.Workstations, target.Workstations);
await CopyAsync(source.Operators, target.Operators);
await CopyAsync(source.PortalUsers, target.PortalUsers);
await CopyAsync(source.QrSessions, target.QrSessions);
await CopyAsync(source.RespondentDevices, target.RespondentDevices);
await CopyAsync(source.SurveyResponses, target.SurveyResponses);
await CopyAsync(source.Ratings, target.Ratings);
await CopyAsync(source.MfaChallenges, target.MfaChallenges);
await CopyAsync(source.PortalSessions, target.PortalSessions);
await CopyAsync(source.RateLimitDecisions, target.RateLimitDecisions);
await CopyAsync(source.AuditLogs, target.AuditLogs);
await CopyAsync(source.Settings, target.Settings);

await transaction.CommitAsync();

Console.WriteLine("Migration completed.");
Console.WriteLine($"Plants: {await target.Plants.CountAsync()}");
Console.WriteLine($"Workstations: {await target.Workstations.CountAsync()}");
Console.WriteLine($"Operators: {await target.Operators.CountAsync()}");
Console.WriteLine($"Survey responses: {await target.SurveyResponses.CountAsync()}");
Console.WriteLine($"Ratings: {await target.Ratings.CountAsync()}");
Console.WriteLine($"Portal users: {await target.PortalUsers.CountAsync()}");
return 0;

async Task CopyAsync<TEntity>(DbSet<TEntity> sourceSet, DbSet<TEntity> targetSet) where TEntity : class
{
    List<TEntity> rows = await sourceSet.AsNoTracking().ToListAsync();
    if (rows.Count == 0) return;
    targetSet.AddRange(rows);
    await target.SaveChangesAsync();
    target.ChangeTracker.Clear();
    Console.WriteLine($"Copied {rows.Count} row(s) into {typeof(TEntity).Name}.");
}
