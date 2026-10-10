using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using X4ModLauncher.Core;
using Forms = System.Windows.Forms;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfControl = System.Windows.Controls.Control;
using WpfMessageBox = System.Windows.MessageBox;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace X4ModLauncher;

// Flow: choosing a profile previews it in the toggles (nothing is written); Apply / Apply & Launch write the
// toggles to the X4 profile content.xml; Save stores the toggles in the selected profile.
public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const string CreateProfileOption = "Create Profile";
    private const string NativeProfileOption = ModProfileLogic.NativeProfileName;
    internal const string DisabledStatus = "Disabled";
    private readonly X4ProfileService _profileService = new();
    private readonly X4LauncherService _launcherService = new();
    private readonly SettingsStore _settingsStore = new();
    private X4LauncherSettings _settings = new();
    private ProfileSnapshot? _snapshot;
    private bool _suppressProfileSelection;
    private string? _lastProfileSelection;
    private bool _profileDropDownOpened;
    private bool _createProfileDialogShown;
    private bool _createProfileDialogOpen;
    private bool _profileInteractionReady;

    public ObservableCollection<ExtensionRow> Rows { get; } = new();
    public ObservableCollection<string> ProfileNames { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindow()
    {
        InitializeComponent();
        ExtensionList.MaxHeight = Math.Min(420, Math.Max(260, SystemParameters.WorkArea.Height - 510));
        // Set directly rather than through a XAML binding: a binding attaches during the first layout pass and
        // re-applies the selection, which raised a selection change that previewed and saved a profile on startup.
        ProfileSelector.ItemsSource = ProfileNames;
        DataContext = this;
        Loaded += (_, _) =>
        {
            RefreshCatalog(loadSavedSettings: true);
            // Only react to profile selection once startup layout has settled, so nothing is written until the user acts.
            Dispatcher.BeginInvoke(new Action(() => _profileInteractionReady = true), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        };
    }

    private string? FolderKey => ModProfileLogic.FolderKey(_settings.ProfilePath);

    private IReadOnlyList<ExtensionEntry> Catalog => Rows.Select(row => row.Entry).ToList();

    private string? SelectedProfileName => ProfileSelector.SelectedItem is string name && !name.Equals(CreateProfileOption, StringComparison.Ordinal)
        ? name
        : null;

    private ModProfile? SelectedProfile => SelectedProfileName is { } name ? ModProfileLogic.Find(_settings.Profiles, FolderKey, name) : null;

    private Dictionary<string, bool> CurrentToggles() => Rows
        .Where(row => row.CanEdit)
        .ToDictionary(row => row.Id, row => row.DesiredEnabled, StringComparer.OrdinalIgnoreCase);

    private bool HasUnappliedChanges() => ModProfileLogic.ChangesToApply(Catalog, CurrentToggles()).Count > 0;

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshCatalog();

    private void WindowDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed
            && e.OriginalSource is DependencyObject source
            && !IsInteractiveSource(source))
        {
            e.Handled = true;
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // The pointer can be released between PreviewMouseLeftButtonDown and
                // the native drag call. That is a cancelled drag, not a launcher error.
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Windows can reject a native move when the window is closing or losing
                // activation. Keep the launcher alive and let the next drag retry.
            }
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ExtensionToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ExtensionRow row })
            ToggleRow(row);
    }

    private void ExtensionRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source
            && (FindParent<WpfCheckBox>(source) is not null || FindParent<WpfButton>(source) is not null))
            return;
        if (sender is FrameworkElement { DataContext: ExtensionRow row })
            ToggleRow(row);
    }

    private void ToggleRow(ExtensionRow row)
    {
        if (!row.UseEnabled)
            return;
        row.DesiredEnabled = !row.DesiredEnabled;
        UpdateDependencyWarnings();
        UpdateProfileState();
    }

    // ----- Mod profiles -----

    private void ProfileSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_profileInteractionReady || _suppressProfileSelection || ProfileSelector.SelectedItem is not string name)
            return;
        if (name.Equals(CreateProfileOption, StringComparison.Ordinal))
        {
            // The name dialog opens when the drop-down closes; the current toggles become the new profile.
            SetNativeProfileLock(false);
            return;
        }
        PreviewProfile(name);
    }

    private void PreviewProfile(string name)
    {        var profile = ModProfileLogic.Find(_settings.Profiles, FolderKey, name);
        if (profile is null)
        {
            UpdateProfileState();
            return;
        }

        var preview = ModProfileLogic.Preview(profile, Catalog);
        var unlisted = preview.UnlistedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Rows)
        {
            if (preview.DesiredStates.TryGetValue(row.Id, out var desired))
                row.DesiredEnabled = desired;
            row.IsUnlisted = unlisted.Contains(row.Id);
        }
        SortRows();
        var isNative = ReferenceEquals(profile, ModProfileLogic.Native);
        SetNativeProfileLock(isNative);
        _lastProfileSelection = profile.Name;
        _settings.SelectedProfileName = profile.Name;
        _settingsStore.Save(_settings);
        UpdateDependencyWarnings();
        UpdateProfileState();

        var on = preview.DesiredStates.Count(pair => pair.Value);
        var off = preview.DesiredStates.Count - on;
        var status = isNative
            ? "Previewing 'Native': every mod off; DLC and the base game are unchanged. Nothing is written to X4 until you press Apply."
            : $"Previewing '{profile.Name}': {on} on, {off} off. Nothing is written to X4 until you press Apply.";
        if (unlisted.Count > 0)
            status += $"\n{unlisted.Count} installed mod(s) are not in this profile and were left as they are; press Save to include them.";
        StatusText.Text = status;
    }

    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var name = SelectedProfileName;
            if (name is null)
                throw new InvalidOperationException("Choose Create Profile to name a new profile, or select an existing profile first.");
            if (name.Equals(NativeProfileOption, StringComparison.Ordinal))
                throw new InvalidOperationException("Native is built in and cannot be overwritten. Choose Create Profile to save these toggles under a new name.");
            SaveCurrentProfile(name);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Profile save failed: {ex.Message}";
            WpfMessageBox.Show(this, ex.Message, "X4 Mod Launcher — Mod Profiles", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var name = SelectedProfileName;
            if (name?.Equals(NativeProfileOption, StringComparison.Ordinal) == true)
                throw new InvalidOperationException("Native is a built-in profile and cannot be deleted.");
            var profile = SelectedProfile;
            if (name is null || profile is null)
                throw new InvalidOperationException("Choose a saved mod profile first.");
            var answer = WpfMessageBox.Show(this, $"Delete the saved mod profile '{profile.Name}'? This does not change the current X4 profile.", "X4 Mod Launcher — Delete Profile", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
                return;
            _settings.Profiles.Remove(profile);
            _settings.SelectedProfileName = null;
            _settingsStore.Save(_settings);
            RefreshProfileList();
            SelectProfileForCurrentState();
            UpdateDependencyWarnings();
            UpdateProfileState();
            StatusText.Text = $"Deleted mod profile '{profile.Name}'. The X4 profile was not changed.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Profile delete failed: {ex.Message}";
            WpfMessageBox.Show(this, ex.Message, "X4 Mod Launcher — Mod Profiles", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ProfileSelector_DropDownOpened(object sender, EventArgs e)
    {
        if (!_profileInteractionReady)
            return;
        _profileDropDownOpened = true;
        _createProfileDialogShown = false;
    }

    private void ProfileSelector_DropDownClosed(object sender, EventArgs e)
    {
        var shouldOpenCreateDialog = _profileDropDownOpened
            && _profileInteractionReady
            && !_createProfileDialogShown
            && !_suppressProfileSelection
            && ProfileSelector.SelectedItem is string name
            && name.Equals(CreateProfileOption, StringComparison.Ordinal);
        _profileDropDownOpened = false;
        if (shouldOpenCreateDialog)
        {
            _createProfileDialogShown = true;
            Dispatcher.BeginInvoke(
                new Action(OpenCreateProfileDialog),
                System.Windows.Threading.DispatcherPriority.ContextIdle);
        }
    }

    private void OpenCreateProfileDialog()
    {
        if (_createProfileDialogOpen)
            return;
        _createProfileDialogOpen = true;
        try
        {
            var dialog = new ProfileNameDialog(this);
            if (dialog.ShowDialog() != true)
            {
                RestoreProfileSelection();
                return;
            }
            try
            {
                var existing = ModProfileLogic.InScope(_settings.Profiles, FolderKey)
                    .FirstOrDefault(profile => profile.Name.Equals(dialog.ProfileName, StringComparison.OrdinalIgnoreCase));
                if (existing is not null)
                {
                    var overwrite = new ThemedDialog(this,
                        "Create Mod Profile",
                        $"A mod profile named '{existing.Name}' already exists. Overwrite it with the current toggles?",
                        "Overwrite",
                        "Cancel");
                    if (overwrite.ShowDialog() != true)
                    {
                        RestoreProfileSelection();
                        return;
                    }
                }
                SaveCurrentProfile(dialog.ProfileName);
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Profile save failed: {ex.Message}";
                WpfMessageBox.Show(this, ex.Message, "X4 Mod Launcher — Mod Profiles", MessageBoxButton.OK, MessageBoxImage.Warning);
                RestoreProfileSelection();
            }
        }
        finally
        {
            _createProfileDialogOpen = false;
        }
    }

    private void SaveCurrentProfile(string profileName)
    {
        ReadSettingsFromControls();
        var name = profileName.Trim();
        if (string.IsNullOrWhiteSpace(name)
            || name.Equals(CreateProfileOption, StringComparison.OrdinalIgnoreCase)
            || name.Equals(NativeProfileOption, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Enter a different name for this mod profile.");
        if (Rows.Count == 0)
            throw new InvalidOperationException("No extensions are loaded. Check the X4 install and profile paths, then press Refresh.");

        var folder = FolderKey;
        var profile = ModProfileLogic.InScope(_settings.Profiles, folder)
            .FirstOrDefault(existing => existing.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            profile = new ModProfile { Name = name, X4ProfileFolder = folder };
            _settings.Profiles.Add(profile);
        }
        profile.Name = name;
        var toggles = CurrentToggles();
        ModProfileLogic.Capture(profile, toggles);
        foreach (var row in Rows)
            row.IsUnlisted = false;

        _settings.SelectedProfileName = name;
        _lastProfileSelection = name;
        _settingsStore.Save(_settings);
        RefreshProfileList();
        SelectProfileWithoutDialog(name);
        SetNativeProfileLock(false);
        UpdateDependencyWarnings();
        UpdateProfileState();
        var on = toggles.Count(pair => pair.Value);
        StatusText.Text = $"Saved mod profile '{name}' ({on} on, {toggles.Count - on} off). "
            + (HasUnappliedChanges() ? "Press Apply to write it to X4." : "X4 already matches it.");
    }

    private void RestoreProfileSelection()
    {
        SelectProfileWithoutDialog(_lastProfileSelection);
        SetNativeProfileLock(SelectedProfileName?.Equals(NativeProfileOption, StringComparison.Ordinal) == true);
        UpdateProfileState();
    }

    private void SelectProfileWithoutDialog(string? name)
    {
        _suppressProfileSelection = true;
        try
        {
            ProfileSelector.SelectedItem = name is not null && ProfileNames.Contains(name) ? name : null;
        }
        finally
        {
            _suppressProfileSelection = false;
        }
    }

    private void RefreshProfileList()
    {
        _suppressProfileSelection = true;
        try
        {
            ProfileNames.Clear();
            ProfileNames.Add(NativeProfileOption);
            foreach (var name in ModProfileLogic.InScope(_settings.Profiles, FolderKey)
                         .Select(profile => profile.Name)
                         .Where(name => !name.Equals(CreateProfileOption, StringComparison.OrdinalIgnoreCase)
                             && !name.Equals(NativeProfileOption, StringComparison.OrdinalIgnoreCase))
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
                ProfileNames.Add(name);
            ProfileNames.Add(CreateProfileOption);
        }
        finally
        {
            _suppressProfileSelection = false;
        }
    }

    /// <summary>
    /// After the toggles are reloaded from X4, select the profile that matches them (the last used one first).
    /// If none matches, keep the last used profile selected so it shows as modified; never fall back to Native,
    /// because Native locks the toggles and would hide that they are not all off.
    /// </summary>
    private void SelectProfileForCurrentState()
    {
        var catalog = Catalog;
        var toggles = CurrentToggles();
        var folder = FolderKey;
        var preferred = _settings.SelectedProfileName is { } saved ? ModProfileLogic.Find(_settings.Profiles, folder, saved) : null;
        var candidates = new[] { preferred, ModProfileLogic.Native }
            .Concat(ModProfileLogic.InScope(_settings.Profiles, folder).OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase))
            .OfType<ModProfile>();
        var pick = candidates.FirstOrDefault(profile => ModProfileLogic.Matches(profile, catalog, toggles))
            ?? (preferred is not null && !ReferenceEquals(preferred, ModProfileLogic.Native) ? preferred : null);

        SelectProfileWithoutDialog(pick?.Name);
        if (pick is not null)
            _lastProfileSelection = pick.Name;
        SetNativeProfileLock(ReferenceEquals(pick, ModProfileLogic.Native));
        var unlisted = pick is null
            ? new HashSet<string>()
            : ModProfileLogic.Preview(pick, catalog).UnlistedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Rows)
            row.IsUnlisted = unlisted.Contains(row.Id);
    }

    private void UpdateProfileState()
    {
        var parts = new List<string>();
        if (Rows.Count > 0)
        {
            var profile = SelectedProfile;
            if (profile is not null
                && !ReferenceEquals(profile, ModProfileLogic.Native)
                && !ModProfileLogic.Matches(profile, Catalog, CurrentToggles()))
                parts.Add("Modified — press Save to keep");
            else if (ProfileSelector.SelectedItem is null)
                parts.Add("No saved profile matches the current setup");
            if (HasUnappliedChanges())
                parts.Add("Not applied yet");
        }
        ProfileStateText.Text = string.Join(" • ", parts);
    }

    // ----- Apply / launch -----

    private void Apply_Click(object sender, RoutedEventArgs e) => ApplyAndMaybeLaunch(launch: false);

    private void ApplyLaunch_Click(object sender, RoutedEventArgs e) => ApplyAndMaybeLaunch(launch: true);

    private void LaunchGame_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ReadSettingsFromControls();
            ValidateInputs(requireExtensionsRoot: false);
            if (_launcherService.IsGameRunning())
                throw new InvalidOperationException("X4 is already running.");
            if (HasUnappliedChanges())
            {
                var confirm = new ThemedDialog(this,
                    "Launch Game",
                    "The toggles shown have changes that are not applied yet.\n\nLaunch X4 with its current mod setup anyway? Use Apply & Launch to apply them first.",
                    "Launch anyway",
                    "Cancel");
                if (confirm.ShowDialog() != true)
                    return;
            }
            _launcherService.Launch(_settings.X4Root!);
            _settingsStore.Save(_settings);
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"X4 could not be launched: {ex.Message}";
            WpfMessageBox.Show(this, ex.Message, "X4 Mod Launcher — Launch Game", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyAndMaybeLaunch(bool launch)
    {
        try
        {
            var outcome = ApplyDesiredState();
            var message = outcome is null
                ? "Nothing to apply — X4 already matches the toggles shown."
                : $"Applied {outcome.Value.Result.Changes.Count} extension change(s). Backup: {Path.GetFileName(outcome.Value.Result.BackupPath)}"
                    + (outcome.Value.PrunedBackups > 0 ? $". Removed {outcome.Value.PrunedBackups} old backup(s)." : ".");
            if (outcome is not null)
                RefreshCatalog();
            StatusText.Text = message;

            if (launch)
            {
                // Launch is deliberately after Apply has returned and the post-write verification has succeeded.
                _launcherService.Launch(_settings.X4Root!);
                Close();
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"No launch performed. Apply failed closed: {ex.Message}";
            WpfMessageBox.Show(this, ex.Message, "X4 Mod Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Writes the toggles to the X4 profile. Returns null when X4 already matches and nothing was written.</summary>
    private (ApplyResult Result, int PrunedBackups)? ApplyDesiredState()
    {
        ReadSettingsFromControls();
        ValidateInputs();
        if (_launcherService.IsGameRunning())
            throw new InvalidOperationException("X4 is already running. Close it before applying profile changes.");

        var changes = ModProfileLogic.ChangesToApply(Catalog, CurrentToggles());
        if (changes.Count == 0)
            return null;
        var result = _profileService.Apply(_settings.ProfilePath!, _settings.BackupDirectory!, Catalog, changes, requireGameClosed: true, isGameRunning: _launcherService.IsGameRunning, backupLabel: GetBackupLabel());
        var pruned = BackupRetention.Prune(_settings.BackupDirectory!, _settings.BackupRetention, result.BackupPath);
        _settingsStore.Save(_settings);
        return (result, pruned.Count);
    }

    private string GetBackupLabel()
    {
        var profile = SelectedProfile;
        if (profile is not null)
            return ModProfileLogic.Matches(profile, Catalog, CurrentToggles()) ? profile.Name : $"{profile.Name}-modified";
        return Path.GetFileName(Path.GetDirectoryName(_settings.ProfilePath ?? string.Empty)) ?? "X4Profile";
    }

    // ----- Backups and cleanup -----

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ReadSettingsFromControls();
            ValidateInputs(requireExtensionsRoot: false);
            if (_launcherService.IsGameRunning())
                throw new InvalidOperationException("X4 is already running. Close it before restoring a profile.");
            var dialog = new BackupSelectionWindow(this, _settings.BackupDirectory!);
            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.SelectedPath))
                return;
            var backup = dialog.SelectedPath;
            var answer = WpfMessageBox.Show(this, $"Restore this backup?\n\n{Path.GetFileName(backup)}\n\nX4 must be closed. The current profile will be replaced and can only be recovered from another backup.", "X4 Mod Launcher — Select Backup", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
                return;
            _profileService.Restore(_settings.ProfilePath!, backup, requireGameClosed: true, _launcherService.IsGameRunning);
            RefreshCatalog();
            StatusText.Text = $"Restored {Path.GetFileName(backup)}. X4 was not launched.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Restore failed: {ex.Message}";
            WpfMessageBox.Show(this, ex.Message, "X4 Mod Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RemoveDeprecated_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var deprecated = Rows.Where(row => row.IsDeprecated).ToList();
            if (deprecated.Count == 0)
            {
                var dialog = new ThemedDialog(
                    this,
                    "Remove Deprecated Entries",
                    "No Deprecated / Old Extension entries were found in the selected profile.",
                    "OK",
                    null);
                dialog.ShowDialog();
                return;
            }
            var selection = new DeprecatedSelectionWindow(this, deprecated);
            if (selection.ShowDialog() != true)
                return;
            var selectedIds = selection.SelectedIds;
            var selectedRows = deprecated.Where(row => selectedIds.Contains(row.Id, StringComparer.OrdinalIgnoreCase)).ToList();
            var names = string.Join(", ", selectedRows.Take(8).Select(row => row.Name));
            if (selectedRows.Count > 8)
                names += $", and {selectedRows.Count - 8} more";
            var confirmation = new ThemedDialog(this,
                "Remove Deprecated Entries",
                $"Remove {selectedRows.Count} Deprecated / Old Extension profile entries?\n\n{names}\n\nThis removes stale <extension> records from content.xml. There are no folders to delete. A backup will be created first. X4 must be closed.",
                "Remove",
                "Cancel");
            if (confirmation.ShowDialog() != true)
                return;

            ReadSettingsFromControls();
            ValidateInputs(requireExtensionsRoot: false);
            var result = _profileService.RemoveDeprecated(_settings.ProfilePath!, _settings.BackupDirectory!, selectedIds, requireGameClosed: true, isGameRunning: _launcherService.IsGameRunning, backupLabel: GetBackupLabel());
            var pruned = BackupRetention.Prune(_settings.BackupDirectory!, _settings.BackupRetention, result.BackupPath);
            var removed = result.RemovedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var profile in ModProfileLogic.InScope(_settings.Profiles, FolderKey))
            {
                profile.Enabled.RemoveAll(id => removed.Contains(id));
                profile.Disabled?.RemoveAll(id => removed.Contains(id));
            }
            _settingsStore.Save(_settings);
            RefreshCatalog();
            StatusText.Text = $"Removed {result.RemovedIds.Count} Deprecated / Old Extension entries. Backup: {Path.GetFileName(result.BackupPath)}"
                + (pruned.Count > 0 ? $". Removed {pruned.Count} old backup(s)." : ".");
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Deprecated-entry cleanup failed: {ex.Message}";
            WpfMessageBox.Show(this, ex.Message, "X4 Mod Launcher — Remove Deprecated Entries", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ----- Paths and settings -----

    private void AutoFind_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Auto-Find is an explicit rediscovery request, but it must not replace the
            // user's saved profiles, backup directory, or retention setting.
            ReadSettingsFromControls();
            _settings = SettingsStore.WithDiscoveredDefaults(new X4LauncherSettings
            {
                Profiles = _settings.Profiles,
                SelectedProfileName = _settings.SelectedProfileName,
                BackupDirectory = _settings.BackupDirectory,
                BackupRetention = _settings.BackupRetention
            });
            ApplySettingsToControls();
            _settingsStore.Save(_settings);
            RefreshCatalog();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Auto-Find could not complete: {ex.Message}. Enter paths manually.";
            WpfMessageBox.Show(this, ex.Message, "X4 Mod Launcher — Auto-Find", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BrowseProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new WpfOpenFileDialog { Filter = "X4 profile XML|content.xml|XML files|*.xml|All files|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true)
        {
            ProfilePathBox.Text = dialog.FileName;
            RefreshCatalog();
        }
    }

    private void BrowseBackup_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Choose where X4 Mod Launcher should store profile backups.",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(BackupDirectoryBox.Text.Trim())
                ? BackupDirectoryBox.Text.Trim()
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            BackupDirectoryBox.Text = dialog.SelectedPath;
            RefreshCatalog();
        }
    }

    private void RefreshCatalog(bool loadSavedSettings = false)
    {
        try
        {
            if (loadSavedSettings)
                _settings = SettingsStore.WithDiscoveredDefaults(_settingsStore.Load());
            else
            {
                ReadSettingsFromControls();
                _settings = SettingsStore.WithDiscoveredDefaults(_settings);
            }
            ApplySettingsToControls();
            ReadSettingsFromControls();
            RefreshProfileList();
            if (string.IsNullOrWhiteSpace(_settings.ProfilePath) || !File.Exists(_settings.ProfilePath))
                throw new FileNotFoundException("Choose an X4 profile content.xml. The launcher never creates or edits saves.");

            _snapshot = _profileService.Read(_settings.ProfilePath!);
            var entries = ExtensionCatalog.Discover(_settings.ExtensionsRoot ?? string.Empty, _snapshot);
            Rows.Clear();
            foreach (var entry in entries)
                Rows.Add(new ExtensionRow(entry));
            SelectProfileForCurrentState();
            SortRows();
            UpdateDependencyWarnings();
            UpdateProfileState();
            var installed = Rows.Count(x => !x.IsDlc && x.IsAvailable);
            var enabled = Rows.Count(x => !x.IsDlc && x.IsAvailable && x.Entry.CurrentEnabled);
            var deprecated = Rows.Count(x => x.IsDeprecated);
            var broken = Rows.Count(x => x.HasMetadataError);
            StatusText.Text = $"Loaded {enabled} enabled of {installed} installed non-DLC extensions; {deprecated} Deprecated / Old Extension entries"
                + (broken > 0 ? $"; {broken} with unreadable metadata (left unchanged)." : ".")
                + $"\nCurrent state is read from {_settings.ProfilePath}.";
        }
        catch (Exception ex)
        {
            Rows.Clear();
            SelectProfileWithoutDialog(null);
            ProfileStateText.Text = string.Empty;
            StatusText.Text = $"Catalog not ready: {ex.Message}";
        }
    }

    /// <summary>
    /// Orders the list: enabled mods, disabled mods, deprecated entries, then DLC; by name within each group.
    /// Called on load and profile preview only, not on each toggle, so rows do not jump while clicking.
    /// </summary>
    private void SortRows()
    {
        var ordered = Rows
            .OrderBy(row => row.IsDlc ? 3 : row.IsDeprecated ? 2 : row.DesiredEnabled ? 0 : 1)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        for (var index = 0; index < ordered.Count; index++)
        {
            var current = Rows.IndexOf(ordered[index]);
            if (current != index)
                Rows.Move(current, index);
        }
        if (Rows.Count > 0)
            ExtensionList.ScrollIntoView(Rows[0]);
    }

    private void UpdateDependencyWarnings()
    {
        var byId = Rows.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var row in Rows)
        {
            if (row.IsDlc)
            {
                row.StatusLabel = "DLC — unchanged by default";
                continue;
            }
            if (row.IsDeprecated)
            {
                row.StatusLabel = "Deprecated / Old Extension";
                continue;
            }
            // Warnings only matter for mods that will be on; a mod that is off cannot break anything.
            if (!row.DesiredEnabled)
            {
                row.StatusLabel = DisabledStatus;
                continue;
            }
            if (row.HasMetadataError)
            {
                row.StatusLabel = $"Warning: metadata error, left unchanged ({row.Entry.MetadataError})";
                continue;
            }
            var missing = row.Entry.Dependencies
                .Where(dep => !dep.Optional
                    && !ExtensionCatalog.IsDlcIdentifier(dep.Id)
                    && (!byId.TryGetValue(dep.Id, out var dependency) || !dependency.IsAvailable || !dependency.DesiredEnabled))
                .Select(dep => dep.Id)
                .ToList();
            row.StatusLabel = missing.Count > 0
                ? $"Warning: dependency disabled or missing ({string.Join(", ", missing)})"
                : row.IsUnlisted
                    ? "Not in this profile — left as is"
                    : "Ready";
        }
    }

    private void ValidateInputs(bool requireExtensionsRoot = true)
    {
        if (string.IsNullOrWhiteSpace(_settings.ProfilePath) || !File.Exists(_settings.ProfilePath))
            throw new FileNotFoundException("Profile content.xml was not found.");
        if (string.IsNullOrWhiteSpace(_settings.BackupDirectory))
            throw new InvalidOperationException("Choose a backup folder.");
        if (requireExtensionsRoot && string.IsNullOrWhiteSpace(_settings.ExtensionsRoot))
            throw new InvalidOperationException("Choose the X4 extensions folder so availability can be verified.");
        if (string.IsNullOrWhiteSpace(_settings.X4Root))
            throw new InvalidOperationException("Choose the X4 install folder.");
    }

    private void ApplySettingsToControls()
    {
        X4RootBox.Text = _settings.X4Root ?? string.Empty;
        ProfilePathBox.Text = _settings.ProfilePath ?? string.Empty;
        BackupDirectoryBox.Text = _settings.BackupDirectory ?? string.Empty;
        BackupRetentionBox.Text = _settings.BackupRetention.ToString();
    }

    private void ReadSettingsFromControls()
    {
        _settings.X4Root = X4RootBox.Text.Trim();
        _settings.ProfilePath = ProfilePathBox.Text.Trim();
        _settings.ExtensionsRoot = string.IsNullOrWhiteSpace(_settings.X4Root) ? null : Path.Combine(_settings.X4Root, "extensions");
        _settings.BackupDirectory = BackupDirectoryBox.Text.Trim();
        if (int.TryParse(BackupRetentionBox.Text.Trim(), out var keep) && keep >= 0)
            _settings.BackupRetention = keep;
    }

    private void SetNativeProfileLock(bool locked)
    {
        foreach (var row in Rows)
            row.SetNativeProfileLocked(locked);
    }

    private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T match)
                return match;
            child = child is FrameworkContentElement content
                ? content.Parent
                : child is System.Windows.Media.Visual || child is System.Windows.Media.Media3D.Visual3D
                    ? System.Windows.Media.VisualTreeHelper.GetParent(child)
                    : null;
        }
        return null;
    }

    private bool IsInteractiveSource(DependencyObject source)
    {
        var control = FindParent<WpfControl>(source);
        return control is not null && !ReferenceEquals(control, this);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class ExtensionRow : INotifyPropertyChanged
{
    private bool _desiredEnabled;
    private string _statusLabel = string.Empty;
    private bool _nativeProfileLocked;
    private bool _isUnlisted;

    public ExtensionRow(ExtensionEntry entry)
    {
        Entry = entry;
        _desiredEnabled = entry.CurrentEnabled;
        _statusLabel = entry.IsDlc ? "DLC — unchanged by default" : entry.IsAvailable ? "Ready" : "Deprecated / Old Extension";
    }

    public ExtensionEntry Entry { get; }
    public string Id => Entry.Id;
    public string Name => Entry.Name;
    public string VersionLabel => string.IsNullOrWhiteSpace(Entry.Version) ? "Unknown" : Entry.Version;
    public bool IsDlc => Entry.IsDlc;
    public bool IsAvailable => Entry.IsAvailable;
    public bool IsDeprecated => !Entry.IsDlc && !Entry.IsAvailable;
    public bool HasMetadataError => Entry.MetadataError is not null;
    public bool CanEdit => Entry.CanToggle;
    public bool UseEnabled => CanEdit && !_nativeProfileLocked;

    /// <summary>True when the selected profile does not mention this extension, so its state was left as is.</summary>
    public bool IsUnlisted
    {
        get => _isUnlisted;
        set { if (_isUnlisted == value) return; _isUnlisted = value; OnPropertyChanged(); }
    }

    public bool DesiredEnabled
    {
        get => _desiredEnabled;
        set
        {
            if (_desiredEnabled == value)
                return;
            _desiredEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DesiredLabel));
            OnPropertyChanged(nameof(UseLabel));
        }
    }
    public string UseLabel => DesiredEnabled ? "On" : "Off";

    public void SetNativeProfileLocked(bool locked)
    {
        if (_nativeProfileLocked == locked)
            return;
        _nativeProfileLocked = locked;
        OnPropertyChanged(nameof(UseEnabled));
    }
    public string CurrentLabel => IsDlc ? "N/A" : IsDeprecated ? (Entry.CurrentEnabled ? "Enabled (missing)" : "Disabled (missing)") : !Entry.CurrentIsPresent ? "Not in profile" : Entry.CurrentEnabled ? "Enabled" : "Disabled";
    public string DesiredLabel => IsDlc ? "N/A" : IsDeprecated ? "Cleanup only" : HasMetadataError ? "Unchanged" : DesiredEnabled ? "Enabled" : "Disabled";
    public string StatusLabel
    {
        get => _statusLabel;
        set { if (_statusLabel == value) return; _statusLabel = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusBrush)); }
    }
    public System.Windows.Media.Brush StatusBrush => StatusLabel.StartsWith("Warning", StringComparison.OrdinalIgnoreCase)
        || StatusLabel.StartsWith("Deprecated", StringComparison.OrdinalIgnoreCase)
        || StatusLabel.StartsWith("Not in this profile", StringComparison.OrdinalIgnoreCase)
        ? System.Windows.Media.Brushes.Gold
        : IsDlc || StatusLabel == MainWindow.DisabledStatus ? System.Windows.Media.Brushes.LightGray : System.Windows.Media.Brushes.LightGreen;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
