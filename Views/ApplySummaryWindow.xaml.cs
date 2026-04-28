using System.Windows;
using EDF6ModLoaderWpf.Helpers;
using EDF6ModLoaderWpf.Models;
using EDF6ModLoaderWpf.ViewModels;

namespace EDF6ModLoaderWpf.Views;

/// <summary>
/// Dialog shown before applying mods to summarize file changes and risks.
/// </summary>
public partial class ApplySummaryWindow : Window
{
    public ApplySummaryWindow(ApplySummaryPreview preview)
    {
        DataContext = new ApplySummaryViewModel(preview);
        InitializeComponent();

        Loaded += (_, _) => FontHelper.ApplyCurrentFont(this);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}