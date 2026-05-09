# Changelog

## [1.4.5] – 2026-05-09

### Added
- **Game Requests tab** — formally request any game be added to the GameGen registry via `POST /api/{key}/request/{appId}`. Pre-populated automatically when an install fails.
- **User stats pane** — left nav footer now shows the signed-in Discord account, plan name, and remaining daily credits fetched from `GET /api/{key}/stats`.
- **Setup wizard** — first-time users are walked through API key and Steam path configuration in a step-by-step `ContentDialog` before their first install attempt, replacing the old blocking warning dialogs.
- **Daily limit dialog** — when generation fails due to quota exhaustion, a dedicated dialog shows the plan name, total daily allowance, and a reset reminder instead of the generic error.
- **Game-not-found flow** — non-quota failures now show an `InfoBar` explaining the Requests tab with a direct "Go to Requests tab" button that pre-selects the game.
- **Hero catalog counter** — the home page now displays **50k+** titles in the GameGen registry stat badge.
- **Circular app icon** — all icon assets (taskbar, title bar, Start, splash) now use a circular alpha-masked logo with a transparent background, removing the black square surround.
- **Download progress bar** — ZIP download from GameGen switches from an indeterminate spinner to a determinate progress bar with percentage once streaming begins.

### Changed
- **Discord account display** — the pane footer now resolves the Discord username through an expanded field list (`discordUsername`, `discord_username`, `discordName`, `globalName`, etc.) and falls back to the raw Discord ID snowflake before showing a generic label.
- **Quota detection** — `CheckQuotaAsync` replaces the old two-method chain, fetching `/stats` once and threading the result to both the detection logic and the dialog, eliminating the duplicate API round-trip on failures.
- **Icon accent color** — the pane footer icon and Discord tag now use Discord's official blurple `#5865F2` rather than the generic purple.

### Fixed
- **Compile error** — `LooksLikeZip` had a bare `=>` with no body (truncation artifact from a prior edit). Restored the correct `PK\x03\x04` magic-byte check.
- **Null dereference** — `apiKey!.Trim()` in `Install_Click` replaced with an explicit null/empty guard that surfaces a clear error message instead of throwing `NullReferenceException`.
- **False-positive quota detection** — removed `"rate"` from the keyword heuristic; it matched unrelated error messages and could incorrectly route network errors to the daily-limit dialog.

---

## [1.4.2] – prior release

Initial changelog entry. See Git history for earlier changes.
