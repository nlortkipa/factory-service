using System.ComponentModel;
using System.Diagnostics;
using System.ServiceProcess;
using TimeoutException = System.TimeoutException;

namespace FactoryService.Setup;

internal static class InstalledClient
{
    private const string ServiceName = "FactoryServiceAgent";
    public const string ServiceFileName = "FactoryService.Service.exe";
    public const string CompanionFileName = "FactoryService.Companion.exe";
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(30);

    public static string InstallDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Factory Service Agent");
    public static string ServicePath => Path.Combine(InstallDirectory, ServiceFileName);
    public static string CompanionPath => Path.Combine(InstallDirectory, CompanionFileName);
    public static bool IsInstalled => File.Exists(ServicePath) && File.Exists(CompanionPath);
    public static bool IsRunning
    {
        get
        {
            using var service = new ServiceController(ServiceName);
            return GetStatus(service) == ServiceControllerStatus.Running;
        }
    }

    public static void WithMachineLock(Action operation)
    {
        using var mutex = new Mutex(false, @"Global\FactoryService.Setup");
        try
        {
            if (!mutex.WaitOne(TimeSpan.FromSeconds(60)))
                throw new TimeoutException("კლიენტის დაყენება ან წაშლა უკვე მიმდინარეობს.");
        }
        catch (AbandonedMutexException) { }
        try { operation(); }
        finally { mutex.ReleaseMutex(); }
    }

    public static void Stop()
    {
        using var service = new ServiceController(ServiceName);
        ServiceControllerStatus? status = GetStatus(service);
        if (status is not null and not ServiceControllerStatus.Stopped)
        {
            if (status == ServiceControllerStatus.StartPending)
                service.WaitForStatus(ServiceControllerStatus.Running, StopTimeout);
            if (status != ServiceControllerStatus.StopPending)
                service.Stop(stopDependentServices: false);
            service.WaitForStatus(ServiceControllerStatus.Stopped, StopTimeout);
        }

        // SCM can report Stopped before the executable releases its file mappings.
        WaitForInstalledProcesses(ServicePath, terminate: false);
        WaitForInstalledProcesses(CompanionPath, terminate: true);
    }

    public static void WaitUntilRunning()
    {
        using var service = new ServiceController(ServiceName);
        service.WaitForStatus(ServiceControllerStatus.Running, StopTimeout);
    }

    private static ServiceControllerStatus? GetStatus(ServiceController service)
    {
        try { return service.Status; }
        catch (InvalidOperationException exception) when (exception.InnerException is Win32Exception { NativeErrorCode: 1060 })
        {
            return null;
        }
    }

    private static void WaitForInstalledProcesses(string executable, bool terminate)
    {
        Process[] processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable));
        try
        {
            foreach (Process process in processes)
            {
                try
                {
                    if (!string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase)) continue;
                    if (terminate) process.Kill();
                    if (!process.WaitForExit((int)StopTimeout.TotalMilliseconds))
                        throw new TimeoutException($"პროცესი დროულად არ დასრულდა: {Path.GetFileName(executable)}.");
                }
                catch (InvalidOperationException) when (process.HasExited) { }
                catch (Win32Exception) when (process.HasExited) { }
            }
        }
        finally
        {
            foreach (Process process in processes) process.Dispose();
        }
    }

    public static void RetrySharingViolation(Action operation)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { operation(); return; }
            catch (IOException exception) when ((exception.HResult & 0xffff) is 32 or 33 && attempt < 20)
            {
                Thread.Sleep(250);
            }
        }
    }
}
