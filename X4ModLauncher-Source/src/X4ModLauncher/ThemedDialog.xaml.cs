using System.Windows;
using System.Windows.Input;

namespace X4ModLauncher;

public partial class ThemedDialog : Window
{
    public ThemedDialog(Window owner, string title, string message, string confirmText, string? cancelText = "Cancel")
    {
        InitializeComponent();
        Owner = owner;
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        if (string.IsNullOrWhiteSpace(cancelText))
        {
            CancelButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            CancelButton.Content = cancelText;
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;

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
