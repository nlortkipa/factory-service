using System.Text.Json;
using System.Text.Json.Serialization;

namespace FactoryService.Common;

public sealed record AgentConfig
{
    [JsonPropertyName("apiBaseUrl")]
    public string ApiBaseUrl { get; init; } = "http://localhost:5000";

    [JsonPropertyName("operatorId")]
    public string OperatorId { get; init; } = "";
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
