using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Clauddy.Logging;
using Clauddy.Services;
using Clauddy.TrayIcon;
using Clauddy.ViewModels;
using Clauddy.Views;

namespace Clauddy;

public partial class App : System.Windows.Application
{
    private Mutex? _singleton;
    private HttpListenerService? _http;
    private EndpointFile? _endpoint;
    private DispatcherTimer? _gcTimer;
    private DispatcherTimer? _usageTimer;
    private FileLogger? _log;
    private TrayController? _tray;
    private MainViewModel? _vm;
    private Settings _settings = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Contains("--uninstall-hooks"))
        {
            try
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var scripts = System.IO.Path.Combine(AppContext.BaseDirectory, "hooks");
                new Clauddy.Services.HookInstaller(home, scripts).UninstallWindows();
            }
            catch { }
            Shutdown(); return;
        }

        _log = FileLogger.Default();
        _log.Info("Clauddy starting");

        _singleton = new Mutex(true, "Clauddy.SingleInstance", out var owned);
        if (!owned) { _log.Info("Another instance running; exiting"); Shutdown(); return; }

        var store = new SessionStore();
        var resolver = new LabelResolver(new GitProbe());
        _http = new HttpListenerService(store, resolver)
        {
            Marshal = a => Dispatcher.Invoke(a),
            Log = msg => _log?.Info(msg)
        };
        var url = _http.Start();

        _endpoint = new EndpointFile(EndpointFile.DefaultPath);
        _endpoint.Write(url);
        _log.Info($"Listening on {url}; endpoint written to {EndpointFile.DefaultPath}");

        var lifecycle = new LifecycleManager(store, () => DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30),
            LifecycleManager.DefaultIsAlive);
        _gcTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _gcTimer.Tick += (_, _) => lifecycle.Tick();
        _gcTimer.Start();

        _settings = new SettingsStore(SettingsStore.DefaultPath).Load();
        _vm = new MainViewModel(store);
        _vm.Scale = _settings.Scale > 0 ? _settings.Scale : 1.0;
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Scale))
            {
                _settings.Scale = _vm.Scale;
                new SettingsStore(SettingsStore.DefaultPath).Save(_settings);
            }
        };
        var win = new MainWindow(_vm);
        ApplyWindowPosition(win, _settings);
        win.LocationChanged += (_, _) =>
        {
            _settings.WindowX = win.Left;
            _settings.WindowY = win.Top;
            new SettingsStore(SettingsStore.DefaultPath).Save(_settings);
        };
        win.Show();
        // Auto-hide when no Claude Code sessions are active. Pops back in when a hook fires;
        // disappears ~30s after the last session ends (via SessionEnd or liveness GC).
        if (_vm.Tiles.Count == 0) win.Hide();
        _vm.Tiles.CollectionChanged += (_, _) =>
        {
            if (_vm.Tiles.Count > 0 && !win.IsVisible) win.Show();
            else if (_vm.Tiles.Count == 0 && win.IsVisible) win.Hide();
        };
        var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName!;
        var autoStart = AutoStartService.Default(exePath);
        _tray = new TrayController(win, autoStart,
            onManageHooks: () => Task.Run(() => RunHookSetupAsync()),
            onCalibrate:   () => Task.Run(() => RunCalibrateAsync()),
            log: _log);
        // Right-click on the widget itself opens the same menu as the tray icon —
        // a recovery path for when the system tray fails to render Clauddy's icon.
        win.ContextMenuFactory = () => _tray!.BuildMenu();
        MainWindow = win;

        // Refresh /usage-style stats every 60s by walking transcript files.
        var usageStats = UsageStatsService.Default();
        void RefreshUsage()
        {
            try
            {
                var stats = usageStats.Compute(DateTimeOffset.UtcNow);
                _vm.UpdateUsage(stats, _settings.Quota5hTokens, _settings.Quota7dTokens);
            }
            catch (Exception ex) { _log?.Warn($"UsageStats refresh failed: {ex.Message}"); }
        }
        RefreshUsage();
        _usageTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _usageTimer.Tick += (_, _) => RefreshUsage();
        _usageTimer.Start();

        if (FirstRunDetector.Default().IsFirstRun())
            _ = RunFirstRunWizardAsync();
    }

    /// <summary>
    /// First launch flow: install hooks, then immediately offer calibration so the
    /// metrics row shows percentages instead of raw token counts. If the user cancels
    /// hook setup, we skip calibration and re-prompt the whole wizard next launch.
    /// </summary>
    private async Task RunFirstRunWizardAsync()
    {
        await RunHookSetupAsync();
        // IsFirstRun flips to false only when the hook ledger has been written.
        // If the user canceled the hook dialog, leave calibration for next launch.
        if (!FirstRunDetector.Default().IsFirstRun())
            await RunCalibrateAsync();
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
        new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                 SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight)
            .Contains(new Point(x, y));

    protected override void OnExit(ExitEventArgs e)
    {
        _log?.Info("Clauddy shutting down");
        _gcTimer?.Stop();
        _usageTimer?.Stop();
        _http?.Stop();
        _endpoint?.Delete();
        _singleton?.ReleaseMutex();
        _tray?.Dispose();
        base.OnExit(e);
    }

    private async Task RunCalibrateAsync()
    {
        await Dispatcher.InvokeAsync(() =>
        {
            try
            {
                var snapshot = UsageStatsService.Default().Compute(DateTimeOffset.UtcNow);
                var dlg = new CalibrateDialog(snapshot, _settings.Plan, _settings.Quota5hTokens, _settings.Quota7dTokens);
                if (dlg.ShowDialog() != true) return;

                _settings.Plan = dlg.SelectedPlan;
                _settings.Quota5hTokens = dlg.Quota5hTokens;
                _settings.Quota7dTokens = dlg.Quota7dTokens;
                new SettingsStore(SettingsStore.DefaultPath).Save(_settings);
                _vm?.UpdateUsage(snapshot, _settings.Quota5hTokens, _settings.Quota7dTokens);
                _log?.Info($"Calibrated: plan={_settings.Plan}, 5h={_settings.Quota5hTokens}, 7d={_settings.Quota7dTokens}");
            }
            catch (Exception ex)
            {
                _log?.Error($"Calibrate failed: {ex}");
                System.Windows.MessageBox.Show($"Calibrate failed:\n{ex.Message}", "Clauddy",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        });
    }

    private async Task RunHookSetupAsync()
    {
        await Dispatcher.InvokeAsync(() =>
        {
            var detector = WslDistroDetector.Default();
            var distros = detector.List();
            var dlg = new HookSetupDialog(distros);
            var ok = dlg.ShowDialog() == true;
            if (!ok) return;

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var scripts = Path.Combine(AppContext.BaseDirectory, "hooks");
            var inst = new HookInstaller(home, scripts);

            try
            {
                if (dlg.InstallWindows) inst.InstallWindows();
                foreach (var d in dlg.InstallDistros) InstallWslDistro(d, home);

                System.Windows.MessageBox.Show("Hooks installed. Restart any open Claude Code sessions for changes to take effect.",
                    "Clauddy", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                _log?.Info($"Hooks installed: windows={dlg.InstallWindows}, wsl=[{string.Join(",", dlg.InstallDistros)}]");
            }
            catch (Exception ex)
            {
                _log?.Error($"Hook install failed: {ex}");
                System.Windows.MessageBox.Show($"Hook install failed:\n{ex.Message}", "Clauddy",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        });
    }

    private void InstallWslDistro(string distro, string winHome)
    {
        var winUser = Environment.UserName;
        var scriptWinPath = Path.Combine(winHome, ".clauddy", "hooks", "install-wsl.sh");
        if (!File.Exists(scriptWinPath))
            File.Copy(Path.Combine(AppContext.BaseDirectory, "hooks", "install-wsl.sh"), scriptWinPath);
        // Convert C:\Users\Ron\.clauddy\hooks\install-wsl.sh to /mnt/c/Users/Ron/.clauddy/hooks/install-wsl.sh
        var wslPath = "/mnt/" + char.ToLowerInvariant(scriptWinPath[0]) +
                      scriptWinPath.Substring(2).Replace('\\', '/');
        var psi = new ProcessStartInfo("wsl.exe",
            $"-d \"{distro}\" -- bash \"{wslPath}\" \"{winUser}\"")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        using var p = Process.Start(psi)!;
        p.WaitForExit(15000);
        if (p.ExitCode != 0)
            throw new Exception($"WSL install for '{distro}' failed: {p.StandardError.ReadToEnd()}");

        // Now patch ~/.claude/settings.json INSIDE the distro.
        // Robustness: if settings.json doesn't exist, treat it as empty {}.
        // Use a heredoc so we don't have to escape JSON quotes through layered shells.
        var hooksJson = EmbeddedHooksJson();
        var patchScript = "mkdir -p ~/.claude && " +
                          "[ -f ~/.claude/settings.json ] || echo '{}' > ~/.claude/settings.json && " +
                          $"jq -s '.[0] * .[1]' ~/.claude/settings.json <(cat <<'EOF'\n{hooksJson}\nEOF\n) > ~/.claude/settings.json.new && " +
                          "mv ~/.claude/settings.json.new ~/.claude/settings.json";
        var psi2 = new ProcessStartInfo("wsl.exe",
            $"-d \"{distro}\" -- bash -c \"{patchScript.Replace("\"", "\\\"")}\"")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        using var p2 = Process.Start(psi2)!;
        p2.WaitForExit(15000);
        if (p2.ExitCode != 0)
            throw new Exception($"WSL settings.json patch for '{distro}' failed: {p2.StandardError.ReadToEnd()}");
    }

    private static string EmbeddedHooksJson()
    {
        var hookCmd = "bash ~/.clauddy/hooks/clauddy-hook.sh";
        var hooksObj = new System.Text.Json.Nodes.JsonObject();
        foreach (var evt in HookInstaller.HookEvents)
            hooksObj[evt] = new System.Text.Json.Nodes.JsonArray(HookInstaller.BuildHookEntry(hookCmd));
        return new System.Text.Json.Nodes.JsonObject { ["hooks"] = hooksObj }.ToJsonString();
    }
}
