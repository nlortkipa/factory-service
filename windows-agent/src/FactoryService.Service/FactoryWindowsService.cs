using FactoryService.Common;
using System.ServiceProcess;

namespace FactoryService.Service;

internal sealed class FactoryWindowsService : ServiceBase
{
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
            AgentConfig config = AgentConfigStore.EnsureExists();
            AgentLog.Machine(
                $"service started; machine={Environment.MachineName}; " +
                $"api={config.ApiBaseUrl}; operator={config.OperatorId}; config={AgentConfigStore.ConfigPath}");
        }
        catch (Exception exception)
        {
            AgentLog.Machine($"service start failed: {exception}");
            throw;
        }
    }

    protected override void OnStop()
    {
        AgentLog.Machine("service stopped");
    }

    protected override void OnShutdown()
    {
        AgentLog.Machine("Windows is shutting down");
        base.OnShutdown();
    }

    protected override void OnSessionChange(SessionChangeDescription changeDescription)
    {
        AgentLog.Machine(
            $"session change; reason={changeDescription.Reason}; " +
            $"sessionId={changeDescription.SessionId}");
        base.OnSessionChange(changeDescription);
    }
}
