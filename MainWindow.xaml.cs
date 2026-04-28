using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using EDF6ModLoaderWpf.Helpers;
using EDF6ModLoaderWpf.Models;
using EDF6ModLoaderWpf.Services;
using EDF6ModLoaderWpf.ViewModels;
using EDF6ModLoaderWpf.Views;

namespace EDF6ModLoaderWpf
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml.
    /// Handles UI events and drag-and-drop reordering; all business logic lives in MainViewModel.
    /// </summary>
    public partial class MainWindow : Window
    {
        private const string RecentImportPrimaryActionTag = "RecentImportPrimaryAction";

        private MainViewModel _viewModel = null!;

        // Drag-and-drop state
        private Point _dragStartPoint;
        private bool _isDragging;
        private ModEntry? _draggedMod;

        public MainWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Initialises the ViewModel and wires up the notification panel.
        /// Called from App.xaml.cs after DI resolves the VM.
        /// </summary>
        public async Task InitializeAsync(MainViewModel viewModel)
        {
            _viewModel = viewModel;
            _viewModel.ShowToastAction = (msg, isError) => NotificationHelper.ShowToast(ToastPanel, msg, isError);
            _viewModel.ShowSettingsDialog = async () =>
            {
                var win = new SettingsWindow(App.GetService<SettingsService>()) { Owner = this };
                return win.ShowDialog() == true;
            };
            _viewModel.ShowImportPreviewDialog = preview =>
            {
                var win = new ImportPreviewWindow(preview) { Owner = this };
                return win.ShowDialog() == true ? win.SelectedImportMode : null;
            };
            _viewModel.ShowApplySummaryDialog = preview =>
            {
                var win = new ApplySummaryWindow(preview) { Owner = this };
                return win.ShowDialog() == true;
            };
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.Mods.CollectionChanged += Mods_CollectionChanged;
            DataContext = _viewModel;

            await _viewModel.InitializeAsync();
            ApplyViewState();
            AutoFitColumns();

            // Show first-time welcome screen if no game has been configured yet
            if (_viewModel.NeedsFirstTimeSetup)
            {
                var welcomeWindow = new WelcomeWindow() { Owner = this };
                if (welcomeWindow.ShowDialog() == true)
                {
                    await _viewModel.ReloadSettingsAsync();
                }
                else
                {
                    Application.Current.Shutdown();
                }
            }
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(MainViewModel.IsGroupViewActive) ||
                args.PropertyName == nameof(MainViewModel.LoadOrderViewActive) ||
                args.PropertyName == nameof(MainViewModel.SearchText) ||
                args.PropertyName == nameof(MainViewModel.ShowActiveOnly) ||
                args.PropertyName == nameof(MainViewModel.ShowConflictsOnly) ||
                args.PropertyName == nameof(MainViewModel.ShowRiskyOnly) ||
                (args.PropertyName == nameof(MainViewModel.IsRefreshing) && _viewModel?.IsRefreshing == false))
            {
                ApplyViewState();
            }
        }

        private void Mods_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems is not null)
            {
                foreach (ModEntry mod in e.OldItems)
                    mod.PropertyChanged -= ModEntry_PropertyChanged;
            }

            if (e.NewItems is not null)
            {
                foreach (ModEntry mod in e.NewItems)
                    mod.PropertyChanged += ModEntry_PropertyChanged;
            }

            ApplyViewState();
        }

        private void ModEntry_PropertyChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (_viewModel?.IsRefreshing == true)
                return;

            if (args.PropertyName == nameof(ModEntry.IsActive) ||
                args.PropertyName == nameof(ModEntry.HasConflict) ||
                args.PropertyName == nameof(ModEntry.IsHighRisk) ||
                args.PropertyName == nameof(ModEntry.Group) ||
                args.PropertyName == nameof(ModEntry.LoadOrder) ||
                args.PropertyName == nameof(ModEntry.WarningSummary) ||
                args.PropertyName == nameof(ModEntry.WarningDetails))
            {
                ApplyViewState();
            }
        }

        private void ToolbarMenuButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { ContextMenu: { } menu } button)
                return;

            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void Window_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(ModEntry)))
            {
                HideDropOverlay();
                return;
            }

            var supportedPaths = GetDroppedImportPaths(e.Data);
            if (supportedPaths.Count == 0)
            {
                HideDropOverlay();

                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    e.Effects = DragDropEffects.None;
                    e.Handled = true;
                }

                return;
            }

            ShowDropOverlay(supportedPaths);
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }

        private void Window_PreviewDragLeave(object sender, DragEventArgs e)
        {
            HideDropOverlay();
        }

        private async void Window_PreviewDrop(object sender, DragEventArgs e)
        {
            HideDropOverlay();

            if (_viewModel is null || e.Data.GetDataPresent(typeof(ModEntry)))
                return;

            var supportedPaths = GetDroppedImportPaths(e.Data);
            if (supportedPaths.Count == 0)
                return;

            e.Handled = true;
            await _viewModel.ImportDroppedPathsAsync(supportedPaths);
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_viewModel is not null)
            {
                _viewModel.Mods.CollectionChanged -= Mods_CollectionChanged;
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                foreach (var mod in _viewModel.Mods)
                    mod.PropertyChanged -= ModEntry_PropertyChanged;
            }
            base.OnClosed(e);
        }

        /// <summary>
        /// Handles checkbox checked / unchecked events in the DataGrid to trigger the toggle command.
        /// </summary>
        private void ModCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel?.IsRefreshing == true) return;

            if (sender is CheckBox { DataContext: ModEntry mod } && _viewModel?.ToggleModCommand is not null)
            {
                _viewModel.ToggleModCommand.Execute(mod);
            }
        }

        private void RecentImportCardPrimaryAction_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not Button currentButton)
                return;

            var requestedDirection = e.Key switch
            {
                Key.Left or Key.Up => -1,
                Key.Right or Key.Down => 1,
                Key.Home => int.MinValue,
                Key.End => int.MaxValue,
                _ => 0
            };

            if (requestedDirection == 0)
                return;

            var actionButtons = GetRecentImportPrimaryActionButtons();
            var currentIndex = actionButtons.IndexOf(currentButton);
            if (currentIndex < 0)
                return;

            int targetIndex = requestedDirection switch
            {
                int.MinValue => 0,
                int.MaxValue => actionButtons.Count - 1,
                _ => Math.Clamp(currentIndex + requestedDirection, 0, actionButtons.Count - 1)
            };

            if (targetIndex == currentIndex)
                return;

            if (actionButtons[targetIndex].Focus())
                e.Handled = true;
        }

        // ── Drag-and-Drop Reorder ────────────────────────────────────

        private void DataGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            _isDragging = false;

            // Only start drag from a DataGridRow, skip if clicking on buttons/checkboxes
            if (e.OriginalSource is DependencyObject source &&
                (FindParent<Button>(source) is not null || FindParent<CheckBox>(source) is not null))
            {
                _draggedMod = null;
                return;
            }

            if (GetDataGridRowFromPoint(e) is { DataContext: ModEntry mod })
                _draggedMod = mod;
            else
                _draggedMod = null;
        }

        private void DataGrid_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_draggedMod is null || !_draggedMod.IsActive || e.LeftButton != MouseButtonState.Pressed)
                return;

            var currentPos = e.GetPosition(null);
            var diff = _dragStartPoint - currentPos;

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                if (!_isDragging)
                {
                    _isDragging = true;
                    var data = new DataObject(typeof(ModEntry), _draggedMod);
                    DragDrop.DoDragDrop(ModsDataGrid, data, DragDropEffects.Move);
                    _isDragging = false;
                    _draggedMod = null;
                }
            }
        }

        private void DataGrid_DragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(ModEntry)))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void DataGrid_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(ModEntry)))
                return;

            var droppedMod = e.Data.GetData(typeof(ModEntry)) as ModEntry;
            if (droppedMod is null || !droppedMod.IsActive)
                return;

            // Determine the target row
            var targetRow = GetDataGridRowFromDragEvent(e);
            if (targetRow?.DataContext is not ModEntry targetMod || targetMod == droppedMod)
                return;

            // Compute the new load order position based on the target
            int newLoadOrder = targetMod.IsActive ? targetMod.LoadOrder : droppedMod.LoadOrder;
            _viewModel.OnDragDropReorder(droppedMod, newLoadOrder);
        }

        // ── Load Order Editing ─────────────────────────────────────

        private void LoadOrderTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Only allow digits
            e.Handled = !int.TryParse(e.Text, out _);
        }

        private void LoadOrderTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox { DataContext: ModEntry mod } textBox && _viewModel is not null)
            {
                if (int.TryParse(textBox.Text, out int newOrder) && newOrder != mod.LoadOrder)
                {
                    _viewModel.SetLoadOrder(mod, newOrder);
                }
            }
        }

        // ── Group Name Editing ───────────────────────────────────────

        private async void GroupTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox { DataContext: ModEntry mod } textBox && _viewModel is not null)
            {
                var newGroup = textBox.Text?.Trim() ?? string.Empty;
                if (newGroup != mod.Group)
                {
                    try
                    {
                        await _viewModel.SetGroupAsync(mod, newGroup);
                        ApplyViewState();
                    }
                    catch (Exception ex)
                    {
                        await SettingsService.LogErrorAsync(ex);
                        NotificationHelper.ShowError("Group Error", ex.Message);
                    }
                }
            }
        }

        // ── Grouping ────────────────────────────────────────────────

        private void ApplyViewState()
        {
            if (_viewModel is null) return;

            var view = CollectionViewSource.GetDefaultView(ModsDataGrid.ItemsSource);
            if (view is null) return;

            using (view.DeferRefresh())
            {
                view.Filter = HasActiveFilters() ? FilterModEntry : null;
                view.GroupDescriptions.Clear();
                view.SortDescriptions.Clear();

                if (_viewModel.LoadOrderViewActive)
                    view.SortDescriptions.Add(new SortDescription(nameof(ModEntry.LoadOrder), ListSortDirection.Ascending));

                if (_viewModel.IsGroupViewActive)
                {
                    view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ModEntry.Group)));
                }
            }
        }

        private bool HasActiveFilters()
        {
            return _viewModel is not null &&
                   (!string.IsNullOrWhiteSpace(_viewModel.SearchText) ||
                    _viewModel.LoadOrderViewActive ||
                    _viewModel.ShowActiveOnly ||
                    _viewModel.ShowConflictsOnly ||
                    _viewModel.ShowRiskyOnly);
        }

        private bool FilterModEntry(object item)
        {
            if (_viewModel is null || item is not ModEntry mod)
                return false;

            if (_viewModel.ShowActiveOnly && !mod.IsActive)
                return false;

            if (_viewModel.LoadOrderViewActive && !mod.IsActive)
                return false;

            if (_viewModel.ShowConflictsOnly && !mod.HasConflict)
                return false;

            if (_viewModel.ShowRiskyOnly && !mod.IsHighRisk)
                return false;

            if (string.IsNullOrWhiteSpace(_viewModel.SearchText))
                return true;

            return ContainsIgnoreCase(mod.ModName, _viewModel.SearchText) ||
                   ContainsIgnoreCase(mod.Description, _viewModel.SearchText) ||
                   ContainsIgnoreCase(mod.Group, _viewModel.SearchText) ||
                   ContainsIgnoreCase(mod.Subfolders, _viewModel.SearchText) ||
                   ContainsIgnoreCase(mod.WarningSummary, _viewModel.SearchText) ||
                   ContainsIgnoreCase(mod.WarningDetails, _viewModel.SearchText);
        }

        // ── Column Auto-Fit ─────────────────────────────────────────

        /// <summary>
        /// Forces all DataGrid columns to recalculate their width after data is loaded.
        /// Star-sized columns are temporarily set to Auto, then restored to star.
        /// </summary>
        private void AutoFitColumns()
        {
            // Remember which columns are star-sized
            var starColumns = new List<(DataGridColumn Column, DataGridLength Original)>();

            foreach (var col in ModsDataGrid.Columns)
            {
                if (col.Width.IsStar)
                {
                    starColumns.Add((col, col.Width));
                    col.Width = DataGridLength.Auto;
                }
                else if (!col.Width.IsAbsolute)
                {
                    // Force Auto columns to recalculate
                    var current = col.Width;
                    col.Width = 0;
                    col.Width = current;
                }
            }

            // Let the layout pass measure with Auto, then restore star columns
            ModsDataGrid.UpdateLayout();

            foreach (var (col, original) in starColumns)
            {
                col.MinWidth = col.ActualWidth;
                col.Width = original;
            }
        }

        // ── Visual tree helpers ──────────────────────────────────────

        private DataGridRow? GetDataGridRowFromPoint(MouseButtonEventArgs e)
        {
            var hit = VisualTreeHelper.HitTest(ModsDataGrid, e.GetPosition(ModsDataGrid));
            return hit?.VisualHit is DependencyObject dep ? FindParent<DataGridRow>(dep) : null;
        }

        private DataGridRow? GetDataGridRowFromDragEvent(DragEventArgs e)
        {
            var pos = e.GetPosition(ModsDataGrid);
            var hit = VisualTreeHelper.HitTest(ModsDataGrid, pos);
            return hit?.VisualHit is DependencyObject dep ? FindParent<DataGridRow>(dep) : null;
        }

        private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var current = child;
            while (current is not null)
            {
                if (current is T found)
                    return found;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private static bool ContainsIgnoreCase(string value, string searchText)
        {
            return value.Contains(searchText, StringComparison.OrdinalIgnoreCase);
        }

        private List<Button> GetRecentImportPrimaryActionButtons()
        {
            var buttons = new List<Button>();
            CollectRecentImportPrimaryActionButtons(this, buttons);
            return buttons;
        }

        private static void CollectRecentImportPrimaryActionButtons(DependencyObject parent, List<Button> buttons)
        {
            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int index = 0; index < childCount; index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                if (child is Button button &&
                    button.IsVisible &&
                    button.IsEnabled &&
                    button.Tag is string tag &&
                    string.Equals(tag, RecentImportPrimaryActionTag, StringComparison.Ordinal))
                {
                    buttons.Add(button);
                }

                CollectRecentImportPrimaryActionButtons(child, buttons);
            }
        }

        private void ShowDropOverlay(IReadOnlyList<string> supportedPaths)
        {
            DropOverlayTitle.Text = supportedPaths.Count == 1
                ? "Release to import this mod"
                : $"Release to import {supportedPaths.Count} items";

            DropOverlayText.Text = supportedPaths.Count == 1
                ? $"{GetDroppedItemDisplayName(supportedPaths[0])} will be previewed before import."
                : "Each dropped .zip archive or mod folder will be previewed before import.";

            DropOverlay.Visibility = Visibility.Visible;
        }

        private void HideDropOverlay()
        {
            DropOverlay.Visibility = Visibility.Collapsed;
        }

        private static IReadOnlyList<string> GetDroppedImportPaths(IDataObject data)
        {
            if (!data.GetDataPresent(DataFormats.FileDrop) || data.GetData(DataFormats.FileDrop) is not string[] droppedPaths)
                return [];

            var supportedPaths = new List<string>();
            foreach (var path in droppedPaths)
            {
                if (Directory.Exists(path) ||
                    (File.Exists(path) && string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase)))
                {
                    supportedPaths.Add(path);
                }
            }

            return supportedPaths;
        }

        private static string GetDroppedItemDisplayName(string path)
        {
            var trimmedPath = Path.TrimEndingDirectorySeparator(path);
            return Path.GetFileName(trimmedPath);
        }
    }
}
