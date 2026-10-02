using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KeyTranslate;

internal static class Program
{
    private const string SingleInstanceName = "KeyTranslate.SingleInstance.v1";

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [STAThread]
    private static void Main()
    {
        using var singleInstance = new Mutex(true, SingleInstanceName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            ActivateExistingInstance();
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }

    private static void ActivateExistingInstance()
    {
        var currentProcess = Process.GetCurrentProcess();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id == currentProcess.Id || process.MainWindowHandle == IntPtr.Zero ||
                    !string.Equals(process.MainWindowTitle, "KeyTranslate", StringComparison.Ordinal))
                {
                    continue;
                }

                ShowWindow(process.MainWindowHandle, 9);
                SetForegroundWindow(process.MainWindowHandle);
                break;
            }
            finally
            {
                process.Dispose();
            }
        }
    }
}
