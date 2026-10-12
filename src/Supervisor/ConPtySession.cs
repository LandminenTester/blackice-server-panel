using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace ReplayPanel.Supervisor;

// ConPTY owns the console. The webhost never owns or receives raw native handles.
public sealed class ConPtySession : IDisposable
{
    private IntPtr pseudo;
    private IntPtr processHandle;
    private FileStream? input;
    private FileStream? output;
    private Task? reader;
    public Process Process { get; private set; } = null!;
    public int? ExitCode() => processHandle!=IntPtr.Zero&&GetExitCodeProcess(processHandle,out var code)&&code!=259?unchecked((int)code):null;
    public string ImagePath()
    {
        var name = new System.Text.StringBuilder(32768);
        var length = name.Capacity;
        Check(QueryFullProcessImageName(processHandle, 0, name, ref length));
        return Path.GetFullPath(name.ToString());
    }
    public void Start(string executable, string directory, Action<string> onOutput, string[]? arguments = null)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("ConPTY requires Windows.");
        IntPtr inRead = IntPtr.Zero, inWrite = IntPtr.Zero, outRead = IntPtr.Zero, outWrite = IntPtr.Zero;
        IntPtr attributes = IntPtr.Zero;
        PROCESS_INFORMATION info = default;
        try
        {
            Check(CreatePipe(out inRead, out inWrite, IntPtr.Zero, 0));
            Check(CreatePipe(out outRead, out outWrite, IntPtr.Zero, 0));
            var hr = CreatePseudoConsole(new COORD(140, 40), inRead, outWrite, 0, out pseudo);
            if (hr != 0) Marshal.ThrowExceptionForHR(hr);
            CloseHandle(inRead); inRead = IntPtr.Zero;
            CloseHandle(outWrite); outWrite = IntPtr.Zero;
            nuint size = 0;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            attributes = Marshal.AllocHGlobal((nint)size);
            Check(InitializeProcThreadAttributeList(attributes, 1, 0, ref size));
            Check(UpdateProcThreadAttribute(attributes, 0, (nuint)0x00020016, pseudo, (nuint)IntPtr.Size, IntPtr.Zero, IntPtr.Zero));
            // Null explicit std handles prevent Windows from duplicating the supervisor's
            // redirected log handles into the child instead of using the pseudoconsole.
            var startup = new STARTUPINFOEX { StartupInfo = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFOEX>(), dwFlags = 0x100 }, lpAttributeList = attributes };
            var commandLine = string.Join(" ", new[] { executable }.Concat(arguments ?? []).Select(QuoteArgument));
            Check(CreateProcess(executable, new System.Text.StringBuilder(commandLine), IntPtr.Zero, IntPtr.Zero, false,
                0x00080000, IntPtr.Zero, directory, ref startup, out info));
            processHandle = info.hProcess;
            CloseHandle(info.hThread); info.hThread = IntPtr.Zero;
            Process = Process.GetProcessById((int)info.dwProcessId);
            input = new FileStream(new SafeFileHandle(inWrite, true), FileAccess.Write, 4096, false); inWrite = IntPtr.Zero;
            output = new FileStream(new SafeFileHandle(outRead, true), FileAccess.Read, 4096, false); outRead = IntPtr.Zero;
            reader = Task.Run(() => {
                try { using var text = new StreamReader(output, System.Text.Encoding.UTF8, false, 4096, leaveOpen: true);
                    var buffer = new char[4096]; int count;
                    while ((count = text.Read(buffer, 0, buffer.Length)) > 0) { onOutput(new string(buffer, 0, count)); }
                } catch (Exception e) when (e is IOException || e is ObjectDisposedException) { }
            });
        }
        catch { if (info.hThread != IntPtr.Zero) CloseHandle(info.hThread); Dispose(); throw; }
        finally
        {
            if (attributes != IntPtr.Zero) { DeleteProcThreadAttributeList(attributes); Marshal.FreeHGlobal(attributes); }
            foreach (var h in new[] { inRead, inWrite, outRead, outWrite }) if (h != IntPtr.Zero) CloseHandle(h);
        }
    }
    public Task SendAsync(string command)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(command + "\r");
        input!.Write(bytes); input.Flush();
        return Task.CompletedTask;
    }
    public void Dispose()
    {
        input?.Dispose(); input = null;
        var h = pseudo; pseudo = IntPtr.Zero;
        // Output remains drained while ClosePseudoConsole waits for its final output.
        if (h != IntPtr.Zero) ClosePseudoConsole(h);
        output?.Dispose(); output = null;
        if (processHandle != IntPtr.Zero) { CloseHandle(processHandle); processHandle = IntPtr.Zero; }
        Process?.Dispose();
    }
    private static void Check(bool ok) { if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error()); }
    private static string QuoteArgument(string value)
    {
        var escaped = System.Text.RegularExpressions.Regex.Replace(value, "(\\\\*)\"", "$1$1\\\"");
        escaped = System.Text.RegularExpressions.Regex.Replace(escaped, "(\\\\+)$", "$1$1");
        return "\"" + escaped + "\"";
    }
    [StructLayout(LayoutKind.Sequential)] private readonly record struct COORD(short X, short Y);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb; public string? lpReserved; public string? lpDesktop; public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }
    [StructLayout(LayoutKind.Sequential)] private struct STARTUPINFOEX { public STARTUPINFO StartupInfo; public IntPtr lpAttributeList; }
    [StructLayout(LayoutKind.Sequential)] private struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public uint dwProcessId, dwThreadId; }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreatePipe(out IntPtr read, out IntPtr write, IntPtr attributes, uint size);
    [DllImport("kernel32.dll")] private static extern int CreatePseudoConsole(COORD size, IntPtr input, IntPtr output, uint flags, out IntPtr handle);
    [DllImport("kernel32.dll")] private static extern void ClosePseudoConsole(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref nuint size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, nuint attribute, IntPtr value, nuint size, IntPtr previous, IntPtr returned);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcess(string application, System.Text.StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint creationFlags, IntPtr environment, string directory, ref STARTUPINFOEX startup, out PROCESS_INFORMATION info);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, System.Text.StringBuilder name, ref int size);
    [DllImport("kernel32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool GetExitCodeProcess(IntPtr process,out uint code);
}
