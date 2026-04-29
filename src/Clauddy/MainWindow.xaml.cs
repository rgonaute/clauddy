using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Clauddy.Services;
using Clauddy.ViewModels;

namespace Clauddy;

public partial class MainWindow : Window
{
    private const int GWL_EXSTYLE   = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int n);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int n, int v);

    private readonly MainViewModel _vm;
    private readonly TerminalFocuser _focuser = new();

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        SourceInitialized += OnSourceInitialized;
        PreviewMouseWheel += OnPreviewMouseWheel;
    }

    /// <summary>Hold Ctrl + scroll wheel to resize the widget. Saved by App on Scale change.</summary>
    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control) return;
        var step = e.Delta > 0 ? 0.1 : -0.1;
        _vm.Scale = Math.Round(_vm.Scale + step, 2);
        e.Handled = true;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }

    private void DragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void Pill_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is TileViewModel tile)
        {
            _vm.Selected = tile;
            // Best-effort: also focus the terminal that owns this Claude Code session.
            // Falls through silently if the chain doesn't yield a windowed ancestor.
            if (tile.Pid > 0) _focuser.TryFocus(tile.Pid);
        }
    }
}
