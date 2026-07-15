using System.IO;
using Microsoft.Win32;

namespace FancySchmancyZones;

/// <summary>
/// "Start with Windows" — a per-user Run entry, so the tray icon (and the hotkeys) are
/// back on their own after a reboot.
///
/// Windows itself is the source of truth here, never a copy in layouts.json: the user can
/// also turn this off in Task Manager's Startup tab, and a menu checkmark that disagreed
/// with what Windows will actually do would be worse than no checkmark at all.
/// </summary>
internal static class Startup
{
    private const string RunKey      = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName   = "Fancy Schmancy Zones";

    /// Installs before v0.11.1 put a shortcut in the Startup folder instead. We still honour
    /// one if it's there, but any toggle moves that user over to the Run entry — two ways in
    /// means two icons in the tray after a sign-in.
    private static string LegacyShortcut =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), ValueName + ".lnk");

    /// <summary>Will Windows actually launch us at sign-in right now?</summary>
    public static bool IsEnabled()
    {
        try
        {
            if (File.Exists(LegacyShortcut)) return true;
            using var run = Registry.CurrentUser.OpenSubKey(RunKey);
            if (run?.GetValue(ValueName) is not string) return false;
            return !IsBlockedByWindows();
        }
        catch { return false; }
    }

    /// <summary>Task Manager's Startup tab doesn't remove the Run entry when you disable an
    /// app — it leaves the entry alone and records a flag here that Windows honours instead.</summary>
    private static bool IsBlockedByWindows()
    {
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
        // 12 bytes, and the low bit of the first is the disabled flag (2/6 = on, 3 = off).
        return approved?.GetValue(ValueName) is byte[] b && b.Length > 0 && (b[0] & 1) != 0;
    }

    /// <summary>Turn start-at-sign-in on or off. Returns what Windows reports afterwards, so
    /// the caller never promises something that didn't take.</summary>
    public static bool SetEnabled(bool on)
    {
        try
        {
            if (on)
            {
                string? exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return IsEnabled();

                using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
                    run.SetValue(ValueName, $"\"{exe}\"");

                // Drop any "disabled" flag left by Task Manager, or the entry above is ignored.
                using (var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true))
                    approved?.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            else
            {
                using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
                run?.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            TryDelete(LegacyShortcut);   // whichever way we just went, the Run entry is the only one
        }
        catch { }

        return IsEnabled();
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
