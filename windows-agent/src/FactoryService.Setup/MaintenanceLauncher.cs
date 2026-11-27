using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using FactoryService.Common;

namespace FactoryService.Setup;

internal static class MaintenanceLauncher
{
    private const int DeleteOnRestart = 4;
    public static ClientMaintenance? Current => ClientMaintenance.Read(InstalledClient.InstallDirectory);

    public static void Install(string payloadUrl)
    {
        string currentExecutable = Environment.ProcessPath!;
        var existing = Current;
        if (existing is not null && existing.PayloadUrl == payloadUrl &&
            string.Equals(existing.LauncherPath(InstalledClient.InstallDirectory), currentExecutable, StringComparison.OrdinalIgnoreCase))
            return;

        string directory = Path.Combine(InstalledClient.InstallDirectory, ClientMaintenance.DirectoryName);
        Directory.CreateDirectory(directory);
        var maintenance = new ClientMaintenance($"FactoryService.Setup-{Guid.NewGuid():N}.exe", payloadUrl);
        string launcher = maintenance.LauncherPath(InstalledClient.InstallDirectory);
        string manifest = Path.Combine(directory, ClientMaintenance.ManifestName);
        string temporaryManifest = launcher + ".json";
        try
        {
            // The shared launcher must carry no operator-specific data from a portal download.
            using (var source = File.OpenRead(currentExecutable))
            using (var destination = File.Open(launcher, FileMode.CreateNew))
                ProvisioningReader.CopyTemplate(source, destination);
            File.WriteAllText(temporaryManifest, JsonSerializer.Serialize(maintenance));
            InstalledClient.RetrySharingViolation(() => File.Move(temporaryManifest, manifest, overwrite: true));
        }
        catch
        {
            File.Delete(launcher);
            File.Delete(temporaryManifest);
            throw;
        }
        foreach (string old in Directory.EnumerateFiles(directory, "FactoryService.Setup-*.exe"))
        {
            if (string.Equals(old, launcher, StringComparison.OrdinalIgnoreCase)) continue;
            try { File.Delete(old); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    public static void RemoveInstalledFiles()
    {
        string install = InstalledClient.InstallDirectory;
        if (!Directory.Exists(install)) return;
        string maintenance = Path.Combine(install, ClientMaintenance.DirectoryName);
        foreach (string file in Directory.EnumerateFiles(install))
            InstalledClient.RetrySharingViolation(() => File.Delete(file));
        foreach (string directory in Directory.EnumerateDirectories(install))
        {
            if (!string.Equals(directory, maintenance, StringComparison.OrdinalIgnoreCase))
                InstalledClient.RetrySharingViolation(() => Directory.Delete(directory, recursive: true));
        }
        if (Directory.Exists(maintenance))
        {
            foreach (string file in Directory.EnumerateFiles(maintenance))
            {
                try { File.Delete(file); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // These names are unique per install, so deferred cleanup cannot remove a future launcher.
                    if (!MoveFileEx(file, null, DeleteOnRestart))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), file);
                }
            }
            if (Directory.EnumerateFileSystemEntries(maintenance).Any()) return;
            Directory.Delete(maintenance);
        }
        Directory.Delete(install);
    }

    [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string existing, string? destination, int flags);
}
