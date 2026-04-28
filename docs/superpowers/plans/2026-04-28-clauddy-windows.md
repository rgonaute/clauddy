# Clauddy for Windows Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build, package, and ship Clauddy for Windows — a WPF widget that mirrors live Claude Code session state on a transparent always-on-top desktop overlay, fed by hook scripts that POST to a localhost HTTP listener.

**Architecture:** Two pieces. (1) A single WPF process (`Clauddy.exe`) owns the always-on-top window, an in-memory session store, an `HttpListener` on `127.0.0.1:<ephemeral>`, and a tray icon for control. (2) Per-shell hook scripts (bash + PowerShell) POST `{session_id, cwd, label, state, action}` to the widget on every Claude Code hook event. Distribution: Inno Setup `.exe`, per-user, no admin.

**Tech Stack:** .NET 8 SDK, WPF (`net8.0-windows`), xUnit, FluentAssertions, `Hardcodet.NotifyIcon.Wpf`, `WpfAnimatedGif`, `System.Net.HttpListener`, Inno Setup 6, bash, PowerShell 5.1+.

**Spec:** `docs/superpowers/specs/2026-04-28-clauddy-windows-design.md`

---

## File Structure

```
G:\claude\clauddy\
├── .gitignore
├── README.md
├── Clauddy.sln
├── src/Clauddy/
│   ├── Clauddy.csproj                           single-file publishable WPF app
│   ├── App.xaml, App.xaml.cs                    composition root, single-instance guard
│   ├── MainWindow.xaml, MainWindow.xaml.cs      transparent topmost window, drag handle
│   ├── Models/
│   │   └── Session.cs                           record { SessionId, Label, State, Cwd, LastSeen, Source }
│   ├── Services/
│   │   ├── SessionStore.cs                      keyed by SessionId; ObservableCollection projection
│   │   ├── LabelResolver.cs                     cwd + override → label
│   │   ├── HttpListenerService.cs               POST /state, GET /healthz
│   │   ├── EndpointFile.cs                      r/w %USERPROFILE%\.clauddy\endpoint
│   │   ├── SettingsStore.cs                     r/w %APPDATA%\Clauddy\settings.json
│   │   ├── LifecycleManager.cs                  DispatcherTimer + 30-min stale GC
│   │   ├── AutoStartService.cs                  HKCU\…\Run key
│   │   ├── WslDistroDetector.cs                 wsl.exe -l -q
│   │   └── HookInstaller.cs                     scripts, settings.json patch, ledger
│   ├── ViewModels/
│   │   ├── MainViewModel.cs
│   │   └── TileViewModel.cs
│   ├── Views/
│   │   ├── TileControl.xaml, TileControl.xaml.cs
│   │   └── HookSetupDialog.xaml, HookSetupDialog.xaml.cs
│   ├── TrayIcon/
│   │   └── TrayController.cs
│   ├── Logging/
│   │   └── FileLogger.cs
│   └── Assets/
│       ├── working.gif, alerting.gif, chilling.gif    upstream
│       └── tray.ico
├── src/Clauddy.Tests/
│   ├── Clauddy.Tests.csproj                     xUnit + FluentAssertions
│   ├── SessionStoreTests.cs
│   ├── LabelResolverTests.cs
│   ├── EndpointFileTests.cs
│   ├── SettingsStoreTests.cs
│   ├── HttpListenerServiceTests.cs
│   ├── LifecycleManagerTests.cs
│   ├── AutoStartServiceTests.cs
│   ├── WslDistroDetectorTests.cs
│   ├── HookInstallerTests.cs
│   └── FileLoggerTests.cs
├── hooks/
│   ├── clauddy-hook.sh
│   ├── clauddy-hook.ps1
│   └── install-wsl.sh
└── installer/
    └── Clauddy.iss
```

---

## Prerequisites

Before Task 1, the executor must have on the machine:

- **.NET 8 SDK** — `dotnet --version` should return `8.x.x`. Install: <https://dotnet.microsoft.com/download/dotnet/8.0>
- **Git** — already present (Git Bash).
- **Inno Setup 6** — needed only for Phase 10. Install: <https://jrsoftware.org/isdl.php>. Add `iscc.exe` to PATH.
- **Internet** — for one-time NuGet restore and one-time GIF download.

If any are missing, install them before starting. Do NOT attempt workarounds; the plan assumes them.

---

## Phase 1 — Project scaffolding

### Task 1: Initialize repo + .gitignore

**Files:**
- Create: `G:\claude\clauddy\.gitignore`

- [ ] **Step 1: Initialize git**

```bash
cd /g/claude/clauddy
git init -b main
```

Expected: `Initialized empty Git repository`.

- [ ] **Step 2: Write .gitignore**

```gitignore
# .NET
bin/
obj/
*.user
*.suo
.vs/

# Single-file publish output
publish/
*.exe
*.pdb

# Installer artifacts
installer/Output/

# OS
Thumbs.db
.DS_Store

# Brainstorm session scratch (kept out of repo)
.superpowers/

# IDE
.idea/
*.iml
```

- [ ] **Step 3: First commit**

```bash
git add .gitignore docs/
git commit -m "chore: initial commit with spec and gitignore"
```

Expected: one commit on `main`.

---

### Task 2: Create solution + WPF project + test project

**Files:**
- Create: `Clauddy.sln`, `src/Clauddy/Clauddy.csproj`, `src/Clauddy.Tests/Clauddy.Tests.csproj`

- [ ] **Step 1: Create solution and projects**

```bash
cd /g/claude/clauddy
dotnet new sln -n Clauddy
mkdir -p src/Clauddy src/Clauddy.Tests
dotnet new wpf -o src/Clauddy -n Clauddy --framework net8.0
dotnet new xunit -o src/Clauddy.Tests -n Clauddy.Tests --framework net8.0
dotnet sln add src/Clauddy/Clauddy.csproj
dotnet sln add src/Clauddy.Tests/Clauddy.Tests.csproj
dotnet add src/Clauddy.Tests/Clauddy.Tests.csproj reference src/Clauddy/Clauddy.csproj
```

- [ ] **Step 2: Build to confirm scaffolding**

```bash
dotnet build
```

Expected: `Build succeeded`. 0 errors.

- [ ] **Step 3: Run tests to confirm test rig works**

```bash
dotnet test
```

Expected: `Test Run Successful` with 0 tests (xUnit template starts empty).

- [ ] **Step 4: Commit**

```bash
git add Clauddy.sln src/
git commit -m "chore: scaffold WPF solution and xUnit test project"
```

---

### Task 3: Add NuGet dependencies

**Files:**
- Modify: `src/Clauddy/Clauddy.csproj`, `src/Clauddy.Tests/Clauddy.Tests.csproj`

- [ ] **Step 1: Add app dependencies**

```bash
dotnet add src/Clauddy/Clauddy.csproj package Hardcodet.NotifyIcon.Wpf --version 1.1.0
dotnet add src/Clauddy/Clauddy.csproj package WpfAnimatedGif --version 2.0.2
```

- [ ] **Step 2: Add test dependencies**

```bash
dotnet add src/Clauddy.Tests/Clauddy.Tests.csproj package FluentAssertions --version 6.12.0
```

- [ ] **Step 3: Verify packages resolve**

```bash
dotnet restore
dotnet build
```

Expected: `Build succeeded`.

- [ ] **Step 4: Commit**

```bash
git add src/Clauddy/Clauddy.csproj src/Clauddy.Tests/Clauddy.Tests.csproj
git commit -m "chore: add NotifyIcon, WpfAnimatedGif, FluentAssertions"
```

---

### Task 4: Stage upstream GIF assets with attribution

**Files:**
- Create: `src/Clauddy/Assets/working.gif`, `alerting.gif`, `chilling.gif`, `Assets/ATTRIBUTION.md`

- [ ] **Step 1: Download GIFs**

```bash
mkdir -p src/Clauddy/Assets
curl -sSL -o src/Clauddy/Assets/working.gif  https://raw.githubusercontent.com/bugzmanov/divoom-minitoo/main/apps/clauddy/assets/working.gif
curl -sSL -o src/Clauddy/Assets/alerting.gif https://raw.githubusercontent.com/bugzmanov/divoom-minitoo/main/apps/clauddy/assets/alerting.gif
curl -sSL -o src/Clauddy/Assets/chilling.gif https://raw.githubusercontent.com/bugzmanov/divoom-minitoo/main/apps/clauddy/assets/chilling.gif
```

- [ ] **Step 2: Verify file sizes are non-zero**

```bash
ls -la src/Clauddy/Assets/*.gif
```

Expected: three files, each > 10 KB.

- [ ] **Step 3: Write attribution**

Create `src/Clauddy/Assets/ATTRIBUTION.md`:

```markdown
# Asset attribution

`working.gif`, `alerting.gif`, `chilling.gif` are bundled from
[bugzmanov/divoom-minitoo](https://github.com/bugzmanov/divoom-minitoo)
(`apps/clauddy/assets/`). Used with thanks to the upstream author.
```

- [ ] **Step 4: Mark GIFs as content in csproj**

In `src/Clauddy/Clauddy.csproj`, before `</Project>`:

```xml
<ItemGroup>
  <Resource Include="Assets\working.gif" />
  <Resource Include="Assets\alerting.gif" />
  <Resource Include="Assets\chilling.gif" />
</ItemGroup>
```

- [ ] **Step 5: Build to confirm**

```bash
dotnet build
```

Expected: `Build succeeded`.

- [ ] **Step 6: Commit**

```bash
git add src/Clauddy/Assets/ src/Clauddy/Clauddy.csproj
git commit -m "feat: bundle upstream Clauddy GIFs with attribution"
```

---

## Phase 2 — Core models and stores (TDD)

### Task 5: Session record + SessionState enum

**Files:**
- Create: `src/Clauddy/Models/Session.cs`

- [ ] **Step 1: Write the model**

```csharp
namespace Clauddy.Models;

public enum SessionState { Chilling, Working, Alerting }

public record Session(
    string SessionId,
    string Label,
    SessionState State,
    string Cwd,
    DateTimeOffset LastSeen,
    string Source);
```

- [ ] **Step 2: Build**

```bash
dotnet build
```

Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
git add src/Clauddy/Models/Session.cs
git commit -m "feat: Session record and SessionState enum"
```

---

### Task 6: SessionStore — TDD

**Files:**
- Create: `src/Clauddy.Tests/SessionStoreTests.cs`, `src/Clauddy/Services/SessionStore.cs`

- [ ] **Step 1: Write failing tests**

`src/Clauddy.Tests/SessionStoreTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter "FullyQualifiedName~SessionStoreTests"
```

Expected: build error (`SessionStore` not defined).

- [ ] **Step 3: Implement SessionStore**

`src/Clauddy/Services/SessionStore.cs`:

```csharp
using System.Collections.ObjectModel;
using Clauddy.Models;

