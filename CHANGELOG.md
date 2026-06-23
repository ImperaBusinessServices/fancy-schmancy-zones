# Changelog

## 0.3.0 — 2026-06-23

First public release.

- Lock the current window arrangement as a named layout; flip between layouts.
- Hotkeys via a low-level keyboard listener (works even when other utilities have
  claimed common shortcuts): `Ctrl+Alt+Shift+L` lock, `Ctrl+Alt+Shift+Q` next,
  `Ctrl+Alt+Shift+W` previous.
- Left- or right-click the tray icon to pick, rename, update, or delete layouts.
- One-click, per-user Windows installer (no admin, no .NET prerequisite) with an
  optional "start at sign-in" choice.
- App icon.

### Known limitation
- Layouts only see windows on the current Windows Virtual Desktop. Full multi-desktop
  support is on the roadmap (needs undocumented, version-specific Windows APIs).
