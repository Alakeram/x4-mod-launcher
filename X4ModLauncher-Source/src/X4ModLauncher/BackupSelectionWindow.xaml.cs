using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace X4ModLauncher;

public partial class BackupSelectionWindow : Window
{
    public BackupSelectionWindow(Window owner, string backupDirectory)
    {
        InitializeComponent();
        Owner = owner;
        Backups = new ObservableCollection<BackupChoice>(LoadBackups(backupDirectory));
        DataContext = this;
    }

    public ObservableCollection<BackupChoice> Backups { get; }
    public string? SelectedPath { get; private set; }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (BackupList.SelectedItem is not BackupChoice choice)
        {
            var dialog = new ThemedDialog(this, "Select Backup", "Select a backup file to restore.", "OK", null);
            dialog.ShowDialog();
            return;
        }

        SelectedPath = choice.FullPath;
        DialogResult = true;
    }

    private void BackupList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Open_Click(sender, e);

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void WindowDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;
        if (e.OriginalSource is DependencyObject source && FindParent<System.Windows.Controls.Control>(source) is not null)
            return;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // The pointer was released before the native drag began.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Windows rejected a native move during activation/close.
        }
    }

    private static IEnumerable<BackupChoice> LoadBackups(string directory)
    {
        if (!Directory.Exists(directory))
            return Array.Empty<BackupChoice>();
        try
        {
            return Directory.EnumerateFiles(directory, "*-Backup-*.xml", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Select(path => new BackupChoice(path))
                .ToList();
        }
        catch (IOException)
        {
            return Array.Empty<BackupChoice>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<BackupChoice>();
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
}

public sealed class BackupChoice
{
    public BackupChoice(string fullPath)
    {
        FullPath = fullPath;
        FileName = Path.GetFileName(fullPath);
        ModifiedLabel = File.GetLastWriteTime(fullPath).ToString("yyyy-MM-dd HH:mm:ss");
    }

    public string FullPath { get; }
    public string FileName { get; }
    public string ModifiedLabel { get; }
}
