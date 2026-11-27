using FactoryService.Common;
using Microsoft.Win32;

namespace FactoryService.Companion;

internal sealed class CompanionApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly Icon _icon;
    private readonly Control _uiDispatcher;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly ConsoleSession _session = new();
    private readonly QrRefreshController _refresh;
    private readonly Func<AgentConfig> _loadConfiguration;
    private bool _exiting;
    private bool _warningShown;

    public CompanionApplicationContext(bool installed)
    {
        _loadConfiguration = installed ? AgentConfigStore.LoadUserConfiguration : AgentConfigStore.LoadOrDefault;
        try
        {
            if (installed) AgentConfigStore.EnsureUserConfiguration();
            else AgentConfigStore.EnsureExists();
        }
        catch (Exception exception) { AgentLog.User($"configuration load failed: {exception}"); }

        var menu = new ContextMenuStrip();
        menu.Items.Add("Refresh Turmo QR", null, async (_, _) => await _refresh!.TickAsync(force: true));
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        _icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? (Icon)SystemIcons.Application.Clone();
        _trayIcon = new NotifyIcon { Icon = _icon, Text = "Kartuli Cement — შეფასება", ContextMenuStrip = menu, Visible = true };
        _trayIcon.DoubleClick += async (_, _) => await _refresh!.TickAsync(force: true);

        _uiDispatcher = new Control(); _uiDispatcher.CreateControl();
        _refresh = new QrRefreshController(_session, new TurmoPanelDisplay(), _loadConfiguration,
            RequestQrAsync, exception =>
            {
                AgentLog.User($"Turmo QR refresh failed: {exception}");
                // Refresh retries every few seconds; notify the user only once per run and
                // keep the full history in the log.
                if (_exiting || _warningShown) return;
                _warningShown = true;
                _trayIcon.ShowBalloonTip(5000, "Factory Service", exception.Message, ToolTipIcon.Warning);
            });
        // Poll ownership as well as listening for notifications: Fast User Switching and
        // console reconnects must be detected even if a session notification is missed.
        _refreshTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _refreshTimer.Tick += async (_, _) => await _refresh.TickAsync();
        _refreshTimer.Start();
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.SessionEnding += OnSessionEnding;
        Application.Idle += ShowInitialQr;
        AgentLog.User($"companion started; user={Environment.UserName}; config={(installed ? AgentConfigStore.UserConfigPath : AgentConfigStore.ConfigPath)}");
    }

    private async void ShowInitialQr(object? sender, EventArgs eventArgs)
    {
        Application.Idle -= ShowInitialQr;
        await _refresh.TickAsync();
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs eventArgs)
    {
        AgentLog.User($"session switch; reason={eventArgs.Reason}");
        // SystemEvents can run off the UI thread. Serialize controller state on the UI loop.
        if (_exiting || !_uiDispatcher.IsHandleCreated) return;
        try
        {
            _uiDispatcher.BeginInvoke(new Action(async () =>
            {
                if (!_exiting) await _refresh.TickAsync();
            }));
        }
        catch (InvalidOperationException) when (_exiting) { }
    }

    private void OnSessionEnding(object sender, SessionEndingEventArgs eventArgs)
    {
        AgentLog.User($"session ending; reason={eventArgs.Reason}");
        ClearPanelBeforeExit();
    }

    private void ClearPanelBeforeExit()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            _refresh.ClearAsync(timeout.Token).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            AgentLog.User($"Turmo panel clear failed during logout: {exception}");
        }
    }

    private static async Task<AgentQrResponse> RequestQrAsync(AgentConfig config, string? currentToken, CancellationToken cancellationToken)
    {
        using var api = new AgentApiClient(config);
        return await api.RequestQrAsync(cancellationToken, currentToken);
    }

    protected override void ExitThreadCore()
    {
        _exiting = true;
        Application.Idle -= ShowInitialQr;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.SessionEnding -= OnSessionEnding;
        _refreshTimer.Stop(); _refreshTimer.Dispose();
        ClearPanelBeforeExit();
        _refresh.Dispose();
        _trayIcon.Visible = false; _trayIcon.Dispose(); _icon.Dispose(); _uiDispatcher.Dispose();
        AgentLog.User("companion stopped"); base.ExitThreadCore();
    }
}
