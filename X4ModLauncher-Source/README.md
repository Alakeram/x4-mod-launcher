# X4 Mod Launcher

[Download the latest packaged release](https://github.com/Alakeram/x4-mod-launcher/releases/latest) · [Download the organized ZIP directly](https://github.com/Alakeram/x4-mod-launcher/releases/download/v1.0.0/X4ModLauncher-Nexus-Organized.zip)

Windows WPF desktop launcher for selecting installed, non-DLC X4: Foundations extensions and safely applying their enabled state to one configurable `content.xml` profile.

## Current status

Implemented:

- native WPF UI targeting .NET 8 for Windows;
- configurable X4 install, profile, and backup paths; the Extensions path is derived from the X4 install;
- discovery of extension folders and their `content.xml` metadata;
- one checkbox per discovered non-DLC extension, with current vs desired state;
- DLC visibility as read-only and unchanged by default;
- named Mod Profiles that save and load desired extension selections;
- Deprecated / Old Extension detection with confirmed stale-profile-entry cleanup;
- selectable timestamped backups named `<ProfileName>-Backup-MM-DD-YYYY_HH-mm-ss.xml`;
- dependency warnings from extension metadata;
- duplicate-ID detection, unavailable-folder warnings, timestamped backups, atomic replacement, post-write verification, and restore;
- explicit `enabled` plus `sync` handling for Native Hotkey API (`ws_3750545906`), preserving the legacy Steam-sync workaround;
- Apply, Apply & Launch, and direct Launch Game actions, with launch blocked when Apply or verification fails;
- packaged X4 icon for the launcher executable and window/taskbar identity;
- offline fixture tests for toggling, XML preservation, duplicates, unavailable folders, Steam sync, and rollback/restore;

## Build and test

Prerequisites: Windows, .NET 8 SDK, and the Windows Desktop runtime/SDK available to the SDK installation.

```powershell
dotnet build .\src\X4ModLauncher\X4ModLauncher.csproj --configuration Release
dotnet run --project .\tests\X4ModLauncher.Core.Tests\X4ModLauncher.Core.Tests.csproj --configuration Release
```

For packaging and release guidance, read [`docs\DISTRIBUTING.md`](docs/DISTRIBUTING.md).

## Safety model

The launcher edits only the configured profile `content.xml`; it never edits saves. X4 must be closed for Apply, cleanup, and Restore. DLC entries are not selectable and show `N/A` for Current and Desired. Before changing the profile, the app verifies the written XML and creates a timestamped backup. Loading or saving a modded save while required extensions are disabled can break save continuity, so use an appropriate save and verify the in-game loaded-extension list after the first launch.
