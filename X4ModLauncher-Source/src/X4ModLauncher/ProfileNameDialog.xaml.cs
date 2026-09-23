using System.Windows;
using System.Windows.Input;

namespace X4ModLauncher;

public partial class ProfileNameDialog : Window
{
    public ProfileNameDialog(Window owner)
    {
        InitializeComponent();
        Owner = owner;
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    public string ProfileName => NameBox.Text.Trim();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ProfileName))
        {
            var dialog = new ThemedDialog(this, "Create Mod Profile", "Enter a name for the mod profile.", "OK", null);
            dialog.ShowDialog();
            NameBox.Focus();
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
