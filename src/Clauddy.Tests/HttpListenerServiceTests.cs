using System.Net;
using System.Net.Http.Json;
using Clauddy.Models;
using Clauddy.Services;
using FluentAssertions;
using Xunit;

namespace Clauddy.Tests;

public class HttpListenerServiceTests : IAsyncLifetime
{
    private readonly SessionStore _store = new();
    private readonly LabelResolver _resolver = new(new StubGit());
    private HttpListenerService? _svc;
    private HttpClient _client = new();
    private string _baseUrl = "";

    private class StubGit : IGitProbe
    {
        public bool TryGetRepoInfo(string cwd, out string repoRoot, out string branch)
        { repoRoot = ""; branch = ""; return false; }
    }

    public Task InitializeAsync()
    {
        _svc = new HttpListenerService(_store, _resolver);
        _baseUrl = _svc.Start();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() { _svc?.Stop(); return Task.CompletedTask; }

    [Fact]
    public async Task Healthz_returns_200_ok()
    {
        var r = await _client.GetAsync($"{_baseUrl}/healthz");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Post_state_upserts_session()
    {
        var r = await _client.PostAsJsonAsync($"{_baseUrl}/state", new
        {
            session_id = "sid-1", cwd = "C:\\repo", label = "", state = "working"
        });
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        await Task.Delay(50);
        _store.Sessions.Should().ContainSingle(s => s.SessionId == "sid-1" && s.State == SessionState.Working);
    }

    [Fact]
    public async Task Post_state_with_action_remove_deletes_session()
    {
        await _client.PostAsJsonAsync($"{_baseUrl}/state",
            new { session_id = "sid-1", cwd = "C:\\repo", label = "", state = "working" });
        await Task.Delay(50);
        await _client.PostAsJsonAsync($"{_baseUrl}/state",
            new { session_id = "sid-1", cwd = "C:\\repo", label = "", action = "remove" });
        await Task.Delay(50);
        _store.Sessions.Should().BeEmpty();
    }

    [Fact]
    public async Task Post_state_with_label_uses_override()
    {
        await _client.PostAsJsonAsync($"{_baseUrl}/state",
            new { session_id = "sid-1", cwd = "C:\\repo", label = "my-label", state = "alerting" });
        await Task.Delay(50);
        _store.Sessions.Single().Label.Should().Be("my-label");
    }

    [Fact]
    public async Task Post_state_with_invalid_state_returns_400()
    {
        var r = await _client.PostAsJsonAsync($"{_baseUrl}/state",
            new { session_id = "sid-1", cwd = "C:\\repo", state = "spinning" });
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_malformed_json_returns_400()
    {
        var r = await _client.PostAsync($"{_baseUrl}/state",
            new StringContent("not-json", System.Text.Encoding.UTF8, "application/json"));
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public void Start_returns_url_with_loopback_and_ephemeral_port()
    {
        _baseUrl.Should().StartWith("http://127.0.0.1:");
        var port = int.Parse(_baseUrl.Split(':').Last());
        port.Should().BeGreaterThan(1024);
    }
}
