namespace FactoryService.Companion;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        string mutexName = $"Local\\FactoryService.Companion.{Environment.UserName}";
        using var mutex = new Mutex(initiallyOwned: true, mutexName, out bool isFirstInstance);

        if (!isFirstInstance)
        {
            MessageBox.Show(
                "Factory Service Companion is already running for this user.",
                "Factory Service",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        Application.Run(new CompanionApplicationContext());
    }
}
