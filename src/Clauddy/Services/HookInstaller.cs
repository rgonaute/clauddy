using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Clauddy.Services;

public class HookInstaller
{
    private readonly string _home;
    private readonly string _scriptSource;
    // Events we register on install. The hook script filters Notification on its
    // own (only permission-prompt messages flip alerting), so we register it.
    private static readonly string[] InstallEvents =
        { "SessionStart", "UserPromptSubmit", "Stop", "SubagentStop", "Notification", "SessionEnd" };
    // Events we sweep on uninstall — same set; kept separate so install/uninstall
    // can diverge if needed without surprising legacy users.
    private static readonly string[] UninstallEvents =
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
        // Drop any stale Clauddy registrations (e.g. legacy Notification hook) before
        // re-adding the current event set, otherwise reinstalling won't unwire them.
        foreach (var evt in UninstallEvents) StripClauddyEntries(hooks, evt);
        var hookCommand = $"bash \"{hooksDir.Replace('\\', '/')}/clauddy-hook.sh\"";
        foreach (var evt in InstallEvents)
        {
            hooks[evt] = new JsonArray(new JsonObject
            {
                ["matcher"] = "*",
                ["hooks"] = new JsonArray(new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = hookCommand,
                    ["_clauddy"] = true
                })
            });
        }
        root["hooks"] = hooks;
        File.WriteAllText(settingsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

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
            foreach (var evt in UninstallEvents.ToList()) StripClauddyEntries(hooks, evt);
            if (hooks.Count == 0) root.Remove("hooks");
        }
        File.WriteAllText(settingsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var hooksDir = Path.Combine(_home, ".clauddy", "hooks");
        try { Directory.Delete(hooksDir, true); } catch { }
        try { File.Delete(Path.Combine(_home, ".clauddy", "installed-hooks.json")); } catch { }
    }

    private static void StripClauddyEntries(JsonObject hooks, string evt)
    {
        if (hooks[evt] is not JsonArray arr) return;
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
