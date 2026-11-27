using System.Text.Json;
using System.Text.Json.Serialization;

namespace FactoryService.Common;

public sealed record AgentConfig
{
    [JsonPropertyName("apiBaseUrl")]
    public string ApiBaseUrl { get; init; } = "https://factory-service.vercel.app";

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

    public static string UserConfigPath => Path.Combine(ConfigDirectory, "config.json");

    public static string ConfigPath => Environment.GetEnvironmentVariable("FACTORY_SERVICE_CONFIG") ?? UserConfigPath;

    // Installed companions never accept a shared machine/operator override. Development
    // launches keep the existing environment overrides for the local dev launcher.
    public static AgentConfig LoadUserConfiguration() => Load(UserConfigPath, applyOverrides: false);
    public static AgentConfig EnsureUserConfiguration() => Ensure(UserConfigPath, applyOverrides: false);

    public static AgentConfig LoadOrDefault()
    {
        return Load(ConfigPath, applyOverrides: true);
    }

    public static AgentConfig EnsureExists()
    {
        return Ensure(ConfigPath, applyOverrides: true);
    }

    private static AgentConfig Load(string path, bool applyOverrides)
    {
        AgentConfig config = File.Exists(path) ? Deserialize(path) : new AgentConfig();
        return applyOverrides ? ApplyEnvironmentOverrides(config) : config;
    }

    private static AgentConfig Ensure(string path, bool applyOverrides)
    {
        if (!File.Exists(path))
        {
            AgentConfig config = new();

            string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (directory is not null) Directory.CreateDirectory(directory);
            // Multiple startup paths may race; never overwrite a config created by another.
            try
            {
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                JsonSerializer.Serialize(stream, config, JsonOptions);
            }
            catch (IOException) when (File.Exists(path)) { return Load(path, applyOverrides); }
            return applyOverrides ? ApplyEnvironmentOverrides(config) : config;
        }
        return Load(path, applyOverrides);
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
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return JsonSerializer.Deserialize<AgentConfig>(reader.ReadToEnd(), JsonOptions)
            ?? throw new InvalidDataException($"Configuration file is empty: {path}");
    }
}
