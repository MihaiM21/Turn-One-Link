using System.Windows;
using Application = System.Windows.Application;

namespace Turn_One_Link;

public partial class App : Application
{
    private System.Windows.Forms.NotifyIcon _trayIcon = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
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
