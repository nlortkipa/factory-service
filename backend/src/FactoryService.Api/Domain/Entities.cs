using System.ComponentModel.DataAnnotations;

namespace FactoryService.Api.Domain;

public enum PortalRole { SystemAdministrator, PlantAdministrator, ReportViewer, Auditor }
public enum ServiceType { Sales, Scale, Loading }
public enum WorkstationStatus { Online, Warning, Offline }
public enum QrSessionStatus { Active, Completed, Expired, Revoked }

public sealed class Plant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(32)] public required string Code { get; set; }
    [MaxLength(200)] public required string Name { get; set; }
    [MaxLength(500)] public string Address { get; set; } = "";
    [MaxLength(100)] public string TimeZone { get; set; } = "Asia/Tbilisi";
    public bool IsActive { get; set; } = true;
}

public sealed class Workstation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(100)] public required string DeviceId { get; set; }
    [MaxLength(100)] public required string ComputerName { get; set; }
    public Guid PlantId { get; set; }
    public Plant? Plant { get; set; }
    [MaxLength(50)] public string TerminalNumber { get; set; } = "";
    [MaxLength(100)] public string ScreenModel { get; set; } = "On-screen preview";
    [MaxLength(100)] public string Serial { get; set; } = "";
    [MaxLength(40)] public string ClientVersion { get; set; } = "";
    [MaxLength(128)] public required string AgentKeyHash { get; set; }
    public DateTimeOffset? LastSeen { get; set; }
    public DateTimeOffset? LastQrRenderedAt { get; set; }
    [MaxLength(160)] public string ActiveWindowsUser { get; set; } = "";
    [MaxLength(80)] public string LastErrorCode { get; set; } = "";
    public WorkstationStatus Status { get; set; } = WorkstationStatus.Offline;
    public bool IsActive { get; set; } = true;
}

public sealed class FactoryOperator
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(160)] public required string AdUsername { get; set; }
    [MaxLength(200)] public required string DisplayName { get; set; }
    public Guid PlantId { get; set; }
    public Plant? Plant { get; set; }
    public Guid? WorkstationId { get; set; }
    public Workstation? Workstation { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset EffectiveFrom { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EffectiveTo { get; set; }
}

public sealed class QrSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(128)] public required string TokenHash { get; set; }
    public Guid OperatorId { get; set; }
    public FactoryOperator? Operator { get; set; }
    public Guid PlantId { get; set; }
    public Plant? Plant { get; set; }
    public Guid WorkstationId { get; set; }
    public Workstation? Workstation { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? OpenedAt { get; set; }
    public DateTimeOffset? SurveyExpiresAt { get; set; }
    [MaxLength(80)] public required string Nonce { get; set; }
    public QrSessionStatus Status { get; set; } = QrSessionStatus.Active;
    public bool IsTest { get; set; }
}

public sealed class RespondentDevice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(128)] public required string DeviceTokenHash { get; set; }
    public DateTimeOffset FirstSeen { get; set; }
    public DateTimeOffset LastSeen { get; set; }
    [MaxLength(32)] public string ConsentVersion { get; set; } = "1.0";
    [MaxLength(500)] public string RiskFlags { get; set; } = "";
}

public sealed class SurveyResponse
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QrSessionId { get; set; }
    public QrSession? QrSession { get; set; }
    public Guid RespondentDeviceId { get; set; }
    public RespondentDevice? RespondentDevice { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public Guid PlantId { get; set; }
    public Plant? Plant { get; set; }
    public Guid OperatorId { get; set; }
    public FactoryOperator? Operator { get; set; }
    public Guid WorkstationId { get; set; }
    public Workstation? Workstation { get; set; }
    [MaxLength(100)] public required string IdempotencyKey { get; set; }
    public bool IsTest { get; set; }
    [MaxLength(500)] public string RiskFlags { get; set; } = "";
    public ICollection<Rating> Ratings { get; set; } = [];
}

public sealed class Rating
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SurveyResponseId { get; set; }
    public SurveyResponse? SurveyResponse { get; set; }
    public ServiceType ServiceType { get; set; }
    public int Score { get; set; }
    [MaxLength(1000)] public string Comment { get; set; } = "";
    [MaxLength(20)] public string AttributionType { get; set; } = "Plant";
    public Guid? OperatorId { get; set; }
    public Guid PlantId { get; set; }
}

public sealed class PortalUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(100)] public required string Username { get; set; }
    [MaxLength(200)] public required string DisplayName { get; set; }
    [MaxLength(128)] public required string PasswordHash { get; set; }
    [MaxLength(128)] public required string PasswordSalt { get; set; }
    [MaxLength(30)] public required string Phone { get; set; }
    public PortalRole Role { get; set; }
    public Guid? PlantId { get; set; }
    public Plant? Plant { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class MfaChallenge
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public PortalUser? User { get; set; }
    [MaxLength(30)] public required string PhoneMasked { get; set; }
    [MaxLength(128)] public required string CodeHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public int Attempts { get; set; }
    [MaxLength(30)] public string SendStatus { get; set; } = "LoggedToTerminal";
    public DateTimeOffset? VerifiedAt { get; set; }
}

public sealed class PortalSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public PortalUser? User { get; set; }
    [MaxLength(128)] public required string TokenHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset LastSeen { get; set; }
}

public sealed class RateLimitDecision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(128)] public required string KeyHash { get; set; }
    [MaxLength(50)] public required string Scope { get; set; }
    public DateTimeOffset WindowStart { get; set; }
    public DateTimeOffset WindowEnd { get; set; }
    [MaxLength(20)] public required string Decision { get; set; }
    [MaxLength(300)] public required string Reason { get; set; }
}

public sealed class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ActorId { get; set; }
    [MaxLength(100)] public required string Action { get; set; }
    [MaxLength(100)] public required string ObjectType { get; set; }
    [MaxLength(100)] public required string ObjectId { get; set; }
    public string BeforeJson { get; set; } = "";
    public string AfterJson { get; set; } = "";
    [MaxLength(80)] public string IpAddress { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; }
    [MaxLength(80)] public required string CorrelationId { get; set; }
}

public sealed class ApplicationSetting
{
    [Key, MaxLength(100)] public required string Key { get; set; }
    [MaxLength(2000)] public required string Value { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
