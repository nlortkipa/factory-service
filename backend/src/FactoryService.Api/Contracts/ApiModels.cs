using FactoryService.Api.Domain;

namespace FactoryService.Api.Contracts;

public sealed record VerifyMfaRequest(Guid ChallengeId, string Code);
public sealed record AgentQrRequest(string OperatorId);
public sealed record RatingInput(ServiceType ServiceType, int Score, string? Comment);
public sealed record SurveySubmitRequest(string IdempotencyKey, IReadOnlyList<RatingInput> Ratings);
public sealed record PlantInput(string Name, string Address, string TimeZone);
public sealed record OperatorInput(string DisplayName, Guid PlantId);

public sealed class FactoryOptions
{
    public string PublicBaseUrl { get; set; } = "http://localhost:5173";
    public string QrSigningKey { get; set; } = "development-signing-key-change-me-now";
    public int QrLifetimeMinutes { get; set; } = 1;
    public int SurveyLifetimeMinutes { get; set; } = 20;
    public int RepeatWindowDays { get; set; } = 7;
    public bool DeviceCookieSecure { get; set; }
    public bool BypassAdminAuthentication { get; set; }
}
