using System.ComponentModel;
using System.Diagnostics;
using FactoryService.Common;

namespace FactoryService.Companion;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        bool installed = args.Contains("--installed");
        bool background = args.Contains("--background");
        if (installed && !background)
        {
            OpenMaintenance();
            return;
        }

        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        string mutexName = $"Local\\FactoryService.Companion.{identity.User!.Value}";
        using var mutex = new Mutex(initiallyOwned: true, mutexName, out bool isFirstInstance);

        if (!isFirstInstance)
        {
            if (!background) MessageBox.Show(
                "შეფასების კლიენტი უკვე გაშვებულია. მისი მენიუ იხილეთ Windows-ის საათთან არსებულ ხატულებში.",
                "ქართული ცემენტი — შეფასება",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        Application.Run(new CompanionApplicationContext(installed));
    }

    private static void OpenMaintenance()
    {
        var maintenance = ClientMaintenance.Read(AppContext.BaseDirectory);
        if (maintenance is null)
        {
            MessageBox.Show("მართვის ფანჯრის დასაყენებლად გაუშვით პორტალიდან ჩამოტვირთული უახლესი ინსტალერი.",
                "ქართული ცემენტი — მომსახურების კლიენტი", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo(maintenance.LauncherPath(AppContext.BaseDirectory))
            {
                UseShellExecute = true,
                WorkingDirectory = Environment.SystemDirectory,
            });
        }
        catch (Win32Exception exception)
        {
            MessageBox.Show(exception.Message, "მართვის ფანჯარა ვერ გაიხსნა", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