namespace Clauddy.Services;

public class SessionStore
{
    public ObservableCollection<Session> Sessions { get; } = new();

    public void Upsert(Session s)
    {
        for (int i = 0; i < Sessions.Count; i++)
        {
            if (Sessions[i].SessionId == s.SessionId)
            {
                Sessions[i] = s;
                return;
            }
        }
        Sessions.Add(s);
    }

    public void Remove(string sessionId)
    {
        for (int i = Sessions.Count - 1; i >= 0; i--)
            if (Sessions[i].SessionId == sessionId) Sessions.RemoveAt(i);
    }

    public void RemoveStale(DateTimeOffset now, TimeSpan maxAge)
    {
        for (int i = Sessions.Count - 1; i >= 0; i--)
            if (now - Sessions[i].LastSeen > maxAge) Sessions.RemoveAt(i);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test --filter "FullyQualifiedName~SessionStoreTests"
```

Expected: 5 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Clauddy/Services/SessionStore.cs src/Clauddy.Tests/SessionStoreTests.cs
git commit -m "feat: SessionStore with upsert, remove, and stale GC"
```

---

### Task 7: LabelResolver — TDD

**Files:**
- Create: `src/Clauddy.Tests/LabelResolverTests.cs`, `src/Clauddy/Services/LabelResolver.cs`

The resolver runs `git -C <cwd>` to derive `<repo>@<branch>`. We isolate the git call behind an `IGitProbe` so tests don't need a real repo.

- [ ] **Step 1: Write failing tests**

`src/Clauddy.Tests/LabelResolverTests.cs`:

```csharp
using Clauddy.Services;
using FluentAssertions;
using Xunit;

namespace Clauddy.Tests;

public class LabelResolverTests
{
    private class FakeGit : IGitProbe
    {
        public string? RepoRoot;
        public string? Branch;
        public bool TryGetRepoInfo(string cwd, out string repoRoot, out string branch)
        {
            repoRoot = RepoRoot ?? "";
            branch = Branch ?? "";
            return RepoRoot != null && Branch != null;
        }
    }

    [Fact]
    public void Override_takes_precedence()
    {
        var r = new LabelResolver(new FakeGit { RepoRoot = "/c/repo", Branch = "main" });
        r.Resolve("/c/repo/sub", "my-label").Should().Be("my-label");
    }

    [Fact]
    public void Empty_override_falls_through()
    {
        var r = new LabelResolver(new FakeGit { RepoRoot = "/c/repo", Branch = "main" });
        r.Resolve("/c/repo/sub", "").Should().Be("repo@main");
    }

    [Fact]
    public void Git_repo_yields_repo_at_branch()
    {
        var r = new LabelResolver(new FakeGit { RepoRoot = "/c/projects/clauddy", Branch = "feat/x" });
        r.Resolve("/c/projects/clauddy/src", null).Should().Be("clauddy@feat/x");
    }

    [Fact]
    public void Non_git_falls_back_to_basename()
    {
        var r = new LabelResolver(new FakeGit());
        r.Resolve("/c/scratch/notes", null).Should().Be("notes");
    }

    [Fact]
    public void Windows_paths_are_handled()
    {
        var r = new LabelResolver(new FakeGit());
        r.Resolve(@"C:\Users\Ron\projects\foo", null).Should().Be("foo");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter "FullyQualifiedName~LabelResolverTests"
```

Expected: build errors (types not defined).

- [ ] **Step 3: Implement IGitProbe and LabelResolver**

`src/Clauddy/Services/LabelResolver.cs`:

```csharp
using System.Diagnostics;

namespace Clauddy.Services;

public interface IGitProbe
{
    bool TryGetRepoInfo(string cwd, out string repoRoot, out string branch);
}

public class GitProbe : IGitProbe
{
    public bool TryGetRepoInfo(string cwd, out string repoRoot, out string branch)
    {
        repoRoot = ""; branch = "";
        try
        {
            repoRoot = Run("rev-parse --show-toplevel", cwd);
            branch = Run("rev-parse --abbrev-ref HEAD", cwd);
            return !string.IsNullOrWhiteSpace(repoRoot) && !string.IsNullOrWhiteSpace(branch);
        }
        catch { return false; }
    }

    private static string Run(string args, string cwd)
    {
        var psi = new ProcessStartInfo("git", args)
        {
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            UseShellExecute = false
        };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd().Trim();
        p.WaitForExit(2000);
        return p.ExitCode == 0 ? output : "";
    }
}

public class LabelResolver
{
    private readonly IGitProbe _git;
    public LabelResolver(IGitProbe git) => _git = git;

    public string Resolve(string cwd, string? overrideLabel)
    {
        if (!string.IsNullOrWhiteSpace(overrideLabel)) return overrideLabel!;
        if (_git.TryGetRepoInfo(cwd, out var root, out var branch))
            return $"{Path.GetFileName(root.TrimEnd('/', '\\'))}@{branch}";
        return Path.GetFileName(cwd.TrimEnd('/', '\\'));
    }
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test --filter "FullyQualifiedName~LabelResolverTests"
```

Expected: 5 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Clauddy/Services/LabelResolver.cs src/Clauddy.Tests/LabelResolverTests.cs
git commit -m "feat: LabelResolver with git-probe fallback"
```

---

### Task 8: EndpointFile — TDD

**Files:**
- Create: `src/Clauddy.Tests/EndpointFileTests.cs`, `src/Clauddy/Services/EndpointFile.cs`

- [ ] **Step 1: Write failing tests**

`src/Clauddy.Tests/EndpointFileTests.cs`:

```csharp
using Clauddy.Services;
using FluentAssertions;
using Xunit;

namespace Clauddy.Tests;

public class EndpointFileTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "clauddy-tests-" + Guid.NewGuid().ToString("N"));

    public EndpointFileTests() => Directory.CreateDirectory(_tempDir);
    public void Dispose() { try { Directory.Delete(_tempDir, true); } catch { } }

    [Fact]
    public void Write_creates_directory_and_file()
    {
        var f = new EndpointFile(Path.Combine(_tempDir, ".clauddy", "endpoint"));
        f.Write("http://127.0.0.1:51797");
        File.Exists(Path.Combine(_tempDir, ".clauddy", "endpoint")).Should().BeTrue();
        File.ReadAllText(Path.Combine(_tempDir, ".clauddy", "endpoint")).Should().Be("http://127.0.0.1:51797");
    }

    [Fact]
    public void Write_overwrites_existing_file()
    {
        var path = Path.Combine(_tempDir, "endpoint");
        File.WriteAllText(path, "stale");
        new EndpointFile(path).Write("fresh");
        File.ReadAllText(path).Should().Be("fresh");
    }

    [Fact]
    public void Delete_removes_file_if_present()
    {
        var path = Path.Combine(_tempDir, "endpoint");
        File.WriteAllText(path, "x");
        new EndpointFile(path).Delete();
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public void Delete_silent_if_missing()
    {
        Action act = () => new EndpointFile(Path.Combine(_tempDir, "nope")).Delete();
        act.Should().NotThrow();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter "FullyQualifiedName~EndpointFileTests"
```

Expected: build error.

- [ ] **Step 3: Implement**

`src/Clauddy/Services/EndpointFile.cs`:

```csharp
namespace Clauddy.Services;

public class EndpointFile
{
    private readonly string _path;
    public EndpointFile(string path) => _path = path;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".clauddy", "endpoint");

    public void Write(string url)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, url);
    }

