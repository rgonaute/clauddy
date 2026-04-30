using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Clauddy.Logging;
using Clauddy.Services;
using H.NotifyIcon;

namespace Clauddy.TrayIcon;

public class TrayController
{
    private readonly TaskbarIcon _icon;
    private readonly Window _window;
    private readonly AutoStartService _autoStart;
    private readonly Func<Task> _onManageHooks;
    private readonly Func<Task> _onCalibrate;
    private readonly FileLogger? _log;

    public TrayController(Window window, AutoStartService autoStart,
        Func<Task> onManageHooks, Func<Task> onCalibrate, FileLogger? log = null)
    {
        _window = window;
        _autoStart = autoStart;
        _onManageHooks = onManageHooks;
        _onCalibrate = onCalibrate;
        _log = log;

        _icon = new TaskbarIcon
        {
            IconSource = new System.Windows.Media.Imaging.BitmapImage(
                new Uri("pack://application:,,,/Assets/tray.ico")),
            ToolTipText = "Clauddy"
        };
        _icon.ContextMenu = BuildMenu();

        // H.NotifyIcon registers the Win32 Shell_NotifyIcon hook lazily — when constructed
        // outside a XAML visual tree it can fail to fire Loaded, leaving the icon invisible.
        // ForceCreate is the documented escape hatch; "false" keeps Efficiency Mode off.
        try
        {
            if (!_icon.IsCreated) _icon.ForceCreate(enablesEfficiencyMode: false);
            _log?.Info($"Tray icon: IsCreated={_icon.IsCreated}");
        }
        catch (Exception ex)
        {
            _log?.Error($"Tray icon ForceCreate failed: {ex}");
        }
    }

    /// <summary>
    /// Builds a fresh context menu with the same items as the tray menu.
    /// Used both by the tray icon and by the widget's right-click fallback,
    /// so the user can quit/calibrate even if the system tray flakes out.
    /// </summary>
    public ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();

        var showHide = new System.Windows.Controls.MenuItem { Header = _window.IsVisible ? "Hide widget" : "Show widget" };
        showHide.Click += (_, _) =>
        {
            if (_window.IsVisible) _window.Hide();
            else _window.Show();
        };
        // Auto-hide can flip Visibility without touching the menu — keep the label synced.
        _window.IsVisibleChanged += (_, _) =>
            showHide.Header = _window.IsVisible ? "Hide widget" : "Show widget";
        menu.Items.Add(showHide);

        var reset = new System.Windows.Controls.MenuItem { Header = "Reset position" };
        reset.Click += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            _window.Left = area.Right - _window.ActualWidth - 16;
            _window.Top = area.Bottom - _window.ActualHeight - 16;
        };
        menu.Items.Add(reset);

        var login = new System.Windows.Controls.MenuItem { Header = "Run at login", IsCheckable = true, IsChecked = _autoStart.IsEnabled() };
        login.Click += (_, _) =>
        {
            if (login.IsChecked) _autoStart.Enable();
            else _autoStart.Disable();
        };
        menu.Items.Add(login);

        var manage = new System.Windows.Controls.MenuItem { Header = "Manage hooks…" };
        manage.Click += async (_, _) => await _onManageHooks();
        menu.Items.Add(manage);

        var calibrate = new System.Windows.Controls.MenuItem { Header = "Calibrate /usage…" };
        calibrate.Click += async (_, _) => await _onCalibrate();
        menu.Items.Add(calibrate);

        var about = new System.Windows.Controls.MenuItem { Header = "About" };
        about.Click += (_, _) => System.Windows.MessageBox.Show(
            $"Clauddy {Assembly.GetExecutingAssembly().GetName().Version}\n" +
            "Persistent widget for Claude Code session state.",
            "Clauddy", MessageBoxButton.OK, MessageBoxImage.Information);
        menu.Items.Add(about);

        menu.Items.Add(new Separator());

        var quit = new System.Windows.Controls.MenuItem { Header = "Quit" };
        quit.Click += (_, _) => System.Windows.Application.Current.Shutdown();
        menu.Items.Add(quit);

        return menu;
    }

    public void Dispose() => _icon.Dispose();
}
