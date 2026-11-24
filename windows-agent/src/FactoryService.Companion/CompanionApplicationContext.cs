using FactoryService.Common;
using Microsoft.Win32;

namespace FactoryService.Companion;

internal sealed class CompanionApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly Control _uiDispatcher;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly ConsoleSession _session = new();
    private readonly QrRefreshController _refresh;
    private readonly Func<AgentConfig> _loadConfiguration;
    private bool _exiting;

    public CompanionApplicationContext(bool installed = false)
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
        menu.Items.Add("Open survey in browser", null, async (_, _) => await OpenSurveyAsync());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        _trayIcon = new NotifyIcon { Icon = SystemIcons.Application, Text = "Kartuli Cement — შეფასება", ContextMenuStrip = menu, Visible = true };
        _trayIcon.DoubleClick += async (_, _) => await _refresh!.TickAsync(force: true);

        _uiDispatcher = new Control(); _uiDispatcher.CreateControl();
        _refresh = new QrRefreshController(_session, new TurmoPanelDisplay(), _loadConfiguration,
            RequestQrAsync, exception =>
            {
                AgentLog.User($"Turmo QR refresh failed: {exception}");
                if (!_exiting) _trayIcon.ShowBalloonTip(5000, "Factory Service", exception.Message, ToolTipIcon.Warning);
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

    private async Task OpenSurveyAsync()
    {
        try
        {
            if (!_session.CanPublish) return;
            AgentConfig config = QrRefreshController.LoadConfiguredOperator(_loadConfiguration);
            AgentQrResponse response = await RequestQrAsync(config, null, CancellationToken.None);
            if (_exiting || !_session.CanPublish || _loadConfiguration() != config) return;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(response.SurveyUrl) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            if (!_exiting) MessageBox.Show(exception.Message, "Factory Service", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        _trayIcon.Visible = false; _trayIcon.Dispose(); _uiDispatcher.Dispose();
        AgentLog.User("companion stopped"); base.ExitThreadCore();
    }
}