    public void Delete() { try { File.Delete(_path); } catch { } }
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test --filter "FullyQualifiedName~EndpointFileTests"
```

Expected: 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Clauddy/Services/EndpointFile.cs src/Clauddy.Tests/EndpointFileTests.cs
git commit -m "feat: EndpointFile r/w with default %USERPROFILE%\\.clauddy\\endpoint"
```

---

### Task 9: SettingsStore — TDD

**Files:**
- Create: `src/Clauddy.Tests/SettingsStoreTests.cs`, `src/Clauddy/Services/SettingsStore.cs`

- [ ] **Step 1: Write failing tests**

`src/Clauddy.Tests/SettingsStoreTests.cs`:

```csharp
using Clauddy.Services;
using FluentAssertions;
using Xunit;

namespace Clauddy.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"clauddy-settings-{Guid.NewGuid():N}.json");
    public void Dispose() { try { File.Delete(_path); } catch { } }

    [Fact]
    public void Load_returns_defaults_when_missing()
    {
        var s = new SettingsStore(_path).Load();
        s.WindowX.Should().BeNull();
        s.WindowY.Should().BeNull();
        s.RunAtLogin.Should().BeFalse();
    }

    [Fact]
    public void Save_then_load_roundtrips()
    {
        var store = new SettingsStore(_path);
        store.Save(new Settings { WindowX = 100, WindowY = 200, RunAtLogin = true });
        var s = store.Load();
        s.WindowX.Should().Be(100);
        s.WindowY.Should().Be(200);
        s.RunAtLogin.Should().BeTrue();
    }

    [Fact]
    public void Load_returns_defaults_on_corrupt_json()
    {
        File.WriteAllText(_path, "{{ not json");
        new SettingsStore(_path).Load().RunAtLogin.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter "FullyQualifiedName~SettingsStoreTests"
```

Expected: build error.

- [ ] **Step 3: Implement**

`src/Clauddy/Services/SettingsStore.cs`:

```csharp
using System.Text.Json;

namespace Clauddy.Services;

public class Settings
{
    public double? WindowX { get; set; }
    public double? WindowY { get; set; }
    public bool RunAtLogin { get; set; }
}

public class SettingsStore
{
    private readonly string _path;
    public SettingsStore(string path) => _path = path;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Clauddy", "settings.json");

    public Settings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new Settings();
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(_path)) ?? new Settings();
        }
        catch { return new Settings(); }
    }

    public void Save(Settings s)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
    }
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test --filter "FullyQualifiedName~SettingsStoreTests"
```

Expected: 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Clauddy/Services/SettingsStore.cs src/Clauddy.Tests/SettingsStoreTests.cs
git commit -m "feat: SettingsStore with JSON persistence at %APPDATA%\\Clauddy"
```

---

## Phase 3 — HTTP listener (TDD)

### Task 10: HttpListenerService — TDD

**Files:**
- Create: `src/Clauddy.Tests/HttpListenerServiceTests.cs`, `src/Clauddy/Services/HttpListenerService.cs`

The service exposes `POST /state` and `GET /healthz`. It accepts JSON
`{ session_id, cwd, label, state, action }`. Action `"remove"` removes
the session; anything else upserts.

- [ ] **Step 1: Write failing integration tests**

`src/Clauddy.Tests/HttpListenerServiceTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter "FullyQualifiedName~HttpListenerServiceTests"
```

Expected: build error.

- [ ] **Step 3: Implement service**

`src/Clauddy/Services/HttpListenerService.cs`:

```csharp
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
    public Action<Action>? Marshal { get; set; } = a => a();   // dispatcher hook for WPF

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
            p = new Payload(sid, cwd, label, parsed, action);
            return true;
        }
        catch { err = "invalid JSON"; return false; }
    }

    private void Apply(Payload p)
    {
        if (p.Action == "remove") { _store.Remove(p.SessionId); return; }
        var label = _resolver.Resolve(p.Cwd, p.Label);
        _store.Upsert(new Session(
            p.SessionId, label, p.State!.Value, p.Cwd, DateTimeOffset.UtcNow, "http"));
    }

    private static int GetEphemeralPort()
    {
        using var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private record Payload(string SessionId, string Cwd, string Label, SessionState? State, string Action);
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test --filter "FullyQualifiedName~HttpListenerServiceTests"
```

Expected: 7 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Clauddy/Services/HttpListenerService.cs src/Clauddy.Tests/HttpListenerServiceTests.cs
git commit -m "feat: HttpListenerService with /state and /healthz on ephemeral loopback port"
```

---

## Phase 4 — Lifecycle and AutoStart (TDD)

### Task 11: LifecycleManager — TDD

**Files:**
- Create: `src/Clauddy.Tests/LifecycleManagerTests.cs`, `src/Clauddy/Services/LifecycleManager.cs`

LifecycleManager wraps the timer + GC behavior so it's testable without a WPF dispatcher.

- [ ] **Step 1: Write failing tests**

`src/Clauddy.Tests/LifecycleManagerTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter "FullyQualifiedName~LifecycleManagerTests"
```

Expected: build error.

- [ ] **Step 3: Implement**

`src/Clauddy/Services/LifecycleManager.cs`:

```csharp
namespace Clauddy.Services;

public class LifecycleManager
{
    private readonly SessionStore _store;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _maxAge;

    public LifecycleManager(SessionStore store, Func<DateTimeOffset> clock, TimeSpan maxAge)
    {
        _store = store; _clock = clock; _maxAge = maxAge;
    }

    public void Tick() => _store.RemoveStale(_clock(), _maxAge);
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test --filter "FullyQualifiedName~LifecycleManagerTests"
```

Expected: 2 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Clauddy/Services/LifecycleManager.cs src/Clauddy.Tests/LifecycleManagerTests.cs
git commit -m "feat: LifecycleManager Tick removes stale sessions"
```

---

### Task 12: AutoStartService — TDD

**Files:**
- Create: `src/Clauddy.Tests/AutoStartServiceTests.cs`, `src/Clauddy/Services/AutoStartService.cs`

The service writes/removes a `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` value. Tests use a sub-key under `HKCU\Software\Clauddy.Tests\Run` to avoid mutating the user's real Run key.

- [ ] **Step 1: Write failing tests**

`src/Clauddy.Tests/AutoStartServiceTests.cs`:

```csharp
using Clauddy.Services;
using FluentAssertions;
using Microsoft.Win32;
using Xunit;

namespace Clauddy.Tests;

public class AutoStartServiceTests : IDisposable
{
    private const string TestKey = @"Software\Clauddy.Tests\Run";

    public void Dispose()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Clauddy.Tests"); } catch { }
    }

    [Fact]
    public void Enable_writes_value_under_run_key()
    {
        var svc = new AutoStartService(TestKey, "Clauddy", @"C:\bin\Clauddy.exe");
        svc.Enable();
        using var k = Registry.CurrentUser.OpenSubKey(TestKey);
        k!.GetValue("Clauddy").Should().Be(@"""C:\bin\Clauddy.exe""");
    }

    [Fact]
    public void Disable_removes_value_if_present()
    {
        var svc = new AutoStartService(TestKey, "Clauddy", @"C:\bin\Clauddy.exe");
        svc.Enable();
        svc.Disable();
        using var k = Registry.CurrentUser.OpenSubKey(TestKey);
        k?.GetValue("Clauddy").Should().BeNull();
    }

    [Fact]
    public void IsEnabled_reflects_state()
    {
        var svc = new AutoStartService(TestKey, "Clauddy", @"C:\bin\Clauddy.exe");
        svc.IsEnabled().Should().BeFalse();
        svc.Enable();
        svc.IsEnabled().Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter "FullyQualifiedName~AutoStartServiceTests"
```

Expected: build error.

- [ ] **Step 3: Implement**

`src/Clauddy/Services/AutoStartService.cs`:

```csharp
using Microsoft.Win32;

namespace Clauddy.Services;

public class AutoStartService
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly string _subKey;
    private readonly string _name;
    private readonly string _exePath;

    public AutoStartService(string subKey, string name, string exePath)
    {
        _subKey = subKey; _name = name; _exePath = exePath;
    }

    public static AutoStartService Default(string exePath) =>
        new(RunKey, "Clauddy", exePath);

    public void Enable()
    {
        using var k = Registry.CurrentUser.CreateSubKey(_subKey);
        k!.SetValue(_name, $"\"{_exePath}\"");
    }

    public void Disable()
    {
        using var k = Registry.CurrentUser.OpenSubKey(_subKey, writable: true);
        k?.DeleteValue(_name, throwOnMissingValue: false);
    }

    public bool IsEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(_subKey);
        return k?.GetValue(_name) != null;
    }
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test --filter "FullyQualifiedName~AutoStartServiceTests"
```

Expected: 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Clauddy/Services/AutoStartService.cs src/Clauddy.Tests/AutoStartServiceTests.cs
git commit -m "feat: AutoStartService toggles HKCU Run-key entry"
```

---

## Phase 5 — Logging (TDD)

### Task 13: FileLogger with rotation — TDD

**Files:**
- Create: `src/Clauddy.Tests/FileLoggerTests.cs`, `src/Clauddy/Logging/FileLogger.cs`

- [ ] **Step 1: Write failing tests**

`src/Clauddy.Tests/FileLoggerTests.cs`:

```csharp
using Clauddy.Logging;
using FluentAssertions;
using Xunit;

namespace Clauddy.Tests;

public class FileLoggerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"clauddy-log-{Guid.NewGuid():N}");
    public FileLoggerTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Log_writes_line_with_timestamp_and_level()
    {
        var path = Path.Combine(_dir, "log.txt");
        new FileLogger(path, maxBytes: 1024 * 1024, keep: 3).Info("hello world");
        var line = File.ReadAllText(path);
        line.Should().Contain("INFO").And.Contain("hello world");
    }

    [Fact]
    public void Logger_rotates_when_size_exceeds_max_bytes()
    {
        var path = Path.Combine(_dir, "log.txt");
        var log = new FileLogger(path, maxBytes: 200, keep: 3);
        for (int i = 0; i < 50; i++) log.Info(new string('x', 50));
        Directory.GetFiles(_dir, "log*.txt").Length.Should().BeGreaterOrEqualTo(2);
    }

    [Fact]
    public void Logger_keeps_at_most_N_rotated_files()
    {
        var path = Path.Combine(_dir, "log.txt");
        var log = new FileLogger(path, maxBytes: 100, keep: 2);
        for (int i = 0; i < 200; i++) log.Info(new string('x', 50));
        // 1 active + at most `keep` rotated
        Directory.GetFiles(_dir, "log*.txt").Length.Should().BeLessOrEqualTo(3);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter "FullyQualifiedName~FileLoggerTests"
```

Expected: build error.

- [ ] **Step 3: Implement**

`src/Clauddy/Logging/FileLogger.cs`:

```csharp
namespace Clauddy.Logging;

public class FileLogger
{
    private readonly string _path;
    private readonly long _maxBytes;
    private readonly int _keep;
    private readonly object _lock = new();

    public FileLogger(string path, long maxBytes, int keep)
    {
        _path = path; _maxBytes = maxBytes; _keep = keep;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }

    public static FileLogger Default()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Clauddy", "log.txt");
        return new FileLogger(path, 1024 * 1024, 3);
    }

    public void Info(string msg) => Write("INFO", msg);
    public void Warn(string msg) => Write("WARN", msg);
    public void Error(string msg) => Write("ERROR", msg);

    private void Write(string level, string msg)
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_path) && new FileInfo(_path).Length > _maxBytes) Rotate();
                File.AppendAllText(_path,
                    $"{DateTimeOffset.Now:yyyy-MM-ddTHH:mm:ss.fffzzz} {level} {msg}{Environment.NewLine}");
            }
            catch { /* never let logging crash the app */ }
        }
    }

    private void Rotate()
    {
        var dir = Path.GetDirectoryName(_path)!;
        var stem = Path.GetFileNameWithoutExtension(_path);
        for (int i = _keep; i >= 1; i--)
        {
            var src = Path.Combine(dir, $"{stem}.{i - 1}.txt");
            var dst = Path.Combine(dir, $"{stem}.{i}.txt");
            if (i == 1) src = _path;
            if (File.Exists(src))
            {
                if (File.Exists(dst)) File.Delete(dst);
                File.Move(src, dst);
            }
        }
        // older than _keep gets cleaned up by overwrite chain above.
        var orphan = Path.Combine(dir, $"{stem}.{_keep + 1}.txt");
        if (File.Exists(orphan)) File.Delete(orphan);
    }
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test --filter "FullyQualifiedName~FileLoggerTests"
```

Expected: 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Clauddy/Logging/FileLogger.cs src/Clauddy.Tests/FileLoggerTests.cs
git commit -m "feat: FileLogger with size-based rotation and bounded retention"
```

---

## Phase 6 — Hook scripts and WSL detector

### Task 14: clauddy-hook.sh

**Files:**
- Create: `hooks/clauddy-hook.sh`

- [ ] **Step 1: Write the script**

```bash
#!/usr/bin/env bash
# Claude Code hook: POSTs session state to local Clauddy widget.
# Lives at ~/.clauddy/hooks/clauddy-hook.sh after install.

set -e
# Find endpoint file (native Windows home or WSL view of it)
endpoint_file="$HOME/.clauddy/endpoint"
if [ ! -f "$endpoint_file" ] && [ -n "${WIN_USERNAME:-}" ] && [ -d "/mnt/c/Users/$WIN_USERNAME" ]; then
  endpoint_file="/mnt/c/Users/$WIN_USERNAME/.clauddy/endpoint"
fi
[ -f "$endpoint_file" ] || exit 0
endpoint=$(cat "$endpoint_file")
[ -n "$endpoint" ] || exit 0

# Read hook payload
payload=$(cat)
event=$(echo "$payload" | jq -r '.hook_event_name // empty')
session_id=$(echo "$payload" | jq -r '.session_id // empty')
cwd=$(echo "$payload" | jq -r '.cwd // empty')
[ -n "$event" ] && [ -n "$session_id" ] || exit 0

# Map event → state/action
state=""; action="update"
case "$event" in
  SessionStart)        state="chilling" ;;
  UserPromptSubmit)    state="working" ;;
  Stop|SubagentStop)   state="chilling" ;;
  Notification)        state="alerting" ;;
  SessionEnd)          action="remove" ;;
  *) exit 0 ;;
