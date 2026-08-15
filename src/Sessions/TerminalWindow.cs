using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ClaudeLauncher.Sessions;

/// <summary>Brings the Windows Terminal window forward. P/Invoke, like Tui/Term.cs - no dependencies.</summary>
public static class TerminalWindow
{
    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr window, int command);

    /// <summary>False when no Windows Terminal window could be found.</summary>
    public static bool Raise()
    {
        try
        {
            foreach (var process in Process.GetProcessesByName("WindowsTerminal"))
            {
                using (process)
                {
                    if (process.MainWindowHandle == IntPtr.Zero) continue;

                    ShowWindowAsync(process.MainWindowHandle, SwRestore);

                    // Windows may refuse this when we are not the foreground app;
                    // it then flashes the taskbar button, which is good enough.
                    SetForegroundWindow(process.MainWindowHandle);
                    return true;
                }
            }
        }
        catch
        {
            // Nothing here is worth failing a keystroke over.
        }

        return false;
    }
}
