using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using FactoryService.Common;
using Microsoft.Win32.SafeHandles;

namespace FactoryService.Service;

// Runs only in the LocalSystem service. Never borrow the installer's token or
// environment: each companion needs the logged-in session's identity and profile.
internal sealed class SessionCompanionLauncher
{
    private readonly string _executable = Path.Combine(AppContext.BaseDirectory, "FactoryService.Companion.exe");

    public static IReadOnlyList<int> GetLoggedInSessions()
    {
        if (!WTSEnumerateSessions(IntPtr.Zero, 0, 1, out IntPtr buffer, out int count))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var result = new List<int>();
            int size = Marshal.SizeOf<SessionInfo>();
            for (int index = 0; index < count; index++)
            {
                SessionInfo session = Marshal.PtrToStructure<SessionInfo>(buffer + index * size);
                if (session.SessionId == 0 || session.State is not (0 or 1 or 4)) continue;
                if (!WTSQuerySessionInformation(IntPtr.Zero, session.SessionId, 5, out IntPtr user, out _)) continue;
                try
                {
                    if (!string.IsNullOrWhiteSpace(Marshal.PtrToStringUni(user))) result.Add(session.SessionId);
                }
                finally { WTSFreeMemory(user); }
            }
            return result;
        }
        finally { WTSFreeMemory(buffer); }
    }

    public void EnsureStarted(int sessionId)
    {
        if (!File.Exists(_executable)) throw new FileNotFoundException("Installed companion is missing.", _executable);
        if (!WTSQueryUserToken(sessionId, out SafeAccessTokenHandle token))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Cannot obtain user token for session {sessionId}.");
        using (token)
        {
            using var identity = new WindowsIdentity(token.DangerousGetHandle());
            if (IsRunning(sessionId, identity.User!.Value)) return;
            // Logged-in profiles are already loaded by Windows. Do not inherit any
            // LOCALAPPDATA/config/operator overrides from the service or installer.
            if (!CreateEnvironmentBlock(out IntPtr environment, token, false))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var startup = new StartupInfo
                {
                    Size = Marshal.SizeOf<StartupInfo>(), Desktop = @"winsta0\default"
                };
                var command = new StringBuilder($"\"{_executable}\" --background --installed");
                if (!CreateProcessAsUser(token, _executable, command, IntPtr.Zero, IntPtr.Zero,
                        false, 0x00000400 | 0x08000000, environment, AppContext.BaseDirectory, ref startup, out ProcessInfo process))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"Cannot start companion in session {sessionId}.");
                CloseHandle(process.Thread);
                CloseHandle(process.Process);
                AgentLog.Machine($"companion started; sessionId={sessionId}; user={identity.Name}; pid={process.ProcessId}");
            }
            finally { DestroyEnvironmentBlock(environment); }
        }
    }

    private bool IsRunning(int sessionId, string userSid)
    {
        Process[] processes = Process.GetProcessesByName("FactoryService.Companion");
        try
        {
            foreach (Process process in processes)
            {
                try
                {
                    if (process.SessionId != sessionId ||
                        !string.Equals(process.MainModule?.FileName, _executable, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!OpenProcessToken(process.Handle, 0x0008, out SafeAccessTokenHandle token)) continue;
                    using (token)
                    using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
                        if (identity.User?.Value == userSid) return true;
                }
                catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
                {
                    // A process may exit between enumeration and inspection.
                }
            }
            return false;
        }
        finally { foreach (Process process in processes) process.Dispose(); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SessionInfo { public int SessionId; public IntPtr StationName; public int State; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public short ShowWindow, ReservedSize;
        public IntPtr ReservedPointer, StandardInput, StandardOutput, StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInfo { public IntPtr Process, Thread; public int ProcessId, ThreadId; }

    [DllImport("wtsapi32.dll", EntryPoint = "WTSEnumerateSessionsW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSEnumerateSessions(IntPtr server, int reserved, int version, out IntPtr sessions, out int count);
    [DllImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQuerySessionInformation(IntPtr server, int sessionId, int informationClass, out IntPtr buffer, out int size);
    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQueryUserToken(int sessionId, out SafeAccessTokenHandle token);
    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);
    [DllImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateEnvironmentBlock(out IntPtr environment, SafeAccessTokenHandle token, [MarshalAs(UnmanagedType.Bool)] bool inherit);
    [DllImport("userenv.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyEnvironmentBlock(IntPtr environment);
    [DllImport("advapi32.dll", EntryPoint = "CreateProcessAsUserW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessAsUser(SafeAccessTokenHandle token, string application, StringBuilder command,
        IntPtr processAttributes, IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        int flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInfo process);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, int access, out SafeAccessTokenHandle token);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