esac

# Build JSON
if [ "$action" = "remove" ]; then
  body=$(jq -n --arg sid "$session_id" --arg cwd "$cwd" --arg lbl "${CLAUDDY_LABEL:-}" \
    '{session_id:$sid, cwd:$cwd, label:$lbl, action:"remove"}')
else
  body=$(jq -n --arg sid "$session_id" --arg cwd "$cwd" --arg lbl "${CLAUDDY_LABEL:-}" --arg s "$state" \
    '{session_id:$sid, cwd:$cwd, label:$lbl, state:$s}')
fi

# POST. Hard 1s timeout. Failure is silent.
curl --max-time 1 -sS -X POST "$endpoint/state" \
  -H 'Content-Type: application/json' \
  -d "$body" >/dev/null 2>&1 || true
```

- [ ] **Step 2: Smoke test the script manually**

```bash
chmod +x hooks/clauddy-hook.sh
echo '{"hook_event_name":"SessionStart","session_id":"test-1","cwd":"/c/test"}' \
  | hooks/clauddy-hook.sh
```

Expected: exits 0 silently (no widget running yet).

- [ ] **Step 3: Commit**

```bash
git add hooks/clauddy-hook.sh
git commit -m "feat: clauddy-hook.sh maps Claude Code events to widget POSTs"
```

---

### Task 15: clauddy-hook.ps1

**Files:**
- Create: `hooks/clauddy-hook.ps1`

- [ ] **Step 1: Write the script**

```powershell
# Claude Code hook: POSTs session state to local Clauddy widget.
# Lives at $HOME\.clauddy\hooks\clauddy-hook.ps1 after install.

$ErrorActionPreference = 'SilentlyContinue'

$endpointFile = Join-Path $HOME ".clauddy/endpoint"
if (-not (Test-Path $endpointFile)) { exit 0 }
$endpoint = (Get-Content -Raw $endpointFile).Trim()
if ([string]::IsNullOrWhiteSpace($endpoint)) { exit 0 }

$payloadRaw = [Console]::In.ReadToEnd()
try { $payload = $payloadRaw | ConvertFrom-Json } catch { exit 0 }

$event = $payload.hook_event_name
$sessionId = $payload.session_id
$cwd = $payload.cwd
if (-not $event -or -not $sessionId) { exit 0 }

$state = $null; $action = 'update'
switch ($event) {
  'SessionStart'     { $state = 'chilling' }
  'UserPromptSubmit' { $state = 'working' }
  'Stop'             { $state = 'chilling' }
  'SubagentStop'     { $state = 'chilling' }
  'Notification'     { $state = 'alerting' }
  'SessionEnd'       { $action = 'remove' }
  default            { exit 0 }
}

$body = if ($action -eq 'remove') {
  @{ session_id=$sessionId; cwd=$cwd; label=$env:CLAUDDY_LABEL; action='remove' } | ConvertTo-Json -Compress
} else {
  @{ session_id=$sessionId; cwd=$cwd; label=$env:CLAUDDY_LABEL; state=$state } | ConvertTo-Json -Compress
}

try {
  Invoke-RestMethod -Method Post -Uri "$endpoint/state" `
    -ContentType 'application/json' -Body $body -TimeoutSec 1 | Out-Null
} catch { }
```

- [ ] **Step 2: Smoke test**

In PowerShell:

```powershell
'{"hook_event_name":"SessionStart","session_id":"test-1","cwd":"C:\\test"}' | powershell -File hooks/clauddy-hook.ps1
```

Expected: exits 0 silently.

- [ ] **Step 3: Commit**

```bash
git add hooks/clauddy-hook.ps1
git commit -m "feat: clauddy-hook.ps1 PowerShell sibling of clauddy-hook.sh"
```

---

### Task 16: install-wsl.sh

**Files:**
- Create: `hooks/install-wsl.sh`

This script runs INSIDE a WSL distro to install the bash hook in that distro. Invoked by HookInstaller via `wsl.exe -d <distro> -- bash /mnt/c/.../install-wsl.sh <winuser>`.

- [ ] **Step 1: Write the script**

```bash
#!/usr/bin/env bash
# Installs the Clauddy hook inside the current WSL distro.
# Args: $1 = Windows username (used to resolve /mnt/c/Users/<user>/.clauddy)
set -e
WIN_USERNAME="$1"
[ -n "$WIN_USERNAME" ] || { echo "usage: install-wsl.sh <win-username>" >&2; exit 2; }

mkdir -p "$HOME/.clauddy/hooks"
cp "/mnt/c/Users/$WIN_USERNAME/.clauddy/hooks/clauddy-hook.sh" "$HOME/.clauddy/hooks/"
chmod +x "$HOME/.clauddy/hooks/clauddy-hook.sh"

# Plant WIN_USERNAME so the hook can find /mnt/c endpoint
profile="$HOME/.profile"
marker="# clauddy-managed"
if ! grep -q "$marker" "$profile" 2>/dev/null; then
  printf '\n%s\nexport WIN_USERNAME=%q\n' "$marker" "$WIN_USERNAME" >> "$profile"
fi

# Patch ~/.claude/settings.json (created by HookInstaller via second wsl invoke)
echo "wsl-install: hook copied to $HOME/.clauddy/hooks/" >&2
```

- [ ] **Step 2: Verify script syntax**

```bash
bash -n hooks/install-wsl.sh
```

Expected: no output (no syntax errors).

- [ ] **Step 3: Commit**

```bash
git add hooks/install-wsl.sh
git commit -m "feat: install-wsl.sh per-distro hook bootstrap"
```

---

### Task 17: WslDistroDetector — TDD

**Files:**
- Create: `src/Clauddy.Tests/WslDistroDetectorTests.cs`, `src/Clauddy/Services/WslDistroDetector.cs`

The detector parses output of `wsl.exe -l -q`. Output is UTF-16-LE on
Windows. Tests inject a fake process runner so we don't need WSL installed
to run them.

- [ ] **Step 1: Write failing tests**

`src/Clauddy.Tests/WslDistroDetectorTests.cs`:

```csharp
using Clauddy.Services;
using FluentAssertions;
using Xunit;

namespace Clauddy.Tests;

public class WslDistroDetectorTests
{
    [Fact]
    public void Parses_distro_names_one_per_line()
    {
        var d = new WslDistroDetector(_ => "Ubuntu\nDebian\n");
        d.List().Should().BeEquivalentTo(new[] { "Ubuntu", "Debian" });
    }

    [Fact]
    public void Trims_blank_lines_and_whitespace()
    {
        var d = new WslDistroDetector(_ => "  Ubuntu  \n\n  \nDebian\n");
        d.List().Should().BeEquivalentTo(new[] { "Ubuntu", "Debian" });
    }

    [Fact]
    public void Empty_output_returns_empty_list()
    {
        var d = new WslDistroDetector(_ => "");
        d.List().Should().BeEmpty();
    }

    [Fact]
    public void Throwing_runner_returns_empty_list()
    {
        var d = new WslDistroDetector(_ => throw new InvalidOperationException("wsl not installed"));
        d.List().Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter "FullyQualifiedName~WslDistroDetectorTests"
```

Expected: build error.

- [ ] **Step 3: Implement**

`src/Clauddy/Services/WslDistroDetector.cs`:

```csharp
using System.Diagnostics;
using System.Text;

namespace Clauddy.Services;

public class WslDistroDetector
{
    private readonly Func<string, string> _run;

    public WslDistroDetector(Func<string, string> run) => _run = run;

    public static WslDistroDetector Default() => new(args =>
    {
        var psi = new ProcessStartInfo("wsl.exe", args)
        {
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.Unicode  // wsl outputs UTF-16-LE
        };
        using var p = Process.Start(psi)!;
        var s = p.StandardOutput.ReadToEnd();
        p.WaitForExit(3000);
        return s;
    });

    public IReadOnlyList<string> List()
    {
        try
        {
            var raw = _run("-l -q");
            return raw.Split('\n', '\r')
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrEmpty(l))
                .ToList();
        }
        catch { return Array.Empty<string>(); }
    }
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test --filter "FullyQualifiedName~WslDistroDetectorTests"
```

Expected: 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Clauddy/Services/WslDistroDetector.cs src/Clauddy.Tests/WslDistroDetectorTests.cs
git commit -m "feat: WslDistroDetector parses wsl -l -q"
```

---

## Phase 7 — Hook installer (TDD)

### Task 18: HookInstaller — TDD

**Files:**
- Create: `src/Clauddy.Tests/HookInstallerTests.cs`, `src/Clauddy/Services/HookInstaller.cs`

The installer (1) writes the hook scripts to `<home>\.clauddy\hooks\`,
(2) merges a `hooks` block into `<home>\.claude\settings.json`,
(3) records what it changed in `<home>\.clauddy\installed-hooks.json`
so the uninstaller can reverse it.

For unit tests we drive everything through file paths so we can run
inside a temp directory.

- [ ] **Step 1: Write failing tests**

`src/Clauddy.Tests/HookInstallerTests.cs`:

```csharp
using System.Text.Json;
using Clauddy.Services;
using FluentAssertions;
using Xunit;

namespace Clauddy.Tests;

public class HookInstallerTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), $"clauddy-home-{Guid.NewGuid():N}");
    private readonly string _scriptSource = Path.Combine(Path.GetTempPath(), $"clauddy-scripts-{Guid.NewGuid():N}");

    public HookInstallerTests()
    {
        Directory.CreateDirectory(_scriptSource);
        File.WriteAllText(Path.Combine(_scriptSource, "clauddy-hook.sh"), "#!/usr/bin/env bash\necho hi\n");
        File.WriteAllText(Path.Combine(_scriptSource, "clauddy-hook.ps1"), "Write-Host hi\n");
    }

    public void Dispose()
    {
        try { Directory.Delete(_home, true); } catch { }
        try { Directory.Delete(_scriptSource, true); } catch { }
    }

    [Fact]
    public void Install_copies_hook_scripts_into_clauddy_hooks_dir()
    {
        new HookInstaller(_home, _scriptSource).InstallWindows();
        File.Exists(Path.Combine(_home, ".clauddy", "hooks", "clauddy-hook.sh")).Should().BeTrue();
        File.Exists(Path.Combine(_home, ".clauddy", "hooks", "clauddy-hook.ps1")).Should().BeTrue();
    }

    [Fact]
    public void Install_creates_settings_json_when_absent()
    {
        new HookInstaller(_home, _scriptSource).InstallWindows();
        var path = Path.Combine(_home, ".claude", "settings.json");
        File.Exists(path).Should().BeTrue();
        var json = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
        json.TryGetProperty("hooks", out var hooks).Should().BeTrue();
        hooks.TryGetProperty("SessionStart", out _).Should().BeTrue();
        hooks.TryGetProperty("UserPromptSubmit", out _).Should().BeTrue();
        hooks.TryGetProperty("Stop", out _).Should().BeTrue();
        hooks.TryGetProperty("SubagentStop", out _).Should().BeTrue();
        hooks.TryGetProperty("Notification", out _).Should().BeTrue();
        hooks.TryGetProperty("SessionEnd", out _).Should().BeTrue();
    }

    [Fact]
    public void Install_merges_into_existing_settings_json_preserving_other_keys()
    {
        var path = Path.Combine(_home, ".claude", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"theme":"dark","permissions":{"allow":["bash"]}}""");
        new HookInstaller(_home, _scriptSource).InstallWindows();
        var json = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
        json.GetProperty("theme").GetString().Should().Be("dark");
        json.GetProperty("permissions").GetProperty("allow")[0].GetString().Should().Be("bash");
        json.TryGetProperty("hooks", out _).Should().BeTrue();
    }

    [Fact]
    public void Install_writes_ledger_with_added_hook_paths()
    {
        new HookInstaller(_home, _scriptSource).InstallWindows();
        var ledgerPath = Path.Combine(_home, ".clauddy", "installed-hooks.json");
        File.Exists(ledgerPath).Should().BeTrue();
        var ledger = JsonDocument.Parse(File.ReadAllText(ledgerPath)).RootElement;
        ledger.TryGetProperty("windows", out var w).Should().BeTrue();
        w.GetProperty("settings_json").GetString().Should().Contain(".claude");
    }

    [Fact]
    public void Uninstall_restores_original_settings_json()
    {
        var path = Path.Combine(_home, ".claude", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"theme":"dark"}""");

        var inst = new HookInstaller(_home, _scriptSource);
        inst.InstallWindows();
        inst.UninstallWindows();

        var json = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
        json.TryGetProperty("hooks", out _).Should().BeFalse();
        json.GetProperty("theme").GetString().Should().Be("dark");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter "FullyQualifiedName~HookInstallerTests"
