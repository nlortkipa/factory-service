using System.ComponentModel;
using System.Diagnostics;
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
        if (args is ["--elevated", "install" or "uninstall"])
        {
            using var progress = new ProgressForm(provisioning, args[1]);
            Application.Run(progress);
            return progress.ExitCode;
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
    private readonly Button _repair;
    private StoredAssignment? _storedAssignment;

    public SetupForm(Provisioning? provisioning)
    {
        _provisioning = provisioning;
        bool installed = File.Exists(InstalledClient.ServicePath);
        bool ready = CanConfigureUser();
        _storedAssignment = ReadCurrentUserConfig();
        bool currentUserConfigured = installed && !string.IsNullOrWhiteSpace(_storedAssignment?.OperatorId);
        bool sameAssignment = currentUserConfigured && provisioning is not null &&
            string.Equals(_storedAssignment!.OperatorId, provisioning.OperatorId, StringComparison.Ordinal);
        Text = "ქართული ცემენტი — მომსახურების კლიენტი";
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
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

        _install = new Button { Text = sameAssignment && !ready ? "განახლება / აღდგენა" : currentUserConfigured && !sameAssignment ? "ოპერატორის შეცვლა" : ready ? "ოპერატორის დაყენება" : "კლიენტის დაყენება", BackColor = Color.FromArgb(11, 156, 85), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI Semibold", 10), Size = new Size(202, 50), Location = new Point(30, 432), Enabled = provisioning is not null, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        _install.FlatAppearance.BorderSize = 0;
        _install.FlatAppearance.MouseOverBackColor = Color.FromArgb(8, 124, 68);
        _install.Click += async (_, _) => await BeginAsync("install", assignOperator: true);
        bool showRepair = ready && provisioning is not null;
        _repair = new Button { Text = "განახლება / აღდგენა", FlatStyle = FlatStyle.Flat, Size = new Size(180, 50), Location = new Point(244, 432), Visible = showRepair, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        _repair.Click += async (_, _) => await BeginAsync("install", assignOperator: false);
        _uninstall = new Button { Text = "წაშლა", BackColor = Color.FromArgb(239, 243, 244), ForeColor = Color.FromArgb(160, 53, 46), FlatStyle = FlatStyle.Flat, Size = new Size(116, 50), Location = new Point(showRepair ? 436 : 244, 432), Enabled = installed, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        _uninstall.FlatAppearance.BorderColor = Color.FromArgb(218, 226, 229);
        _uninstall.Click += async (_, _) => await BeginAsync("uninstall", assignOperator: false);
        Controls.AddRange([user, card, _install, _repair, _uninstall]);
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
        }
        catch
        {
            _currentOperator.Text = $"ოპერატორი: {_storedAssignment!.OperatorId}";
            _currentPlant.Text = "ქარხანა: მონაცემები მიუწვდომელია";
        }
    }

    private async Task BeginAsync(string action, bool assignOperator)
    {
        try
        {
            if (!assignOperator || !CanConfigureUser())
            {
                var start = new ProcessStartInfo(Environment.ProcessPath!, $"--elevated {action}") { UseShellExecute = true, Verb = "runas" };
                using Process process = Process.Start(start) ?? throw new InvalidOperationException("დაყენება ვერ დაიწყო.");
                Hide();
                await process.WaitForExitAsync();
                if (process.ExitCode != 0)
                {
                    Show();
                    return;
                }
            }
            if (assignOperator) WriteCurrentUserConfig(_provisioning!);
            if (action == "uninstall")
            {
                if (File.Exists(UserConfigPath))
                    InstalledClient.RetrySharingViolation(() => File.Delete(UserConfigPath));
                Shortcuts.RemoveForCurrentUser();
            }
            else
            {
                string[] failures = Shortcuts.CreateForCurrentUser();
                if (failures.Length > 0)
                {
                    Show();
                    MessageBox.Show(this,
                        "კლიენტი დაყენებულია. Windows-მა ზოგიერთი მალსახმობი ვერ შექმნა." +
                        Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, failures),
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            Close();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Show();
            _status.Text = exception is System.ComponentModel.Win32Exception { NativeErrorCode: 1223 }
                ? "ადმინისტრატორის დასტური გაუქმდა"
                : $"დაყენება ვერ დაიწყო: {exception.Message}";
            _status.ForeColor = Color.FromArgb(180, 48, 40);
        }
    }

    private static bool CanConfigureUser() =>
        InstalledClient.IsInstalled && File.Exists(Shortcuts.IconPath) && Shortcuts.Exists && InstalledClient.IsRunning;

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
        string temporary = UserConfigPath + "." + Guid.NewGuid().ToString("N") + ".new";
        try
        {
            File.WriteAllText(temporary, json, new UTF8Encoding(false));
            InstalledClient.RetrySharingViolation(() => File.Move(temporary, UserConfigPath, true));
        }
        finally { File.Delete(temporary); }
    }
}

internal sealed class ProgressForm : Form
{
    private readonly Provisioning? _provisioning;
    private readonly string _action;
    private readonly Label _message;
    private readonly ProgressBar _progress;
    public int ExitCode { get; private set; } = 1;

    public ProgressForm(Provisioning? provisioning, string action)
    {
        _provisioning = provisioning; _action = action;
        Text = "ქართული ცემენტი — მომსახურების კლიენტი";
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
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
            using var identity = WindowsIdentity.GetCurrent();
            if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                throw new UnauthorizedAccessException("კლიენტის დაყენებასა და წაშლას Windows-ის ადმინისტრატორის უფლებები სჭირდება.");

            if (_action == "uninstall") await Task.Run(() => InstalledClient.WithMachineLock(Uninstall));
            else
            {
                if (_provisioning is null) throw new InvalidDataException("ამ გამშვებს ოპერატორი არ აქვს მინიჭებული.");
                await InstallAsync(_provisioning);
            }
            _progress.Style = ProgressBarStyle.Blocks; _progress.Value = 100;
            ExitCode = 0;
            _message.Text = _action == "uninstall" ? "კლიენტი წარმატებით წაიშალა" : "კლიენტი დაყენებულია და ავტომატურად გაეშვება";
            if (_action == "install" && !await Task.Run(TurmoDisplay.IsLikelyConnected))
            {
                // Shown once here instead of repeatedly from the tray while the panel is missing.
                MessageBox.Show(this,
                    "Turmo ეკრანი ვერ მოიძებნა. შეამოწმეთ, რომ ეკრანი USB-ით არის მიერთებული და TURMO-ს ოფიციალური პროგრამა დახურულია. " +
                    "კლიენტი დაყენებულია და ეკრანის მიერთებისთანავე ავტომატურად აჩვენებს QR კოდს.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Close();
                return;
            }
            await Task.Delay(1200); Close();
        }
        catch (Exception exception)
        {
            _progress.Style = ProgressBarStyle.Blocks; _progress.Value = 0;
            string step = _message.Text.TrimEnd('…');
            _message.Text = $"ოპერაცია ვერ დასრულდა — {step}";
            var details = new TextBox { Location = new Point(37, 174), Size = new Size(495, 55), Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Text = $"{step}{Environment.NewLine}{exception.Message}" };
            Controls.Add(details);
            details.BringToFront();
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
            _message.Text = "დაყენება მზადდება…";
            await Task.Run(() => InstalledClient.WithMachineLock(() => InstallPayload(zip)));
        }
        finally { try { Directory.Delete(tempRoot, true); } catch { } }
    }

    private void InstallPayload(string zip)
    {
        var update = new PayloadUpdate(InstalledClient.InstallDirectory);
        bool preserveRecovery = false;
        try
        {
            Invoke(() => _message.Text = "კლიენტის პაკეტი იშლება…");
            update.Prepare(zip);

            bool wasRunning = InstalledClient.IsRunning;
            bool hadService = InstalledClient.HasService;
            bool serviceConfigurationStarted = false;
            try
            {
                Invoke(() => _message.Text = "სისტემური სერვისი ჩერდება…");
                InstalledClient.Stop();
                Invoke(() => _message.Text = "კლიენტის ფაილები ახლდება…");
                update.Apply();
                Invoke(() => _message.Text = "სისტემური სერვისი ეშვება…");
                serviceConfigurationStarted = true;
                ConfigureAndStartService();
            }
            catch (Exception updateError)
            {
                try
                {
                    if (serviceConfigurationStarted) InstalledClient.Stop();
                    if (update.HasChanges) update.Restore();
                    if (serviceConfigurationStarted && !hadService)
                        ServiceRegistration.Remove();
                    if (wasRunning && !InstalledClient.IsRunning)
                        InstalledClient.Start();
                }
                catch (Exception recoveryError)
                {
                    preserveRecovery = update.HasChanges;
                    string recoveryPath = preserveRecovery ? $"{Environment.NewLine}{update.RecoveryDirectory}" : "";
                    throw new InvalidOperationException($"{updateError.Message}{Environment.NewLine}აღდგენა ვერ დასრულდა: {recoveryError.Message}{recoveryPath}", recoveryError);
                }
                throw;
            }
            Invoke(() => _message.Text = "მალსახმობები იქმნება…");
            Invoke(Shortcuts.Create);
        }
        finally { if (!preserveRecovery) update.CleanUp(); }
    }

    private static void ConfigureAndStartService()
    {
        ServiceRegistration.Configure();
        InstalledClient.Start();
        using (RegistryKey key = Registry.LocalMachine.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            key.SetValue("FactoryServiceCompanion", $"\"{InstalledClient.CompanionPath}\" --background --installed");
        SetLegacyTaskEnabled(false);
    }

    private void Uninstall()
    {
        Invoke(() => _message.Text = "კლიენტი ჩერდება და იშლება…");
        using (RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)) key?.DeleteValue("FactoryServiceCompanion", false);
        InstalledClient.Stop();
        ServiceRegistration.Remove();
        SetLegacyTaskEnabled(true);
        Shortcuts.Remove();
        string install = InstalledClient.InstallDirectory;
        if (Directory.Exists(install))
            InstalledClient.RetrySharingViolation(() => Directory.Delete(install, true));
    }

    private static void SetLegacyTaskEnabled(bool enabled)
    {
        // The official Turmo task may not be installed. Its helper must never stall setup.
        string file = Path.Combine(Environment.SystemDirectory, "schtasks.exe");
        string action = enabled ? "/Enable" : "/Disable";
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo(file, $"/Change /TN TURMO_ScheduleTask {action}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is not null && !process.WaitForExit(10_000))
                process.Kill();
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException) { }
    }
}

internal static class TurmoDisplay
{
    private static readonly string[] Identifiers = ["USB35INCHIPSV2", "TURMO"];

    // Mirrors the companion's port selection: a port identified as Turmo, or the only
    // serial port present. Errors count as "connected" so the check never blocks setup.
    public static bool IsLikelyConnected()
    {
        try
        {
            using (RegistryKey? serialComm = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM"))
            {
                int ports = serialComm?.GetValueNames().Length ?? 0;
                if (ports == 0) return false;
                if (ports == 1) return true;
            }
            using RegistryKey? usb = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
            if (usb is null) return true;
            foreach (string device in usb.GetSubKeyNames())
            {
                using RegistryKey? deviceKey = usb.OpenSubKey(device);
                if (deviceKey is null) continue;
                foreach (string instance in deviceKey.GetSubKeyNames())
                {
                    using RegistryKey? instanceKey = deviceKey.OpenSubKey(instance);
                    string metadata = string.Join(" ", device, instanceKey?.GetValue("FriendlyName"), instanceKey?.GetValue("DeviceDesc"));
                    if (Identifiers.Any(id => metadata.Contains(id, StringComparison.OrdinalIgnoreCase))) return true;
                }
            }
            return false;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return true; }
    }
}
