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
                "შეფასების კლიენტი უკვე გაშვებულია. მისი მენიუ იხილეთ Windows-ის საათთან არსებულ ხატულებში.",
                "ქართული ცემენტი — შეფასება",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        Application.Run(new CompanionApplicationContext(args.Contains("--installed")));
    }
}
