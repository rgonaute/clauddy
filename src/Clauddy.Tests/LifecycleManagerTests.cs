using Clauddy.Models;
using Clauddy.Services;
using FluentAssertions;
using Xunit;

namespace Clauddy.Tests;

public class LifecycleManagerTests
{
    [Fact]
    public void Tick_does_not_remove_stale_sessions()
    {
        // Sessions are kept around as a running inventory; only SessionEnd / process
        // death should remove them. Idle ones get reskinned as Sleeping instead.
        var store = new SessionStore();
        var now = DateTimeOffset.UtcNow;
        store.Upsert(new("fresh",   "f", SessionState.Working, "/c/f", now, "x"));
        store.Upsert(new("ancient", "a", SessionState.Working, "/c/a", now.AddDays(-7), "x"));
        var mgr = new LifecycleManager(store, () => now, TimeSpan.FromMinutes(30));
        mgr.Tick();
        store.Sessions.Should().HaveCount(2);
    }

    [Fact]
    public void Tick_transitions_long_idle_chilling_to_sleeping()
    {
        var store = new SessionStore();
        var now = DateTimeOffset.UtcNow;
        store.Upsert(new("fresh", "f", SessionState.Chilling, "/c/f", now,                    "x"));
        store.Upsert(new("idle",  "i", SessionState.Chilling, "/c/i", now.AddMinutes(-31),    "x"));
        var mgr = new LifecycleManager(store, () => now, TimeSpan.FromMinutes(30));
        mgr.Tick();
        store.Sessions.Single(s => s.SessionId == "fresh").State.Should().Be(SessionState.Chilling);
        store.Sessions.Single(s => s.SessionId == "idle").State.Should().Be(SessionState.Sleeping);
    }

    [Fact]
    public void Tick_only_sleeps_chilling_sessions_not_working_or_alerting()
    {
        // Working/Alerting reflect what Claude Code is actively doing — a long-running
        // tool call shouldn't be reskinned as sleeping just because no hook fired in 30 min.
        var store = new SessionStore();
        var now = DateTimeOffset.UtcNow;
        store.Upsert(new("w", "w", SessionState.Working,  "/c/w", now.AddHours(-2), "x"));
        store.Upsert(new("a", "a", SessionState.Alerting, "/c/a", now.AddHours(-2), "x"));
        var mgr = new LifecycleManager(store, () => now, TimeSpan.FromMinutes(30));
        mgr.Tick();
        store.Sessions.Single(s => s.SessionId == "w").State.Should().Be(SessionState.Working);
        store.Sessions.Single(s => s.SessionId == "a").State.Should().Be(SessionState.Alerting);
    }

    [Fact]
    public void Tick_removes_sessions_whose_process_is_dead()
    {
        var store = new SessionStore();
        var now = DateTimeOffset.UtcNow;
        store.Upsert(new("alive", "a", SessionState.Working, "/c/a", now, "x", 0, 0, 0, 0, 0, Pid: 100));
        store.Upsert(new("dead",  "d", SessionState.Working, "/c/d", now, "x", 0, 0, 0, 0, 0, Pid: 200));
        // Liveness probe says PID 100 is alive, 200 is dead.
        var mgr = new LifecycleManager(store, () => now, TimeSpan.FromMinutes(30), pid => pid == 100);
        mgr.Tick();
        store.Sessions.Select(s => s.SessionId).Should().BeEquivalentTo(new[] { "alive" });
    }

    [Fact]
    public void Tick_does_not_check_liveness_for_pid_zero()
    {
        // Sessions reported before the PID-on-hook change have Pid=0.
        // We must not treat them as dead; existing stale-timeout still applies.
        var store = new SessionStore();
        var now = DateTimeOffset.UtcNow;
        store.Upsert(new("legacy", "l", SessionState.Working, "/c/l", now, "x", 0, 0, 0, 0, 0, Pid: 0));
        var probeCalls = 0;
        var mgr = new LifecycleManager(store, () => now, TimeSpan.FromMinutes(30), _ => { probeCalls++; return false; });
        mgr.Tick();
        probeCalls.Should().Be(0);
        store.Sessions.Should().HaveCount(1);
    }

    [Fact]
    public void DefaultIsAlive_treats_unopenable_pid_as_alive()
    {
        // OpenProcess returns ERROR_INVALID_PARAMETER both for "PID never existed" (Git Bash
        // sends $PPID=1 from MSYS pseudo-pids) AND "PID existed and just died" — we can't
        // tell which. Mapping that to "dead" mass-removes Git Bash sessions ~30s after their
        // last hook. Stale GC handles the actual-cleanup case instead.
        LifecycleManager.DefaultIsAlive(int.MaxValue).Should().BeTrue();
        LifecycleManager.DefaultIsAlive(1).Should().BeTrue();
    }

    [Fact]
    public void Tick_without_liveness_probe_only_does_staleness()
    {
        var store = new SessionStore();
        var now = DateTimeOffset.UtcNow;
        store.Upsert(new("a", "a", SessionState.Working, "/c/a", now, "x", 0, 0, 0, 0, 0, Pid: 999));
        var mgr = new LifecycleManager(store, () => now, TimeSpan.FromMinutes(30));
        mgr.Tick();
        store.Sessions.Should().HaveCount(1);
    }
}
