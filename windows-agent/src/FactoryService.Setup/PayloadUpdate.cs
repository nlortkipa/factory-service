using System.IO.Compression;

namespace FactoryService.Setup;

internal sealed class PayloadUpdate(string installDirectory)
{
    private readonly string _workDirectory = Path.Combine(installDirectory, ".setup-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _replaced = [];
    private string Incoming => Path.Combine(_workDirectory, "incoming");
    private string Previous => Path.Combine(_workDirectory, "previous");
    public bool HasChanges => _replaced.Count > 0;
    public string RecoveryDirectory => _workDirectory;

    public void Prepare(string archive)
    {
        ZipFile.ExtractToDirectory(archive, Incoming);
        if (!File.Exists(Path.Combine(Incoming, InstalledClient.ServiceFileName)) ||
            !File.Exists(Path.Combine(Incoming, InstalledClient.CompanionFileName)))
            throw new InvalidDataException("ჩამოტვირთული კლიენტის პაკეტი არასრულია.");
    }

    public void Apply()
    {
        foreach (string source in Directory.GetFiles(Incoming, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(Incoming, source);
            string destination = Path.Combine(installDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (File.Exists(destination))
            {
                string backup = Path.Combine(Previous, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                InstalledClient.RetrySharingViolation(() => File.Copy(destination, backup, overwrite: true));
            }

            // Replacing a single file keeps the installed directory and its permissions in place.
            InstalledClient.RetrySharingViolation(() => File.Move(source, destination, overwrite: true), retryAccessDenied: true);
            _replaced.Add(relative);
        }
    }

    public void Restore()
    {
        for (int i = _replaced.Count - 1; i >= 0; i--)
        {
            string relative = _replaced[i];
            string destination = Path.Combine(installDirectory, relative);
            string backup = Path.Combine(Previous, relative);
            if (File.Exists(backup))
                InstalledClient.RetrySharingViolation(() => File.Move(backup, destination, overwrite: true), retryAccessDenied: true);
            else
                InstalledClient.RetrySharingViolation(() => File.Delete(destination), retryAccessDenied: true);
            _replaced.RemoveAt(i);
        }
    }

    public void CleanUp()
    {
        try { Directory.Delete(_workDirectory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
