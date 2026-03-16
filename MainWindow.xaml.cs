using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
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
            _viewModel.NotificationPanel = ToastPanel;
            _viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.IsGroupViewActive))
                    ApplyGrouping();
            };
            DataContext = _viewModel;

            await _viewModel.InitializeAsync();
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
                    await _viewModel.SetGroupAsync(mod, newGroup);
                    ApplyGrouping();
                }
            }
        }

        // ── Grouping ────────────────────────────────────────────────

        private void ApplyGrouping()
        {
            if (_viewModel is null) return;

            var view = CollectionViewSource.GetDefaultView(ModsDataGrid.ItemsSource);
            if (view is null) return;

            view.GroupDescriptions.Clear();
            if (_viewModel.IsGroupViewActive)
            {
                view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ModEntry.Group)));
            }
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
    }
}