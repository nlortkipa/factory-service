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
    private const string ApplicationDirectoryName = "Factory Service Agent";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string ConfigDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ApplicationDirectoryName);

    public static string MachineDataDirectory => Environment.GetEnvironmentVariable("FACTORY_SERVICE_MACHINE_DATA")
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            ApplicationDirectoryName);

    public static string ConfigPath => Environment.GetEnvironmentVariable("FACTORY_SERVICE_CONFIG")
        ?? Path.Combine(ConfigDirectory, "config.json");

    public static AgentConfig LoadOrDefault()
    {
        AgentConfig config = File.Exists(ConfigPath) ? Deserialize(ConfigPath) : new AgentConfig();
        return ApplyEnvironmentOverrides(config);
    }

    public static AgentConfig EnsureExists()
    {
        if (!File.Exists(ConfigPath))
        {
            AgentConfig config = new();

            string? directory = Path.GetDirectoryName(Path.GetFullPath(ConfigPath));
            if (directory is not null) Directory.CreateDirectory(directory);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, JsonOptions));
            return ApplyEnvironmentOverrides(config);
        }
        return LoadOrDefault();
    }

    private static AgentConfig ApplyEnvironmentOverrides(AgentConfig config)
    {
        string? apiBaseUrl = Environment.GetEnvironmentVariable("FACTORY_SERVICE_API_BASE_URL");
        string? operatorId = Environment.GetEnvironmentVariable("FACTORY_SERVICE_OPERATOR_ID");
        return config with
        {
            ApiBaseUrl = string.IsNullOrWhiteSpace(apiBaseUrl) ? config.ApiBaseUrl : apiBaseUrl.Trim(),
            OperatorId = string.IsNullOrWhiteSpace(operatorId) ? config.OperatorId : operatorId.Trim()
        };
    }

    private static AgentConfig Deserialize(string path)
    {
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AgentConfig>(json, JsonOptions)
            ?? throw new InvalidDataException($"Configuration file is empty: {path}");
    }
}
