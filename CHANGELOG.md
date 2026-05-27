# Changelog

## [3.1.0] - 2026-05-27

### Added
- **Bulk Operations** — You can now select multiple games in the grid to install or remove manifests in bulk.
- **Quota Error Handling** — Specific prompt guiding users to upgrade their plan when hitting their daily generation limit.

---

## [2.0.0] – 2026-05-09

### Added
- **Admin dashboard session cards** — each connected client now shows a full card in ManifestAdmin: avatar circle with online/offline dot, username, role badge (gold/orange/muted), staff badge (Discord blurple), plan badge, last event, usage counter, and last-seen timestamp.
- **Admin action buttons on cards** — every session card has a context-aware **Disable/Enable App** button (`ToggleDisableCommand`) and a **Force Update** button (`ForceUpdateCommand`), moving all admin control surface to the ManifestAdmin dashboard. The user app only executes commands it receives — it contains no admin UI.
- **DISABLED badge on cards** — when a key is disabled the session row gains a red "DISABLED" badge so the status is visible at a glance without opening the button.
- **Heartbeat timer** — user app now sends `POST /api/report` (type: heartbeat) every 5 minutes so the admin dashboard can accurately track which clients are online vs. gone offline. Timer is stopped cleanly on window close.

---

## [1.4.6] – 2026-05-09

### Added
- **Machine activation** — app now calls `POST /api/{key}/activate` on every startup, registering the machine (stable HWID persisted in settings), OS version, and app version with the GameGen server. The richer response (user identity + usage) replaces the separate `/stats` call, saving a round-trip. Falls back to `/stats` automatically if activation returns an error.
- **New-user welcome** — when the activation response includes `isNewUser: true`, a success `InfoBar` appears briefly in the main window welcoming the user and confirming their plan.
- **Stable machine ID** — a GUID is generated on first launch, persisted in `AppSettings.MachineId`, and sent with every activation so the server can correlate installs across sessions.

### Changed
- `RefreshUserStatsAsync` now runs activation first; `/stats` is the fallback, not the primary call.
- `resetAt` from the activation endpoint is a Unix timestamp (integer) — the client normalises it to ISO-8601 so the existing `ResetAtLabel` formatting works unchanged.

---

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
