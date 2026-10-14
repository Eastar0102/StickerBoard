using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace StickerBoard;

public partial class App : Application
{
    static Mutex? _mutex;
    static bool _ownsMutex;

    /// <summary>Set before closing the main window to start a fresh instance on exit (e.g. after a language change).</summary>
    public static bool RestartRequested { get; set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        // One instance per data folder
        var id = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(
            System.Text.Encoding.UTF8.GetBytes(Storage.Dir.ToLowerInvariant())))[..16];
        _mutex = new Mutex(true, @"Local\StickerBoard." + id, out bool created);
        _ownsMutex = created;
        if (!created)
        {
            ActivateExistingInstance();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log(args.Exception);
            args.Handled = true;
        };

        base.OnStartup(e);
        var data = Storage.Load();
        L.Init(data.Language);
        new MainWindow(data).Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Release the single-instance lock before starting the replacement process
        if (_ownsMutex) _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        _mutex = null;
        if (RestartRequested && Environment.ProcessPath is { } exe)
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        base.OnExit(e);
    }

    public static void Log(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Storage.Dir);
            File.AppendAllText(Path.Combine(Storage.Dir, "error.log"), $"[{DateTime.Now:s}] {ex}\n\n");
        }
        catch { }
    }

    static void ActivateExistingInstance()
    {
        var me = Process.GetCurrentProcess();
        foreach (var p in Process.GetProcessesByName(me.ProcessName))
        {
            if (p.Id == me.Id || p.MainWindowHandle == IntPtr.Zero) continue;
            ShowWindow(p.MainWindowHandle, 9 /* SW_RESTORE */);
            SetForegroundWindow(p.MainWindowHandle);
            break;
        }
    }

    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
}
