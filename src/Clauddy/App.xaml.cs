using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Clauddy.Logging;
using Clauddy.Services;
using Clauddy.ViewModels;

namespace Clauddy;

public partial class App : System.Windows.Application
{
    private Mutex? _singleton;
    private HttpListenerService? _http;
    private EndpointFile? _endpoint;
    private DispatcherTimer? _gcTimer;
    private FileLogger? _log;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _log = FileLogger.Default();
        _log.Info("Clauddy starting");

        _singleton = new Mutex(true, "Clauddy.SingleInstance", out var owned);
        if (!owned) { _log.Info("Another instance running; exiting"); Shutdown(); return; }

        var store = new SessionStore();
        var resolver = new LabelResolver(new GitProbe());
        _http = new HttpListenerService(store, resolver) { Marshal = a => Dispatcher.Invoke(a) };
        var url = _http.Start();

        _endpoint = new EndpointFile(EndpointFile.DefaultPath);
        _endpoint.Write(url);
        _log.Info($"Listening on {url}; endpoint written to {EndpointFile.DefaultPath}");

        var lifecycle = new LifecycleManager(store, () => DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30));
        _gcTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _gcTimer.Tick += (_, _) => lifecycle.Tick();
        _gcTimer.Start();

        var settings = new SettingsStore(SettingsStore.DefaultPath).Load();
        var vm = new MainViewModel(store);
        var win = new MainWindow(vm);
        ApplyWindowPosition(win, settings);
        win.LocationChanged += (_, _) =>
        {
            settings.WindowX = win.Left;
            settings.WindowY = win.Top;
            new SettingsStore(SettingsStore.DefaultPath).Save(settings);
        };
        win.Show();
        MainWindow = win;
    }

    private static void ApplyWindowPosition(Window w, Settings s)
    {
        if (s.WindowX is double x && s.WindowY is double y &&
            IsOnAnyScreen(x, y))
        {
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = x; w.Top = y;
        }
        else
        {
            var area = SystemParameters.WorkArea;
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Loaded += (_, _) =>
            {
                w.Left = area.Right - w.ActualWidth - 16;
                w.Top = area.Bottom - w.ActualHeight - 16;
            };
        }
    }

    private static bool IsOnAnyScreen(double x, double y) =>
        System.Windows.Forms.Screen.AllScreens.Any(scr => scr.WorkingArea.Contains((int)x, (int)y));

    protected override void OnExit(ExitEventArgs e)
    {
        _log?.Info("Clauddy shutting down");
        _gcTimer?.Stop();
        _http?.Stop();
        _endpoint?.Delete();
        _singleton?.ReleaseMutex();
        base.OnExit(e);
    }
}
