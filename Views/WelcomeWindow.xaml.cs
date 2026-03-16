using System.Windows;
using EDF6ModLoaderWpf.Services;

namespace EDF6ModLoaderWpf.Views;

/// <summary>
/// First-launch welcome screen — lets the user pick which EDF game to set up first.
/// </summary>
public partial class WelcomeWindow : Window
{
    /// <summary>
    /// The game ID chosen by the user ("EDF41", "EDF5", or "EDF6").
    /// </summary>
    public string SelectedGameId { get; private set; } = "EDF6";

    public WelcomeWindow()
    {
        InitializeComponent();
    }

    private void GetStarted_Click(object sender, RoutedEventArgs e)
    {
        SelectedGameId = Edf41Radio.IsChecked == true ? "EDF41"
            : Edf5Radio.IsChecked == true ? "EDF5"
            : "EDF6";

        // Open the Settings window focused on the chosen game
        var settingsService = App.GetService<SettingsService>();
        var settingsWindow = new SettingsWindow(settingsService, initialGameId: SelectedGameId)
        {
            Owner = this
        };

        if (settingsWindow.ShowDialog() == true)
        {
            DialogResult = true;
            Close();
        }
    }
}
