using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Clauddy.Models;
using Clauddy.Services;

namespace Clauddy.ViewModels;

public class MainViewModel
{
    public ObservableCollection<TileViewModel> Tiles { get; } = new();
    private readonly Dictionary<string, TileViewModel> _byId = new();

    public MainViewModel(SessionStore store)
    {
        store.Sessions.CollectionChanged += OnSessionsChanged;
        foreach (var s in store.Sessions) Add(s);
    }

    private void OnSessionsChanged(object? _, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset) { _byId.Clear(); Tiles.Clear(); return; }
        if (e.OldItems != null) foreach (Session s in e.OldItems) Remove(s.SessionId);
        if (e.NewItems != null) foreach (Session s in e.NewItems) Upsert(s);
    }

    private void Add(Session s)
    {
        var tile = new TileViewModel { Label = s.Label, State = s.State, Tokens = s.Tokens };
        _byId[s.SessionId] = tile;
        Tiles.Add(tile);
    }

    private void Upsert(Session s)
    {
        if (_byId.TryGetValue(s.SessionId, out var tile))
        {
            tile.Label = s.Label;
            tile.State = s.State;
            tile.Tokens = s.Tokens;
        }
        else Add(s);
    }

    private void Remove(string sid)
    {
        if (_byId.TryGetValue(sid, out var tile))
        {
            Tiles.Remove(tile);
            _byId.Remove(sid);
        }
    }
}
