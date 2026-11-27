using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FactoryService.Setup;

internal static class ServiceRegistration
{
    private const uint ScManagerConnect = 0x0001;
    private const uint ScManagerCreateService = 0x0002;
    private const uint ServiceChangeConfig = 0x0002;
    private const uint ServiceStart = 0x0010;
    private const uint DeleteAccess = 0x10000;
    private const uint ServiceWin32OwnProcess = 0x0010;
    private const uint ServiceAutoStart = 2;
    private const uint ServiceErrorNormal = 1;
    private const uint ServiceNoChange = uint.MaxValue;
    private const uint ServiceConfigDescription = 1;
    private const uint ServiceConfigFailureActions = 2;
    private const uint RestartAction = 1;
    private const int ErrorServiceDoesNotExist = 1060;
    private const int ErrorServiceMarkedForDelete = 1072;

    public static void Configure()
    {
        using ServiceHandle manager = OpenSCManager(null, null, ScManagerConnect | ScManagerCreateService);
        if (manager.IsInvalid) throw Error("OpenSCManager");

        const uint access = ServiceChangeConfig | ServiceStart;
        string binaryPath = $"\"{InstalledClient.ServicePath}\"";
        using ServiceHandle existing = OpenService(manager, InstalledClient.ServiceName, access);
        if (!existing.IsInvalid)
        {
            if (!ChangeServiceConfig(existing, ServiceNoChange, ServiceAutoStart, ServiceNoChange,
                    binaryPath, null, IntPtr.Zero, null, "LocalSystem", null, null))
                throw Error("ChangeServiceConfig");
            ConfigureDetails(existing);
            return;
        }

        int error = Marshal.GetLastWin32Error();
        if (error != ErrorServiceDoesNotExist) throw Error("OpenService", error);
        using ServiceHandle created = CreateService(manager, InstalledClient.ServiceName, "Factory Service Agent",
            access, ServiceWin32OwnProcess, ServiceAutoStart, ServiceErrorNormal,
            binaryPath, null, IntPtr.Zero, null, null, null);
        if (created.IsInvalid) throw Error("CreateService");
        ConfigureDetails(created);
    }

    public static void Remove()
    {
        using ServiceHandle manager = OpenSCManager(null, null, ScManagerConnect);
        if (manager.IsInvalid) throw Error("OpenSCManager");
        using ServiceHandle service = OpenService(manager, InstalledClient.ServiceName, DeleteAccess);
        if (service.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            if (error is ErrorServiceDoesNotExist or ErrorServiceMarkedForDelete) return;
            throw Error("OpenService", error);
        }
        if (!DeleteService(service))
        {
            int error = Marshal.GetLastWin32Error();
            if (error != ErrorServiceMarkedForDelete) throw Error("DeleteService", error);
        }
    }

    private static void ConfigureDetails(ServiceHandle service)
    {
        var description = new ServiceDescription { Text = "ავტომატურად უშვებს მომსახურების შეფასების კლიენტს." };
        if (!ChangeServiceDescription(service, ServiceConfigDescription, ref description))
            throw Error("ChangeServiceConfig2 (description)");

        RecoveryAction[] actions = [new(RestartAction, 5_000), new(RestartAction, 15_000), new(RestartAction, 30_000)];
        GCHandle pinned = GCHandle.Alloc(actions, GCHandleType.Pinned);
        try
        {
            var recovery = new FailureActions { ResetPeriod = 86_400, Count = (uint)actions.Length, Actions = pinned.AddrOfPinnedObject() };
            if (!ChangeServiceFailureActions(service, ServiceConfigFailureActions, ref recovery))
                throw Error("ChangeServiceConfig2 (recovery)");
        }
        finally { pinned.Free(); }
    }

    private static Win32Exception Error(string operation) => Error(operation, Marshal.GetLastWin32Error());
    private static Win32Exception Error(string operation, int error) =>
        new(error, $"{operation}: {new Win32Exception(error).Message} (Windows: {error})");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ServiceDescription { public string Text; }

    [StructLayout(LayoutKind.Sequential)]
    private struct FailureActions
    {
        public uint ResetPeriod;
        public IntPtr RebootMessage;
        public IntPtr Command;
        public uint Count;
        public IntPtr Actions;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct RecoveryAction(uint type, uint delay)
    {
        public readonly uint Type = type;
        public readonly uint Delay = delay;
    }

    private sealed class ServiceHandle() : SafeHandleZeroOrMinusOneIsInvalid(ownsHandle: true)
    {
        protected override bool ReleaseHandle() => CloseServiceHandle(handle);
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ServiceHandle OpenSCManager(string? machine, string? database, uint access);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ServiceHandle OpenService(ServiceHandle manager, string serviceName, uint access);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ServiceHandle CreateService(ServiceHandle manager, string serviceName, string displayName,
        uint access, uint serviceType, uint startType, uint errorControl, string binaryPath,
        string? loadOrderGroup, IntPtr tagId, string? dependencies, string? account, string? password);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeServiceConfig(ServiceHandle service, uint serviceType, uint startType, uint errorControl,
        string? binaryPath, string? loadOrderGroup, IntPtr tagId, string? dependencies,
        string? account, string? password, string? displayName);

    [DllImport("advapi32.dll", EntryPoint = "ChangeServiceConfig2W", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeServiceDescription(ServiceHandle service, uint infoLevel, ref ServiceDescription description);

    [DllImport("advapi32.dll", EntryPoint = "ChangeServiceConfig2W", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeServiceFailureActions(ServiceHandle service, uint infoLevel, ref FailureActions actions);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteService(ServiceHandle service);

    [DllImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr service);
}
