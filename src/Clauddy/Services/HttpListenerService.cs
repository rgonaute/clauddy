using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Clauddy.Models;

namespace Clauddy.Services;

public class HttpListenerService
{
    private readonly SessionStore _store;
    private readonly LabelResolver _resolver;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    public Action<Action>? Marshal { get; set; } = a => a();

    public HttpListenerService(SessionStore store, LabelResolver resolver)
    {
        _store = store; _resolver = resolver;
    }

    public string Start()
    {
        var port = GetEphemeralPort();
        var url = $"http://127.0.0.1:{port}/";
        _listener = new HttpListener();
        _listener.Prefixes.Add(url);
        _listener.Start();
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => Loop(_cts.Token));
        return url.TrimEnd('/');
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _listener?.Close();
    }

    private async Task Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener!.GetContextAsync(); }
            catch { return; }
            _ = Task.Run(() => Handle(ctx));
        }
    }

    private async Task Handle(HttpListenerContext ctx)
    {
        try
        {
            var path = ctx.Request.Url!.AbsolutePath;
            if (ctx.Request.HttpMethod == "GET" && path == "/healthz")
            {
                ctx.Response.StatusCode = 200;
                using var w = new StreamWriter(ctx.Response.OutputStream);
                await w.WriteAsync("ok");
                return;
            }
            if (ctx.Request.HttpMethod == "POST" && path == "/state")
            {
                using var sr = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
                var body = await sr.ReadToEndAsync();
                if (!TryParse(body, out var p, out var err))
                {
                    ctx.Response.StatusCode = 400;
                    using var w = new StreamWriter(ctx.Response.OutputStream);
                    await w.WriteAsync(err);
                    return;
                }
                Marshal!(() => Apply(p));
                ctx.Response.StatusCode = 200;
                return;
            }
            ctx.Response.StatusCode = 404;
        }
        finally { try { ctx.Response.Close(); } catch { } }
    }

    private bool TryParse(string body, out Payload p, out string err)
    {
        p = default!; err = "";
        try
        {
            using var doc = JsonDocument.Parse(body);
            var r = doc.RootElement;
            string sid = r.GetProperty("session_id").GetString() ?? "";
            string cwd = r.TryGetProperty("cwd", out var c) ? c.GetString() ?? "" : "";
            string label = r.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "";
            string action = r.TryGetProperty("action", out var a) ? a.GetString() ?? "update" : "update";
            string? state = r.TryGetProperty("state", out var s) ? s.GetString() : null;
            long tokens = r.TryGetProperty("tokens", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt64() : 0;
            if (string.IsNullOrEmpty(sid)) { err = "session_id required"; return false; }
            SessionState? parsed = null;
            if (state != null)
            {
                parsed = state switch
                {
                    "chilling" => SessionState.Chilling,
                    "working"  => SessionState.Working,
                    "alerting" => SessionState.Alerting,
                    _          => null
                };
                if (parsed == null) { err = $"invalid state '{state}'"; return false; }
            }
            else if (action != "remove") { err = "state required unless action=remove"; return false; }
            p = new Payload(sid, cwd, label, parsed, action, tokens);
            return true;
        }
        catch { err = "invalid JSON"; return false; }
    }

    private void Apply(Payload p)
    {
        if (p.Action == "remove") { _store.Remove(p.SessionId); return; }
        var label = _resolver.Resolve(p.Cwd, p.Label);
        _store.Upsert(new Session(
            p.SessionId, label, p.State!.Value, p.Cwd, DateTimeOffset.UtcNow, "http", p.Tokens));
    }

    private static int GetEphemeralPort()
    {
        using var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private record Payload(string SessionId, string Cwd, string Label, SessionState? State, string Action, long Tokens);
}
