# GameGen App

A Windows desktop app for searching the Steam Store, managing Steam game manifests, and installing them via the [GameGen](https://gamegen.lol) API — built with WinUI 3 (Windows App SDK, unpackaged).

---

## Features

- **Steam Store search** — look up any game by name and retrieve its Steam App ID
- **Installed games browser** — scan your local Steam library from `appmanifest` files without launching Steam
- **Manifest library** — track every manifest you have installed through the app, with one-click removal
- **One-click manifest install** — calls the GameGen `/api/{key}/generate/{id}` endpoint and drops the output straight into your `stplug-in` and `depotcache` folders
- **SteamTools integration** — optionally invokes SteamTools for manifest activation after install
- **Discord Rich Presence** — shows what you are browsing in your Discord status (can be disabled)
- **Dark / light theme** — follows Windows or forces dark Fluent styling
- **Auto-updater** — checks GitHub Releases on demand, downloads the new exe in-app with a progress bar, and hot-swaps it on restart (no installer needed)

---

## Requirements

- Windows 10 version 1809 (build 17763) or later
- A [GameGen](https://gamegen.lol) account and API key
- Steam installed (paths are auto-detected from the registry; manual override available)
- [SteamTools](https://github.com/BeyondDimension/SteamTools) *(optional, for manifest activation)*

---

## Getting Started

1. Download `ManifestApp.exe` from the [latest release](https://github.com/TheMich157/gamegen-app/releases/latest)
2. Run it — no installer required
3. On first launch the setup wizard will ask for:
   - Your **Steam install folder** (auto-detected if Steam is installed normally)
   - Your **GameGen API key** (stored in Windows Credential Locker — never written to disk in plain text)
4. Search for a game, select it, and click **Install manifests**

---

## Settings

| Setting | Description |
|---|---|
| Steam install folder | Overrides registry detection |
| stplug-in folder | Where `.lua` plugin scripts are placed |
| depotcache folder | Where `.manifest` files are placed |
| SteamTools.exe | Optional path override |
| GameGen API base URL | Leave blank to use `https://gamegen.lol` |
| Dark theme | Force dark Fluent styling regardless of Windows setting |
| Discord Rich Presence | Toggle activity status in Discord |

---


## License

This project is licensed under the terms described in [LICENSE](LICENSE).
