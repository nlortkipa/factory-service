using System.Diagnostics;
using FactoryService.Common;
using FactoryService.Companion;
using FactoryService.Service;

if (args.Contains("--mutex-child"))
{
    using Mutex childMutex = TurmoPanelDisplay.CreatePanelMutex();
    Console.WriteLine("ready");
    if (!childMutex.WaitOne(TimeSpan.FromSeconds(10))) return 1;
    Console.WriteLine("acquired");
    childMutex.ReleaseMutex();
    return 0;
}

var tests = new (string Name, Func<Task> Run)[]
{
    ("One installation starts all existing sessions and later logins", () =>
    {
        int[] sessions = [1, 2];
        var running = new HashSet<int>();
        var launches = new List<int>();
        using var supervisor = new CompanionSupervisor(() => sessions, id =>
        {
            if (running.Add(id)) launches.Add(id);
        }, exception => throw exception);
        supervisor.Refresh();
        supervisor.Refresh();
        sessions = [1, 2, 3];
        supervisor.Refresh();
        Check(launches.SequenceEqual([1, 2, 3]), "Did not start each existing/new user exactly once");
        running.Remove(2);
        supervisor.Refresh();
        Check(launches.SequenceEqual([1, 2, 3, 2]), "Missing companion was not restarted");
        return Task.CompletedTask;
    }),
    ("A profile not ready yet cannot block another user and is retried", () =>
    {
        bool ready = false;
        var running = new HashSet<int>();
        var errors = new List<Exception>();
        using var supervisor = new CompanionSupervisor(() => [1, 2], id =>
        {
            if (id == 1 && !ready) throw new InvalidOperationException("Profile not ready");
            running.Add(id);
        }, errors.Add);
        supervisor.Refresh();
        Check(running.SetEquals([2]) && errors.Count == 1, "A failed session blocked the next user");
        ready = true;
        supervisor.Refresh();
        Check(running.SetEquals([1, 2]), "Failed session was never retried");
        return Task.CompletedTask;
    }),
    ("Stopping supervision prevents update/uninstall from restarting companions", () =>
    {
        int launches = 0;
        var supervisor = new CompanionSupervisor(() => [1], _ => launches++, _ => { });
        supervisor.Dispose();
        supervisor.Refresh();
        Check(launches == 0, "Stopped service launched a companion");
        return Task.CompletedTask;
    }),
    ("Installed configuration ignores shared environment overrides", () =>
    {
        AgentConfig baseline = AgentConfigStore.LoadUserConfiguration();
        string[] keys = ["FACTORY_SERVICE_CONFIG", "FACTORY_SERVICE_OPERATOR_ID", "FACTORY_SERVICE_API_BASE_URL"];
        string?[] saved = keys.Select(Environment.GetEnvironmentVariable).ToArray();
        try
        {
            Environment.SetEnvironmentVariable(keys[0], @"C:\not-a-user-profile\shared-config.json");
            Environment.SetEnvironmentVariable(keys[1], "wrong-installing-admin");
            Environment.SetEnvironmentVariable(keys[2], "https://wrong.example.test");
            Check(AgentConfigStore.LoadUserConfiguration() == baseline, "Installed client used shared config/operator");
        }
        finally { for (int i = 0; i < keys.Length; i++) Environment.SetEnvironmentVariable(keys[i], saved[i]); }
        return Task.CompletedTask;
    }),
    ("Native enumeration finds logged-in Windows sessions", () =>
    {
        var sessions = SessionCompanionLauncher.GetLoggedInSessions();
        Check(sessions.All(id => id > 0) && sessions.Distinct().Count() == sessions.Count, "Invalid session enumeration");
        Console.WriteLine($"  Logged-in sessions: {string.Join(", ", sessions)}");
        return Task.CompletedTask;
    }),
    ("QR polls every five seconds and only redraws after submission or expiry", async () =>
    {
        var rig = new Rig();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        AgentQrResponse current = Rig.Response("Administrator") with { ExpiresAt = now.AddMinutes(5) };
        bool completed = false;
        int calls = 0;
        using var controller = rig.Controller("Administrator", utcNow: () => now, refreshRequest: (_, previous, _) =>
        {
            calls++;
            if (calls > 1)
                Check(previous == (current.ExpiresAt > now ? current.Token : null), "Current token not sent correctly");
            if (completed || current.ExpiresAt <= now)
                current = current with { Token = Guid.NewGuid().ToString(), ExpiresAt = now.AddMinutes(5) };
            return Task.FromResult(current);
        });
        await controller.TickAsync();
        Check(calls == 1 && rig.QrWrites == 1, "Initial QR was not published");
        now = now.AddSeconds(4);
        await controller.TickAsync();
        Check(calls == 1, "Polled before the five-second interval");
        now = now.AddSeconds(1);
        await controller.TickAsync();
        Check(calls == 2 && rig.QrWrites == 1, "Unused QR was redrawn");
        completed = true;
        now = now.AddSeconds(5);
        await controller.TickAsync();
        Check(calls == 3 && rig.QrWrites == 2 && rig.LastToken == current.Token, "Completed QR was not replaced");
        completed = false;
        now = current.ExpiresAt;
        await controller.TickAsync();
        Check(calls == 4 && rig.QrWrites == 3, "Expired QR was not refreshed");
    }),
    ("Background user's timer cannot request or overwrite a QR", async () =>
    {
        var rig = new Rig { ActiveUser = "Test", Screen = "Test" };
        using var admin = rig.Controller("Administrator");
        await admin.TickAsync();
        await admin.TickAsync(force: true);
        Check(rig.Requests.Count == 0 && rig.Screen == "Test", "Background user touched the panel/API");
    }),
    ("Switch to an unconfigured user clears the previous QR without an API request", async () =>
    {
        var rig = new Rig();
        using var admin = rig.Controller("Administrator");
        using var test = rig.Controller("Test", () => new AgentConfig());
        await admin.TickAsync();
        Check(rig.Screen == "Administrator", "Initial QR missing");
        rig.ActiveUser = "Test";
        await test.TickAsync();
        await admin.TickAsync(force: true);
        Check(rig.Screen == "unavailable" && rig.Requests.SequenceEqual(["Administrator"]), "Previous user's QR survived");
    }),
    ("Switching both directions preserves the foreground user's QR", async () =>
    {
        var rig = new Rig();
        using var admin = rig.Controller("Administrator");
        using var test = rig.Controller("Test");
        await admin.TickAsync();
        rig.ActiveUser = "Test";
        await test.TickAsync();
        await admin.TickAsync(force: true);
        Check(rig.Screen == "Test", "Admin overwrote Test");
        rig.ActiveUser = "Administrator";
        await admin.TickAsync();
        await test.TickAsync(force: true);
        Check(rig.Screen == "Administrator", "Test overwrote Admin");
    }),
    ("Delayed old-user response is discarded even without a session notification", async () =>
    {
        var rig = new Rig();
        var pending = new TaskCompletionSource<AgentQrResponse>();
        using var admin = rig.Controller("Administrator", request: (_, _) => pending.Task);
        using var test = rig.Controller("Test");
        Task oldRefresh = admin.TickAsync();
        rig.ActiveUser = "Test";
        await test.TickAsync();
        pending.SetResult(Rig.Response("Administrator"));
        await oldRefresh;
        Check(rig.Screen == "Test", "Delayed response overwrote new user");
    }),
    ("Switch cancels in-flight requests and rapid switch-back refreshes", async () =>
    {
        var rig = new Rig();
        CancellationToken observed = default;
        var pending = new TaskCompletionSource<AgentQrResponse>();
        bool first = true;
        using var admin = rig.Controller("Administrator", request: (config, token) =>
        {
            observed = token;
            if (!first) return Task.FromResult(Rig.Response(config.OperatorId));
            first = false;
            return pending.Task;
        });
        Task oldRefresh = admin.TickAsync();
        rig.ActiveUser = "Test";
        await admin.TickAsync();
        Check(observed.IsCancellationRequested, "Request was not cancelled");
        rig.ActiveUser = "Administrator";
        await admin.TickAsync();
        pending.SetResult(Rig.Response("stale"));
        await oldRefresh;
        await admin.TickAsync();
        Check(rig.Screen == "Administrator", "Rapid switch-back left a stale QR");
    }),
    ("Lock clears QR and unlock requests a fresh QR", async () =>
    {
        var rig = new Rig();
        using var admin = rig.Controller("Administrator");
        await admin.TickAsync();
        rig.ActiveUser = null;
        await admin.TickAsync();
        Check(rig.Screen == "unavailable", "Locked screen retained QR");
        rig.ActiveUser = "Administrator";
        await admin.TickAsync();
        Check(rig.Screen == "Administrator" && rig.Requests.Count == 2, "Unlock did not refresh");
    }),
    ("Logout clears QR and prevents any later refresh", async () =>
    {
        var rig = new Rig();
        using var admin = rig.Controller("Administrator");
        await admin.TickAsync();
        Check(rig.Screen == "Administrator", "Initial QR missing");
        await admin.ClearAsync();
        await admin.TickAsync(force: true);
        Check(rig.Screen == "unavailable" && rig.Requests.Count == 1, "Logout retained or republished the QR");
    }),
    ("Unreadable config never reuses the last valid operator", async () =>
    {
        var rig = new Rig();
        bool broken = false;
        using var admin = rig.Controller("Administrator", () => broken
            ? throw new InvalidDataException("malformed config") : new AgentConfig { OperatorId = "Administrator" });
        await admin.TickAsync();
        broken = true;
        await admin.TickAsync(force: true);
        Check(rig.Screen == "unavailable" && rig.Requests.Count == 1, "Last valid operator reused");
    }),
    ("Config changes during an API call discard the old operator response", async () =>
    {
        var rig = new Rig();
        string id = "old";
        var pending = new TaskCompletionSource<AgentQrResponse>();
        using var admin = rig.Controller("Administrator", () => new AgentConfig { OperatorId = id }, (_, _) => pending.Task);
        Task refresh = admin.TickAsync();
        id = "new";
        pending.SetResult(Rig.Response("old"));
        await refresh;
        Check(rig.Screen == "unavailable", "Old config response published");
    }),
    ("Ownership is checked again after waiting for the panel", async () =>
    {
        var rig = new Rig();
        rig.BeforeQrWrite = () => { rig.ActiveUser = "Test"; rig.Screen = "Test"; };
        using var admin = rig.Controller("Administrator");
        await admin.TickAsync();
        Check(rig.Screen == "Test", "Queued frame overwrote current user");
    }),
    ("A delayed error cannot erase the new user's QR", async () =>
    {
        var rig = new Rig();
        var pending = new TaskCompletionSource<AgentQrResponse>();
        using var admin = rig.Controller("Administrator", request: (_, _) => pending.Task);
        using var test = rig.Controller("Test");
        Task refresh = admin.TickAsync();
        rig.ActiveUser = "Test";
        await test.TickAsync();
        pending.SetException(new HttpRequestException("offline"));
        await refresh;
        Check(rig.Screen == "Test", "Error screen erased new user's QR");
    }),
    ("Disposal prevents late writes", async () =>
    {
        var rig = new Rig();
        var pending = new TaskCompletionSource<AgentQrResponse>();
        var admin = rig.Controller("Administrator", request: (_, _) => pending.Task);
        Task refresh = admin.TickAsync();
        admin.Dispose();
        pending.SetResult(Rig.Response("Administrator"));
        await refresh;
        Check(rig.Screen == "unavailable", "Disposed controller published");
    }),
    ("Global panel mutex serializes separate processes", () =>
    {
        // Synchronous: a Windows mutex must be released on the acquiring thread.
        using Mutex mutex = TurmoPanelDisplay.CreatePanelMutex();
        Check(mutex.WaitOne(TimeSpan.FromSeconds(2)), "Could not acquire panel mutex");
        using var child = new Process { StartInfo = new ProcessStartInfo(Environment.ProcessPath!)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
        } };
        child.StartInfo.ArgumentList.Add("--mutex-child");
        try
        {
            child.Start();
            Check(child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult() == "ready", "Child failed to open mutex");
            Task<string?> acquired = child.StandardOutput.ReadLineAsync();
            Check(!acquired.Wait(150), "Second process entered while mutex held");
            mutex.ReleaseMutex();
            Check(acquired.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult() == "acquired", "Child could not acquire after release");
            Check(child.WaitForExit(10000) && child.ExitCode == 0, "Mutex child failed");
        }
        finally { if (!child.HasExited) child.Kill(); }
        return Task.CompletedTask;
    }),
    ("Native Windows console ownership query succeeds", () =>
    {
        var session = new ConsoleSession();
        Console.WriteLine($"  Native session: canPublish={session.CanPublish}; canClear={session.CanClear}");
        return Task.CompletedTask;
    }),
    ("Panel typography is independent of Windows DPI scaling", () =>
    {
        using var font = TurmoPanelDisplay.CreateFixedFont("Segoe UI", 16, FontStyle.Bold);
        SizeF at100Percent = MeasureTextAtDpi(font, 96);
        SizeF at200Percent = MeasureTextAtDpi(font, 192);
        Check(Math.Abs(at100Percent.Width - at200Percent.Width) < 0.01f &&
              Math.Abs(at100Percent.Height - at200Percent.Height) < 0.01f,
            "Panel font changed size with Windows DPI scaling");
        return Task.CompletedTask;
    }),
    ("Dense QR codes keep the full panel footprint", () =>
    {
        using Bitmap shortQr = TurmoPanelDisplay.RenderQr(Rig.Response("Operator") with
        {
            SurveyUrl = "https://example.test/s/short"
        });
        using Bitmap denseQr = TurmoPanelDisplay.RenderQr(Rig.Response("Operator") with
        {
            SurveyUrl = $"https://example.test/s/{new string('x', 320)}"
        });
        Rectangle shortBounds = FindDarkBounds(shortQr, new Rectangle(0, 90, 320, 276));
        Rectangle denseBounds = FindDarkBounds(denseQr, new Rectangle(0, 90, 320, 276));
        Check(shortBounds.Width >= 210 && denseBounds.Width >= 240,
            $"QR footprint shrank unexpectedly (short={shortBounds.Width}px, dense={denseBounds.Width}px)");
        return Task.CompletedTask;
    })
};

