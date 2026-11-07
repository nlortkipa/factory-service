using System.Diagnostics;
using System.IO.Compression;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace FactoryService.Setup;

internal sealed record Provisioning(string ApiBaseUrl, string PayloadUrl, string OperatorId, string OperatorName, string PlantName);

internal static class Program
{
    internal static readonly byte[] ProvisioningMarker = Encoding.ASCII.GetBytes("FSCONFIGv1");

    [STAThread]
    private static int Main(string[] args)
    {
        Provisioning? provisioning = ProvisioningReader.ReadCurrentExecutable();
        if (args.Length == 2 && args[0] == "--validate-provisioning")
            return provisioning?.OperatorId == args[1] ? 0 : 2;

        ApplicationConfiguration.Initialize();
        if (args.Length >= 2 && args[0] == "--elevated")
        {
            using var progress = new ProgressForm(provisioning, args[1]);
            Application.Run(progress);
            return 0;
        }
        Application.Run(new SetupForm(provisioning));
        return 0;
    }
}

internal static class ProvisioningReader
{
    public static Provisioning? ReadCurrentExecutable()
    {
        string? development = Environment.GetEnvironmentVariable("FACTORY_SERVICE_SETUP_CONFIG");
        if (!string.IsNullOrWhiteSpace(development)) return Deserialize(development);

        using var stream = File.OpenRead(Environment.ProcessPath!);
        int footerSize = Program.ProvisioningMarker.Length + sizeof(int);
        if (stream.Length < footerSize) return null;
        stream.Seek(-footerSize, SeekOrigin.End);
        byte[] marker = new byte[Program.ProvisioningMarker.Length];
        stream.ReadExactly(marker);
        if (!marker.SequenceEqual(Program.ProvisioningMarker)) return null;
        Span<byte> lengthBytes = stackalloc byte[sizeof(int)];
        stream.ReadExactly(lengthBytes);
        int length = BitConverter.ToInt32(lengthBytes);
        if (length <= 0 || length > 16_384 || stream.Length < footerSize + length) return null;
        stream.Seek(-(footerSize + length), SeekOrigin.End);
        byte[] json = new byte[length];
        stream.ReadExactly(json);
        return Deserialize(Encoding.UTF8.GetString(json));
    }

    private static Provisioning? Deserialize(string json)
    {
        try
        {
            var value = JsonSerializer.Deserialize<Provisioning>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return value is not null && Uri.TryCreate(value.ApiBaseUrl, UriKind.Absolute, out _) &&
                Uri.TryCreate(value.PayloadUrl, UriKind.Absolute, out _) && !string.IsNullOrWhiteSpace(value.OperatorId) ? value : null;
        }
        catch (JsonException) { return null; }
    }
}

internal sealed class SetupForm : Form
{
    private readonly Provisioning? _provisioning;
    private readonly Label _status;
    private readonly Button _install;
    private readonly Button _uninstall;

