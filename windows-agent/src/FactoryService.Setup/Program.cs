using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace FactoryService.Setup;

internal sealed record Provisioning(string ApiBaseUrl, string PayloadUrl, string OperatorId, string OperatorName, string PlantName);
internal sealed record StoredAssignment(string ApiBaseUrl, string OperatorId, string? OperatorName, string? PlantName);
internal sealed record AssignmentLookup(string OperatorName, string PlantName);

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
    private readonly Label _currentOperator;
    private readonly Label _currentPlant;
    private readonly Button _install;
    private readonly Button _uninstall;
    private StoredAssignment? _storedAssignment;

    public SetupForm(Provisioning? provisioning)
    {
        _provisioning = provisioning;
        bool installed = Installed();
        _storedAssignment = ReadCurrentUserConfig();
        bool currentUserConfigured = installed && !string.IsNullOrWhiteSpace(_storedAssignment?.OperatorId);
        bool sameAssignment = currentUserConfigured && provisioning is not null &&
            string.Equals(_storedAssignment!.OperatorId, provisioning.OperatorId, StringComparison.Ordinal);
        Text = "ქართული ცემენტი — მომსახურების კლიენტი";
        ClientSize = new Size(720, 520);
        MinimumSize = new Size(680, 500);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(239, 243, 244);
        Font = new Font("Segoe UI", 10);

        var user = new Label { Text = $"Windows მომხმარებელი: {Environment.UserName}", ForeColor = Color.FromArgb(72, 91, 102), Font = new Font("Segoe UI Semibold", 9), AutoEllipsis = true, Location = new Point(32, 25), Size = new Size(650, 28), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        var card = new Panel { BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Location = new Point(30, 61), Size = new Size(660, 345), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        var currentTitle = new Label { Text = "მიმდინარე კონფიგურაცია", ForeColor = Color.FromArgb(86, 103, 113), Font = new Font("Segoe UI", 8, FontStyle.Bold), AutoSize = true, Location = new Point(28, 23) };
        string currentOperatorName = CurrentOperatorText(_storedAssignment, currentUserConfigured);
        string currentPlantName = CurrentPlantText(_storedAssignment, currentUserConfigured);
        _currentOperator = new Label { Text = $"ოპერატორი: {currentOperatorName}", ForeColor = Color.FromArgb(25, 53, 70), Font = new Font("Segoe UI Semibold", 11), AutoEllipsis = true, Location = new Point(28, 52), Size = new Size(600, 30), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        _currentPlant = new Label { Text = $"ქარხანა: {currentPlantName}", ForeColor = Color.FromArgb(25, 53, 70), Font = new Font("Segoe UI Semibold", 11), AutoEllipsis = true, Location = new Point(28, 88), Size = new Size(600, 30), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        var statusDot = new Label { Text = currentUserConfigured ? "" : "●", ForeColor = Color.FromArgb(139, 154, 162), Font = new Font("Segoe UI", 9), AutoSize = true, Location = new Point(28, 126) };
        _status = new Label { Text = currentUserConfigured ? "" : installed ? "კლიენტი დაყენებულია, მაგრამ ეს მომხმარებელი არ არის გამართული" : "კლიენტი ჯერ არ არის დაყენებული", ForeColor = Color.FromArgb(72, 91, 102), Font = new Font("Segoe UI Semibold", 9), Location = new Point(50, 125), Size = new Size(575, 28), AutoEllipsis = true, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        var divider = new Panel { BackColor = Color.FromArgb(229, 235, 237), Location = new Point(28, 170), Size = new Size(600, 1), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        var incomingTitle = new Label { Text = sameAssignment ? "ამ EXE-ში იგივე კონფიგურაციაა" : currentUserConfigured ? "დაყენების შემდეგ შეიცვლება" : "დასაყენებელი კონფიგურაცია", ForeColor = Color.FromArgb(86, 103, 113), Font = new Font("Segoe UI", 8, FontStyle.Bold), AutoSize = true, Location = new Point(28, 194) };
        var incomingOperator = new Label { Text = $"ოპერატორი: {provisioning?.OperatorName ?? "—"}", ForeColor = Color.FromArgb(25, 53, 70), Font = new Font("Segoe UI Semibold", 11), AutoEllipsis = true, Location = new Point(28, 224), Size = new Size(600, 30), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        var incomingPlant = new Label { Text = $"ქარხანა: {provisioning?.PlantName ?? "—"}", ForeColor = Color.FromArgb(25, 53, 70), Font = new Font("Segoe UI Semibold", 11), AutoEllipsis = true, Location = new Point(28, 260), Size = new Size(600, 30), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        card.Controls.AddRange([currentTitle, _currentOperator, _currentPlant, statusDot, _status, divider, incomingTitle, incomingOperator, incomingPlant]);

        _install = new Button { Text = sameAssignment ? "განახლება / აღდგენა" : currentUserConfigured ? "ოპერატორის შეცვლა" : "კლიენტის დაყენება", BackColor = Color.FromArgb(11, 156, 85), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI Semibold", 10), Size = new Size(202, 50), Location = new Point(30, 432), Enabled = provisioning is not null, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        _install.FlatAppearance.BorderSize = 0;
        _install.FlatAppearance.MouseOverBackColor = Color.FromArgb(8, 124, 68);
        _install.Click += (_, _) => Begin("install");
        _uninstall = new Button { Text = "წაშლა", BackColor = Color.FromArgb(239, 243, 244), ForeColor = Color.FromArgb(160, 53, 46), FlatStyle = FlatStyle.Flat, Size = new Size(116, 50), Location = new Point(244, 432), Enabled = installed, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        _uninstall.FlatAppearance.BorderColor = Color.FromArgb(218, 226, 229);
        _uninstall.Click += (_, _) => Begin("uninstall");
        Controls.AddRange([user, card, _install, _uninstall]);
        if (currentUserConfigured && string.IsNullOrWhiteSpace(_storedAssignment?.OperatorName))
            Shown += ResolveStoredAssignment;
    }

    private static string CurrentOperatorText(StoredAssignment? assignment, bool configured) => !configured ? "—" :
        !string.IsNullOrWhiteSpace(assignment?.OperatorName) ? assignment.OperatorName : "მონაცემები იტვირთება…";

    private static string CurrentPlantText(StoredAssignment? assignment, bool configured) => !configured ? "—" :
        !string.IsNullOrWhiteSpace(assignment?.PlantName) ? assignment.PlantName : "მონაცემები იტვირთება…";

    private async void ResolveStoredAssignment(object? sender, EventArgs eventArgs)
    {
        Shown -= ResolveStoredAssignment;
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(_storedAssignment!.ApiBaseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(10) };
            using HttpResponseMessage response = await client.PostAsJsonAsync("api/agent/qr", new { _storedAssignment.OperatorId, CurrentToken = (string?)null });
            response.EnsureSuccessStatusCode();
            AssignmentLookup? lookup = await response.Content.ReadFromJsonAsync<AssignmentLookup>();
            if (lookup is null) return;
            _storedAssignment = _storedAssignment with { OperatorName = lookup.OperatorName, PlantName = lookup.PlantName };
            _currentOperator.Text = $"ოპერატორი: {lookup.OperatorName}";
            _currentPlant.Text = $"ქარხანა: {lookup.PlantName}";
            WriteStoredAssignment(_storedAssignment);
        }
        catch
        {
            _currentOperator.Text = $"ოპერატორი: {_storedAssignment!.OperatorId}";
            _currentPlant.Text = "ქარხანა: მონაცემები მიუწვდომელია";
        }
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
                ? "ადმინისტრატორის დასტური გაუქმდა"
                : $"დაყენება ვერ დაიწყო: {exception.Message}";
            _status.ForeColor = Color.FromArgb(180, 48, 40);
        }
    }

    private static string UserConfigPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Factory Service Agent", "config.json");
    private static StoredAssignment? ReadCurrentUserConfig()
    {
        try
        {
            if (!File.Exists(UserConfigPath)) return null;
            return JsonSerializer.Deserialize<StoredAssignment>(File.ReadAllText(UserConfigPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private static void WriteCurrentUserConfig(Provisioning value)
    {
        WriteStoredAssignment(new StoredAssignment(value.ApiBaseUrl.TrimEnd('/'), value.OperatorId, value.OperatorName, value.PlantName));
    }

    private static void WriteStoredAssignment(StoredAssignment value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(UserConfigPath)!);
        string json = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
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
        Text = "ქართული ცემენტი — მომსახურების კლიენტი";
        ClientSize = new Size(570, 250); StartPosition = FormStartPosition.CenterScreen; BackColor = Color.White;
        AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Segoe UI", 10); ControlBox = false;
        var accent = new Panel { Dock = DockStyle.Left, Width = 7, BackColor = Color.FromArgb(11, 156, 85) };
        var kicker = new Label { Text = "ქართული ცემენტი", Font = new Font("Segoe UI", 8, FontStyle.Bold), ForeColor = Color.FromArgb(8, 124, 68), AutoSize = true, Location = new Point(38, 32) };
        var title = new Label { Text = action == "uninstall" ? "კლიენტის წაშლა" : "კლიენტის დაყენება", Font = new Font("Sylfaen", 19, FontStyle.Bold), ForeColor = Color.FromArgb(16, 42, 59), AutoSize = true, Location = new Point(34, 54) };
        _message = new Label { Text = "მზადდება…", ForeColor = Color.FromArgb(92, 111, 125), AutoEllipsis = true, Location = new Point(37, 112), Size = new Size(495, 28) };
        _progress = new ProgressBar { Location = new Point(38, 151), Size = new Size(494, 8), Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 24 };
        var note = new Label { Text = "არ დახუროთ ფანჯარა პროცესის დასრულებამდე", ForeColor = Color.FromArgb(132, 146, 153), Font = new Font("Segoe UI", 8), AutoSize = true, Location = new Point(38, 184) };
        Controls.AddRange([accent, kicker, title, _message, _progress, note]);
        Shown += async (_, _) => await RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            if (_action == "uninstall") await Task.Run(Uninstall);
            else
            {
                if (_provisioning is null) throw new InvalidDataException("ამ გამშვებს ოპერატორი არ აქვს მინიჭებული.");
                await InstallAsync(_provisioning);
            }
            _progress.Style = ProgressBarStyle.Blocks; _progress.Value = 100;
            _message.Text = _action == "uninstall" ? "კლიენტი წარმატებით წაიშალა" : "კლიენტი დაყენებულია და ავტომატურად გაეშვება";
            await Task.Delay(1200); Close();
        }
        catch (Exception exception)
        {
            _progress.Style = ProgressBarStyle.Blocks; _progress.Value = 0;
            _message.Text = $"დაყენება ვერ დასრულდა: {exception.Message}";
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
            Invoke(() => _message.Text = "კლიენტის უახლესი ვერსია იტვირთება…");
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
            if (!File.Exists(service) || !File.Exists(companion)) throw new InvalidDataException("ჩამოტვირთული კლიენტის პაკეტი არასრულია.");

            Invoke(() => _message.Text = "სისტემური სერვისი ყენდება…");
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
            Run("sc.exe", "description FactoryServiceAgent \"ავტომატურად უშვებს მომსახურების შეფასების კლიენტს.\"");
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
        Invoke(() => _message.Text = "კლიენტი ჩერდება და იშლება…");
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
        using var process = Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = false, CreateNoWindow = true }) ?? throw new InvalidOperationException($"{file} ვერ გაეშვა.");
        process.WaitForExit();
        if (!allowFailure && process.ExitCode != 0) throw new InvalidOperationException($"{file} შეცდომით დასრულდა (კოდი: {process.ExitCode}).");
        return process.ExitCode;
    }
}
