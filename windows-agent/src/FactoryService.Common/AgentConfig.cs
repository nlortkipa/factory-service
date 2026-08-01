using System.Text.Json;
using System.Text.Json.Serialization;

namespace FactoryService.Common;

public sealed record AgentConfig
{
    public string ApiBaseUrl { get; init; } = "http://localhost:5106";
    public string AgentKey { get; init; } = "demo-agent-key";
    public string DeviceId { get; init; } = "demo-workstation";
    public string ComputerName { get; init; } = Environment.MachineName;
    public string WindowsUsernameOverride { get; init; } = "";
    public int QrRefreshSeconds { get; init; } = 60;
    public int HeartbeatSeconds { get; init; } = 60;
    public int QrDisplaySeconds { get; init; } = 0;
    public bool QrTopMost { get; init; } = true;
    public bool TestMode { get; init; }

    [JsonIgnore]
    public string EffectiveWindowsUsername => string.IsNullOrWhiteSpace(WindowsUsernameOverride)
        ? $"{Environment.UserDomainName}\\{Environment.UserName}"
        : WindowsUsernameOverride;
}

public static class AgentConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    // The requested configuration is source/deployment-local. It is copied beside both executables.
    public static string ConfigDirectory => AppContext.BaseDirectory;
    public static string ConfigPath => Environment.GetEnvironmentVariable("FACTORY_SERVICE_CONFIG")
        ?? Path.Combine(ConfigDirectory, "config.json");

    public static AgentConfig LoadOrDefault()
    {
        if (!File.Exists(ConfigPath)) return new AgentConfig();
        string json = File.ReadAllText(ConfigPath);
        return JsonSerializer.Deserialize<AgentConfig>(json, JsonOptions)
            ?? throw new InvalidDataException($"Configuration file is empty: {ConfigPath}");
    }

    public static AgentConfig EnsureExists()
    {
        if (!File.Exists(ConfigPath))
        {
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(new AgentConfig(), JsonOptions));
            return new AgentConfig();
        }
        return LoadOrDefault();
    }
}