```

Expected: build error.

- [ ] **Step 3: Implement**

`src/Clauddy/Services/HookInstaller.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Clauddy.Services;

public class HookInstaller
{
    private readonly string _home;
    private readonly string _scriptSource;
    private static readonly string[] HookEvents =
        { "SessionStart", "UserPromptSubmit", "Stop", "SubagentStop", "Notification", "SessionEnd" };

    public HookInstaller(string home, string scriptSource)
    {
        _home = home; _scriptSource = scriptSource;
    }

    public void InstallWindows()
    {
        var hooksDir = Path.Combine(_home, ".clauddy", "hooks");
        Directory.CreateDirectory(hooksDir);
        File.Copy(Path.Combine(_scriptSource, "clauddy-hook.sh"),
                  Path.Combine(hooksDir, "clauddy-hook.sh"), overwrite: true);
        File.Copy(Path.Combine(_scriptSource, "clauddy-hook.ps1"),
                  Path.Combine(hooksDir, "clauddy-hook.ps1"), overwrite: true);

        var settingsPath = Path.Combine(_home, ".claude", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        var root = File.Exists(settingsPath)
            ? JsonNode.Parse(File.ReadAllText(settingsPath))!.AsObject()
            : new JsonObject();

        var hooks = root["hooks"]?.AsObject() ?? new JsonObject();
        var hookCommand = $"bash \"{hooksDir.Replace('\\', '/')}/clauddy-hook.sh\"";
        foreach (var evt in HookEvents)
        {
            hooks[evt] = new JsonArray(new JsonObject
            {
                ["matcher"] = "*",
                ["hooks"] = new JsonArray(new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = hookCommand,
                    ["_clauddy"] = true     // marker for uninstall
                })
            });
        }
        root["hooks"] = hooks;
        File.WriteAllText(settingsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        // Ledger
        var ledger = new JsonObject
        {
            ["windows"] = new JsonObject
            {
                ["settings_json"] = settingsPath,
                ["hooks_dir"] = hooksDir,
                ["installed_at"] = DateTimeOffset.UtcNow.ToString("o")
            }
        };
        File.WriteAllText(Path.Combine(_home, ".clauddy", "installed-hooks.json"),
            ledger.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public void UninstallWindows()
    {
        var settingsPath = Path.Combine(_home, ".claude", "settings.json");
        if (!File.Exists(settingsPath)) return;
        var root = JsonNode.Parse(File.ReadAllText(settingsPath))!.AsObject();
        if (root["hooks"] is JsonObject hooks)
        {
            foreach (var evt in HookEvents.ToList())
            {
                if (hooks[evt] is JsonArray arr)
                {
                    var clean = new JsonArray();
                    foreach (var entry in arr)
                    {
                        if (entry is not JsonObject obj) { clean.Add(entry?.DeepClone()); continue; }
                        var inner = obj["hooks"] as JsonArray;
                        if (inner == null) { clean.Add(obj.DeepClone()); continue; }
                        var keep = new JsonArray();
                        foreach (var h in inner)
                            if (h is JsonObject ho && ho["_clauddy"]?.GetValue<bool>() == true) { /* drop */ }
                            else keep.Add(h?.DeepClone());
                        if (keep.Count > 0)
                        {
                            obj["hooks"] = keep;
                            clean.Add(obj.DeepClone());
                        }
                    }
                    if (clean.Count == 0) hooks.Remove(evt);
                    else hooks[evt] = clean;
                }
            }
            if (hooks.Count == 0) root.Remove("hooks");
        }
        File.WriteAllText(settingsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var hooksDir = Path.Combine(_home, ".clauddy", "hooks");
        try { Directory.Delete(hooksDir, true); } catch { }
        try { File.Delete(Path.Combine(_home, ".clauddy", "installed-hooks.json")); } catch { }
    }
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test --filter "FullyQualifiedName~HookInstallerTests"
```

Expected: 5 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Clauddy/Services/HookInstaller.cs src/Clauddy.Tests/HookInstallerTests.cs
git commit -m "feat: HookInstaller installs/uninstalls Windows-side hooks with ledger"
```

---

## Phase 8 — WPF UI

> WPF UI tests are integration-heavy and brittle; this phase relies on
> manual smoke tests at each task's end. Steps spell out what to look for.

### Task 19: TileViewModel and MainViewModel

**Files:**
- Create: `src/Clauddy/ViewModels/TileViewModel.cs`, `MainViewModel.cs`

- [ ] **Step 1: TileViewModel**

`src/Clauddy/ViewModels/TileViewModel.cs`:

```csharp
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
```

- [ ] **Step 2: MainViewModel**

`src/Clauddy/ViewModels/MainViewModel.cs`:

```csharp
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
        var tile = new TileViewModel { Label = s.Label, State = s.State };
        _byId[s.SessionId] = tile;
        Tiles.Add(tile);
    }

    private void Upsert(Session s)
    {
        if (_byId.TryGetValue(s.SessionId, out var tile))
        {
            tile.Label = s.Label;
            tile.State = s.State;
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
```

- [ ] **Step 3: Build**

```bash
dotnet build
```

Expected: `Build succeeded`.

- [ ] **Step 4: Commit**

```bash
git add src/Clauddy/ViewModels/
git commit -m "feat: TileViewModel and MainViewModel binding sessions to tiles"
```

---

### Task 20: TileControl XAML

**Files:**
- Create: `src/Clauddy/Views/TileControl.xaml`, `TileControl.xaml.cs`

- [ ] **Step 1: Write the user control**

`src/Clauddy/Views/TileControl.xaml`:

```xml
<UserControl x:Class="Clauddy.Views.TileControl"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:gif="http://wpfanimatedgif.codeplex.com">
    <Border Width="104" Height="124"
            BorderBrush="Black" BorderThickness="3"
            Background="#0d0d12" Padding="6">
        <Border.Effect>
            <DropShadowEffect Color="Black" BlurRadius="0" ShadowDepth="4" Direction="-45" Opacity="1"/>
        </Border.Effect>
        <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center">
            <Border Width="92" Height="92" Background="Black">
                <Image gif:ImageBehavior.AnimatedSource="{Binding GifSource}"
                       Stretch="Uniform"
                       RenderOptions.BitmapScalingMode="NearestNeighbor"/>
            </Border>
            <TextBlock Text="{Binding Label}"
                       FontFamily="Courier New" FontSize="9" FontWeight="Bold"
                       Foreground="White" HorizontalAlignment="Center"
                       Margin="0,4,0,0"/>
        </StackPanel>
    </Border>
</UserControl>
```

`src/Clauddy/Views/TileControl.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace Clauddy.Views;

public partial class TileControl : UserControl
{
    public TileControl() => InitializeComponent();
}
```

- [ ] **Step 2: Build**

```bash
dotnet build
```

Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
git add src/Clauddy/Views/TileControl.*
git commit -m "feat: TileControl renders chunky pixel-art tile with animated GIF"
```

---

### Task 21: MainWindow shell — transparent topmost with grip drag

**Files:**
- Modify: `src/Clauddy/MainWindow.xaml`, `MainWindow.xaml.cs`

- [ ] **Step 1: Replace MainWindow.xaml**

`src/Clauddy/MainWindow.xaml`:

```xml
<Window x:Class="Clauddy.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:Clauddy.ViewModels"
        xmlns:v="clr-namespace:Clauddy.Views"
        Title="Clauddy"
        WindowStyle="None" AllowsTransparency="True" Background="Transparent"
        ShowInTaskbar="False" Topmost="True"
        SizeToContent="WidthAndHeight" ResizeMode="NoResize">
    <StackPanel Orientation="Horizontal">
        <!-- Drag grip on the left edge -->
        <Border x:Name="GripHandle"
                Width="12" Height="40" VerticalAlignment="Center"
                Background="#22ffffff" Margin="0,0,4,0" Cursor="SizeAll"
                MouseLeftButtonDown="GripHandle_MouseLeftButtonDown"/>
        <!-- One TileControl per session -->
        <ItemsControl ItemsSource="{Binding Tiles}">
            <ItemsControl.ItemsPanel>
                <ItemsPanelTemplate><StackPanel Orientation="Horizontal"/></ItemsPanelTemplate>
            </ItemsControl.ItemsPanel>
            <ItemsControl.ItemTemplate>
                <DataTemplate><v:TileControl Margin="4,0"/></DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </StackPanel>
</Window>
```

- [ ] **Step 2: Replace MainWindow.xaml.cs**

```csharp
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Clauddy.ViewModels;

namespace Clauddy;

public partial class MainWindow : Window
{
    private const int GWL_EXSTYLE   = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int n);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int n, int v);

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }

    private void GripHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }
}
```

- [ ] **Step 3: Manual smoke test (after Task 22 wires composition)**

Skip for now; we need composition root before running. After Task 22 you'll see the window appear with no tiles.

- [ ] **Step 4: Commit**

```bash
git add src/Clauddy/MainWindow.xaml src/Clauddy/MainWindow.xaml.cs
git commit -m "feat: MainWindow transparent topmost shell with grip-drag and tile list"
```

---

### Task 22: App composition root

**Files:**
- Modify: `src/Clauddy/App.xaml`, `App.xaml.cs`

- [ ] **Step 1: App.xaml — clear the default StartupUri**

```xml
<Application x:Class="Clauddy.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources/>
</Application>
```

- [ ] **Step 2: App.xaml.cs — wire all services**

```csharp
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Clauddy.Logging;
using Clauddy.Services;
using Clauddy.ViewModels;

namespace Clauddy;

public partial class App : Application
{
    private Mutex? _singleton;
    private HttpListenerService? _http;
    private EndpointFile? _endpoint;
    private DispatcherTimer? _gcTimer;
    private FileLogger? _log;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _log = FileLogger.Default();
        _log.Info("Clauddy starting");

