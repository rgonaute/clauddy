using System.ComponentModel;
using System.Runtime.CompilerServices;
using Clauddy.Models;

namespace Clauddy.ViewModels;

public class TileViewModel : INotifyPropertyChanged
{
    private string _label = "";
    private SessionState _state;

    public string Label
    {
        get => _label;
        set { if (_label != value) { _label = value; OnChanged(); } }
    }

    public SessionState State
    {
        get => _state;
        set { if (_state != value) { _state = value; OnChanged(); OnChanged(nameof(GifSource)); } }
    }

    public string GifSource => State switch
    {
        SessionState.Working  => "pack://application:,,,/Assets/working.gif",
        SessionState.Alerting => "pack://application:,,,/Assets/alerting.gif",
        _                     => "pack://application:,,,/Assets/chilling.gif"
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
