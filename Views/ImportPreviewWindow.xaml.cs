using System.Windows;
using EDF6ModLoaderWpf.Helpers;
using EDF6ModLoaderWpf.Models;
using EDF6ModLoaderWpf.ViewModels;

namespace EDF6ModLoaderWpf.Views;

/// <summary>
/// Preview dialog shown before importing a mod.
/// </summary>
public partial class ImportPreviewWindow : Window
{
    private readonly ImportPreviewViewModel _viewModel;

    public ImportPreviewWindow(ModImportPreview preview)
    {
        _viewModel = new ImportPreviewViewModel(preview);
        DataContext = _viewModel;
        InitializeComponent();

        Loaded += (_, _) => FontHelper.ApplyCurrentFont(this);
    }

    public ModImportMode SelectedImportMode => _viewModel.SelectedImportMode;

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}