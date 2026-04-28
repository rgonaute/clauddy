using System.ComponentModel;
using System.Runtime.CompilerServices;
using Clauddy.Models;

namespace Clauddy.ViewModels;

/// <summary>
/// Per-session view-model. Surfaces label + state for the pill bar, plus tokens
/// for the metrics row when this tile is selected.
/// </summary>
public class TileViewModel : INotifyPropertyChanged
{
    public string SessionId { get; init; } = "";

    /// <summary>Hook script's PID (its own $$). Used to walk up to the terminal hwnd on click.</summary>
    public int Pid { get; set; }

    private string _label = "";
    public string Label { get => _label; set { if (_label != value) { _label = value; OnChanged(); } } }

    private SessionState _state;
    public SessionState State
    {
        get => _state;
        set
        {
            if (_state == value) return;
            _state = value;
            OnChanged();
            OnChanged(nameof(IsAlerting));
            OnChanged(nameof(StateDot));
        }
    }

    public bool IsAlerting => _state == SessionState.Alerting;

    /// <summary>Color string for the small status dot on the pill.</summary>
    public string StateDot => _state switch
    {
        SessionState.Alerting => "#ef4444",
        SessionState.Working  => "#f59e0b",
        _                     => "#6b7280"
    };

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnChanged(); } }
    }

    private long _inputTokens;
    public long InputTokens { get => _inputTokens; set { if (_inputTokens != value) { _inputTokens = value; OnChanged(); OnChanged(nameof(TotalTokens)); OnChanged(nameof(CacheHitRate)); } } }

    private long _outputTokens;
    public long OutputTokens { get => _outputTokens; set { if (_outputTokens != value) { _outputTokens = value; OnChanged(); OnChanged(nameof(TotalTokens)); } } }

    private long _cacheCreationTokens;
    public long CacheCreationTokens { get => _cacheCreationTokens; set { if (_cacheCreationTokens != value) { _cacheCreationTokens = value; OnChanged(); OnChanged(nameof(TotalTokens)); OnChanged(nameof(CacheHitRate)); } } }

    private long _cacheReadTokens;
    public long CacheReadTokens { get => _cacheReadTokens; set { if (_cacheReadTokens != value) { _cacheReadTokens = value; OnChanged(); OnChanged(nameof(TotalTokens)); OnChanged(nameof(CacheHitRate)); } } }

    public long TotalTokens => _inputTokens + _outputTokens + _cacheCreationTokens + _cacheReadTokens;

    public double CacheHitRate
    {
        get
        {
            var totalInput = _inputTokens + _cacheCreationTokens + _cacheReadTokens;
            return totalInput == 0 ? 0 : (double)_cacheReadTokens / totalInput;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
