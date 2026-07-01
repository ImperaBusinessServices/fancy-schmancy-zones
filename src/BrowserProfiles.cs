using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace FancySchmancyZones;

/// <summary>
/// Works out which Chrome/Edge/Brave profile a browser window is using.
///
/// Chromium doesn't expose the profile through the window or the process — Chrome's toolbar
/// avatar button (class "AvatarToolbarButton") is labelled with the profile's display name,
/// and Edge has its own version (class "EdgeAvatarToolbarButton", labelled e.g.
/// "Personal Profile" or "Profile 2 Profile, Please sign in"). We read that label via UI
/// Automation and map it to the on-disk profile folder (e.g. "Default", "Profile 10") that
/// --profile-directory needs to relaunch that exact profile.
///
/// IMPORTANT: if two profiles share the same display name, the label alone can't tell them
/// apart — nothing in the accessibility tree reveals more (checked: no unique AutomationId,
/// no tooltip, no hidden value). Rather than guess and risk landing a window in the wrong
/// profile, ambiguous names are treated as "unknown" so callers fall back to their normal,
/// profile-blind matching.
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

    private static string AvatarClassName(string proc) =>
        proc.Equals("msedge", StringComparison.OrdinalIgnoreCase) ? "EdgeAvatarToolbarButton" : "AvatarToolbarButton";

    private sealed class ProfileMap
    {
        // Display label -> profile folder, for labels that identify exactly one profile.
        public readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase);
        // Labels shared by 2+ profiles — deliberately excluded from Map above.
        public readonly HashSet<string> Ambiguous = new(StringComparer.OrdinalIgnoreCase);
    }

    // Profile lists barely change during a session, so read the file once per browser.
    private static readonly Dictionary<string, ProfileMap> _cache = new();

    private static ProfileMap GetMap(string proc)
    {
        string key = proc.ToLowerInvariant();
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var result = new ProfileMap();
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
                        foreach (var label in DisplayLabels(prof.Value))
                            AddLabel(result, label, prof.Name);
                }
            }
        }
        catch { /* unreadable Local State — profiles just won't be distinguished */ }

        _cache[key] = result;
        return result;
    }

    // A profile can show up under more than one label: its internal "name" (what the avatar
    // button on Chrome shows) and, on Edge, a separate "shortcut_name" (what Edge's button and
    // window title show, e.g. "Personal" for the primary profile).
    private static IEnumerable<string> DisplayLabels(JsonElement profile)
    {
        string? name = profile.TryGetProperty("name", out var n) ? n.GetString() : null;
        string? shortcut = profile.TryGetProperty("shortcut_name", out var s) ? s.GetString() : null;
        if (!string.IsNullOrEmpty(name)) yield return name;
        if (!string.IsNullOrEmpty(shortcut) && !string.Equals(shortcut, name, StringComparison.OrdinalIgnoreCase))
            yield return shortcut;
    }

    private static void AddLabel(ProfileMap m, string label, string folder)
    {
        if (m.Ambiguous.Contains(label)) return;
        if (m.Map.TryGetValue(label, out var existing))
        {
            if (!existing.Equals(folder, StringComparison.OrdinalIgnoreCase))
            {
                m.Map.Remove(label);
                m.Ambiguous.Add(label);   // two different profiles, same label — can't tell apart
            }
        }
        else m.Map[label] = folder;
    }

    /// <summary>True if this browser has 2+ profiles sharing a display name (detection is unreliable for those).</summary>
    public static bool HasAmbiguousProfiles(string proc) => GetMap(proc).Ambiguous.Count > 0;

    /// <summary>
    /// The profile FOLDER this browser window is using (e.g. "Profile 5"), or "" if unknown
    /// (no avatar found, or its name is shared by more than one profile).
    /// </summary>
    public static string DetectFolder(IntPtr hwnd, string proc)
    {
        var map = GetMap(proc);
        if (map.Map.Count == 0 && map.Ambiguous.Count == 0) return "";
        try
        {
            var cond = new System.Windows.Automation.PropertyCondition(
                AutomationElement.ClassNameProperty, AvatarClassName(proc));
            var el = AutomationElement.FromHandle(hwnd);
            var avatar = el?.FindFirst(TreeScope.Descendants, cond);
            return MatchProfile(avatar?.Current.Name, map);
        }
        catch { return ""; }
    }

    private static string MatchProfile(string? raw, ProfileMap map)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        string s = CleanLabel(raw);

        // Prefer a parenthetical profile name if present: "Keith (188PHV)" -> "188PHV".
        int lp = s.LastIndexOf('('), rp = s.LastIndexOf(')');
        if (lp >= 0 && rp > lp)
        {
            string inner = s[(lp + 1)..rp].Trim();
            if (map.Map.TryGetValue(inner, out var f1)) return f1;
            if (map.Ambiguous.Contains(inner)) return "";
        }

        if (map.Map.TryGetValue(s, out var f2)) return f2;
        if (map.Ambiguous.Contains(s)) return "";

        // Last resort: any known, unambiguous profile name appearing in the label.
        foreach (var kv in map.Map)
            if (s.Contains(kv.Key, StringComparison.OrdinalIgnoreCase)) return kv.Value;
        return "";
    }

    // Strips the chrome around the actual profile name in each browser's avatar label:
    // Chrome: "Hi, Keith" -> "Keith"
    // Edge:   "Personal Profile" -> "Personal" ; "Profile 2 Profile, Please sign in" -> "Profile 2"
    private static string CleanLabel(string raw)
    {
        string s = raw.Trim();
        s = Regex.Replace(s, @"^Hi,\s*", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @",\s*Please sign in$", "", RegexOptions.IgnoreCase).Trim();
        s = Regex.Replace(s, @"\s+Profile$", "", RegexOptions.IgnoreCase).Trim();
        return s;
    }
}
