using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;

namespace Clauddy.Views;

public class DistroOption : INotifyPropertyChanged
{
    public string Name { get; init; } = "";
    private bool _selected;
    public bool Selected
    {
        get => _selected;
        set { _selected = value; PropertyChanged?.Invoke(this, new(nameof(Selected))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class HookSetupDialog : System.Windows.Window
{
    public bool InstallWindows { get; private set; }
    public List<string> InstallDistros { get; private set; } = new();
    public long Quota5hTokens { get; private set; }
    public ObservableCollection<DistroOption> Distros { get; } = new();

    public HookSetupDialog(IEnumerable<string> distros, long initialQuota = 0)
    {
        InitializeComponent();
        foreach (var d in distros) Distros.Add(new DistroOption { Name = d });
        DistroList.ItemsSource = Distros;
        if (Distros.Count == 0) NoDistros.Visibility = System.Windows.Visibility.Visible;
        if (initialQuota > 0) QuotaInput.Text = initialQuota.ToString();
    }

    private void InstallBtn_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        InstallWindows = WindowsCheck.IsChecked == true;
        InstallDistros = Distros.Where(d => d.Selected).Select(d => d.Name).ToList();
        Quota5hTokens = long.TryParse((QuotaInput.Text ?? "").Trim(), out var q) && q > 0 ? q : 0;
        DialogResult = true; Close();
    }

    private void CancelBtn_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        DialogResult = false; Close();
    }

    private void QuotaInput_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        // Numeric-only input: reject anything else.
        e.Handled = !int.TryParse(e.Text, out _);
    }
}
