namespace FactoryService.Common;

public static class AgentLog
{
    private static readonly object Sync = new();

    public static void Machine(string message)
    {
        string directory = Path.Combine(AgentConfigStore.MachineDataDirectory, "logs");
        Append(directory, "service.log", message);
    }

    public static void User(string message)
    {
        string directory = Path.Combine(AgentConfigStore.ConfigDirectory, "logs");
        Append(directory, "companion.log", message);
    }

    private static void Append(string directory, string fileName, string message)
    {
        lock (Sync)
        {
            Directory.CreateDirectory(directory);
            string line = $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(directory, fileName), line);
        }
    }
}