    public SetupForm(Provisioning? provisioning)
    {
        _provisioning = provisioning;
        Text = "Factory Service — Client setup";
        ClientSize = new Size(720, 520);
        MinimumSize = new Size(680, 500);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(239, 244, 246);
        Font = new Font("Segoe UI", 10);

        var header = new Panel { Dock = DockStyle.Top, Height = 138, BackColor = Color.FromArgb(25, 53, 81), Padding = new Padding(34, 24, 34, 18) };
        var mark = new Label { Text = "KC", ForeColor = Color.White, BackColor = Color.FromArgb(0, 156, 69), Font = new Font("Segoe UI", 14, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter, Size = new Size(54, 54), Location = new Point(34, 27) };
        var title = new Label { Text = "Factory Service client", ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 22), AutoSize = true, Location = new Point(108, 24) };
        var subtitle = new Label { Text = "One launcher. The right operator. No config files to edit.", ForeColor = Color.FromArgb(193, 211, 222), AutoSize = true, Location = new Point(111, 68) };
        header.Controls.AddRange([mark, title, subtitle]);

        var card = new Panel { BackColor = Color.White, Location = new Point(34, 166), Size = new Size(652, 250), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Padding = new Padding(28) };
        var assignmentLabel = new Label { Text = "THIS CLIENT IS ASSIGNED TO", ForeColor = Color.FromArgb(0, 125, 56), Font = new Font("Segoe UI", 8, FontStyle.Bold), AutoSize = true, Location = new Point(28, 24) };
        var operatorName = new Label { Text = provisioning?.OperatorName ?? "This launcher is not provisioned", ForeColor = Color.FromArgb(25, 53, 81), Font = new Font("Segoe UI Semibold", 20), AutoEllipsis = true, Location = new Point(28, 48), Size = new Size(590, 40) };
        var plant = new Label { Text = provisioning?.PlantName ?? "Download it from an operator row in the administration portal.", ForeColor = Color.FromArgb(92, 111, 125), AutoEllipsis = true, Location = new Point(30, 88), Size = new Size(585, 26) };
        var divider = new Panel { BackColor = Color.FromArgb(224, 232, 236), Location = new Point(28, 124), Size = new Size(596, 1), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        var serverCaption = new Label { Text = "Server", ForeColor = Color.FromArgb(111, 124, 136), Font = new Font("Segoe UI", 8, FontStyle.Bold), AutoSize = true, Location = new Point(28, 143) };
        var server = new Label { Text = provisioning?.ApiBaseUrl ?? "—", ForeColor = Color.FromArgb(25, 53, 81), AutoEllipsis = true, Location = new Point(28, 164), Size = new Size(590, 24) };
        _status = new Label { Text = Installed() ? "● Client is installed on this computer" : "○ Client is not installed yet", ForeColor = Installed() ? Color.FromArgb(0, 125, 56) : Color.FromArgb(92, 111, 125), Location = new Point(28, 204), AutoSize = true };
        card.Controls.AddRange([assignmentLabel, operatorName, plant, divider, serverCaption, server, _status]);

        _install = new Button { Text = Installed() ? "Repair / update" : "Install client", BackColor = Color.FromArgb(0, 156, 69), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI Semibold", 10), Size = new Size(180, 48), Location = new Point(34, 442), Enabled = provisioning is not null };
        _install.FlatAppearance.BorderSize = 0;
        _install.Click += (_, _) => Begin("install");
        _uninstall = new Button { Text = "Uninstall", BackColor = Color.White, ForeColor = Color.FromArgb(151, 43, 35), FlatStyle = FlatStyle.Flat, Size = new Size(130, 48), Location = new Point(226, 442), Enabled = Installed() };
        _uninstall.FlatAppearance.BorderColor = Color.FromArgb(231, 202, 199);
        _uninstall.Click += (_, _) => Begin("uninstall");
        var note = new Label { Text = "Windows will ask for administrator approval.", ForeColor = Color.FromArgb(111, 124, 136), AutoSize = true, Location = new Point(388, 457), Anchor = AnchorStyles.Top | AnchorStyles.Right };

        Controls.AddRange([header, card, _install, _uninstall, note]);
    }

    private void Begin(string action)
    {
        try
        {
            if (action == "install") WriteCurrentUserConfig(_provisioning!);

            var start = new ProcessStartInfo(Environment.ProcessPath!, $"--elevated {action}") { UseShellExecute = true, Verb = "runas" };
            Process.Start(start);
            if (action == "uninstall") DeleteCurrentUserConfig();
            Close();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _status.Text = exception is System.ComponentModel.Win32Exception { NativeErrorCode: 1223 }
                ? "Administrator approval was cancelled."
                : $"Could not start setup: {exception.Message}";
            _status.ForeColor = Color.FromArgb(180, 48, 40);
        }
    }

    private static string UserConfigPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Factory Service Agent", "config.json");
    private static void WriteCurrentUserConfig(Provisioning value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(UserConfigPath)!);
        string json = JsonSerializer.Serialize(new { apiBaseUrl = value.ApiBaseUrl.TrimEnd('/'), operatorId = value.OperatorId }, new JsonSerializerOptions { WriteIndented = true });
        string temporary = UserConfigPath + ".new";
        File.WriteAllText(temporary, json, new UTF8Encoding(false));
        File.Move(temporary, UserConfigPath, true);
    }
    private static void DeleteCurrentUserConfig() { if (File.Exists(UserConfigPath)) File.Delete(UserConfigPath); }
    private static bool Installed() => File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Factory Service Agent", "FactoryService.Service.exe"));
}

internal sealed class ProgressForm : Form
{
    private readonly Provisioning? _provisioning;
    private readonly string _action;
    private readonly Label _message;
    private readonly ProgressBar _progress;

