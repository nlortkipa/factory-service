using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Folder = System.Environment.SpecialFolder;

namespace FactoryService.Setup;

internal static class Shortcuts
{
    private const string Name = "Kartuli Cement - Factory Service.lnk";
    private const string LegacyName = "ქართული ცემენტი — შეფასება.lnk";
    private static readonly Guid ShellLinkClass = new("00021401-0000-0000-C000-000000000046");

    public static string IconPath => Path.Combine(InstalledClient.InstallDirectory, "FactoryService.ico");
    public static bool Exists =>
        (ExistsIn(Folder.CommonDesktopDirectory) || ExistsIn(Folder.DesktopDirectory)) &&
        (ExistsIn(Folder.CommonPrograms) || ExistsIn(Folder.Programs));

    public static void Create()
    {
        TryCreate(Folder.CommonDesktopDirectory);
        TryCreate(Folder.CommonPrograms);
    }

    public static string[] CreateForCurrentUser()
    {
        var failures = new List<string>();
        if (!ExistsIn(Folder.CommonDesktopDirectory) && TryCreate(Folder.DesktopDirectory) is { } desktopError)
            failures.Add(desktopError);
        if (!ExistsIn(Folder.CommonPrograms) && TryCreate(Folder.Programs) is { } startMenuError)
            failures.Add(startMenuError);
        return failures.ToArray();
    }

    public static void Remove()
    {
        RemoveFrom(Folder.CommonDesktopDirectory);
        RemoveFrom(Folder.CommonPrograms);
    }

    public static void RemoveForCurrentUser()
    {
        RemoveFrom(Folder.DesktopDirectory);
        RemoveFrom(Folder.Programs);
    }

    private static string? GetPath(Folder folder, string name)
    {
        string directory = Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify);
        return string.IsNullOrEmpty(directory) ? null : Path.Combine(directory, name);
    }

    private static bool ExistsIn(Folder folder) => File.Exists(GetPath(folder, Name));

    private static string? TryCreate(Folder folder)
    {
        string? path = GetPath(folder, Name);
        if (path is null) return $"Windows საქაღალდე ვერ მოიძებნა: {folder}";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            Write(path);
            Delete(GetPath(folder, LegacyName));
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or COMException)
        {
            return $"{path}{Environment.NewLine}{exception.Message}";
        }
    }

    private static void RemoveFrom(Folder folder)
    {
        Delete(GetPath(folder, Name));
        Delete(GetPath(folder, LegacyName));
    }

    private static void Delete(string? path)
    {
        if (!File.Exists(path)) return;
        try { File.Delete(path!); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static void Write(string path)
    {
        string companion = InstalledClient.CompanionPath;
        Type type = Type.GetTypeFromCLSID(ShellLinkClass, throwOnError: true)!;
        var link = (IShellLinkW)Activator.CreateInstance(type)!;
        try
        {
            link.SetPath(companion);
            link.SetArguments("--installed");
            link.SetWorkingDirectory(InstalledClient.InstallDirectory);
            link.SetDescription("ქართული ცემენტი — შეფასება");
            link.SetIconLocation(File.Exists(IconPath) ? IconPath : companion, 0);
            ((IPersistFile)link).Save(path, true);
        }
        finally { Marshal.FinalReleaseComObject(link); }
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int capacity, IntPtr findData, uint flags);
        void GetIDList(out IntPtr itemIdList);
        void SetIDList(IntPtr itemIdList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder description, int capacity);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int capacity);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int capacity);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out ushort hotkey);
        void SetHotkey(ushort hotkey);
        void GetShowCmd(out int showCommand);
        void SetShowCmd(int showCommand);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int capacity, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr owner, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
