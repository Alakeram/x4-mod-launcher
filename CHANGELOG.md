# Changelog

All notable changes to X4 Mod Launcher are documented here. Versions follow [Semantic Versioning](https://semver.org/).

## [1.1.0] - Unreleased

### Changed
- **Removed redundant buttons.** Load and Load & Launch are gone. Choosing a profile in the drop-down now previews it in the toggle list without writing anything; Apply / Apply & Launch write what is shown. Previously Apply ignored the selected profile and only wrote the toggles, so selecting a profile (including Native) and pressing Apply could write a different setup than the one named, with a misleading backup label.
- "Save Current" is now "Save". A status note next to the profile shows "Modified — press Save to keep" and "Not applied yet" when the toggles differ from the saved profile or from X4.
- On startup and after Apply, the profile matching the current X4 setup is selected automatically.
- Mod Profiles now remember which mods were off as well as on. A mod that is not in a profile (for example one installed after the profile was saved) keeps its current state and is flagged "Not in this profile — left as is" instead of being switched off silently. Profiles saved with v1.0 keep their old behavior until saved again.
- Mod Profiles belong to the X4 profile folder they were created for, so separate X4 profiles no longer share one list. Existing profiles are assigned to the configured X4 profile on first start.
- Steam installs are launched through Steam (`steam://rungameid/392160`); other installs still start `X4.exe`.
- Launch Game asks for confirmation when the toggles shown have not been applied.
- Apply skips writing (and creating a backup) when X4 already matches the toggles.
- Creating a profile with an existing name asks before overwriting it.
- The extension list shows enabled mods first, then disabled mods, then deprecated entries and DLC. It re-sorts on load, after Apply, and when a profile is chosen, not on every toggle.
- Disabled mods no longer show warnings (missing dependencies, metadata errors, not in profile); their status reads "Disabled". Warnings only appear for mods that are on.

### Added
- Backup retention: only the newest 20 launcher backups are kept by default ("Keep newest backups", 0 keeps all). Only files named by the launcher are deleted, and never the backup just made.

### Fixed
- An installed mod with an unreadable `content.xml` was listed as Deprecated / Old Extension and could have its profile entry removed by cleanup. It is now shown as installed with a metadata error and left unchanged.
- The status message after Apply was immediately overwritten by the reload message.
- Settings are written atomically, and an unreadable settings file is kept as `settings.json.corrupt-<timestamp>` instead of being replaced, so saved profiles are not lost.

### Notes
- The settings file is upgraded to a new format on first save. v1.0 cannot read profiles saved by v1.1; downgrading would show an empty profile list.

### Repository
- Repository cleanup: removed all compiled binaries (the self-contained `X4ModLauncher/` runtime DLLs and executables) and the duplicate `X4ModLauncher-Source/` upload. The repository now contains only the application source code, the README, and this changelog, with a fresh history. The test project and `.gitignore` are kept local-only. Release executables are published on the [GitHub Releases](https://github.com/Alakeram/x4-mod-launcher/releases) page; the v1.0.0 release and tag are unchanged.
- `VersionPrefix` set to match releases (the v1.0.0 executable reported `0.1.0`).

## [1.0.0] - 2026-09-21

Initial public release.

- Named Mod Profiles (save, load, load & launch, delete) and a built-in `Native` profile.
- Per-extension toggles for installed non-DLC extensions; DLC shown read-only.
- Steam install and X4 profile auto-discovery.
- Timestamped, selectable profile backups with restore.
- Deprecated / Old Extension detection and cleanup.
- Dependency warnings, duplicate-ID detection, atomic writes with post-write verification and rollback.
- Explicit Steam `sync` handling for Native Hotkey API.
- Apply, Apply & Launch, and Launch Game actions.