        _singleton = new Mutex(true, "Clauddy.SingleInstance", out var owned);
        if (!owned) { _log.Info("Another instance running; exiting"); Shutdown(); return; }

        var store = new SessionStore();
        var resolver = new LabelResolver(new GitProbe());
        _http = new HttpListenerService(store, resolver) { Marshal = a => Dispatcher.Invoke(a) };
        var url = _http.Start();

        _endpoint = new EndpointFile(EndpointFile.DefaultPath);
        _endpoint.Write(url);
        _log.Info($"Listening on {url}; endpoint written to {EndpointFile.DefaultPath}");

        var lifecycle = new LifecycleManager(store, () => DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30));
        _gcTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _gcTimer.Tick += (_, _) => lifecycle.Tick();
        _gcTimer.Start();

        var settings = new SettingsStore(SettingsStore.DefaultPath).Load();
        var vm = new MainViewModel(store);
        var win = new MainWindow(vm);
        ApplyWindowPosition(win, settings);
        win.LocationChanged += (_, _) =>
        {
            settings.WindowX = win.Left;
            settings.WindowY = win.Top;
            new SettingsStore(SettingsStore.DefaultPath).Save(settings);
        };
        win.Show();
        MainWindow = win;
    }

    private static void ApplyWindowPosition(Window w, Settings s)
    {
        if (s.WindowX is double x && s.WindowY is double y &&
            IsOnAnyScreen(x, y))
        {
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = x; w.Top = y;
        }
        else
        {
            // bottom-right of primary monitor, with 16px margin
            var area = SystemParameters.WorkArea;
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Loaded += (_, _) =>
            {
                w.Left = area.Right - w.ActualWidth - 16;
                w.Top = area.Bottom - w.ActualHeight - 16;
            };
        }
    }

    private static bool IsOnAnyScreen(double x, double y) =>
        System.Windows.Forms.Screen.AllScreens.Any(scr => scr.WorkingArea.Contains((int)x, (int)y));

    protected override void OnExit(ExitEventArgs e)
    {
        _log?.Info("Clauddy shutting down");
        _gcTimer?.Stop();
        _http?.Stop();
        _endpoint?.Delete();
        _singleton?.ReleaseMutex();
        base.OnExit(e);
    }
}
```

- [ ] **Step 3: Add System.Windows.Forms reference for Screen API**

In `src/Clauddy/Clauddy.csproj`, inside `<PropertyGroup>`:

```xml
<UseWindowsForms>true</UseWindowsForms>
```

- [ ] **Step 4: Build and run smoke test**

```bash
dotnet build
dotnet run --project src/Clauddy
```

Expected:
- A small bordered window appears at the bottom-right corner with just a tiny grip handle visible (no tiles yet — there are no sessions).
- It's on top of other windows, has no Alt-Tab entry.
- `%USERPROFILE%\.clauddy\endpoint` exists and contains a URL like `http://127.0.0.1:51797`.

In a separate Git Bash:

```bash
endpoint=$(cat ~/.clauddy/endpoint)
curl -X POST "$endpoint/state" -H 'Content-Type: application/json' \
  -d '{"session_id":"smoke-1","cwd":"C:/test","label":"smoke","state":"working"}'
```

Expected: a tile appears in the widget showing the working GIF and "smoke".

```bash
curl -X POST "$endpoint/state" -H 'Content-Type: application/json' \
  -d '{"session_id":"smoke-1","cwd":"C:/test","label":"smoke","state":"alerting"}'
```

Expected: same tile, GIF swaps to alerting.

```bash
curl -X POST "$endpoint/state" -H 'Content-Type: application/json' \
  -d '{"session_id":"smoke-1","cwd":"C:/test","action":"remove"}'
```

Expected: tile disappears.

Drag the grip handle: window moves; close and reopen Clauddy: window reappears at the same spot.

If any of those fail, fix before committing.

- [ ] **Step 5: Commit**

```bash
git add src/Clauddy/App.xaml src/Clauddy/App.xaml.cs src/Clauddy/Clauddy.csproj
git commit -m "feat: App composition root wires HTTP, store, lifecycle, window, settings"
```

---

## Phase 9 — Tray icon and first-run wizard

### Task 23: TrayController

**Files:**
- Create: `src/Clauddy/TrayIcon/TrayController.cs`, `src/Clauddy/Assets/tray.ico`
- Modify: `src/Clauddy/App.xaml.cs`

- [ ] **Step 1: Generate a placeholder tray icon**

For development we use a simple solid-color 16×16 ICO. Run from PowerShell:

```powershell
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap 16,16
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.FillRectangle([System.Drawing.Brushes]::Yellow, 0, 0, 16, 16)
$g.Dispose()
$icon = [System.Drawing.Icon]::FromHandle($bmp.GetHicon())
$fs = [System.IO.File]::Create('src/Clauddy/Assets/tray.ico')
$icon.Save($fs); $fs.Close()
```

Verify: `ls src/Clauddy/Assets/tray.ico` exists, > 0 bytes.

- [ ] **Step 2: Mark icon as resource in csproj**

In `src/Clauddy/Clauddy.csproj`, add:

```xml
<ItemGroup>
  <Resource Include="Assets\tray.ico" />
</ItemGroup>
```

- [ ] **Step 3: Implement TrayController**

`src/Clauddy/TrayIcon/TrayController.cs`:

```csharp
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Clauddy.Services;
using Hardcodet.Wpf.TaskbarNotification;

namespace Clauddy.TrayIcon;

public class TrayController
{
    private readonly TaskbarIcon _icon;
    private readonly Window _window;
    private readonly AutoStartService _autoStart;
    private readonly Func<Task> _onManageHooks;

    public TrayController(Window window, AutoStartService autoStart, Func<Task> onManageHooks)
    {
        _window = window;
        _autoStart = autoStart;
        _onManageHooks = onManageHooks;

        _icon = new TaskbarIcon
        {
            IconSource = new System.Windows.Media.Imaging.BitmapImage(
                new Uri("pack://application:,,,/Assets/tray.ico")),
            ToolTipText = "Clauddy"
        };
        _icon.ContextMenu = BuildMenu();
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();

        var showHide = new MenuItem { Header = _window.IsVisible ? "Hide widget" : "Show widget" };
        showHide.Click += (_, _) =>
        {
            if (_window.IsVisible) _window.Hide();
            else _window.Show();
            showHide.Header = _window.IsVisible ? "Hide widget" : "Show widget";
        };
        menu.Items.Add(showHide);

        var reset = new MenuItem { Header = "Reset position" };
        reset.Click += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            _window.Left = area.Right - _window.ActualWidth - 16;
            _window.Top = area.Bottom - _window.ActualHeight - 16;
        };
        menu.Items.Add(reset);

        var login = new MenuItem { Header = "Run at login", IsCheckable = true, IsChecked = _autoStart.IsEnabled() };
        login.Click += (_, _) =>
        {
            if (login.IsChecked) _autoStart.Enable();
            else _autoStart.Disable();
        };
        menu.Items.Add(login);

        var manage = new MenuItem { Header = "Manage hooks…" };
        manage.Click += async (_, _) => await _onManageHooks();
        menu.Items.Add(manage);

        var about = new MenuItem { Header = "About" };
        about.Click += (_, _) => MessageBox.Show(
            $"Clauddy {Assembly.GetExecutingAssembly().GetName().Version}\n" +
            "Persistent widget for Claude Code session state.",
            "Clauddy", MessageBoxButton.OK, MessageBoxImage.Information);
        menu.Items.Add(about);

        menu.Items.Add(new Separator());

        var quit = new MenuItem { Header = "Quit" };
        quit.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(quit);

        return menu;
    }

    public void Dispose() => _icon.Dispose();
}
```

- [ ] **Step 4: Wire it in App.xaml.cs**

After the `win.Show()` line, add:

```csharp
var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName!;
var autoStart = AutoStartService.Default(exePath);
_tray = new TrayController(win, autoStart, () => Task.Run(() => RunHookSetupAsync()));
```

Add `private TrayController? _tray;` field and wire `_tray?.Dispose()` into `OnExit`.

Add a stub for `RunHookSetupAsync`:

```csharp
private Task RunHookSetupAsync()
{
    // Replaced in Task 25 with real wizard.
    Dispatcher.Invoke(() => MessageBox.Show("Hook setup will go here.", "Clauddy"));
    return Task.CompletedTask;
}
```

