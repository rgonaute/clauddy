using Clauddy.Models;
using Clauddy.Services;
using FluentAssertions;
using Xunit;

namespace Clauddy.Tests;

public class SessionStoreTests
{
    private readonly SessionStore _store = new();

    [Fact]
    public void Upsert_inserts_new_session_into_observable_collection()
    {
        var s = new Session("sid-1", "repo@main", SessionState.Working, "/c/repo",
            DateTimeOffset.UtcNow, "windows");
        _store.Upsert(s);
        _store.Sessions.Should().ContainSingle().Which.SessionId.Should().Be("sid-1");
    }

    [Fact]
    public void Upsert_updates_existing_session_in_place()
    {
        var t0 = DateTimeOffset.UtcNow;
        _store.Upsert(new("sid-1", "repo@main", SessionState.Working, "/c/repo", t0, "windows"));
        _store.Upsert(new("sid-1", "repo@main", SessionState.Alerting, "/c/repo", t0.AddSeconds(5), "windows"));
        _store.Sessions.Should().ContainSingle();
        _store.Sessions[0].State.Should().Be(SessionState.Alerting);
    }

    [Fact]
    public void Remove_deletes_by_session_id()
    {
        _store.Upsert(new("sid-1", "a", SessionState.Working, "/c/a", DateTimeOffset.UtcNow, "windows"));
        _store.Upsert(new("sid-2", "b", SessionState.Chilling, "/c/b", DateTimeOffset.UtcNow, "windows"));
        _store.Remove("sid-1");
        _store.Sessions.Select(s => s.SessionId).Should().BeEquivalentTo(new[] { "sid-2" });
    }

    [Fact]
    public void Remove_unknown_id_is_silent_noop()
    {
        Action act = () => _store.Remove("nope");
        act.Should().NotThrow();
    }

    [Fact]
    public void RemoveStale_removes_sessions_older_than_cutoff()
    {
        var now = DateTimeOffset.UtcNow;
        _store.Upsert(new("fresh", "f", SessionState.Working, "/c/f", now, "windows"));
        _store.Upsert(new("stale", "s", SessionState.Working, "/c/s", now.AddMinutes(-31), "windows"));
        _store.RemoveStale(now, TimeSpan.FromMinutes(30));
        _store.Sessions.Select(s => s.SessionId).Should().BeEquivalentTo(new[] { "fresh" });
    }
}
