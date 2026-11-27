using System.Text.Json;

namespace FactoryService.Common;

public sealed record ClientMaintenance(string LauncherName, string PayloadUrl)
{
    public const string DirectoryName = "Maintenance";
    public const string ManifestName = "launcher.json";

    public string LauncherPath(string installDirectory) => Path.Combine(installDirectory, DirectoryName, LauncherName);

    public static ClientMaintenance? Read(string installDirectory)
    {
        try
        {
            string manifest = Path.Combine(installDirectory, DirectoryName, ManifestName);
            if (!File.Exists(manifest)) return null;
            var maintenance = JsonSerializer.Deserialize<ClientMaintenance>(File.ReadAllText(manifest));
            return maintenance is { LauncherName.Length: > 0 } &&
                Path.GetFileName(maintenance.LauncherName) == maintenance.LauncherName &&
                maintenance.LauncherName.StartsWith("FactoryService.Setup-", StringComparison.Ordinal) &&
                maintenance.LauncherName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                Uri.TryCreate(maintenance.PayloadUrl, UriKind.Absolute, out _) &&
                File.Exists(maintenance.LauncherPath(installDirectory))
                ? maintenance : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }
}
