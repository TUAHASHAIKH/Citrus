param([string]$Application = '')
$ErrorActionPreference = 'Stop'
if (!$Application) { $Application = Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\payload\Citrus.exe' }
$Application = [IO.Path]::GetFullPath($Application)
# A private desktop exercises the real entry point without activating or exiting
# the user's existing Citrus instance on the interactive desktop.
Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
public static class CitrusDesktopCheck {
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    public struct StartupInfo {
        public int cb; public string reserved, desktop, title;
        public int x,y,xSize,ySize,xChars,yChars,fill,flags;
        public short show, reservedSize; public IntPtr reservedData,input,output,error;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessInfo { public IntPtr process, thread; public int pid, tid; }
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    public static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr mode, int flags, uint access, IntPtr security);
    [DllImport("user32.dll")] public static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern bool CreateProcess(string application, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes,
        bool inherit, int flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInfo process);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll")] static extern bool GetExitCodeProcess(IntPtr handle, out uint code);
    public static uint WaitExit(IntPtr handle, uint milliseconds) {
        try {
            if (WaitForSingleObject(handle, milliseconds) != 0) throw new Exception("Child process did not exit before the deadline.");
            uint code; if (!GetExitCodeProcess(handle, out code)) throw new Win32Exception();
            return code;
        } finally { CloseHandle(handle); }
    }
    public static ProcessInfo Start(string application, string arguments, string desktop) {
        var startup = new StartupInfo { cb=Marshal.SizeOf(typeof(StartupInfo)), desktop=desktop };
        ProcessInfo process;
        if (!CreateProcess(application, new StringBuilder("\""+application+"\" "+arguments), IntPtr.Zero, IntPtr.Zero,
            false, 0x08000000, IntPtr.Zero, System.IO.Path.GetDirectoryName(application), ref startup, out process))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        CloseHandle(process.thread); return process;
    }
}
'@
$desktopName = 'CitrusCheck-' + [Guid]::NewGuid().ToString('N')
$desktop = [CitrusDesktopCheck]::CreateDesktop($desktopName, [IntPtr]::Zero, [IntPtr]::Zero, 0, 0x10000000, [IntPtr]::Zero)
if ($desktop -eq [IntPtr]::Zero) { throw 'Could not create the isolated test desktop.' }
$running = $null
try {
    $instance = [CitrusDesktopCheck]::Start($Application, '--background', $desktopName)
    $running = [Diagnostics.Process]::GetProcessById($instance.pid)
    $signal = $null
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while (!$signal -and [DateTime]::UtcNow -lt $deadline) {
        try { $signal = [Threading.EventWaitHandle]::OpenExisting("Local.Citrus.Shutdown.$desktopName") }
        catch [Threading.WaitHandleCannotBeOpenedException] { Start-Sleep -Milliseconds 100 }
        if ($running.HasExited) { throw 'Background tray instance exited during startup.' }
    }
    if (!$signal) { throw 'Background tray instance did not expose its shutdown signal.' }
    $signal.Dispose()
    $duplicate = [CitrusDesktopCheck]::Start($Application, '--background', $desktopName)
    if ([CitrusDesktopCheck]::WaitExit($duplicate.process, 5000) -ne 0) { throw 'Duplicate background launch failed.' }
    if ($running.HasExited) { throw 'Duplicate launch terminated the original instance.' }
    $shutdown = [CitrusDesktopCheck]::Start($Application, '--shutdown', $desktopName)
    if ([CitrusDesktopCheck]::WaitExit($shutdown.process, 35000) -ne 0) { throw 'Graceful shutdown command failed.' }
    if (!$running.WaitForExit(5000)) { throw 'Background tray app remained alive after shutdown.' }
    Write-Host 'PASS: real background entry point, desktop-scoped single instance, graceful installer shutdown.'
} finally {
    if ($running) {
        if (!$running.HasExited) { $running.Kill(); $running.WaitForExit(5000) | Out-Null }
        $running.Dispose()
    }
    if ($instance.process -ne [IntPtr]::Zero) { [CitrusDesktopCheck]::CloseHandle($instance.process) | Out-Null }
    [CitrusDesktopCheck]::CloseDesktop($desktop) | Out-Null
}
