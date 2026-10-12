using System.Diagnostics;
using System.Text;
namespace ReplayPanel.Supervisor;
/// <summary>Opens the operating system's own folder dialog on the host running the supervisor.</summary>
public static class NativePicker
{
    private const string WindowsScript = "[Console]::OutputEncoding=[Text.Encoding]::UTF8;Add-Type -AssemblyName System.Windows.Forms;" +
        "$f=New-Object System.Windows.Forms.FolderBrowserDialog;$f.ShowNewFolderButton=$true;$f.SelectedPath=$env:REPLAY_PICK_INITIAL;" +
        "$o=New-Object System.Windows.Forms.Form;$o.TopMost=$true;$o.ShowInTaskbar=$false;$o.StartPosition='CenterScreen';$o.Opacity=0;$o.Show();" +
        "if($f.ShowDialog($o) -eq 'OK'){[Console]::Out.Write($f.SelectedPath)}";
    public static (string State, string Path) Pick(string initial)
    {
        if (OperatingSystem.IsWindows()) return Run("powershell.exe", ["-NoProfile", "-NonInteractive", "-STA", "-WindowStyle", "Hidden", "-Command", WindowsScript], initial);
        if (OperatingSystem.IsMacOS()) return Run("osascript", ["-e", "POSIX path of (choose folder)"], initial);
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))) return ("unavailable", "");
        var dir = initial.TrimEnd('/') + "/";
        var zenity = Run("zenity", ["--file-selection", "--directory", "--filename=" + dir], initial);
        return zenity.State == "unavailable" ? Run("kdialog", ["--getexistingdirectory", dir], initial) : zenity;
    }
    private static (string State, string Path) Run(string file, string[] args, string initial)
    {
        var info = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8 };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        info.Environment["REPLAY_PICK_INITIAL"] = initial;
        Process process;
        try { process = Process.Start(info)!; } catch (System.ComponentModel.Win32Exception) { return ("unavailable", ""); }
        using (process) {
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            if (process.ExitCode != 0 || output.Length == 0) return ("cancelled", "");
            if (output.Length > 3) output = output.TrimEnd('/', '\\');
            return ("done", output);
        }
    }
}
