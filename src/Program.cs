// DandyPicker
// Runs several Claude Desktop instances side by side, each with its own data
// folder (and therefore its own signed-in account), via Electron's
// --user-data-dir switch. Profiles live in profiles.json next to the .exe.
// Removing a profile here never deletes its data folder.
//
// Build with build.cmd (needs the .NET 10 SDK).

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace DandyPicker
{
    class App : Application
    {
        public override void Initialize() { AppTheme.Init(this); }
    }

    static class Program
    {
        public const string Title = "DandyPicker";

        public static string LogPath { get { return Path.Combine(AppContext.BaseDirectory, "dandypicker-error.log"); } }

        [DllImport("user32.dll")] static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);

        [STAThread]
        static void Main()
        {
            bool first;
            using (var mutex = new Mutex(true, "DandyPicker.SingleInstance", out first))
            {
                if (!first)
                {
                    // Already open: bring that window forward instead of opening a second one.
                    IntPtr h = FindWindow(null, Title);
                    if (h != IntPtr.Zero) { ShowWindow(h, 9); SetForegroundWindow(h); }
                    return;
                }

                AppBuilder.Configure<App>()
                    .UsePlatformDetect()
                    .With(new Win32PlatformOptions { OverlayPopups = true })
                    .SetupWithoutStarting();
                Dispatcher.UIThread.UnhandledException += (s, e) =>
                {
                    Report(e.Exception);
                    e.Handled = true;
                };
                try
                {
                    var w = new MainWindow();
                    var done = new CancellationTokenSource();
                    w.Closed += delegate { done.Cancel(); };
                    w.Show();
                    Dispatcher.UIThread.MainLoop(done.Token);
                }
                catch (Exception e)
                {
                    Report(e);
                }
            }
        }

        // Anything unexpected: write it to the log and say where, so it can be fixed.
        public static void Report(Exception e)
        {
            try { File.AppendAllText(LogPath, DateTime.Now + "\n" + e + "\n\n"); } catch { }
            try { Dialogs.Info(null, "Something went wrong", e.Message + "\n\nDetails were saved to:\n" + LogPath); } catch { }
        }
    }
}
