using Clauddy.Models;
using Clauddy.Services;
using FluentAssertions;
using Xunit;

namespace Clauddy.Tests;

public class LifecycleManagerTests
{
    [Fact]
    public void Tick_removes_stale_sessions()
    {
        var store = new SessionStore();
        var now = DateTimeOffset.UtcNow;
        store.Upsert(new("fresh", "f", SessionState.Working, "/c/f", now, "x"));
        store.Upsert(new("stale", "s", SessionState.Working, "/c/s", now.AddHours(-1), "x"));
        var mgr = new LifecycleManager(store, () => now, TimeSpan.FromMinutes(30));
        mgr.Tick();
        store.Sessions.Select(s => s.SessionId).Should().BeEquivalentTo(new[] { "fresh" });
    }

    [Fact]
    public void Tick_with_no_stale_is_noop()
    {
        var store = new SessionStore();
        var now = DateTimeOffset.UtcNow;
        store.Upsert(new("a", "a", SessionState.Working, "/c/a", now, "x"));
        var mgr = new LifecycleManager(store, () => now, TimeSpan.FromMinutes(30));
        mgr.Tick();
        store.Sessions.Should().HaveCount(1);
    }
}
