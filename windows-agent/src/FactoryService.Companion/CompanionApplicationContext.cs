using FactoryService.Common;
using Microsoft.Win32;

namespace FactoryService.Companion;

internal sealed class CompanionApplicationContext : ApplicationContext
{
    private readonly AgentConfig _config;
    private readonly NotifyIcon _trayIcon;
    private readonly Control _uiDispatcher;
    private QrDisplayForm? _qrForm;

    public CompanionApplicationContext()
    {
        try
        {
            _config = AgentConfigStore.LoadOrDefault();
        }
        catch (Exception exception)
        {
            AgentLog.User($"configuration load failed; using defaults: {exception}");
            _config = new AgentConfig();
        }

        var menu = new ContextMenuStrip();
        menu.Items.Add("Show QR", null, (_, _) => ShowQr());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Factory Service",
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => ShowQr();

        _uiDispatcher = new Control();
        _uiDispatcher.CreateControl();

        SystemEvents.SessionSwitch += OnSessionSwitch;
        Application.Idle += ShowInitialQr;
        AgentLog.User($"companion started; user={Environment.UserName}");
    }

    private void ShowInitialQr(object? sender, EventArgs eventArgs)
    {
        Application.Idle -= ShowInitialQr;
        ShowQr();
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs eventArgs)
    {
        AgentLog.User($"session switch; reason={eventArgs.Reason}");

        if (eventArgs.Reason == SessionSwitchReason.SessionUnlock && !_uiDispatcher.IsDisposed)
        {
            _uiDispatcher.BeginInvoke(ShowQr);
        }
    }

    private void ShowQr()
    {
        if (_qrForm is { IsDisposed: false })
        {
            _qrForm.Activate();
            return;
        }

        _qrForm = new QrDisplayForm(_config);
        _qrForm.FormClosed += (_, _) => _qrForm = null;
        _qrForm.Show();
        _qrForm.Activate();
        AgentLog.User("QR window shown");
    }

    protected override void ExitThreadCore()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _uiDispatcher.Dispose();
        _qrForm?.Close();
        AgentLog.User("companion stopped");
        base.ExitThreadCore();
    }
}
