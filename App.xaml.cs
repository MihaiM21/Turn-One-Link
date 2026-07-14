using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using DotNetEnv;
using Application = System.Windows.Application;

namespace Turn_One_Link;

public partial class App : Application
{
    private System.Windows.Forms.NotifyIcon _trayIcon = null!;
    private bool _highResTimerActive;

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")] private static extern uint TimeBeginPeriod(uint uMilliseconds);
    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")] private static extern uint TimeEndPeriod(uint uMilliseconds);

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            if (TimeBeginPeriod(1) == 0) _highResTimerActive = true;
        }
        catch { }

        // Load environment variables from .env file
        try
        {
            var envPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".env");
            if (File.Exists(envPath))
            {
                Env.Load(envPath);
                System.Diagnostics.Debug.WriteLine("Environment variables loaded from .env file");
            }

            // Also try to load .env.local for local overrides
            var envLocalPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".env.local");
            if (File.Exists(envLocalPath))
            {
                Env.Load(envLocalPath);
                System.Diagnostics.Debug.WriteLine("Environment variables loaded from .env.local file");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load .env file: {ex.Message}");
        }

        base.OnStartup(e);

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "Turn One Link",
            Icon = LoadTrayIcon(),
            Visible = false
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => ShowMainWindow());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    public void MinimizeToTray()
    {
        MainWindow?.Hide();
        _trayIcon.Visible = true;
    }

    public void ShowMainWindow()
    {
        if (MainWindow == null) return;
        _trayIcon.Visible = false;
        MainWindow.Show();
        MainWindow.WindowState = WindowState.Normal;
        MainWindow.Activate();
    }

    public void ExitApp()
    {
        ((MainWindow)MainWindow!).PrepareExit();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        if (_highResTimerActive)
        {
            try { TimeEndPeriod(1); } catch { }
            _highResTimerActive = false;
        }
        base.OnExit(e);
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Content/logo32.png");
            var stream = GetResourceStream(uri)?.Stream;
            if (stream != null)
            {
                var bmp = new System.Drawing.Bitmap(stream);
                return System.Drawing.Icon.FromHandle(bmp.GetHicon());
            }
        }
        catch { }
        return System.Drawing.SystemIcons.Application;
    }
}
