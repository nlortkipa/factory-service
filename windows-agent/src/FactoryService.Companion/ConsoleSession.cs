using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace FactoryService.Companion;

internal interface IPanelSession
{
    bool CanPublish { get; }
    bool CanClear { get; }
}

internal sealed class ConsoleSession : IPanelSession
{
    private readonly int _sessionId = Process.GetCurrentProcess().SessionId;
    private readonly string _userSid = WindowsIdentity.GetCurrent().User!.Value;

    public bool CanPublish => ReadState() is { Unlocked: true, IsCurrentUser: true };
    // A background user must never erase the foreground user's QR.
    public bool CanClear => ReadState() is var state && (!state.Unlocked || state.IsCurrentUser);

    private (bool Unlocked, bool IsCurrentUser) ReadState()
    {
        uint consoleId = WTSGetActiveConsoleSessionId();
        if (consoleId == uint.MaxValue) return (false, false);
        if (!WTSQuerySessionInformation(IntPtr.Zero, consoleId, 25, out IntPtr buffer, out int size))
            return (true, false); // Unknown state: fail closed for both QR and clearing.
        try
        {
            // WTSINFOEX contains a DWORD level followed by an 8-byte-aligned union.
            // Only the level-1 header is needed here.
            if (size < 20 || Marshal.ReadInt32(buffer) != 1) return (true, false);
            bool unlocked = Marshal.ReadInt32(buffer, 12) == 0 && Marshal.ReadInt32(buffer, 16) == 1;
            if (consoleId != _sessionId) return (unlocked, false);
            string user = ReadString(consoleId, 5); // WTSUserName
            string domain = ReadString(consoleId, 7); // WTSDomainName
            if (string.IsNullOrWhiteSpace(user)) return (unlocked, false);
            try
            {
                var account = string.IsNullOrEmpty(domain) ? new NTAccount(user) : new NTAccount(domain, user);
                return (unlocked, account.Translate(typeof(SecurityIdentifier)).Value == _userSid);
            }
            catch (IdentityNotMappedException) { return (unlocked, false); }
        }
        finally { WTSFreeMemory(buffer); }
    }

    private static string ReadString(uint sessionId, int informationClass)
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, sessionId, informationClass, out IntPtr buffer, out _)) return "";
        try { return Marshal.PtrToStringUni(buffer) ?? ""; }
        finally { WTSFreeMemory(buffer); }
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQuerySessionInformation(IntPtr server, uint sessionId, int informationClass,
        out IntPtr buffer, out int bytesReturned);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);
}
