using FactoryService.Common;
using System.ServiceProcess;
using System.Security.Principal;

namespace FactoryService.Service;

internal sealed class FactoryWindowsService : ServiceBase
{
    private CompanionSupervisor? _companions;
    private System.Threading.Timer? _sessionTimer;
    public FactoryWindowsService()
    {
        ServiceName = "FactoryServiceAgent";
        CanStop = true;
        CanShutdown = true;
        CanHandleSessionChangeEvent = true;
        AutoLog = false;
    }

    public void StartForConsole() => OnStart([]);

    public void StopForConsole() => OnStop();

    protected override void OnStart(string[] args)
    {
        try
        {
            AgentLog.Machine(
                $"service started; machine={Environment.MachineName}; " +
                $"data={AgentConfigStore.MachineDataDirectory}");
            using var identity = WindowsIdentity.GetCurrent();
            if (identity.IsSystem)
            {
                var launcher = new SessionCompanionLauncher();
                _companions = new CompanionSupervisor(SessionCompanionLauncher.GetLoggedInSessions,
                    launcher.EnsureStarted, exception => AgentLog.Machine($"companion launch failed; retrying: {exception}"));
                // Covers users already signed in during installation, later logons,
                // missed notifications, slow profile loading, and companion crashes.
                _sessionTimer = new System.Threading.Timer(_ => _companions.Refresh(), null, TimeSpan.Zero, TimeSpan.FromSeconds(10));
            }
        }
        catch (Exception exception)
        {
            AgentLog.Machine($"service start failed: {exception}");
            throw;
        }
    }

    protected override void OnStop()
    {
        _sessionTimer?.Dispose();
        _companions?.Dispose();
        AgentLog.Machine("service stopped");
    }

    protected override void OnShutdown()
    {
        _sessionTimer?.Dispose();
        _companions?.Dispose();
        AgentLog.Machine("Windows is shutting down");
        base.OnShutdown();
    }

    protected override void OnSessionChange(SessionChangeDescription changeDescription)
    {
        AgentLog.Machine(
            $"session change; reason={changeDescription.Reason}; " +
            $"sessionId={changeDescription.SessionId}");
        if (changeDescription.Reason is SessionChangeReason.SessionLogon or SessionChangeReason.SessionUnlock
            or SessionChangeReason.ConsoleConnect or SessionChangeReason.RemoteConnect)
            ThreadPool.QueueUserWorkItem(_ => _companions?.Refresh());
        base.OnSessionChange(changeDescription);
    }
}
