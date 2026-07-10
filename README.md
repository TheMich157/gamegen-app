# GameGen App

A Windows desktop app for searching the Steam Store, managing Steam game manifests, and installing them via the [GameGen](https://gamegen.lol) API — built with WinUI 3 (Windows App SDK, unpackaged).

---

## Features

- **Steam Store search** — look up any game by name and retrieve its Steam App ID
- **Installed games browser** — scan your local Steam library from `appmanifest` files without launching Steam
- **Manifest library** — track every manifest you have installed through the app, with one-click removal
- **One-click manifest install** — calls the GameGen `/api/v2/generate/{id}` endpoint (Bearer auth) and drops the output into your `stplug-in` and `depotcache` folders
- **Online fixes** — when a matching fix exists, use **Fix available** on the game page to download and apply it manually (auto-install is off by default in Settings)
- **SteamTools integration** — optionally invokes SteamTools for manifest activation after install
- **Discord Rich Presence** — shows what you are browsing in your Discord status (can be disabled)
- **Dark / light theme** — follows Windows or forces dark Fluent styling
- **Auto-updater** — checks GitHub Releases on startup and in Settings; install is user-initiated with optional SHA-256 verification when `ManifestApp.exe.sha256` is attached to the release

---

## Requirements

- Windows 10 version 1809 (build 17763) or later
- [.NET 10 SDK](https://dotnet.microsoft.com/download) and Visual Studio 2022 with the **Windows application development** workload (for WinUI 3)
- A [GameGen](https://gamegen.lol) account and API key
- Steam installed (paths are auto-detected from the registry; manual override available)
- [SteamTools](https://github.com/BeyondDimension/SteamTools) *(optional, for manifest activation)*

---

## Getting Started (release build)

1. Download `ManifestApp.exe` from the [latest release](https://github.com/TheMich157/gamegen-app/releases/latest)
2. Run it — no installer required
3. On first launch the setup wizard will ask for:
   - Your **Steam install folder** (auto-detected if Steam is installed normally)
   - Your **GameGen API key** (stored with Windows DPAPI — not written to disk in plain text)
4. Search for a game, select it, and click **Install manifests**

---

## Building from source

```powershell
git clone https://github.com/TheMich157/gamegen-app.git
cd gamegen-app

dotnet restore ManifestApp.slnx
dotnet test ManifestApp.Core.Tests/ManifestApp.Core.Tests.csproj -c Release
dotnet build ManifestApp/ManifestApp.csproj -c Release -p:Platform=x64
```

Run the unpackaged app from Visual Studio using the **ManifestApp (Unpackaged)** profile, or publish:

```powershell
dotnet publish ManifestApp/ManifestApp.csproj -c Release -p:Platform=x64
```

**Assets:** placeholder icons and logos live under `ManifestApp/Assets/`. Replace them with production artwork before shipping a release.

**Publish profiles:** MSIX/pubxml files are local-only (gitignored). Create `ManifestApp/Properties/PublishProfiles/win-x64.pubxml` in Visual Studio if you use Package publish.

---

## Settings

| Setting | Description |
|---|---|
| Steam install folder | Overrides registry detection |
| stplug-in folder | Where `.lua` plugin scripts are placed |
| depotcache folder | Where `.manifest` files are placed |
| SteamTools.exe | Optional path override |
| GameGen API base URL | Leave blank to use `https://gamegen.lol` |
| Auto-install Steam game and online fix | Off by default; use **Fix available** on game pages for manual fixes |
| Dark theme | Force dark Fluent styling regardless of Windows setting |
| Discord Rich Presence | Toggle activity status in Discord |

---

## License

This project is licensed under the terms described in [LICENSE](LICENSE).