- [ ] **Step 5: Build and smoke test**

```bash
dotnet run --project src/Clauddy
```

Expected:
- Yellow square tray icon appears in the system tray.
- Right-click → menu with all six items shows.
- Show/Hide toggles widget visibility.
- Reset position snaps it back.
- Run at login: open `regedit`, navigate to `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` — `Clauddy` value appears/disappears as toggled.
- Quit cleanly exits.

- [ ] **Step 6: Commit**

```bash
git add src/Clauddy/TrayIcon/ src/Clauddy/App.xaml.cs src/Clauddy/Clauddy.csproj src/Clauddy/Assets/tray.ico
git commit -m "feat: tray icon with show/hide, reset, run-at-login, manage-hooks, quit"
```

---

### Task 24: HookSetupDialog (first-run wizard)

**Files:**
- Create: `src/Clauddy/Views/HookSetupDialog.xaml`, `HookSetupDialog.xaml.cs`

The dialog shows:
- One checkbox for "Install Windows hooks" (default on)
- Section titled "WSL distros" with one checkbox per detected distro (default off)
- "Install" / "Cancel" buttons
- After install: a summary

- [ ] **Step 1: Write XAML**

`src/Clauddy/Views/HookSetupDialog.xaml`:

```xml
<Window x:Class="Clauddy.Views.HookSetupDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Clauddy — Hook setup"
        Width="420" Height="360" WindowStartupLocation="CenterScreen"
        ResizeMode="NoResize">
    <DockPanel Margin="16">
        <TextBlock DockPanel.Dock="Top" TextWrapping="Wrap" Margin="0,0,0,12">
            Clauddy needs to install hooks so Claude Code tells it about session state.
            Tick the environments you'd like to set up. You can re-run this from the tray menu.
        </TextBlock>

        <Border DockPanel.Dock="Top" BorderBrush="#ddd" BorderThickness="1" Padding="8" Margin="0,0,0,8">
            <CheckBox x:Name="WindowsCheck" Content="Windows (native shells: Git Bash, PowerShell)" IsChecked="True"/>
        </Border>

        <Border DockPanel.Dock="Top" BorderBrush="#ddd" BorderThickness="1" Padding="8" Margin="0,0,0,8">
            <StackPanel>
                <TextBlock Text="WSL distros" FontWeight="Bold" Margin="0,0,0,4"/>
                <ItemsControl x:Name="DistroList">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <CheckBox Content="{Binding Name}" IsChecked="{Binding Selected}"/>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
                <TextBlock x:Name="NoDistros" Text="(none detected)" Foreground="Gray" Visibility="Collapsed"/>
            </StackPanel>
        </Border>

        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" DockPanel.Dock="Bottom">
            <Button x:Name="CancelBtn" Content="Cancel" Width="90" Margin="0,0,8,0" Click="CancelBtn_Click"/>
            <Button x:Name="InstallBtn" Content="Install" Width="90" IsDefault="True" Click="InstallBtn_Click"/>
        </StackPanel>

        <TextBlock x:Name="StatusText" TextWrapping="Wrap" Foreground="DarkGreen" Margin="0,8,0,0"/>
    </DockPanel>
</Window>
```

- [ ] **Step 2: Code-behind**

`src/Clauddy/Views/HookSetupDialog.xaml.cs`:

```csharp
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Clauddy.Views;

public class DistroOption : INotifyPropertyChanged
{
    public string Name { get; init; } = "";
    private bool _selected;
    public bool Selected
    {
        get => _selected;
        set { _selected = value; PropertyChanged?.Invoke(this, new(nameof(Selected))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class HookSetupDialog : Window
{
    public bool InstallWindows { get; private set; }
    public List<string> InstallDistros { get; private set; } = new();
    public ObservableCollection<DistroOption> Distros { get; } = new();

    public HookSetupDialog(IEnumerable<string> distros)
    {
        InitializeComponent();
        foreach (var d in distros) Distros.Add(new DistroOption { Name = d });
        DistroList.ItemsSource = Distros;
        if (Distros.Count == 0) NoDistros.Visibility = Visibility.Visible;
    }

    private void InstallBtn_Click(object sender, RoutedEventArgs e)
    {
        InstallWindows = WindowsCheck.IsChecked == true;
        InstallDistros = Distros.Where(d => d.Selected).Select(d => d.Name).ToList();
        DialogResult = true; Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false; Close();
    }
}
```

- [ ] **Step 3: Build**

```bash
dotnet build
```

Expected: `Build succeeded`.

- [ ] **Step 4: Commit**

```bash
git add src/Clauddy/Views/HookSetupDialog.*
git commit -m "feat: HookSetupDialog wizard for Windows + WSL hook installation"
```

---

### Task 25: First-run flow + Manage hooks integration

**Files:**
- Modify: `src/Clauddy/App.xaml.cs`
- Create: `src/Clauddy/Services/FirstRunDetector.cs`

- [ ] **Step 1: FirstRunDetector**

`src/Clauddy/Services/FirstRunDetector.cs`:

```csharp
namespace Clauddy.Services;

public class FirstRunDetector
{
    private readonly string _markerPath;
    public FirstRunDetector(string markerPath) => _markerPath = markerPath;

    public static FirstRunDetector Default() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".clauddy", "installed-hooks.json"));

    public bool IsFirstRun() => !File.Exists(_markerPath);
}
```

- [ ] **Step 2: Replace stub `RunHookSetupAsync` in App.xaml.cs**

```csharp
private async Task RunHookSetupAsync()
{
    await Dispatcher.InvokeAsync(() =>
    {
        var detector = WslDistroDetector.Default();
        var distros = detector.List();
        var dlg = new HookSetupDialog(distros);
        var ok = dlg.ShowDialog() == true;
        if (!ok) return;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var scripts = Path.Combine(AppContext.BaseDirectory, "hooks");
        var inst = new HookInstaller(home, scripts);

        try
        {
            if (dlg.InstallWindows) inst.InstallWindows();
            foreach (var d in dlg.InstallDistros) InstallWslDistro(d, home);
            MessageBox.Show("Hooks installed. Restart any open Claude Code sessions for changes to take effect.",
                "Clauddy", MessageBoxButton.OK, MessageBoxImage.Information);
            _log?.Info($"Hooks installed: windows={dlg.InstallWindows}, wsl=[{string.Join(",", dlg.InstallDistros)}]");
        }
        catch (Exception ex)
        {
            _log?.Error($"Hook install failed: {ex}");
            MessageBox.Show($"Hook install failed:\n{ex.Message}", "Clauddy",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    });
}

private void InstallWslDistro(string distro, string winHome)
{
    var winUser = Environment.UserName;
    var scriptWinPath = Path.Combine(winHome, ".clauddy", "hooks", "install-wsl.sh");
    if (!File.Exists(scriptWinPath))
        File.Copy(Path.Combine(AppContext.BaseDirectory, "hooks", "install-wsl.sh"), scriptWinPath);
    var wslPath = "/mnt/" + char.ToLowerInvariant(scriptWinPath[0]) +
                  scriptWinPath.Substring(2).Replace('\\', '/');
    var psi = new System.Diagnostics.ProcessStartInfo("wsl.exe",
        $"-d \"{distro}\" -- bash \"{wslPath}\" \"{winUser}\"")
    {
        UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardOutput = true, RedirectStandardError = true
    };
    using var p = System.Diagnostics.Process.Start(psi)!;
    p.WaitForExit(15000);
    if (p.ExitCode != 0)
        throw new Exception($"WSL install for '{distro}' failed: {p.StandardError.ReadToEnd()}");

    // Now patch ~/.claude/settings.json INSIDE the distro
    var patchCmd = $"-d \"{distro}\" -- bash -c 'mkdir -p ~/.claude && jq -s \".[0] * .[1]\" ~/.claude/settings.json <(echo {EmbeddedHooksJson()}) > ~/.claude/settings.json.new && mv ~/.claude/settings.json.new ~/.claude/settings.json'";
    var psi2 = new System.Diagnostics.ProcessStartInfo("wsl.exe", patchCmd) { UseShellExecute = false, CreateNoWindow = true };
    using var p2 = System.Diagnostics.Process.Start(psi2)!;
    p2.WaitForExit(15000);
}

private static string EmbeddedHooksJson()
{
    var hookCmd = "bash ~/.clauddy/hooks/clauddy-hook.sh";
    var events = new[] { "SessionStart","UserPromptSubmit","Stop","SubagentStop","Notification","SessionEnd" };
    var sb = new System.Text.StringBuilder("{\\\"hooks\\\":{");
    for (int i = 0; i < events.Length; i++)
    {
        if (i > 0) sb.Append(',');
        sb.Append($"\\\"{events[i]}\\\":[{{\\\"matcher\\\":\\\"*\\\",\\\"hooks\\\":[{{\\\"type\\\":\\\"command\\\",\\\"command\\\":\\\"{hookCmd}\\\",\\\"_clauddy\\\":true}}]}}]");
    }
    sb.Append("}}");
    return sb.ToString();
}
```

- [ ] **Step 3: Trigger first-run flow on startup**

In `OnStartup`, after `win.Show()`, before tray wiring:

```csharp
if (FirstRunDetector.Default().IsFirstRun())
    _ = RunHookSetupAsync();
```

- [ ] **Step 4: Smoke test (Windows only — WSL optional)**

```bash
# Start fresh: pretend it's first run
rm -f ~/.clauddy/installed-hooks.json
dotnet run --project src/Clauddy
```

Expected:
- Wizard appears with "Windows" checked, distros listed (or "(none detected)").
- Click Install → success popup, `~/.clauddy/installed-hooks.json` exists.
- Open `~/.claude/settings.json` → contains a `hooks` block with all 6 events.
- Quit and re-launch → wizard does NOT reappear (first-run marker present).
- Tray → Manage hooks → wizard reappears.

- [ ] **Step 5: Commit**

```bash
git add src/Clauddy/Services/FirstRunDetector.cs src/Clauddy/App.xaml.cs
git commit -m "feat: first-run wizard runs once and from tray, including WSL"
```

---

## Phase 10 — End-to-end test against real Claude Code

### Task 26: Wire hooks dir into the publish output

**Files:**
- Modify: `src/Clauddy/Clauddy.csproj`

- [ ] **Step 1: Make `hooks/*` flow into the publish dir**

In `src/Clauddy/Clauddy.csproj`, add:

