using System.Windows;
using EDF6ModLoaderWpf.Helpers;
using EDF6ModLoaderWpf.Services;
using EDF6ModLoaderWpf.ViewModels;

namespace EDF6ModLoaderWpf.Views;

/// <summary>
/// Settings window with tabbed per-game configuration.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly string? _initialGameId;

    public SettingsWindow(SettingsService settingsService, string? initialGameId = null)
    {
        _initialGameId = initialGameId;

        var vm = new SettingsViewModel(settingsService, () =>
        {
            // Called after a successful save
            DialogResult = true;
            Close();
        });

        DataContext = vm;
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            FontHelper.ApplyCurrentFont(this);
            await vm.LoadSettingsCommand.ExecuteAsync(null);
            if (_initialGameId is not null)
                vm.SelectGame(_initialGameId);
        };
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