    public ProgressForm(Provisioning? provisioning, string action)
    {
        _provisioning = provisioning; _action = action;
        Text = "Factory Service — Client setup";
        ClientSize = new Size(540, 220); StartPosition = FormStartPosition.CenterScreen; BackColor = Color.White;
        Font = new Font("Segoe UI", 10); ControlBox = false;
        Controls.Add(new Label { Text = action == "uninstall" ? "Removing Factory Service client" : "Installing Factory Service client", Font = new Font("Segoe UI Semibold", 17), ForeColor = Color.FromArgb(25, 53, 81), AutoSize = true, Location = new Point(34, 34) });
        _message = new Label { Text = "Preparing…", ForeColor = Color.FromArgb(92, 111, 125), AutoEllipsis = true, Location = new Point(36, 84), Size = new Size(470, 28) };
        _progress = new ProgressBar { Location = new Point(36, 124), Size = new Size(468, 12), Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 24 };
        Controls.AddRange([_message, _progress]);
        Shown += async (_, _) => await RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            if (_action == "uninstall") await Task.Run(Uninstall);
            else
            {
                if (_provisioning is null) throw new InvalidDataException("This launcher has no operator assignment.");
                await InstallAsync(_provisioning);
            }
            _progress.Style = ProgressBarStyle.Blocks; _progress.Value = 100;
            _message.Text = _action == "uninstall" ? "Client removed successfully." : "Client installed. It will start automatically.";
            await Task.Delay(1200); Close();
        }
        catch (Exception exception)
        {
            _progress.Style = ProgressBarStyle.Blocks; _progress.Value = 0;
            _message.Text = $"Setup failed: {exception.Message}";
            ControlBox = true;
        }
    }

    private async Task InstallAsync(Provisioning provisioning)
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "FactoryService-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            string zip = Path.Combine(tempRoot, "client.zip");
            Invoke(() => _message.Text = "Downloading the latest client…");
            using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
            using (var response = await client.GetAsync(provisioning.PayloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                await using var target = File.Create(zip);
                await response.Content.CopyToAsync(target);
            }
            string payload = Path.Combine(tempRoot, "payload");
            ZipFile.ExtractToDirectory(zip, payload);
            string service = Path.Combine(payload, "FactoryService.Service.exe");
            string companion = Path.Combine(payload, "FactoryService.Companion.exe");
            if (!File.Exists(service) || !File.Exists(companion)) throw new InvalidDataException("The downloaded client package is incomplete.");

            Invoke(() => _message.Text = "Installing the Windows service…");
            Run("sc.exe", "stop FactoryServiceAgent", allowFailure: true);
            Run("taskkill.exe", "/IM FactoryService.Companion.exe /F", allowFailure: true);
            string install = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Factory Service Agent");
            Directory.CreateDirectory(install);
            foreach (string source in Directory.EnumerateFiles(payload, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(payload, source);
                string destination = Path.Combine(install, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, true);
            }
            string installedService = Path.Combine(install, "FactoryService.Service.exe");
            string installedCompanion = Path.Combine(install, "FactoryService.Companion.exe");
            if (Run("sc.exe", "query FactoryServiceAgent", true) != 0)
                Run("sc.exe", $"create FactoryServiceAgent binPath= \"\\\"{installedService}\\\"\" DisplayName= \"Factory Service Agent\" start= auto");
            else Run("sc.exe", $"config FactoryServiceAgent binPath= \"\\\"{installedService}\\\"\" start= auto obj= LocalSystem");
            Run("sc.exe", "description FactoryServiceAgent \"Starts the Factory Service companion for signed-in users.\"");
            Run("sc.exe", "failure FactoryServiceAgent reset= 86400 actions= restart/5000/restart/15000/restart/30000");
            using (RegistryKey key = Registry.LocalMachine.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                key.SetValue("FactoryServiceCompanion", $"\"{installedCompanion}\" --background --installed");
            Run("schtasks.exe", "/Change /TN \"TURMO_ScheduleTask\" /Disable", true);
            Run("sc.exe", "start FactoryServiceAgent");
        }
        finally { try { Directory.Delete(tempRoot, true); } catch { } }
    }

    private void Uninstall()
    {
        Invoke(() => _message.Text = "Stopping and removing the client…");
        using (RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)) key?.DeleteValue("FactoryServiceCompanion", false);
        Run("sc.exe", "stop FactoryServiceAgent", true);
        Run("taskkill.exe", "/IM FactoryService.Companion.exe /F", true);
        Run("sc.exe", "delete FactoryServiceAgent", true);
        Run("schtasks.exe", "/Change /TN \"TURMO_ScheduleTask\" /Enable", true);
        string install = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Factory Service Agent");
        if (Directory.Exists(install))
        {
            try { Directory.Delete(install, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static int Run(string file, string arguments, bool allowFailure = false)
    {
        using var process = Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = false, CreateNoWindow = true }) ?? throw new InvalidOperationException($"Could not start {file}.");
        process.WaitForExit();
        if (!allowFailure && process.ExitCode != 0) throw new InvalidOperationException($"{file} failed with exit code {process.ExitCode}.");
        return process.ExitCode;
    }
}
