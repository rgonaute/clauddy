using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Clauddy.Models;
using Clauddy.Services;

namespace Clauddy.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    public ObservableCollection<TileViewModel> Tiles { get; } = new();
    private readonly Dictionary<string, TileViewModel> _byId = new();

    private TileViewModel? _selected;
    public TileViewModel? Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            if (_selected != null) _selected.IsSelected = false;
            _selected = value;
            if (_selected != null) _selected.IsSelected = true;
            OnChanged();
            OnChanged(nameof(BigGifSource));
            OnChanged(nameof(SelectedTokensDisplay));
            OnChanged(nameof(SelectedCachePercent));
            OnChanged(nameof(HasSelection));
        }
    }

    public bool HasSelection => _selected != null;

    public string BigGifSource => (_selected?.State ?? SessionState.Chilling) switch
    {
        SessionState.Working  => "pack://application:,,,/Assets/working.gif",
        SessionState.Alerting => "pack://application:,,,/Assets/alerting.gif",
        _                     => "pack://application:,,,/Assets/chilling.gif"
    };

    public string SelectedTokensDisplay => _selected == null ? "—" : FormatTokens(_selected.TotalTokens);

    public string SelectedCachePercent => _selected == null ? "—" : $"{_selected.CacheHitRate:P0}";

    // Aggregate /usage-style metrics (set by App's UsageStats timer).
    private string _window5h = "—";
    public string Window5hDisplay { get => _window5h; set { _window5h = value; OnChanged(); } }

    private string _window7d = "—";
    public string Window7dDisplay { get => _window7d; set { _window7d = value; OnChanged(); } }

    private double _aggCacheRate;
    public double AggregateCacheRate { get => _aggCacheRate; set { _aggCacheRate = value; OnChanged(); OnChanged(nameof(AggregateCachePercent)); } }
    public string AggregateCachePercent => $"{_aggCacheRate:P0}";

    public MainViewModel(SessionStore store)
    {
        store.Sessions.CollectionChanged += OnSessionsChanged;
        foreach (var s in store.Sessions) Add(s);
    }

    public void UpdateUsage(UsageStats stats, long quota5h)
    {
        Window5hDisplay = quota5h > 0
            ? $"5h: {(double)stats.Last5HoursTokens / quota5h:P0}"
            : $"5h: {FormatTokens(stats.Last5HoursTokens)}";
        Window7dDisplay = $"Week: {FormatTokens(stats.Last7DaysTokens)}";
        AggregateCacheRate = stats.Last5HoursCacheRate;
    }

    public static string FormatTokens(long n) => n switch
    {
        < 1_000        => n.ToString(),
        < 10_000       => $"{n / 1000.0:0.0}K",
        < 1_000_000    => $"{n / 1000:0}K",
        < 10_000_000   => $"{n / 1_000_000.0:0.0}M",
        _              => $"{n / 1_000_000:0}M"
    };

    private void OnSessionsChanged(object? _, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset) { _byId.Clear(); Tiles.Clear(); Selected = null; return; }

        if (e.Action == NotifyCollectionChangedAction.Replace)
        {
            if (e.NewItems != null) foreach (Session s in e.NewItems) Upsert(s);
            return;
        }

        if (e.OldItems != null) foreach (Session s in e.OldItems) Remove(s.SessionId);
        if (e.NewItems != null) foreach (Session s in e.NewItems) Upsert(s);
    }

    private void Add(Session s)
    {
        var tile = new TileViewModel
        {
            SessionId = s.SessionId,
            Pid = s.Pid,
            Label = s.Label,
            State = s.State,
            InputTokens = s.InputTokens,
            OutputTokens = s.OutputTokens,
            CacheCreationTokens = s.CacheCreationTokens,
            CacheReadTokens = s.CacheReadTokens
        };
        tile.PropertyChanged += OnTilePropertyChanged;
        _byId[s.SessionId] = tile;
        Tiles.Add(tile);
        // Auto-select the first tile so the big face shows something.
        Selected ??= tile;
    }

    private void Upsert(Session s)
    {
        if (_byId.TryGetValue(s.SessionId, out var tile))
        {
            tile.Label = s.Label;
            tile.State = s.State;
            // Latest hook payload is the freshest pid for that session.
            if (s.Pid != 0) tile.Pid = s.Pid;
            tile.InputTokens = s.InputTokens;
            tile.OutputTokens = s.OutputTokens;
            tile.CacheCreationTokens = s.CacheCreationTokens;
            tile.CacheReadTokens = s.CacheReadTokens;
        }
        else Add(s);
    }

    private void Remove(string sid)
    {
        if (_byId.TryGetValue(sid, out var tile))
        {
            tile.PropertyChanged -= OnTilePropertyChanged;
            Tiles.Remove(tile);
            _byId.Remove(sid);
            if (Selected == tile) Selected = Tiles.FirstOrDefault();
        }
    }

    private void OnTilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender == _selected)
        {
            // Cascade: when the selected tile's state or tokens change, the big face
            // and the per-session metrics need to repaint.
            if (e.PropertyName == nameof(TileViewModel.State)) OnChanged(nameof(BigGifSource));
            if (e.PropertyName is nameof(TileViewModel.InputTokens)
                              or nameof(TileViewModel.OutputTokens)
                              or nameof(TileViewModel.CacheCreationTokens)
                              or nameof(TileViewModel.CacheReadTokens))
            {
                OnChanged(nameof(SelectedTokensDisplay));
                OnChanged(nameof(SelectedCachePercent));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
