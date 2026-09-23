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

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const string CreateProfileOption = "Create Profile";
    private const string NativeProfileOption = "Native";
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
        ExtensionList.MaxHeight = Math.Min(420, Math.Max(260, SystemParameters.WorkArea.Height - 470));
        DataContext = this;
        Loaded += (_, _) =>
        {
            RefreshCatalog(loadSavedSettings: true);
            _profileInteractionReady = true;
        };
    }

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
        if (sender is FrameworkElement { DataContext: ExtensionRow row } && row.UseEnabled)
        {
            row.DesiredEnabled = !row.DesiredEnabled;
            UpdateDependencyWarnings();
        }
    }

    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureProfileStorage();
            var name = ProfileSelector.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(name)
                || name.Equals(CreateProfileOption, StringComparison.Ordinal)
                || name.Equals(NativeProfileOption, StringComparison.Ordinal))
                throw new InvalidOperationException("Choose Create Profile to name a new profile, or select an existing profile first.");
            SaveCurrentProfile(name);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Profile save failed: {ex.Message}";
            WpfMessageBox.Show(this, ex.Message, "X4 Mod Launcher — Mod Profiles", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void LoadProfile_Click(object sender, RoutedEventArgs e) => LoadSelectedProfile(launch: false);

    private void LoadLaunchProfile_Click(object sender, RoutedEventArgs e) => LoadSelectedProfile(launch: true);

    private void LoadSelectedProfile(bool launch)
    {
        try
        {
            EnsureProfileStorage();
            var name = ProfileSelector.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(name) || name.Equals(CreateProfileOption, StringComparison.Ordinal))
                throw new InvalidOperationException("Choose a saved mod profile first.");
            if (name.Equals(NativeProfileOption, StringComparison.Ordinal))
            {
                foreach (var row in Rows.Where(row => row.CanEdit))
                    row.DesiredEnabled = false;
            }
            else
            {
                if (!_settings.ModProfiles.TryGetValue(name, out var enabledIds))
                    throw new InvalidOperationException("Choose a saved mod profile first.");
                var enabled = enabledIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var row in Rows.Where(row => row.CanEdit))
                    row.DesiredEnabled = enabled.Contains(row.Id);
            }
            _settings.SelectedProfileName = name;
            _lastProfileSelection = name;
            _settingsStore.Save(_settings);
            UpdateDependencyWarnings();
            var result = ApplyDesiredState();
            StatusText.Text = name.Equals(NativeProfileOption, StringComparison.Ordinal)
                ? $"Loaded and applied built-in profile 'Native'. All editable mods are disabled; DLC and main game content remain unchanged. Backup: {Path.GetFileName(result.BackupPath)}"
                : $"Loaded and applied mod profile '{name}'. Backup: {Path.GetFileName(result.BackupPath)}";
            RefreshCatalog();
            if (launch)
            {
                _launcherService.Launch(_settings.X4Root!);
                Close();
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Profile load failed: {ex.Message}";
            WpfMessageBox.Show(this, ex.Message, "X4 Mod Launcher — Mod Profiles", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureProfileStorage();
            var name = ProfileSelector.SelectedItem as string;
            if (name?.Equals(NativeProfileOption, StringComparison.Ordinal) == true)
                throw new InvalidOperationException("Native is a built-in profile and cannot be deleted.");
            if (string.IsNullOrWhiteSpace(name) || !_settings.ModProfiles.ContainsKey(name))
                throw new InvalidOperationException("Choose a saved mod profile first.");
            var answer = WpfMessageBox.Show(this, $"Delete the saved mod profile '{name}'? This does not change the current X4 profile.", "X4 Mod Launcher — Delete Profile", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
                return;
            _settings.ModProfiles.Remove(name);
            _settings.SelectedProfileName = null;
            _settingsStore.Save(_settings);
            RefreshProfileList(defaultToNative: true);
            StatusText.Text = $"Deleted mod profile '{name}'. The X4 profile was not changed.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Profile delete failed: {ex.Message}";
            WpfMessageBox.Show(this, ex.Message, "X4 Mod Launcher — Mod Profiles", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ProfileSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_profileInteractionReady || _suppressProfileSelection || ProfileSelector.SelectedItem is not string name)
            return;
        if (name.Equals(CreateProfileOption, StringComparison.Ordinal))
        {
            SetNativeProfileLock(false);
            return;
        }

        SetNativeProfileLock(name.Equals(NativeProfileOption, StringComparison.Ordinal));
        _lastProfileSelection = name;
        _settings.SelectedProfileName = name;
        _settingsStore.Save(_settings);
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
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    SaveCurrentProfile(dialog.ProfileName);
                }
                catch (Exception ex)
                {
                    StatusText.Text = $"Profile save failed: {ex.Message}";
                    WpfMessageBox.Show(this, ex.Message, "X4 Mod Launcher — Mod Profiles", MessageBoxButton.OK, MessageBoxImage.Warning);
                    RestoreProfileSelection();
                }
            }
            else
            {
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
        EnsureProfileStorage();
        var name = profileName.Trim();
        if (string.IsNullOrWhiteSpace(name)
            || name.Equals(CreateProfileOption, StringComparison.OrdinalIgnoreCase)
            || name.Equals(NativeProfileOption, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Enter a different name for this mod profile.");

        var existingKey = _settings.ModProfiles.Keys.FirstOrDefault(key => key.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existingKey is not null && !existingKey.Equals(name, StringComparison.Ordinal))
            _settings.ModProfiles.Remove(existingKey);
        _settings.ModProfiles[name] = Rows
            .Where(row => row.CanEdit && row.DesiredEnabled)
            .Select(row => row.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _settings.SelectedProfileName = name;
        _lastProfileSelection = name;
        _settingsStore.Save(_settings);
        RefreshProfileList();
        SelectProfileWithoutDialog(name);
        StatusText.Text = $"Saved mod profile '{name}' with {_settings.ModProfiles[name].Count} enabled extensions. Press Apply to use it in X4.";
    }

    private void RestoreProfileSelection()
    {
        SelectProfileWithoutDialog(string.IsNullOrWhiteSpace(_lastProfileSelection) ? CreateProfileOption : _lastProfileSelection);
    }

    private void SelectProfileWithoutDialog(string name)
    {
        _suppressProfileSelection = true;
        try
        {
            ProfileSelector.SelectedItem = ProfileNames.Contains(name) ? name : CreateProfileOption;
        }
        finally
        {
            _suppressProfileSelection = false;
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
            EnsureProfileStorage();
            var removed = result.RemovedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var profile in _settings.ModProfiles.Values)
                profile.RemoveAll(id => removed.Contains(id));
            _settingsStore.Save(_settings);
            StatusText.Text = $"Removed {result.RemovedIds.Count} Deprecated / Old Extension entries. Backup: {Path.GetFileName(result.BackupPath)}";
            RefreshCatalog();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Deprecated-entry cleanup failed: {ex.Message}";
            WpfMessageBox.Show(this, ex.Message, "X4 Mod Launcher — Remove Deprecated Entries", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExtensionRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source
            && (FindParent<WpfCheckBox>(source) is not null || FindParent<WpfButton>(source) is not null))
            return;
        if (sender is FrameworkElement { DataContext: ExtensionRow row } && row.UseEnabled)
        {
            row.DesiredEnabled = !row.DesiredEnabled;
            UpdateDependencyWarnings();
        }
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

    private void AutoFind_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Auto-Find is an explicit rediscovery request, but it must not replace the
            // user's saved profiles or custom backup directory.
            ReadSettingsFromControls();
            var preservedProfiles = _settings.ModProfiles;
            var preservedSelectedProfile = _settings.SelectedProfileName;
            var preservedBackupDirectory = _settings.BackupDirectory;
            _settings = SettingsStore.WithDiscoveredDefaults(new X4LauncherSettings
            {
                ModProfiles = preservedProfiles,
                SelectedProfileName = preservedSelectedProfile,
                BackupDirectory = preservedBackupDirectory
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
            var result = ApplyDesiredState();
            StatusText.Text = $"Applied {result.Changes.Count} extension states. Backup: {Path.GetFileName(result.BackupPath)}";
            RefreshCatalog();

            if (launch)
            {
                // Launch is deliberately after Apply has returned and the post-write verification has succeeded.
                _launcherService.Launch(_settings.X4Root!);
                StatusText.Text += " X4 launched. In-game loaded-extension verification is still required.";
                Close();
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"No launch performed. Apply failed closed: {ex.Message}";
            WpfMessageBox.Show(this, ex.Message, "X4 Mod Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private ApplyResult ApplyDesiredState()
    {
        ReadSettingsFromControls();
        ValidateInputs();
        if (_launcherService.IsGameRunning())
            throw new InvalidOperationException("X4 is already running. Close it before applying profile changes.");

        var desired = Rows
            .Where(x => x.CanEdit && !x.IsDlc)
            .Where(x => !x.Entry.CurrentIsPresent
                ? x.DesiredEnabled
                : x.DesiredEnabled != x.Entry.CurrentEnabled
                    || (LegacyExtensionCatalog.IsNativeHotkeyApi(x.Id) && x.Entry.CurrentSync != x.DesiredEnabled))
            .ToDictionary(x => x.Id, x => x.DesiredEnabled, StringComparer.OrdinalIgnoreCase);
        var result = _profileService.Apply(_settings.ProfilePath!, _settings.BackupDirectory!, Rows.Select(x => x.Entry).ToList(), desired, requireGameClosed: true, isGameRunning: _launcherService.IsGameRunning, backupLabel: GetBackupLabel());
        _settingsStore.Save(_settings);
        return result;
    }

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
            StatusText.Text = $"Restored {Path.GetFileName(backup)}. X4 was not launched.";
            RefreshCatalog();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Restore failed: {ex.Message}";
            WpfMessageBox.Show(this, ex.Message, "X4 Mod Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
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
            EnsureProfileStorage();
            ApplySettingsToControls();
            ReadSettingsFromControls();
            RefreshProfileList(defaultToNative: loadSavedSettings);
            if (string.IsNullOrWhiteSpace(_settings.ProfilePath) || !File.Exists(_settings.ProfilePath))
                throw new FileNotFoundException("Choose an X4 profile content.xml. The launcher never creates or edits saves.");

            _snapshot = _profileService.Read(_settings.ProfilePath!);
            var entries = ExtensionCatalog.Discover(_settings.ExtensionsRoot ?? string.Empty, _snapshot);
            Rows.Clear();
            foreach (var entry in entries)
                Rows.Add(new ExtensionRow(entry));
            UpdateDependencyWarnings();
            var installed = Rows.Count(x => !x.IsDlc && x.IsAvailable);
            var enabled = Rows.Count(x => !x.IsDlc && x.IsAvailable && x.Entry.CurrentEnabled);
            var deprecated = Rows.Count(x => x.IsDeprecated);
            StatusText.Text = $"Loaded {enabled} enabled of {installed} installed non-DLC extensions; {deprecated} Deprecated / Old Extension entries.\nCurrent state is read from {_settings.ProfilePath}.";
            SetNativeProfileLock(ProfileSelector.SelectedItem is string selectedProfile
                && selectedProfile.Equals(NativeProfileOption, StringComparison.Ordinal));
        }
        catch (Exception ex)
        {
            Rows.Clear();
            StatusText.Text = $"Catalog not ready: {ex.Message}";
        }
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
            var missing = row.Entry.Dependencies
                .Where(dep => !dep.Optional
                    && !ExtensionCatalog.IsDlcIdentifier(dep.Id)
                    && (!byId.TryGetValue(dep.Id, out var dependency) || !dependency.IsAvailable || !dependency.DesiredEnabled))
                .Select(dep => dep.Id)
                .ToList();
            row.StatusLabel = missing.Count == 0 ? "Ready" : $"Warning: dependency disabled or missing ({string.Join(", ", missing)})";
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
    }

    private void ReadSettingsFromControls()
    {
        _settings.X4Root = X4RootBox.Text.Trim();
        _settings.ProfilePath = ProfilePathBox.Text.Trim();
        _settings.ExtensionsRoot = string.IsNullOrWhiteSpace(_settings.X4Root) ? null : Path.Combine(_settings.X4Root, "extensions");
        _settings.BackupDirectory = BackupDirectoryBox.Text.Trim();
    }

    private string GetBackupLabel()
    {
        if (ProfileSelector.SelectedItem is string selected
            && !selected.Equals(CreateProfileOption, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(selected))
            return selected;
        return Path.GetFileName(Path.GetDirectoryName(_settings.ProfilePath ?? string.Empty)) ?? "X4Profile";
    }

    private void EnsureProfileStorage()
    {
        _settings.ModProfiles ??= new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    }

    private void SetNativeProfileLock(bool locked)
    {
        foreach (var row in Rows)
            row.SetNativeProfileLocked(locked);
    }

    private void RefreshProfileList(bool defaultToNative = false)
    {
        EnsureProfileStorage();
        // Re-read persisted profiles as a defensive migration path for launchers that
        // were opened with an older settings schema or a stale in-memory settings object.
        // This does not overwrite the current selection; it only restores named entries.
        var persisted = _settingsStore.Load();
        foreach (var profile in persisted.ModProfiles)
            _settings.ModProfiles[profile.Key] = profile.Value;
        _suppressProfileSelection = true;
        ProfileNames.Clear();
        ProfileNames.Add(NativeProfileOption);
        foreach (var name in _settings.ModProfiles.Keys
                     .Where(name => !name.Equals(CreateProfileOption, StringComparison.OrdinalIgnoreCase)
                         && !name.Equals(NativeProfileOption, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            ProfileNames.Add(name);
        ProfileNames.Add(CreateProfileOption);
        var selected = defaultToNative
            ? NativeProfileOption
            : _settings.SelectedProfileName is not null && ProfileNames.Contains(_settings.SelectedProfileName)
                ? _settings.SelectedProfileName
                : CreateProfileOption;
        ProfileSelector.SelectedItem = selected;
        if (!selected.Equals(CreateProfileOption, StringComparison.Ordinal))
            _lastProfileSelection = selected;
        _suppressProfileSelection = false;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class ExtensionRow : INotifyPropertyChanged
{
    private bool _desiredEnabled;
    private string _statusLabel = string.Empty;
    private bool _nativeProfileLocked;

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
    public bool CanEdit => Entry.IsAvailable && !Entry.IsDlc;
    public bool UseEnabled => CanEdit && !_nativeProfileLocked;
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
    public string DesiredLabel => IsDlc ? "N/A" : IsDeprecated ? "Cleanup only" : DesiredEnabled ? "Enabled" : "Disabled";
    public string StatusLabel
    {
        get => _statusLabel;
        set { if (_statusLabel == value) return; _statusLabel = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusBrush)); }
    }
    public System.Windows.Media.Brush StatusBrush => StatusLabel.StartsWith("Warning", StringComparison.OrdinalIgnoreCase) || StatusLabel.StartsWith("Deprecated", StringComparison.OrdinalIgnoreCase)
        ? System.Windows.Media.Brushes.Gold
        : IsDlc ? System.Windows.Media.Brushes.LightGray : System.Windows.Media.Brushes.LightGreen;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
