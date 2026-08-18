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

    public static string MachineDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        ApplicationDirectoryName);

    public static string ConfigPath => Environment.GetEnvironmentVariable("FACTORY_SERVICE_CONFIG")
        ?? Path.Combine(ConfigDirectory, "config.json");

    public static AgentConfig LoadOrDefault()
    {
        if (!File.Exists(ConfigPath)) return new AgentConfig();
        return Deserialize(ConfigPath);
    }

    public static AgentConfig EnsureExists()
    {
        if (!File.Exists(ConfigPath))
        {
            AgentConfig config = new();

            string? directory = Path.GetDirectoryName(Path.GetFullPath(ConfigPath));
            if (directory is not null) Directory.CreateDirectory(directory);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, JsonOptions));
            return config;
        }
        return LoadOrDefault();
    }

    private static AgentConfig Deserialize(string path)
    {
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AgentConfig>(json, JsonOptions)
            ?? throw new InvalidDataException($"Configuration file is empty: {path}");
    }
}
