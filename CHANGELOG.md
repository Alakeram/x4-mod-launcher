# Changelog

All notable changes to X4 Mod Launcher are documented here. Versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

- Repository cleanup: removed all compiled binaries (the self-contained `X4ModLauncher/` runtime DLLs and executables) and the duplicate `X4ModLauncher-Source/` upload. The repository now contains only source code, tests, the README, and this changelog, with a fresh history. Release executables are published on the [GitHub Releases](https://github.com/Alakeram/x4-mod-launcher/releases) page; the v1.0.0 release and tag are unchanged.
- `VersionPrefix` set to `1.0.0` to match the published release (the v1.0.0 executable reports `0.1.0`).

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
