namespace FactoryService.Companion;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        string mutexName = $"Local\\FactoryService.Companion.{identity.User!.Value}";
        bool installed = args.Contains("--installed");
        bool background = args.Contains("--background");
        using var openRequested = new EventWaitHandle(false, EventResetMode.AutoReset, mutexName + ".Open");
        using var mutex = new Mutex(initiallyOwned: true, mutexName, out bool isFirstInstance);

        if (!isFirstInstance)
        {
            if (installed && !background) openRequested.Set();
            else if (!background) MessageBox.Show(
                "შეფასების კლიენტი უკვე გაშვებულია. მისი მენიუ იხილეთ Windows-ის საათთან არსებულ ხატულებში.",
                "ქართული ცემენტი — შეფასება",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (installed && !background) openRequested.Set();
        Application.Run(new CompanionApplicationContext(installed, openRequested));
    }
}
