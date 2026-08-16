using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FactoryService.Api.Contracts;
using FactoryService.Api.Data;
using FactoryService.Api.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FactoryService.Api.Security;

public static class SecureHash
{
    public static string RandomToken(int bytes = 32) => Base64Url(RandomNumberGenerator.GetBytes(bytes));
    public static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string Hmac(string value, string key) => Base64Url(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(value)));

    public static (string Hash, string Salt) HashPassword(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210_000, HashAlgorithmName.SHA256, 32);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    public static bool VerifyPassword(string password, string hash, string salt)
    {
        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(salt), 210_000, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(actual, Convert.FromBase64String(hash));
    }

    public static bool FixedEquals(string left, string right)
    {
        byte[] a = Encoding.UTF8.GetBytes(left);
        byte[] b = Encoding.UTF8.GetBytes(right);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed record QrTokenPayload(Guid SessionId, Guid OperatorId, Guid PlantId, long IssuedAt, long ExpiresAt, string Nonce);

public sealed class QrTokenService(IOptions<FactoryOptions> options)
{
    private readonly FactoryOptions _options = options.Value;

    public string Create(QrTokenPayload payload)
    {
        string body = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(payload)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{body}.{SecureHash.Hmac(body, _options.QrSigningKey)}";
    }

    public bool TryValidate(string token, out QrTokenPayload? payload, bool requireUnexpired = true)
    {
        payload = null;
        string[] parts = token.Split('.', 2);
        if (parts.Length != 2 || !SecureHash.FixedEquals(SecureHash.Hmac(parts[0], _options.QrSigningKey), parts[1])) return false;
        try
        {
            string base64 = parts[0].Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');
            payload = JsonSerializer.Deserialize<QrTokenPayload>(Convert.FromBase64String(base64));
            return payload is not null && (!requireUnexpired || payload.ExpiresAt > DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }
        catch { return false; }
    }
}

public sealed class PortalAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    FactoryDbContext db) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();
        string hash = SecureHash.Sha256(header[7..].Trim());
        PortalSession? session = await db.PortalSessions.Include(x => x.User)
            .SingleOrDefaultAsync(x => x.TokenHash == hash);
        if (session is not null && session.ExpiresAt <= DateTimeOffset.UtcNow) session = null;
        if (session?.User is not { IsActive: true } user) return AuthenticateResult.Fail("Invalid or expired session.");

        if (DateTimeOffset.UtcNow - session.LastSeen > TimeSpan.FromMinutes(20))
        {
            db.PortalSessions.Remove(session);
            await db.SaveChangesAsync();
            return AuthenticateResult.Fail("Session timed out.");
        }

        session.LastSeen = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.GivenName, user.DisplayName),
            new(ClaimTypes.Role, user.Role.ToString())
        };
        if (user.PlantId is Guid plantId) claims.Add(new("plant_id", plantId.ToString()));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}

public static class ClaimsExtensions
{
    public static Guid UserId(this ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public static Guid? PlantScope(this ClaimsPrincipal user) => Guid.TryParse(user.FindFirstValue("plant_id"), out Guid id) ? id : null;
    public static bool IsSystemAdmin(this ClaimsPrincipal user) => user.IsInRole(PortalRole.SystemAdministrator.ToString());
}