int failed = 0;
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception exception) { failed++; Console.Error.WriteLine($"FAIL {test.Name}: {exception}"); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} tests passed.");
return failed == 0 ? 0 : 1;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static SizeF MeasureTextAtDpi(Font font, float dpi)
{
    using var bitmap = new Bitmap(320, 480);
    bitmap.SetResolution(dpi, dpi);
    using Graphics graphics = Graphics.FromImage(bitmap);
    return graphics.MeasureString("KARTULI CEMENT", font);
}

static Rectangle FindDarkBounds(Bitmap bitmap, Rectangle searchArea)
{
    int left = searchArea.Right;
    int top = searchArea.Bottom;
    int right = searchArea.Left - 1;
    int bottom = searchArea.Top - 1;
    for (int y = searchArea.Top; y < searchArea.Bottom; y++)
    for (int x = searchArea.Left; x < searchArea.Right; x++)
    {
        Color pixel = bitmap.GetPixel(x, y);
        if (pixel.R > 16 || pixel.G > 16 || pixel.B > 16) continue;
        left = Math.Min(left, x);
        top = Math.Min(top, y);
        right = Math.Max(right, x);
        bottom = Math.Max(bottom, y);
    }
    return right < left ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
}

sealed class Rig : IPanelDisplay
{
    public string? ActiveUser = "Administrator";
    public string Screen = "previous-user";
    public List<string> Requests { get; } = [];
    public Action? BeforeQrWrite;
    public int QrWrites;
    public string? LastToken;

