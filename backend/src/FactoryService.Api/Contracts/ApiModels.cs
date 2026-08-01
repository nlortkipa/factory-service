using FactoryService.Api.Domain;

namespace FactoryService.Api.Contracts;

public sealed record LoginRequest(string Username, string Password);
public sealed record VerifyMfaRequest(Guid ChallengeId, string Code);
public sealed record AgentQrRequest(string DeviceId, string ComputerName, string WindowsUsername, string ClientVersion, bool IsTest = false);
public sealed record AgentHeartbeatRequest(string DeviceId, string ComputerName, string WindowsUsername, string ClientVersion, string Status, string? ErrorCode, DateTimeOffset? LastRenderAt);
public sealed record RatingInput(ServiceType ServiceType, int Score, string? Comment);
public sealed record SurveySubmitRequest(string IdempotencyKey, IReadOnlyList<RatingInput> Ratings);
public sealed record PlantInput(string Code, string Name, string Address, string TimeZone, bool IsActive);
public sealed record OperatorInput(string AdUsername, string DisplayName, string PublicDisplayName, Guid PlantId, Guid? WorkstationId, bool IsActive);
public sealed record WorkstationInput(string DeviceId, string ComputerName, Guid PlantId, string TerminalNumber, string ScreenModel, string Serial, string AgentKey, bool IsActive);

public sealed class FactoryOptions
{
    public string PublicBaseUrl { get; set; } = "http://localhost:5173";
    public string QrSigningKey { get; set; } = "development-signing-key-change-me-now";
    public int QrLifetimeMinutes { get; set; } = 3;
    public int SurveyLifetimeMinutes { get; set; } = 20;
    public int RepeatWindowDays { get; set; } = 7;
    public bool DeviceCookieSecure { get; set; }
}
