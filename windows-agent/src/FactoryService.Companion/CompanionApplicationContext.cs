using FactoryService.Common;
using Microsoft.Win32;

namespace FactoryService.Companion;

internal sealed class CompanionApplicationContext : ApplicationContext
{
    private readonly AgentConfig _config;
    private readonly AgentApiClient _api;
    private readonly NotifyIcon _trayIcon;
    private readonly Control _uiDispatcher;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private bool _refreshing;

    public CompanionApplicationContext()
    {
        try { _config = AgentConfigStore.EnsureExists(); }
        catch (Exception exception) { AgentLog.User($"configuration load failed; using defaults: {exception}"); _config = new AgentConfig(); }
        _api = new AgentApiClient(_config);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Refresh Turmo QR", null, async (_, _) => await RefreshQrAsync());
        menu.Items.Add("Open survey in browser", null, async (_, _) => await OpenSurveyAsync());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        _trayIcon = new NotifyIcon { Icon = SystemIcons.Application, Text = "Kartuli Cement — შეფასება", ContextMenuStrip = menu, Visible = true };
        _trayIcon.DoubleClick += async (_, _) => await RefreshQrAsync();

        _uiDispatcher = new Control(); _uiDispatcher.CreateControl();
        _refreshTimer = new System.Windows.Forms.Timer { Interval = 60 * 1000 };
        _refreshTimer.Tick += async (_, _) => await RefreshQrAsync();
        _refreshTimer.Start();

        SystemEvents.SessionSwitch += OnSessionSwitch;
        Application.Idle += ShowInitialQr;
        AgentLog.User($"companion started; operator={_config.OperatorId}; config={AgentConfigStore.ConfigPath}");
    }

    private async void ShowInitialQr(object? sender, EventArgs eventArgs)
    {
        Application.Idle -= ShowInitialQr;
        await RefreshQrAsync();
    }

    private async void OnSessionSwitch(object sender, SessionSwitchEventArgs eventArgs)
    {
        AgentLog.User($"session switch; reason={eventArgs.Reason}");
        if (eventArgs.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon or SessionSwitchReason.RemoteConnect)
            await RefreshQrAsync();
    }

    private async Task RefreshQrAsync()
    {
        if (_refreshing) return; _refreshing = true;
        try
        {
            AgentQrResponse response = await _api.RequestQrAsync();
            await TurmoPanelDisplay.ShowQrAsync(response);
            AgentLog.User($"Turmo QR refreshed; expires={response.ExpiresAt:O}; operator={response.OperatorName}");
        }
        catch (Exception exception)
        {
            AgentLog.User($"Turmo QR refresh failed: {exception}");
            if (exception is not TurmoPanelException)
            {
                string detail = exception is AgentApiException apiException && !string.IsNullOrWhiteSpace(apiException.HelpdeskReference)
                    ? $"{apiException.Message}\n{apiException.HelpdeskReference}" : "სერვერთან კავშირი ვერ დამყარდა";
                try { await TurmoPanelDisplay.ShowUnavailableAsync(detail); }
                catch (Exception panelException) { AgentLog.User($"Turmo unavailable screen failed: {panelException}"); }
            }

            _trayIcon.ShowBalloonTip(5000, "Factory Service", exception.Message, ToolTipIcon.Warning);
        }
        finally { _refreshing = false; }
    }

    private async Task OpenSurveyAsync()
    {
        try
        {
            AgentQrResponse response = await _api.RequestQrAsync();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(response.SurveyUrl) { UseShellExecute = true });
        }
        catch (Exception exception) { MessageBox.Show(exception.Message, "Factory Service", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    protected override void ExitThreadCore()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch; _refreshTimer.Stop(); _refreshTimer.Dispose();
        _trayIcon.Visible = false; _trayIcon.Dispose(); _uiDispatcher.Dispose(); _api.Dispose();
        AgentLog.User("companion stopped"); base.ExitThreadCore();
    }
}