    public QrRefreshController Controller(string user, Func<AgentConfig>? config = null,
        Func<AgentConfig, CancellationToken, Task<AgentQrResponse>>? request = null, Func<DateTimeOffset>? utcNow = null,
        Func<AgentConfig, string?, CancellationToken, Task<AgentQrResponse>>? refreshRequest = null)
        => new(new Session(this, user), this, config ?? (() => new AgentConfig { OperatorId = user }),
            refreshRequest ?? ((configuration, _, token) =>
            {
                if (request is not null) return request(configuration, token);
                Requests.Add(configuration.OperatorId);
                return Task.FromResult(Response(configuration.OperatorId));
            }), _ => { }, utcNow);

    public static AgentQrResponse Response(string user) => new("token", "https://example.test/s/token",
        DateTimeOffset.UtcNow.AddMinutes(5), user, "Plant");

    public Task ShowQrAsync(AgentQrResponse response, Func<bool> canWrite, CancellationToken token)
    {
        BeforeQrWrite?.Invoke();
        token.ThrowIfCancellationRequested();
        if (canWrite()) { Screen = response.OperatorName; QrWrites++; LastToken = response.Token; }
        return Task.CompletedTask;
    }

    public Task ShowUnavailableAsync(string detail, Func<bool> canWrite, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (canWrite()) Screen = "unavailable";
        return Task.CompletedTask;
    }

    private sealed class Session(Rig rig, string user) : IPanelSession
    {
        public bool CanPublish => rig.ActiveUser == user;
        public bool CanClear => rig.ActiveUser is null || CanPublish;
    }
}
