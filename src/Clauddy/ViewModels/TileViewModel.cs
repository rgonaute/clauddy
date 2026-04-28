using System.ComponentModel;
using System.Runtime.CompilerServices;
using Clauddy.Models;

namespace Clauddy.ViewModels;

public class TileViewModel : INotifyPropertyChanged
{
    private string _label = "";
    private SessionState _state;
    private long _tokens;

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

    public long Tokens
    {
        get => _tokens;
        set { if (_tokens != value) { _tokens = value; OnChanged(); OnChanged(nameof(TokensDisplay)); } }
    }

    public string TokensDisplay => Format(_tokens);

    private static string Format(long n) => n switch
    {
        < 1_000          => n.ToString(),
        < 10_000         => $"{n / 1000.0:0.0}K",
        < 1_000_000      => $"{n / 1000:0}K",
        < 10_000_000     => $"{n / 1_000_000.0:0.0}M",
        _                => $"{n / 1_000_000:0}M"
    };

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
