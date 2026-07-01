using System.IO;
using System.Text.Json;
using System.Windows.Automation;

namespace FancySchmancyZones;

/// <summary>
/// Works out which Chrome/Edge profile a browser window is using.
///
/// The trick: Chromium doesn't tell us the profile through the window or the process,
/// but the toolbar's avatar button (class "AvatarToolbarButton") is labelled with the
/// profile's name — e.g. "Keith", "Keith (188PHV)". We read that label via UI Automation
/// and map it to the on-disk profile folder (e.g. "Default", "Profile 10") that Chrome's
/// --profile-directory switch needs to relaunch that exact profile.
/// </summary>
public static class BrowserProfiles
{
    private static string? UserDataDir(string proc)
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return proc.ToLowerInvariant() switch
        {
            "chrome" => Path.Combine(local, "Google", "Chrome", "User Data"),
            "msedge" => Path.Combine(local, "Microsoft", "Edge", "User Data"),
            "brave"  => Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data"),
            _ => null
        };
    }

    // Profile lists barely change during a session, so read the file once per browser.
    private static readonly Dictionary<string, Dictionary<string, string>> _cache = new();

    /// <summary>Map of profile display-name -> profile folder for a browser (case-insensitive).</summary>
    public static Dictionary<string, string> NameToFolder(string proc)
    {
        string key = proc.ToLowerInvariant();
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var dir = UserDataDir(proc);
            var path = dir == null ? null : Path.Combine(dir, "Local State");
            if (path != null && File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("profile", out var pe) &&
                    pe.TryGetProperty("info_cache", out var ic))
                {
                    foreach (var prof in ic.EnumerateObject())
                    {
                        if (prof.Value.TryGetProperty("name", out var nameEl))
                        {
                            var nm = nameEl.GetString();
                            if (!string.IsNullOrEmpty(nm)) map[nm] = prof.Name; // name -> folder
                        }
                    }
                }
            }
        }
        catch { /* unreadable Local State — profiles just won't be distinguished */ }

        _cache[key] = map;
        return map;
    }

    private static readonly Condition AvatarCond =
        new PropertyCondition(AutomationElement.ClassNameProperty, "AvatarToolbarButton");

    /// <summary>
    /// The profile FOLDER this browser window is using (e.g. "Profile 5"), or "" if unknown.
    /// </summary>
    public static string DetectFolder(IntPtr hwnd, string proc)
    {
        var map = NameToFolder(proc);
        if (map.Count == 0) return "";
        try
        {
            var el = AutomationElement.FromHandle(hwnd);
            var avatar = el?.FindFirst(TreeScope.Descendants, AvatarCond);
            return MatchProfile(avatar?.Current.Name, map);
        }
        catch { return ""; }
    }

    /// <summary>
    /// Turn an avatar label ("Hi, Keith", "Keith (188PHV)") into a profile folder.
    /// A customised profile shows its name in parentheses; otherwise the whole label is the name.
    /// </summary>
    internal static string MatchProfile(string? raw, Dictionary<string, string> map)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        string s = raw.Trim();
        if (s.StartsWith("Hi, ", StringComparison.OrdinalIgnoreCase)) s = s[4..].Trim();

        // Prefer the parenthetical profile name if present: "Keith (188PHV)" -> "188PHV".
        int lp = s.LastIndexOf('('), rp = s.LastIndexOf(')');
        if (lp >= 0 && rp > lp)
        {
            string inner = s[(lp + 1)..rp].Trim();
            if (map.TryGetValue(inner, out var f1)) return f1;
        }
        if (map.TryGetValue(s, out var f2)) return f2;

        // Last resort: any known profile name appearing in the label.
        foreach (var kv in map)
            if (s.Contains(kv.Key, StringComparison.OrdinalIgnoreCase)) return kv.Value;
        return "";
    }
}
