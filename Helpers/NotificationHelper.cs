using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace EDF6ModLoaderWpf.Helpers;

/// <summary>
/// Provides lightweight in-app toast notifications shown at the bottom of the main window.
/// </summary>
public static class NotificationHelper
{
    /// <summary>
    /// Shows a short toast-style notification inside the given panel.
    /// The message fades away after a few seconds.
    /// </summary>
    public static void ShowToast(Panel container, string message, bool isError = false)
    {
        var border = new Border
        {
            Background = isError
                ? new SolidColorBrush(Color.FromRgb(200, 50, 50))
                : new SolidColorBrush(Color.FromRgb(50, 150, 80)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16, 10, 16, 10),
            Margin = new Thickness(0, 4, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = new TextBlock
            {
                Text = message,
                Foreground = Brushes.White,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap
            }
        };

        container.Children.Add(border);

        // Auto-remove after 4 seconds
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (_, _) =>
        {
            container.Children.Remove(border);
            timer.Stop();
        };
        timer.Start();
    }

    /// <summary>
    /// Shows a confirmation dialog with OK / Cancel and returns the user's choice.
    /// </summary>
    public static bool Confirm(string title, string message)
    {
        var result = MessageBox.Show(
            message,
            title,
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        return result == MessageBoxResult.OK;
    }

    /// <summary>
    /// Shows an error dialog.
    /// </summary>
    public static void ShowError(string title, string message)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
