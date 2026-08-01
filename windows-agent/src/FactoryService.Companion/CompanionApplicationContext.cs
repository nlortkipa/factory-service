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
    private QrDisplayForm? _qrForm;
    private bool _refreshing;
    private DateTimeOffset? _lastRenderAt;

    public CompanionApplicationContext()
    {
        try { _config = AgentConfigStore.EnsureExists(); }
        catch (Exception exception) { AgentLog.User($"configuration load failed; using defaults: {exception}"); _config = new AgentConfig(); }
        _api = new AgentApiClient(_config);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Refresh QR", null, async (_, _) => await RefreshQrAsync(forceShow: true));
        menu.Items.Add("Open survey in browser", null, async (_, _) => await OpenSurveyAsync());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        _trayIcon = new NotifyIcon { Icon = SystemIcons.Application, Text = "Kartuli Cement — შეფასება", ContextMenuStrip = menu, Visible = true };
        _trayIcon.DoubleClick += async (_, _) => await RefreshQrAsync(forceShow: true);

        _uiDispatcher = new Control(); _uiDispatcher.CreateControl();
        _refreshTimer = new System.Windows.Forms.Timer { Interval = Math.Max(15, _config.QrRefreshSeconds) * 1000 };
        _refreshTimer.Tick += async (_, _) => await RefreshQrAsync(forceShow: _qrForm is { IsDisposed: false });
        _refreshTimer.Start();

        SystemEvents.SessionSwitch += OnSessionSwitch;
        Application.Idle += ShowInitialQr;
        AgentLog.User($"companion started; user={_config.EffectiveWindowsUsername}; config={AgentConfigStore.ConfigPath}");
    }

    private async void ShowInitialQr(object? sender, EventArgs eventArgs)
    {
        Application.Idle -= ShowInitialQr;
        await RefreshQrAsync(forceShow: true);
    }

    private async void OnSessionSwitch(object sender, SessionSwitchEventArgs eventArgs)
    {
        AgentLog.User($"session switch; reason={eventArgs.Reason}");
        if (eventArgs.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon or SessionSwitchReason.RemoteConnect)
            await RefreshQrAsync(forceShow: true);
        else if (eventArgs.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff or SessionSwitchReason.RemoteDisconnect)
            _qrForm?.Close();
    }

    private async Task RefreshQrAsync(bool forceShow)
    {
        if (_refreshing) return; _refreshing = true;
        try
        {
            AgentQrResponse response = await _api.RequestQrAsync();
            _lastRenderAt = DateTimeOffset.UtcNow;
            await _api.SendHeartbeatAsync("Online", null, _lastRenderAt);
            if (forceShow)
            {
                _qrForm?.Close(); _qrForm = new QrDisplayForm(_config, response);
                _qrForm.FormClosed += (_, _) => _qrForm = null; _qrForm.Show(); _qrForm.Activate();
            }
            AgentLog.User($"QR refreshed; expires={response.ExpiresAt:O}; operator={response.OperatorName}");
        }
        catch (Exception exception)
        {
            string? code = (exception as AgentApiException)?.Code;
            try { await _api.SendHeartbeatAsync("Warning", code ?? "API_UNAVAILABLE", _lastRenderAt); } catch { }
            AgentLog.User($"QR refresh failed: {exception}");
            if (forceShow)
            {
                string detail = exception is AgentApiException apiException && !string.IsNullOrWhiteSpace(apiException.HelpdeskReference)
                    ? $"{apiException.Message}\n{apiException.HelpdeskReference}" : "სერვერთან კავშირი ვერ დამყარდა";
                _qrForm?.Close(); _qrForm = new QrDisplayForm(_config, null, detail);
                _qrForm.FormClosed += (_, _) => _qrForm = null; _qrForm.Show(); _qrForm.Activate();
            }
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
        _trayIcon.Visible = false; _trayIcon.Dispose(); _uiDispatcher.Dispose(); _qrForm?.Close(); _api.Dispose();
        AgentLog.User("companion stopped"); base.ExitThreadCore();
    }
}
