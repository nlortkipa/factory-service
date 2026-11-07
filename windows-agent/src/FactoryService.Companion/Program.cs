namespace FactoryService.Companion;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        string mutexName = $"Local\\FactoryService.Companion.{identity.User!.Value}";
        using var mutex = new Mutex(initiallyOwned: true, mutexName, out bool isFirstInstance);

        if (!isFirstInstance)
        {
            if (!args.Contains("--background")) MessageBox.Show(
                "Factory Service Companion is already running for this user.",
                "Factory Service",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        Application.Run(new CompanionApplicationContext(args.Contains("--installed")));
    }
}
