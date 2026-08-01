using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FactoryService.Api.Contracts;
using FactoryService.Api.Data;
using FactoryService.Api.Endpoints;
using FactoryService.Api.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<FactoryOptions>(builder.Configuration.GetSection("FactoryService"));
string provider = builder.Configuration["Database:Provider"] ?? "SqlServer";
string connection = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");
builder.Services.AddDbContext<FactoryDbContext>(options =>
{
    if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase)) options.UseSqlite(connection);
    else options.UseSqlServer(connection, sql => sql.EnableRetryOnFailure());
});

builder.Services.AddSingleton<QrTokenService>();
builder.Services.AddAuthentication("Portal")
    .AddScheme<AuthenticationSchemeOptions, PortalAuthenticationHandler>("Portal", _ => { });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Reports", policy => policy.RequireRole("SystemAdministrator", "PlantAdministrator", "ReportViewer", "Auditor"))
    .AddPolicy("Manage", policy => policy.RequireRole("SystemAdministrator", "PlantAdministrator"))
    .AddPolicy("SystemAdmin", policy => policy.RequireRole("SystemAdministrator"));
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins("http://localhost:5173", "https://localhost:5173")
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
builder.Services.AddRateLimiter(options => options.AddPolicy("public", context =>
    RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

WebApplication app = builder.Build();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
    await next();
});
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTimeOffset.UtcNow }));
app.MapFactoryServiceApi();

using (IServiceScope scope = app.Services.CreateScope())
{
    await SeedData.InitializeAsync(scope.ServiceProvider.GetRequiredService<FactoryDbContext>());
}

await app.RunAsync();

public partial class Program;