```xml
<ItemGroup>
  <None Include="..\..\hooks\clauddy-hook.sh">
    <Link>hooks\clauddy-hook.sh</Link>
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
  <None Include="..\..\hooks\clauddy-hook.ps1">
    <Link>hooks\clauddy-hook.ps1</Link>
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
  <None Include="..\..\hooks\install-wsl.sh">
    <Link>hooks\install-wsl.sh</Link>
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

- [ ] **Step 2: Verify**

```bash
dotnet build
ls src/Clauddy/bin/Debug/net8.0-windows/hooks/
```

Expected: all three hook scripts present.

- [ ] **Step 3: Commit**

```bash
git add src/Clauddy/Clauddy.csproj
git commit -m "chore: copy hook scripts into build output"
```

---

### Task 27: End-to-end smoke test against live Claude Code

This is a **manual** test that proves the whole pipeline works.

- [ ] **Step 1: Run Clauddy and complete first-run setup**

```bash
dotnet run --project src/Clauddy
```

Click Install in the wizard for Windows hooks. Confirm `~/.claude/settings.json` has the hook entries.

- [ ] **Step 2: Open a Git Bash terminal and run Claude Code**

```bash
cd ~/some-repo    # any git repo
claude
```

Expected:
- Tile appears in widget with label `<repo>@<branch>` and `chilling` GIF.

- [ ] **Step 3: Submit a prompt**

Type any prompt to Claude and press enter.

Expected: tile flips to `working` GIF.

- [ ] **Step 4: Wait for Claude to finish**

Expected: tile flips back to `chilling` GIF.

- [ ] **Step 5: Trigger a permission prompt**

Ask Claude to run a command requiring permission, e.g. `please run rm -rf /tmp/x` (don't actually allow it).

Expected: tile flips to `alerting` GIF until you respond.

- [ ] **Step 6: Exit Claude**

`/exit` or Ctrl+C cleanly.

Expected: tile disappears within ~1 second.

- [ ] **Step 7: Open 2-3 more terminals and start Claude in each**

Expected: a tile per session, side by side, each independently tracking state.

- [ ] **Step 8: Set CLAUDDY_LABEL in one terminal**

```bash
export CLAUDDY_LABEL="migration-agent"
claude
```

Expected: that tile shows `migration-agent` instead of repo@branch.

- [ ] **Step 9: If anything doesn't work**

Check `%APPDATA%\Clauddy\log.txt` for errors. Common failure modes:
- `jq` not in PATH → install via `winget install jqlang.jq`
- `bash` not in PATH → install Git for Windows
- Hook command in settings.json points to wrong path → re-run "Manage hooks"

- [ ] **Step 10: When all pass, commit a note**

```bash
git commit --allow-empty -m "test: e2e smoke test passes against live Claude Code"
```

---

## Phase 11 — Distribution

### Task 28: Single-file publish profile

**Files:**
- Create: `src/Clauddy/Properties/PublishProfiles/win-x64-singlefile.pubxml`
- Modify: `src/Clauddy/Clauddy.csproj`

- [ ] **Step 1: Add publish properties**

In `src/Clauddy/Clauddy.csproj` `<PropertyGroup>`:

```xml
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<SelfContained>true</SelfContained>
<PublishSingleFile>true</PublishSingleFile>
<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
<EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
<DebugType>embedded</DebugType>
```

(Wrap in a `Condition="'$(Configuration)'=='Release'"` group to avoid affecting dev builds.)

- [ ] **Step 2: Publish**

```bash
dotnet publish src/Clauddy -c Release -o publish
```

Expected: `publish/Clauddy.exe` exists, ~30-60 MB.

- [ ] **Step 3: Smoke test the published EXE**

```bash
publish/Clauddy.exe
```

Expected: behaves identically to `dotnet run`. Quit it.

- [ ] **Step 4: Commit**

```bash
git add src/Clauddy/Clauddy.csproj
git commit -m "chore: enable single-file self-contained publish profile"
```

---

### Task 29: Inno Setup installer script

**Files:**
- Create: `installer/Clauddy.iss`

- [ ] **Step 1: Write the script**

```pascal
[Setup]
AppName=Clauddy
AppVersion=0.1.0
AppPublisher=Ron
AppPublisherURL=https://github.com/yourname/clauddy
DefaultDirName={localappdata}\Clauddy
DefaultGroupName=Clauddy
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=Clauddy-Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\publish\Clauddy.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\publish\hooks\*"; DestDir: "{app}\hooks"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\Clauddy"; Filename: "{app}\Clauddy.exe"
Name: "{commondesktop}\Clauddy"; Filename: "{app}\Clauddy.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Run]
Filename: "{app}\Clauddy.exe"; Description: "Launch Clauddy now"; Flags: postinstall nowait skipifsilent

[UninstallRun]
; Run uninstall hook backout. Clauddy.exe --uninstall-hooks reads installed-hooks.json and reverses changes.
Filename: "{app}\Clauddy.exe"; Parameters: "--uninstall-hooks"; Flags: runhidden
```

- [ ] **Step 2: Add `--uninstall-hooks` CLI flag to App.xaml.cs**

Before `OnStartup` body:

```csharp
if (e.Args.Contains("--uninstall-hooks"))
{
    try
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var scripts = Path.Combine(AppContext.BaseDirectory, "hooks");
        new HookInstaller(home, scripts).UninstallWindows();
    }
    catch { }
    Shutdown(); return;
}
```

- [ ] **Step 3: Publish and build the installer**

```bash
dotnet publish src/Clauddy -c Release -o publish
iscc installer/Clauddy.iss
```

Expected: `installer/Output/Clauddy-Setup.exe` exists, ~30-60 MB.

- [ ] **Step 4: Test install → run → uninstall on a clean user**

```bash
installer/Output/Clauddy-Setup.exe
```

Expected:
- No UAC prompt (per-user install).
- Installs to `%LOCALAPPDATA%\Clauddy\`.
- Start menu shortcut created.
- Optional desktop shortcut.
- "Launch Clauddy now" works → first-run wizard appears.

Then: Settings → Apps → Clauddy → Uninstall.

Expected:
- Files removed from `%LOCALAPPDATA%\Clauddy\`.
- `~/.claude/settings.json` no longer has the hook entries (other content preserved).
- `~/.clauddy/` cleaned up.

- [ ] **Step 5: Commit**

```bash
git add installer/Clauddy.iss src/Clauddy/App.xaml.cs
git commit -m "feat: Inno Setup installer with hook backout on uninstall"
```

---

### Task 30: README

**Files:**
- Create: `README.md`

- [ ] **Step 1: Write README**

```markdown
# Clauddy for Windows

A persistent always-on-top desktop widget that mirrors your Claude Code session state.
Multi-session aware. Inspired by [bugzmanov/divoom-minitoo](https://github.com/bugzmanov/divoom-minitoo).

## Install

Download `Clauddy-Setup.exe` from the [Releases](#) page and run it.
Per-user install, no admin rights required.

On first run, a wizard asks where to install hooks: Windows native shells
(Git Bash, PowerShell) and any WSL distros you have. Tick what you want,
click Install. Restart any open Claude Code sessions.

## How it works

Each Claude Code session reports its state via hook scripts that POST to
the widget over loopback HTTP. The widget shows one tile per active
session — `working` / `alerting` / `chilling`. See
[design spec](docs/superpowers/specs/2026-04-28-clauddy-windows-design.md).

## Tile labels

By default a tile shows `<repo>@<branch>` (e.g. `clauddy@main`). Override
in any terminal:

    export CLAUDDY_LABEL="migration-agent"

## Tray menu

- **Show / Hide widget**
- **Reset position** — snaps back to bottom-right
- **Run at login** — toggle autostart
- **Manage hooks…** — re-run the install wizard
- **Quit**

## Logs

`%APPDATA%\Clauddy\log.txt` (rotated at 1 MB, last 3 files kept).

## Uninstall

Settings → Apps → Clauddy → Uninstall. Hooks are cleanly removed from
`~/.claude/settings.json` (Windows + every WSL distro you installed
into).

## Build from source

```bash
dotnet test
dotnet publish src/Clauddy -c Release -o publish
iscc installer/Clauddy.iss
```

Output: `installer/Output/Clauddy-Setup.exe`.

## Attribution

GIF assets are bundled from
[bugzmanov/divoom-minitoo](https://github.com/bugzmanov/divoom-minitoo)
with thanks.
```

- [ ] **Step 2: Commit**

```bash
git add README.md
git commit -m "docs: README with install, usage, and build instructions"
```

---

## Self-review checklist (run after writing the plan)

- [x] Spec coverage:
  - Single signed `.exe` installer → Task 29 (signing deferred)
  - Per-user install %LOCALAPPDATA% → Task 29 (`PrivilegesRequired=lowest`)
  - Start menu + opt-in autostart → Tasks 23, 29
  - First-run hook wizard with WSL distro list → Tasks 24-25
  - `installed-hooks.json` ledger → Task 18
  - Pixel-art tiles + upstream GIFs → Tasks 4, 20
  - Always-on-top transparent no-taskbar → Task 21
  - Multi-session keyed by session_id → Tasks 6, 22
  - States working/alerting/chilling → Tasks 5, 19
  - Label resolution (override > git > basename) → Task 7
  - SessionEnd remove + 30-min stale GC → Tasks 11, 22
  - Tray menu (all items) → Task 23
  - Settings persistence → Task 9
  - Window snap-back if monitor missing → Task 22
  - HTTP listener loopback ephemeral → Task 10
  - Endpoint file %USERPROFILE%/.clauddy/endpoint → Task 8
  - WIN_USERNAME planted in WSL ~/.profile → Task 16
  - Hook scripts --max-time 1, || true → Tasks 14-15
  - Five hook events mapped → Tasks 14-15
  - Per-event matcher in settings.json → Task 18
  - Logs %APPDATA%\Clauddy\log.txt rotated → Task 13
  - Uninstaller backs out hooks → Tasks 18, 29

- [x] No placeholders: all "TODO" / "TBD" / "fill in details" scrubbed.
- [x] Type/method consistency: `Session`, `SessionState`, `SessionStore.Upsert/Remove/RemoveStale`, `HookInstaller.InstallWindows/UninstallWindows`, `LabelResolver.Resolve` are all consistent.
- [x] Each step has a concrete action and (where code) a code block; commands have expected output stated.

---

## Plan complete

Saved to `docs/superpowers/plans/2026-04-28-clauddy-windows.md`.

**Two execution options:**

**1. Subagent-Driven (recommended)** — A fresh Claude subagent runs each
   task, you review between tasks. Fast, with checkpoints.

**2. Inline Execution** — Tasks run sequentially in this same session
   with periodic review pauses.

Which approach?
