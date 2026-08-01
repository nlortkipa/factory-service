using FactoryService.Service;
using System.ServiceProcess;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("Factory Service Agent requires Windows.");
    return 1;
}

using var service = new FactoryWindowsService();

if (!Environment.UserInteractive)
{
    ServiceBase.Run(service);
    return 0;
}

Console.WriteLine("Factory Service Agent is running in console mode.");
Console.WriteLine("Press Ctrl+C to stop.");

using var stopped = new ManualResetEventSlim(false);
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    stopped.Set();
};

service.StartForConsole();
stopped.Wait();
service.StopForConsole();
return 0;
