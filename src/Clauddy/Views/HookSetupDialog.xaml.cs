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
    public ObservableCollection<DistroOption> Distros { get; } = new();

    public HookSetupDialog(IEnumerable<string> distros)
    {
        InitializeComponent();
        foreach (var d in distros) Distros.Add(new DistroOption { Name = d });
        DistroList.ItemsSource = Distros;
        if (Distros.Count == 0) NoDistros.Visibility = System.Windows.Visibility.Visible;
    }

    private void InstallBtn_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        InstallWindows = WindowsCheck.IsChecked == true;
        InstallDistros = Distros.Where(d => d.Selected).Select(d => d.Name).ToList();
        DialogResult = true; Close();
    }

    private void CancelBtn_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        DialogResult = false; Close();
    }
}
