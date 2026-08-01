using System.Text.Json;
using System.Text.Json.Serialization;

namespace FactoryService.Common;

public sealed record AgentConfig
{
    public string ApiBaseUrl { get; init; } = "https://api.example.invalid";
    public string FactoryId { get; init; } = "factory-not-configured";
    public string DeviceId { get; init; } = Environment.MachineName;
    public string TestQrPayload { get; init; } = "https://www.youtube.com/";
    public int QrDisplaySeconds { get; init; } = 0;
    public bool QrTopMost { get; init; } = true;
}

public static class AgentConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string ConfigDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "FactoryService");

    public static string ConfigPath => Path.Combine(ConfigDirectory, "config.json");

    public static AgentConfig LoadOrDefault()
    {
        if (!File.Exists(ConfigPath))
        {
            return new AgentConfig();
        }

        string json = File.ReadAllText(ConfigPath);
        return JsonSerializer.Deserialize<AgentConfig>(json, JsonOptions)
            ?? throw new InvalidDataException($"Configuration file is empty: {ConfigPath}");
    }

    public static AgentConfig EnsureExists()
    {
        Directory.CreateDirectory(ConfigDirectory);

        if (!File.Exists(ConfigPath))
        {
            AgentConfig defaults = new();
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(defaults, JsonOptions));
            return defaults;
        }

        return LoadOrDefault();
    }
}
