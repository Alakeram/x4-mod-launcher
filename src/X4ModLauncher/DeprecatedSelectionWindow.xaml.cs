using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using X4ModLauncher.Core;

namespace X4ModLauncher;

public partial class DeprecatedSelectionWindow : Window
{
    public DeprecatedSelectionWindow(Window owner, IEnumerable<ExtensionRow> rows)
    {
        InitializeComponent();
        Owner = owner;
        Choices = new ObservableCollection<DeprecatedChoice>(rows.Select(row => new DeprecatedChoice(row.Id, row.Name)));
        DataContext = this;
    }

    public ObservableCollection<DeprecatedChoice> Choices { get; }

    public IReadOnlyList<string> SelectedIds => Choices
        .Where(choice => choice.IsSelected)
        .Select(choice => choice.Id)
        .ToList();

    private void RemoveAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var choice in Choices)
            choice.IsSelected = true;
        DialogResult = true;
    }

    private void RemoveSelected_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedIds.Count == 0)
        {
            var dialog = new ThemedDialog(this,
                "Selection Required",
                "Select at least one Deprecated / Old Extension entry, or choose Remove ALL.",
                "OK",
                null);
            dialog.ShowDialog();
            return;
        }

        DialogResult = true;
    }

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

public sealed class DeprecatedChoice : INotifyPropertyChanged
{
    private bool _isSelected;

    public DeprecatedChoice(string id, string name)
    {
        Id = id;
        Name = name;
    }

    public string Id { get; }
    public string Name { get; }
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
