using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Clauddy.Services;
using Hardcodet.Wpf.TaskbarNotification;

namespace Clauddy.TrayIcon;

public class TrayController
{
    private readonly TaskbarIcon _icon;
    private readonly Window _window;
    private readonly AutoStartService _autoStart;
    private readonly Func<Task> _onManageHooks;

    public TrayController(Window window, AutoStartService autoStart, Func<Task> onManageHooks)
    {
        _window = window;
        _autoStart = autoStart;
        _onManageHooks = onManageHooks;

        _icon = new TaskbarIcon
        {
            IconSource = new System.Windows.Media.Imaging.BitmapImage(
                new Uri("pack://application:,,,/Assets/tray.ico")),
            ToolTipText = "Clauddy"
        };
        _icon.ContextMenu = BuildMenu();
    }

    private ContextMenu BuildMenu()
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
