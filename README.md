# X4 Mod Launcher

[Download the latest release](https://github.com/Alakeram/x4-mod-launcher/releases/latest) · [Changelog](CHANGELOG.md)

Windows WPF desktop launcher for X4: Foundations. Create named mod profiles, switch between mod configurations in one click, and launch the game. It safely applies the enabled state of installed, non-DLC extensions to one configurable X4 profile `content.xml`.

## Features

- native WPF UI targeting .NET 8 for Windows;
- configurable X4 install, profile, and backup paths, with Steam auto-discovery; the Extensions path is derived from the X4 install;
- discovery of extension folders and their `content.xml` metadata (name, version, dependencies);
- one toggle per discovered non-DLC extension, with current vs desired state;
- DLC visibility as read-only and unchanged by default;
- named Mod Profiles that save and load desired extension selections, plus a built-in `Native` profile (all mods off);
- Deprecated / Old Extension detection with confirmed stale-profile-entry cleanup;
- selectable timestamped backups named `<ProfileName>-Backup-MM-DD-YYYY_HH-mm-ss.xml`;
- dependency warnings from extension metadata;
- duplicate-ID detection, unavailable-folder warnings, atomic replacement, post-write verification, and restore;
- explicit `enabled` plus `sync` handling for Native Hotkey API (`ws_3750545906`), so Steam Workshop sync cannot re-enable it in an unmodded profile;
- Apply, Apply & Launch, Load & Launch, and direct Launch Game actions, with launch blocked when Apply or verification fails.

Known limitations:

- no automated in-game loaded-extension verification exists yet;
- the release is not code-signed.

## Safety model

The launcher edits only the configured profile `content.xml`; it never edits saves. X4 must be closed for Apply, cleanup, and Restore. DLC entries are not selectable and show `N/A` for Current and Desired. Before changing the profile, the app verifies the written XML and creates a timestamped backup. Loading or saving a modded save while required extensions are disabled can break save continuity, so use an appropriate save and verify the in-game loaded-extension list after the first launch.

Settings and Mod Profiles are stored in `%LocalAppData%\X4ModLauncher\settings.json`. The launcher is local-only: no telemetry, downloads, or auto-update.

## Build

Prerequisites: Windows and the .NET 8 SDK (includes the Windows Desktop SDK).

```powershell
dotnet build .\src\X4ModLauncher\X4ModLauncher.csproj --configuration Release
```

Publish the single-file release executable:

```powershell
dotnet publish .\src\X4ModLauncher\X4ModLauncher.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -o .\artifacts\publish\win-x64
```

Bump `VersionPrefix` in `Directory.Build.props` and add a `CHANGELOG.md` entry for each release.

## Project layout

- `src/X4ModLauncher.Core` — profile XML read/apply/restore, extension discovery, settings, game launch (no UI).
- `src/X4ModLauncher` — WPF application.

Made by: Alakeram
