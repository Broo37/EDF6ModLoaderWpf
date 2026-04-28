using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using EDF6ModLoaderWpf.Helpers;
using EDF6ModLoaderWpf.Models;
using EDF6ModLoaderWpf.Services;
using Microsoft.Win32;

namespace EDF6ModLoaderWpf.ViewModels;

/// <summary>
/// ViewModel for the main window — mod list, load order management, conflict detection,
/// and multi-game switching.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private const int MaxRecentImportsPerGame = 5;

    private readonly ModService _modService;
    private readonly BackupService _backupService;
    private readonly FileService _fileService;
    private readonly SettingsService _settingsService;
    private readonly LoadOrderService _loadOrderService;
    private readonly GameSwitchService _gameSwitchService;
    private ModPreset? _activePresetSnapshot;

    private AppSettings _settings = new();

    /// <summary>Collection of mods displayed in the DataGrid.</summary>
    public ObservableCollection<ModEntry> Mods { get; } = [];

    /// <summary>Conflict report entries for the info panel.</summary>
    public ObservableCollection<ConflictInfo> ConflictReport { get; } = [];

    /// <summary>Available game profiles for the game selector.</summary>
    public ObservableCollection<GameProfile> AvailableGames { get; } = [];

    public ObservableCollection<string> AvailablePresets { get; } = [];

    public ObservableCollection<RecentImportEntry> PinnedRecentImports { get; } = [];

    public ObservableCollection<RecentImportEntry> RecentImports { get; } = [];

    /// <summary>Currently active game profile.</summary>
    [ObservableProperty]
    private GameProfile? _activeGame;

    /// <summary>Dynamic window title showing the active game.</summary>
    [ObservableProperty]
    private string _windowTitle = "🛡️ EDF Mod Manager";

    [ObservableProperty]
    private string _gameRootDirectory = string.Empty;

    [ObservableProperty]
    private string _modsLibraryDirectory = string.Empty;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>True while RefreshAsync is repopulating the DataGrid (suppresses checkbox events).</summary>
    [ObservableProperty]
    private bool _isRefreshing;

    /// <summary>Whether the conflict report panel is visible.</summary>
    [ObservableProperty]
    private bool _isConflictPanelVisible;

    /// <summary>Whether undo is available.</summary>
    [ObservableProperty]
    private bool _canUndo;

    /// <summary>True when in-memory mod state differs from the deployed files.</summary>
    [ObservableProperty]
    private bool _hasPendingChanges;

    [ObservableProperty]
    private bool _hasApplyBackupSnapshot;

    [ObservableProperty]
    private int _warningModCount;

    [ObservableProperty]
    private int _highRiskModCount;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _showActiveOnly;

    [ObservableProperty]
    private bool _showConflictsOnly;

    [ObservableProperty]
    private bool _showRiskyOnly;

    [ObservableProperty]
    private string _selectedPresetName = string.Empty;

    [ObservableProperty]
    private string _presetNameInput = string.Empty;

    [ObservableProperty]
    private string _activePresetName = string.Empty;

    [ObservableProperty]
    private bool _isActivePresetDirty;

    [ObservableProperty]
    private bool _hasRecentImports;

    [ObservableProperty]
    private bool _hasPinnedRecentImports;

    [ObservableProperty]
    private bool _hasUnpinnedRecentImports;

    [ObservableProperty]
    private int _pinnedRecentImportCount;

    [ObservableProperty]
    private int _recentImportCount;

    [ObservableProperty]
    private string _pinnedRecentImportsHeader = "Pinned";

    [ObservableProperty]
    private string _recentImportsHeader = "Recent";

    [ObservableProperty]
    private RecentImportEntry? _latestRecentImport;

    [ObservableProperty]
    private bool _isBatchImportActive;

    [ObservableProperty]
    private string _batchImportProgressLabel = string.Empty;

    [ObservableProperty]
    private string _batchImportCurrentItemLabel = string.Empty;

    /// <summary>Currently selected mod in the DataGrid.</summary>
    [ObservableProperty]
    private ModEntry? _selectedMod;

    /// <summary>Current view mode: true = load order view (active only), false = all mods.</summary>
    [ObservableProperty]
    private bool _loadOrderViewActive;

    /// <summary>Whether grouping by Group column is active.</summary>
    [ObservableProperty]
    private bool _isGroupViewActive;

    /// <summary>Whether the first-time setup (Welcome screen) is needed.</summary>
    public bool NeedsFirstTimeSetup { get; private set; }

    /// <summary>Wired up by MainWindow to forward toast calls to the UI layer.</summary>
    public Action<string, bool>? ShowToastAction { get; set; }

    /// <summary>Wired up by MainWindow to open the settings dialog; returns true when settings were saved.</summary>
    public Func<Task<bool>>? ShowSettingsDialog { get; set; }

    /// <summary>Wired up by MainWindow to show the richer import preview dialog.</summary>
    public Func<ModImportPreview, ModImportMode?>? ShowImportPreviewDialog { get; set; }

    /// <summary>Wired up by MainWindow to confirm the pre-apply change summary.</summary>
    public Func<ApplySummaryPreview, bool>? ShowApplySummaryDialog { get; set; }

    /// <summary>AppData directory for the active game's registry.</summary>
    private string ActiveRegistryDir =>
        ActiveGame is not null ? SettingsService.GetGameAppDataFolder(ActiveGame.GameId) : string.Empty;

    private string ActiveExecutablePath =>
        ActiveGame is null ? string.Empty : Path.Combine(ActiveGame.GameRootPath, ActiveGame.ExecutableName);

    public bool CanLaunchGame =>
        ActiveGame is not null && ActiveGame.IsConfigured && File.Exists(ActiveExecutablePath);

    public bool CanApplyAndLaunch => HasPendingChanges && CanLaunchGame;

    public bool CanRestoreLastApply =>
        ActiveGame is not null && ActiveGame.IsConfigured && HasApplyBackupSnapshot;

    public bool CanManagePresets => ActiveGame is not null && ActiveGame.IsConfigured;

    public bool CanSavePreset =>
        CanManagePresets && (!string.IsNullOrWhiteSpace(PresetNameInput) || !string.IsNullOrWhiteSpace(SelectedPresetName));

    public bool CanLoadPreset => CanManagePresets && !string.IsNullOrWhiteSpace(SelectedPresetName);

    public bool CanDeletePreset => CanManagePresets && !string.IsNullOrWhiteSpace(SelectedPresetName);

    public bool CanRenamePreset =>
        CanManagePresets &&
        !string.IsNullOrWhiteSpace(SelectedPresetName) &&
        !string.IsNullOrWhiteSpace(PresetNameInput) &&
        !string.Equals(SelectedPresetName, PresetNameInput, StringComparison.OrdinalIgnoreCase);

    public string ActivePresetDisplayName => string.IsNullOrWhiteSpace(ActivePresetName) ? "none" : ActivePresetName;

    public MainViewModel(ModService modService, BackupService backupService, FileService fileService,
        SettingsService settingsService, LoadOrderService loadOrderService,
        GameSwitchService gameSwitchService)
    {
        _modService = modService;
        _backupService = backupService;
        _fileService = fileService;
        _settingsService = settingsService;
        _loadOrderService = loadOrderService;
        _gameSwitchService = gameSwitchService;

        // Subscribe to game-switched messages (e.g. from Settings window)
        WeakReferenceMessenger.Default.Register<GameSwitchedMessage>(this, async (r, m) =>
        {
            var vm = (MainViewModel)r;
            await vm.ActivateGameAsync(m.NewGame, showToast: true);
        });
    }

    public async Task InitializeAsync()
    {
        _settings = await _settingsService.LoadAsync();

        // Populate game selector
        AvailableGames.Clear();
        foreach (var profile in _settings.GameProfiles)
            AvailableGames.Add(profile);

        var configuredProfiles = _settings.GameProfiles
            .Where(p => p.IsConfigured)
            .ToList();

        if (!_settings.SetupCompleted)
        {
            if (configuredProfiles.Count == 0)
            {
                NeedsFirstTimeSetup = true;
                return;
            }

            var detectedProfile = _settings.GameProfiles
                .FirstOrDefault(p => p.GameId == _settings.ActiveGameId && p.IsConfigured)
                ?? configuredProfiles
                    .OrderByDescending(p => p.LastOpened ?? DateTime.MinValue)
                    .First();

            await ActivateGameAsync(detectedProfile, showToast: false);
            ShowToast($"✨ Auto-detected {detectedProfile.ShortName}. Review Settings if you want to adjust the paths.");
            return;
        }

        // Activate the last-used game
        var activeProfile = _settings.GameProfiles
            .FirstOrDefault(p => p.GameId == _settings.ActiveGameId)
            ?? _settings.GameProfiles.First();

        await ActivateGameAsync(activeProfile, showToast: false);
    }

    // ── Game Switching ───────────────────────────────────────────────

    /// <summary>
    /// Activates a game profile: updates UI state, paths, theme, and refreshes mods.
    /// </summary>
    private async Task ActivateGameAsync(GameProfile profile, bool showToast = true)
    {
        // Mark which game is active in the selector
        foreach (var g in AvailableGames)
            g.IsActiveGame = g.GameId == profile.GameId;

        ActiveGame = profile;
        GameRootDirectory = profile.GameRootPath;
        ModsLibraryDirectory = profile.ModLibraryPath;
        WindowTitle = $"🛡️ EDF Mod Manager — {profile.DisplayName}";

        // Apply per-game accent color
        ThemeHelper.ApplyAccentColor(profile.BannerColor);

        if (profile.IsConfigured)
        {
            await RefreshAsync();
        }
        else
        {
            Mods.Clear();
            ClearPresetState();
            ConflictReport.Clear();
            IsConflictPanelVisible = false;
            StatusText = $"{profile.ShortName} is not configured. Open Settings to set up paths.";
        }

        if (showToast)
            ShowToast($"Switched to {profile.ShortName}!");
    }

    [RelayCommand]
    private async Task SwitchGameAsync(string gameId)
    {
        if (IsBusy) return;
        if (ActiveGame?.GameId == gameId) return;

        IsBusy = true;
        try
        {
            // Save current game's load order before switching
            if (ActiveGame?.IsConfigured == true && Mods.Count > 0)
            {
                await _loadOrderService.SaveToRegistryAsync(Mods.ToList(), ActiveRegistryDir);
            }

            // Persist the switch and activate
            var profile = await _gameSwitchService.SwitchAsync(gameId, _settings);
            if (profile is not null)
                await ActivateGameAsync(profile);
        }
        catch (Exception ex)
        {
            await SettingsService.LogErrorAsync(ex);
            NotificationHelper.ShowError("Switch Error", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ── Refresh ──────────────────────────────────────────────────────

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (ActiveGame is null || !ActiveGame.IsConfigured) return;

        IsBusy = true;
        IsRefreshing = true;
        StatusText = "Refreshing...";

        try
        {
            await ReloadActiveGameModsAsync(showCreatedToast: true);
        }
        catch (Exception ex)
        {
            await SettingsService.LogErrorAsync(ex);
            NotificationHelper.ShowError("Refresh Error", ex.Message);
        }
        finally
        {
            IsRefreshing = false;
            IsBusy = false;
        }
    }

    // ── Toggle Mod ───────────────────────────────────────────────────

    [RelayCommand]
    private void ToggleMod(ModEntry mod)
    {
        if (mod is null || ActiveGame is null) return;

        _loadOrderService.PushUndoState(Mods);
        CanUndo = _loadOrderService.CanUndo;

        if (mod.IsActive)
        {
            // Enabling — assign next load order
            LoadOrderService.AssignNextLoadOrder(mod, Mods);
            ShowToast($"✅ {mod.ModName} enabled (#{mod.LoadOrder}).");
        }
        else
        {
            // Disabling — reset load order and resequence
            mod.LoadOrder = 0;
            LoadOrderService.ResequenceLoadOrders(Mods.ToList());
            ShowToast($"❌ {mod.ModName} disabled.");
        }

        MarkPendingChanges();
    }

    // ── Move Up / Down ───────────────────────────────────────────────

    [RelayCommand]
    private void MoveUp(ModEntry? mod)
    {
        if (mod is null || !mod.IsActive) return;
        var activeMods = Mods.Where(m => m.IsActive).OrderBy(m => m.LoadOrder).ToList();
        var idx = activeMods.IndexOf(mod);
        if (idx <= 0) return;

        _loadOrderService.PushUndoState(Mods);
        CanUndo = _loadOrderService.CanUndo;

        LoadOrderService.SwapLoadOrder(mod, activeMods[idx - 1]);
        MarkPendingChanges();
    }

    [RelayCommand]
    private void MoveDown(ModEntry? mod)
    {
        if (mod is null || !mod.IsActive) return;
        var activeMods = Mods.Where(m => m.IsActive).OrderBy(m => m.LoadOrder).ToList();
        var idx = activeMods.IndexOf(mod);
        if (idx < 0 || idx >= activeMods.Count - 1) return;

        _loadOrderService.PushUndoState(Mods);
        CanUndo = _loadOrderService.CanUndo;

        LoadOrderService.SwapLoadOrder(mod, activeMods[idx + 1]);
        MarkPendingChanges();
    }

    // ── Set Load Order by Number ─────────────────────────────────────

    public void SetLoadOrder(ModEntry mod, int newOrder)
    {
        if (mod is null || !mod.IsActive) return;

        var activeMods = Mods.Where(m => m.IsActive).OrderBy(m => m.LoadOrder).ToList();
        int count = activeMods.Count;
        newOrder = Math.Clamp(newOrder, 1, count);

        if (mod.LoadOrder == newOrder) return;

        _loadOrderService.PushUndoState(Mods);
        CanUndo = _loadOrderService.CanUndo;

        activeMods.Remove(mod);
        int insertIdx = Math.Clamp(newOrder - 1, 0, activeMods.Count);
        activeMods.Insert(insertIdx, mod);

        for (int i = 0; i < activeMods.Count; i++)
            activeMods[i].LoadOrder = i + 1;

        MarkPendingChanges();
        ShowToast($"📝 {mod.ModName} → #{newOrder}");
    }

    // ── Set Group ────────────────────────────────────────────────────

    public async Task SetGroupAsync(ModEntry mod, string groupName)
    {
        if (mod is null || ActiveGame is null) return;

        _loadOrderService.PushUndoState(Mods);
        CanUndo = _loadOrderService.CanUndo;

        mod.Group = groupName.Trim();

        await _loadOrderService.SaveToRegistryAsync(Mods.ToList(), ActiveRegistryDir);
        ShowToast(string.IsNullOrEmpty(mod.Group)
            ? $"🗂️ {mod.ModName} removed from group."
            : $"🗂️ {mod.ModName} → group \"{mod.Group}\"");
    }

    // ── Toggle Group View ────────────────────────────────────────────

    [RelayCommand]
    private void ToggleGroupView()
    {
        IsGroupViewActive = !IsGroupViewActive;
    }

    public void OnDragDropReorder(ModEntry draggedMod, int newLoadOrder)
    {
        if (!draggedMod.IsActive) return;

        _loadOrderService.PushUndoState(Mods);
        CanUndo = _loadOrderService.CanUndo;

        var activeMods = Mods.Where(m => m.IsActive).OrderBy(m => m.LoadOrder).ToList();
        activeMods.Remove(draggedMod);
        int insertIdx = Math.Clamp(newLoadOrder - 1, 0, activeMods.Count);
        activeMods.Insert(insertIdx, draggedMod);

        for (int i = 0; i < activeMods.Count; i++)
            activeMods[i].LoadOrder = i + 1;

        MarkPendingChanges();
    }

    // ── Undo ─────────────────────────────────────────────────────────

    [RelayCommand]
    private void Undo()
    {
        if (!_loadOrderService.CanUndo || ActiveGame is null) return;

        IsRefreshing = true;
        try
        {
            _loadOrderService.TryUndo(Mods);
            CanUndo = _loadOrderService.CanUndo;
            MarkPendingChanges();
            ShowToast("↩️ Undo applied.");
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    // ── Enable / Disable All ─────────────────────────────────────────

    [RelayCommand]
    private void EnableAll()
    {
        if (ActiveGame is null) return;

        _loadOrderService.PushUndoState(Mods);
        CanUndo = _loadOrderService.CanUndo;

        IsRefreshing = true;
        try
        {
            int order = 1;
            foreach (var mod in Mods.OrderBy(m => m.LoadOrder == 0 ? int.MaxValue : m.LoadOrder))
            {
                mod.IsActive = true;
                mod.LoadOrder = order++;
            }
        }
        finally
        {
            IsRefreshing = false;
        }

        MarkPendingChanges();
        ShowToast("✅ All mods enabled.");
    }

    [RelayCommand]
    private void DisableAll()
    {
        if (ActiveGame is null) return;

        _loadOrderService.PushUndoState(Mods);
        CanUndo = _loadOrderService.CanUndo;

        IsRefreshing = true;
        try
        {
            foreach (var mod in Mods)
            {
                mod.IsActive = false;
                mod.LoadOrder = 0;
            }
        }
        finally
        {
            IsRefreshing = false;
        }

        MarkPendingChanges();
        ShowToast("❌ All mods disabled.");
    }

    // ── Sorting / View ───────────────────────────────────────────────

    [RelayCommand]
    private void SortByName()
    {
        _loadOrderService.PushUndoState(Mods);
        CanUndo = _loadOrderService.CanUndo;

        var sorted = Mods.Where(m => m.IsActive).OrderBy(m => m.ModName, StringComparer.OrdinalIgnoreCase).ToList();
        for (int i = 0; i < sorted.Count; i++)
            sorted[i].LoadOrder = i + 1;

        MarkPendingChanges();
        ShowToast("🔃 Sorted by name. Load order updated.");
    }

    [RelayCommand]
    private void ShowLoadOrderView()
    {
        LoadOrderViewActive = true;
        var ordered = Mods.Where(m => m.IsActive).OrderBy(m => m.LoadOrder).ToList();
        Mods.Clear();
        foreach (var m in ordered)
            Mods.Add(m);
    }

    [RelayCommand]
    private async Task ShowAllModsViewAsync()
    {
        LoadOrderViewActive = false;
        await RefreshAsync();
    }

    // ── Conflict Panel ───────────────────────────────────────────────

    [RelayCommand]
    private void HideConflictPanel()
    {
        IsConflictPanelVisible = false;
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        ShowActiveOnly = false;
        ShowConflictsOnly = false;
        ShowRiskyOnly = false;
    }

    [RelayCommand]
    private async Task ImportZipAsync()
    {
        if (ActiveGame is null || !ActiveGame.IsConfigured)
            return;

        var dialog = new OpenFileDialog
        {
            Title = $"Import {ActiveGame.DisplayName} Mod Archive",
            Filter = "Zip archives (*.zip)|*.zip",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
            return;

        var preview = await PreviewImportAsync(
            () => _fileService.PreviewModArchiveAsync(dialog.FileName, ActiveGame.ModLibraryPath, ActiveGame.GameId));
        var importMode = preview is null ? null : GetImportMode(preview);
        if (preview is null || importMode is null)
            return;

        await ImportModAsync(() => _fileService.ImportModArchiveAsync(dialog.FileName, ActiveGame.ModLibraryPath, ActiveGame.GameId, importMode.Value),
            sourceLabel: Path.GetFileName(dialog.FileName));
    }

    [RelayCommand]
    private async Task ImportFolderAsync()
    {
        if (ActiveGame is null || !ActiveGame.IsConfigured)
            return;

        var dialog = new OpenFolderDialog
        {
            Title = $"Import {ActiveGame.DisplayName} Mod Folder"
        };

        if (dialog.ShowDialog() != true)
            return;

        var preview = await PreviewImportAsync(
            () => _fileService.PreviewModFolderAsync(dialog.FolderName, ActiveGame.ModLibraryPath, ActiveGame.GameId));
        var importMode = preview is null ? null : GetImportMode(preview);
        if (preview is null || importMode is null)
            return;

        await ImportModAsync(() => _fileService.ImportModFolderAsync(dialog.FolderName, ActiveGame.ModLibraryPath, ActiveGame.GameId, importMode.Value),
            sourceLabel: Path.GetFileName(dialog.FolderName));
    }

    public async Task ImportDroppedPathsAsync(IEnumerable<string> droppedPaths)
    {
        if (IsBusy)
        {
            ShowToast("Wait for the current operation to finish before importing more mods.", isError: true);
            return;
        }

        if (ActiveGame is null || !ActiveGame.IsConfigured)
        {
            ShowToast("Configure a game before importing dropped mods.", isError: true);
            return;
        }

        var candidatePaths = droppedPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidatePaths.Count == 0)
        {
            ShowToast("Drop a .zip archive or mod folder to import.", isError: true);
            return;
        }

        bool showPerItemToast = candidatePaths.Count == 1;
        var importOutcomes = new List<BatchImportItemOutcome>();
        int completedCount = 0;

        if (!showPerItemToast)
            BeginBatchImport(candidatePaths.Count);

        try
        {
            for (int index = 0; index < candidatePaths.Count; index++)
            {
                var candidatePath = candidatePaths[index];

                if (!showPerItemToast)
                {
                    UpdateBatchImportProgress(
                        completedCount,
                        candidatePaths.Count,
                        $"Previewing {GetImportDisplayName(candidatePath)} ({index + 1}/{candidatePaths.Count})");
                }

                var outcome = await ImportPathAsync(candidatePath, showPerItemToast);
                importOutcomes.Add(outcome);

                if (outcome.Result is not null)
                {
                    completedCount++;

                    if (!showPerItemToast)
                    {
                        UpdateBatchImportProgress(
                            completedCount,
                            candidatePaths.Count,
                            $"Imported {outcome.Result.ModName} ({completedCount}/{candidatePaths.Count})");
                    }
                }
                else if (!showPerItemToast)
                {
                    UpdateBatchImportProgress(
                        completedCount,
                        candidatePaths.Count,
                        $"Skipped {outcome.SourceLabel} ({outcome.SummaryLabel.ToLowerInvariant()})");
                }
            }
        }
        finally
        {
            if (!showPerItemToast)
                EndBatchImport();
        }

        if (!showPerItemToast)
            ShowBatchImportSummary(importOutcomes);
    }

    [RelayCommand]
    private async Task SavePresetAsync()
    {
        if (!CanManagePresets || ActiveGame is null) return;

        var presetName = string.IsNullOrWhiteSpace(PresetNameInput)
            ? SelectedPresetName
            : PresetNameInput;

        if (string.IsNullOrWhiteSpace(presetName))
        {
            ShowToast("Enter a preset name or select one to overwrite.", isError: true);
            return;
        }

        IsBusy = true;
        try
        {
            var savedName = await _loadOrderService.SavePresetAsync(Mods.ToList(), ActiveRegistryDir, presetName);
            await RefreshPresetStateAsync(savedName);
            PresetNameInput = string.Empty;
            ShowToast($"💾 Preset \"{savedName}\" saved.");
        }
        catch (Exception ex)
        {
            await SettingsService.LogErrorAsync(ex);
            NotificationHelper.ShowError("Preset Error", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LoadPresetAsync()
    {
        if (!CanLoadPreset || ActiveGame is null) return;

        IsBusy = true;
        IsRefreshing = true;
        try
        {
            _loadOrderService.PushUndoState(Mods);
            CanUndo = _loadOrderService.CanUndo;

            if (!await _loadOrderService.LoadPresetAsync(Mods, ActiveRegistryDir, SelectedPresetName))
            {
                ShowToast("Selected preset could not be loaded.", isError: true);
                return;
            }

            await RefreshPresetStateAsync(SelectedPresetName);
            MarkPendingChanges();
            ShowToast($"📚 Preset \"{SelectedPresetName}\" loaded. Click Apply Mods to deploy it.");
        }
        catch (Exception ex)
        {
            await SettingsService.LogErrorAsync(ex);
            NotificationHelper.ShowError("Preset Error", ex.Message);
        }
        finally
        {
            IsRefreshing = false;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeletePresetAsync()
    {
        if (!CanDeletePreset || ActiveGame is null) return;

        var presetName = SelectedPresetName;
        var result = MessageBox.Show(
            $"Delete preset \"{presetName}\"?",
            "Delete Preset",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        IsBusy = true;
        try
        {
            if (!await _loadOrderService.DeletePresetAsync(ActiveRegistryDir, presetName))
            {
                ShowToast("Selected preset could not be deleted.", isError: true);
                return;
            }

            if (string.Equals(PresetNameInput, presetName, StringComparison.OrdinalIgnoreCase))
                PresetNameInput = string.Empty;

            await RefreshPresetStateAsync();
            ShowToast($"🗑️ Preset \"{presetName}\" deleted.");
        }
        catch (Exception ex)
        {
            await SettingsService.LogErrorAsync(ex);
            NotificationHelper.ShowError("Preset Error", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RenamePresetAsync()
    {
        if (!CanRenamePreset || ActiveGame is null) return;

        var existingName = SelectedPresetName;
        var newName = PresetNameInput;
        IsBusy = true;
        try
        {
            var renamedName = await _loadOrderService.RenamePresetAsync(ActiveRegistryDir, existingName, newName);
            await RefreshPresetStateAsync(renamedName);
            PresetNameInput = string.Empty;
            ShowToast($"✏️ Preset \"{existingName}\" renamed to \"{renamedName}\".");
        }
        catch (Exception ex)
        {
            await SettingsService.LogErrorAsync(ex);
            NotificationHelper.ShowError("Preset Error", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void MakeLoserWin(ConflictInfo? conflict)
    {
        ApplyConflictPriorityChange(conflict, selectWinnerAfterChange: false);
    }

    [RelayCommand]
    private void MoveWinnerDown(ConflictInfo? conflict)
    {
        ApplyConflictPriorityChange(conflict, selectWinnerAfterChange: true);
    }

    private void ApplyConflictPriorityChange(ConflictInfo? conflict, bool selectWinnerAfterChange)
    {
        if (conflict is null)
            return;

        var winner = FindModByName(conflict.WinnerModName);
        var loser = FindModByName(conflict.LoserModName);
        if (winner is null || loser is null || !winner.IsActive || !loser.IsActive)
        {
            ShowToast("Conflict mods could not be found in the active list.", isError: true);
            return;
        }

        _loadOrderService.PushUndoState(Mods);
        CanUndo = _loadOrderService.CanUndo;

        var activeMods = Mods.Where(m => m.IsActive).OrderBy(m => m.LoadOrder).ToList();
        activeMods.Remove(loser);

        var winnerIndex = activeMods.IndexOf(winner);
        if (winnerIndex < 0)
        {
            ShowToast("Conflict winner could not be reordered.", isError: true);
            return;
        }

        activeMods.Insert(Math.Min(winnerIndex + 1, activeMods.Count), loser);

        for (int i = 0; i < activeMods.Count; i++)
            activeMods[i].LoadOrder = i + 1;

        SelectedMod = selectWinnerAfterChange ? winner : loser;
        MarkPendingChanges();
        ShowToast(selectWinnerAfterChange
            ? $"⬇️ \"{winner.ModName}\" moved below \"{loser.ModName}\" for {conflict.FileName}."
            : $"⚔️ \"{loser.ModName}\" now outranks \"{winner.ModName}\" for {conflict.FileName}.");
    }

    [RelayCommand]
    private void SelectConflictWinner(ConflictInfo? conflict)
    {
        SelectConflictMod(conflict?.WinnerModName, "winner");
    }

    [RelayCommand]
    private void SelectConflictLoser(ConflictInfo? conflict)
    {
        SelectConflictMod(conflict?.LoserModName, "loser");
    }

    // ── Folder Commands ──────────────────────────────────────────────

    [RelayCommand]
    private void OpenGameFolder()
    {
        if (ActiveGame is null || !Directory.Exists(ActiveGame.GameRootPath)) return;
        try
        {
            Process.Start("explorer.exe", ActiveGame.GameRootPath);
        }
        catch (Exception ex)
        {
            _ = SettingsService.LogErrorAsync(ex);
            ShowToast("Could not open game folder.", isError: true);
        }
    }

    [RelayCommand]
    private void OpenModsLibrary()
    {
        if (ActiveGame is null || !Directory.Exists(ActiveGame.ModLibraryPath)) return;
        try
        {
            Process.Start("explorer.exe", ActiveGame.ModLibraryPath);
        }
        catch (Exception ex)
        {
            _ = SettingsService.LogErrorAsync(ex);
            ShowToast("Could not open mods library folder.", isError: true);
        }
    }

    [RelayCommand]
    private async Task LaunchGameAsync()
    {
        if (!CanLaunchGame)
        {
            ShowToast("Game executable not found for the active profile.", isError: true);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = ActiveExecutablePath,
                WorkingDirectory = ActiveGame!.GameRootPath,
                UseShellExecute = true
            });

            ShowToast($"🎮 Launching {ActiveGame!.ShortName}...");
        }
        catch (Exception ex)
        {
            await SettingsService.LogErrorAsync(ex);
            NotificationHelper.ShowError("Launch Error", ex.Message);
        }
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        if (ShowSettingsDialog is null) return;
        if (await ShowSettingsDialog())
            await ReloadSettingsAsync();
    }

    public async Task ReloadSettingsAsync()
    {
        _settings = await _settingsService.LoadAsync();

        // Refresh available games
        AvailableGames.Clear();
        foreach (var p in _settings.GameProfiles)
            AvailableGames.Add(p);

        // Re-activate current game (paths may have changed)
        var profile = _settings.GameProfiles
            .FirstOrDefault(p => p.GameId == _settings.ActiveGameId)
            ?? _settings.GameProfiles.FirstOrDefault();

        if (profile is not null)
            await ActivateGameAsync(profile, showToast: false);

        NeedsFirstTimeSetup = false;
    }

    // ── Apply Mods (deploy to game folder) ─────────────────────────

    [RelayCommand]
    private async Task ApplyModsAsync()
    {
        await ApplyModsInternalAsync();
    }

    [RelayCommand]
    private async Task ApplyAndLaunchAsync()
    {
        if (await ApplyModsInternalAsync())
            await LaunchGameAsync();
    }

    private async Task<bool> ApplyModsInternalAsync()
    {
        if (ActiveGame is null) return false;

        if (!await ConfirmApplySummaryAsync())
            return false;

        IsBusy = true;

        try
        {
            var progress = new Progress<string>(msg => StatusText = msg);
            await _modService.ApplyAllModsAsync(Mods.ToList(), ActiveGame.GameRootPath, ActiveRegistryDir, progress);
            RefreshConflictReport();
            UpdateStatusBar();
            HasPendingChanges = false;
            ShowToast("🚀 Mods applied successfully!");
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            NotificationHelper.ShowError("Permission Error",
                "Unable to copy/delete files. Try running the app as Administrator.");
            await SettingsService.LogErrorAsync(new UnauthorizedAccessException("File permission error during apply."));
        }
        catch (Exception ex)
        {
            await SettingsService.LogErrorAsync(ex);
            NotificationHelper.ShowError("Apply Error", ex.Message);
        }
        finally
        {
            UpdateApplyBackupState();
            IsBusy = false;
        }

        return false;
    }

    [RelayCommand]
    private async Task RestoreLastApplyBackupAsync()
    {
        if (IsBusy || ActiveGame is null || !CanRestoreLastApply)
            return;

        var confirmed = NotificationHelper.Confirm(
            "Restore Last Apply",
            $"Restore the last apply backup for {ActiveGame.ShortName}? This will replace the current managed files in the game's Mods folder and reload the backed-up deployed state.");

        if (!confirmed)
            return;

        IsBusy = true;
        IsRefreshing = true;
        try
        {
            var progress = new Progress<string>(msg => StatusText = msg);
            var snapshot = await _backupService.RestoreLastApplyBackupAsync(ActiveGame.GameRootPath, ActiveRegistryDir, progress);
            await ReloadActiveGameModsAsync(showCreatedToast: false);
            ShowToast($"↩️ Restored last apply backup from {snapshot.CreatedAt:yyyy-MM-dd HH:mm}.");
        }
        catch (UnauthorizedAccessException)
        {
            NotificationHelper.ShowError("Permission Error",
                "Unable to restore the last apply backup. Try running the app as Administrator.");
            await SettingsService.LogErrorAsync(new UnauthorizedAccessException("File permission error during backup restore."));
        }
        catch (Exception ex)
        {
            await SettingsService.LogErrorAsync(ex);
            NotificationHelper.ShowError("Restore Error", ex.Message);
        }
        finally
        {
            UpdateApplyBackupState();
            IsRefreshing = false;
            IsBusy = false;
        }
    }

    private async Task<bool> ConfirmApplySummaryAsync()
    {
        try
        {
            var preview = await BuildApplySummaryPreviewAsync();
            if (ShowApplySummaryDialog is not null)
                return ShowApplySummaryDialog(preview);

            var messageResult = MessageBox.Show(
                BuildApplySummaryFallbackText(preview),
                "Apply Summary",
                MessageBoxButton.OKCancel,
                preview.HighRiskModCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
            return messageResult == MessageBoxResult.OK;
        }
        catch (Exception ex)
        {
            await SettingsService.LogErrorAsync(ex);
            NotificationHelper.ShowError("Apply Summary Error", ex.Message);
            return false;
        }
    }

    private async Task<ApplySummaryPreview> BuildApplySummaryPreviewAsync()
    {
        if (ActiveGame is null)
            throw new InvalidOperationException("No active game is selected.");

        var allMods = Mods.ToList();
        var activeMods = allMods
            .Where(mod => mod.IsActive)
            .OrderBy(mod => mod.LoadOrder)
            .ToList();
        var winnerMap = ConflictService.BuildWinnerMap(allMods);
        var registry = await _fileService.LoadRegistryAsync(ActiveRegistryDir);

        var currentOwnerByFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var modEntry in registry.ActiveMods)
        {
            foreach (var relPath in modEntry.Files)
                currentOwnerByFile[relPath] = modEntry.Name;
        }

        var currentFiles = currentOwnerByFile.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nextFiles = winnerMap.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var addedFiles = nextFiles
            .Except(currentFiles, StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var removedFiles = currentFiles
            .Except(nextFiles, StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var replacedFiles = winnerMap
            .Where(entry =>
                currentOwnerByFile.TryGetValue(entry.Key, out var currentOwner) &&
                !string.Equals(currentOwner, entry.Value.ModName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(entry => $"{entry.Key} -> {entry.Value.ModName}")
            .ToList();

        var conflictGroups = _modService.GetConflictReport(allMods)
            .GroupBy(conflict => conflict.RelativePath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var conflictSummaries = conflictGroups
            .Select(group =>
            {
                var winner = group.First().WinnerModName;
                var losers = string.Join(", ", group
                    .Select(conflict => conflict.LoserModName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
                return $"{group.Key} -> {winner} overrides {losers}";
            })
            .ToList();

        var highRiskMods = activeMods
            .Where(mod => mod.IsHighRisk)
            .Select(mod => mod.ModName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ApplySummaryPreview
        {
            GameDisplayName = ActiveGame.DisplayName,
            ActivePresetName = ActivePresetName,
            IsActivePresetDirty = IsActivePresetDirty,
            ActiveModCount = activeMods.Count,
            WinningFileCount = winnerMap.Count,
            AddedFileCount = addedFiles.Count,
            RemovedFileCount = removedFiles.Count,
            ReplacedFileCount = replacedFiles.Count,
            ConflictFileCount = conflictGroups.Count,
            HighRiskModCount = highRiskMods.Count,
            ActiveMods = activeMods.Select(mod => $"{mod.LoadOrder}. {mod.ModName}").ToList(),
            AddedFiles = addedFiles,
            RemovedFiles = removedFiles,
            ReplacedFiles = replacedFiles,
            ConflictSummaries = conflictSummaries,
            HighRiskMods = highRiskMods
        };
    }

    private static string BuildApplySummaryFallbackText(ApplySummaryPreview preview)
    {
        var lines = new List<string>
        {
            $"Game: {preview.GameDisplayName}",
            $"Active mods: {preview.ActiveModCount}",
            $"Winning files: {preview.WinningFileCount}",
            $"Add: {preview.AddedFileCount}",
            $"Remove: {preview.RemovedFileCount}",
            $"Swap winners: {preview.ReplacedFileCount}",
            $"Conflicted files: {preview.ConflictFileCount}",
            $"High-risk mods: {preview.HighRiskModCount}"
        };

        if (!string.IsNullOrWhiteSpace(preview.ActivePresetName))
        {
            lines.Add(preview.IsActivePresetDirty
                ? $"Loadout: {preview.ActivePresetName} (modified)"
                : $"Loadout: {preview.ActivePresetName}");
        }

        lines.Add(string.Empty);
        lines.Add("A last apply backup snapshot will be created automatically before managed files are cleaned.");
        lines.Add(string.Empty);
        lines.Add("Continue and apply these changes?");

        return string.Join(Environment.NewLine, lines);
    }

    private async Task ReloadActiveGameModsAsync(bool showCreatedToast)
    {
        if (ActiveGame is null || !ActiveGame.IsConfigured)
            return;

        bool created = _fileService.EnsureModsFolderStructure(ActiveGame.GameRootPath);
        if (created && showCreatedToast)
            ShowToast("📁 Mods folder structure created / verified.");

        var entries = _fileService.ScanModsLibrary(ActiveGame.ModLibraryPath);
        _fileService.AnnotateModWarnings(entries, ActiveGame.GameId);

        await _loadOrderService.ApplyRegistryToModsAsync(entries, ActiveRegistryDir);
        _modService.RefreshStatuses(entries);

        Mods.Clear();
        foreach (var entry in entries.OrderBy(e => e.IsActive ? e.LoadOrder : int.MaxValue))
            Mods.Add(entry);

        await RefreshPresetStateAsync();

        RefreshConflictReport();
        UpdateStatusBar();
        HasPendingChanges = false;
        UpdateApplyBackupState();
    }

    /// <summary>
    /// Marks that in-memory state has changed and refreshes conflict / status UI.
    /// Does NOT touch the filesystem — call ApplyModsAsync to deploy.
    /// </summary>
    private void MarkPendingChanges()
    {
        _modService.RefreshStatuses(Mods.ToList());
        RefreshConflictReport();
        UpdatePresetDirtyState();
        UpdateStatusBar();
        HasPendingChanges = true;
    }

    private void RefreshConflictReport()
    {
        ConflictReport.Clear();
        var conflicts = _modService.GetConflictReport(Mods.ToList());
        foreach (var c in conflicts)
            ConflictReport.Add(c);

        IsConflictPanelVisible = ConflictReport.Count > 0;
    }

    private void UpdateStatusBar()
    {
        int activeCount = Mods.Count(m => m.IsActive);
        int conflictCount = Mods.Count(m => m.HasConflict);
        WarningModCount = Mods.Count(m => m.HasWarnings);
        HighRiskModCount = Mods.Count(m => m.IsHighRisk);

        var sb = new StringBuilder();
        if (ActiveGame is not null)
            sb.Append($"🎮 {ActiveGame.ShortName} | ");
        sb.Append($"✅ {activeCount} mod(s) active");
        if (!string.IsNullOrWhiteSpace(ActivePresetName))
            sb.Append($" | 📚 {ActivePresetName}");
        if (IsActivePresetDirty)
            sb.Append(" | ✏️ loadout modified");
        if (conflictCount > 0)
            sb.Append($" | ⚠️ {conflictCount} conflict(s) detected");
        if (WarningModCount > 0)
            sb.Append($" | 🛡️ {WarningModCount} warning(s)");
        if (HighRiskModCount > 0)
            sb.Append($" | 🔥 {HighRiskModCount} high-risk");

        StatusText = sb.ToString();
    }

    private void ShowToast(string message, bool isError = false)
    {
        ShowToastAction?.Invoke(message, isError);
    }

    private void BeginBatchImport(int totalCount)
    {
        IsBatchImportActive = true;
        BatchImportProgressLabel = $"Batch import 0/{totalCount}";
        BatchImportCurrentItemLabel = "Preparing dropped items...";
    }

    private void UpdateBatchImportProgress(int completedCount, int totalCount, string currentItemLabel)
    {
        BatchImportProgressLabel = $"Batch import {completedCount}/{totalCount}";
        BatchImportCurrentItemLabel = currentItemLabel;
    }

    private void EndBatchImport()
    {
        IsBatchImportActive = false;
        BatchImportProgressLabel = string.Empty;
        BatchImportCurrentItemLabel = string.Empty;
    }

    private static string GetImportDisplayName(string sourcePath)
    {
        var normalizedPath = Path.TrimEndingDirectorySeparator(sourcePath);
        return Path.GetFileName(normalizedPath);
    }

    private void ShowBatchImportSummary(IReadOnlyList<BatchImportItemOutcome> importOutcomes)
    {
        if (importOutcomes.Count == 0)
            return;

        var importedResults = importOutcomes
            .Where(outcome => outcome.Result is not null)
            .Select(outcome => outcome.Result!)
            .ToList();

        var skippedOutcomes = importOutcomes
            .Where(outcome => outcome.Result is null)
            .ToList();

        var messageParts = new List<string>();

        if (importedResults.Count > 0)
        {
            var importedNames = importedResults
                .Select(result => result.ModName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var previewNames = string.Join(", ", importedNames.Take(3));
            var moreNote = importedNames.Count > 3 ? $" and {importedNames.Count - 3} more" : string.Empty;

            var summaryNotes = new List<string>();
            int replacedCount = importedResults.Count(result => result.ReplacedExisting);
            int scaffoldedCount = importedResults.Count(result => result.CreatedMetadata);

            if (replacedCount > 0)
                summaryNotes.Add($"{replacedCount} replaced existing");

            if (scaffoldedCount > 0)
                summaryNotes.Add($"{scaffoldedCount} scaffolded metadata");

            var extraDetail = summaryNotes.Count > 0
                ? $" ({string.Join(", ", summaryNotes)})"
                : string.Empty;

            messageParts.Add($"📥 Imported {importedResults.Count} dropped item(s): {previewNames}{moreNote}.{extraDetail}");
        }

        if (skippedOutcomes.Count > 0)
        {
            var skippedPreview = string.Join(", ", skippedOutcomes
                .Take(3)
                .Select(outcome => $"{outcome.SourceLabel} ({outcome.SummaryLabel})"));
            var moreNote = skippedOutcomes.Count > 3 ? $" and {skippedOutcomes.Count - 3} more" : string.Empty;
            messageParts.Add($"Skipped {skippedOutcomes.Count}: {skippedPreview}{moreNote}.");
        }

        if (messageParts.Count == 0)
            return;

        ShowToast(
            string.Join(" ", messageParts),
            isError: importedResults.Count == 0 && skippedOutcomes.Count > 0);
    }

    [RelayCommand]
    private void SelectRecentImport(RecentImportEntry? entry)
    {
        if (entry is null)
            return;

        ClearFilters();
        LoadOrderViewActive = false;

        var mod = FindModByName(entry.ModName);
        if (mod is null)
        {
            ShowToast($"Recent import could not be found: {entry.ModName}.", isError: true);
            return;
        }

        SelectedMod = mod;
        ShowToast($"📌 Selected recent import: {mod.ModName}.");
    }

    [RelayCommand]
    private void OpenRecentImportFolder(RecentImportEntry? entry)
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.FolderPath))
            return;

        if (!Directory.Exists(entry.FolderPath))
        {
            ShowToast($"Recent import folder could not be found: {entry.ModName}.", isError: true);
            return;
        }

        try
        {
            Process.Start("explorer.exe", entry.FolderPath);
        }
        catch (Exception ex)
        {
            _ = SettingsService.LogErrorAsync(ex);
            ShowToast("Could not open recent import folder.", isError: true);
        }
    }

    [RelayCommand]
    private void SelectLatestRecentImport()
    {
        SelectRecentImport(LatestRecentImport);
    }

    [RelayCommand]
    private void OpenLatestRecentImportFolder()
    {
        OpenRecentImportFolder(LatestRecentImport);
    }

    [RelayCommand]
    private async Task ToggleRecentImportPinAsync(RecentImportEntry? entry)
    {
        if (IsBusy || ActiveGame is null || entry is null)
            return;

        var existingEntry = FindRecentImportEntry(entry);
        if (existingEntry is null)
        {
            ShowToast($"Recent import could not be found: {entry.ModName}.", isError: true);
            return;
        }

        bool originalPinned = existingEntry.IsPinned;
        existingEntry.IsPinned = !existingEntry.IsPinned;
        NormalizeRecentImports(ActiveGame.RecentImports);
        SyncRecentImportsForActiveGame();

        IsBusy = true;
        try
        {
            await _settingsService.SaveGameConfigAsync(ActiveGame);
            ShowToast(existingEntry.IsPinned
                ? $"📌 Pinned {existingEntry.ModName} in recent imports."
                : $"Removed pin from {existingEntry.ModName}.");
        }
        catch (Exception ex)
        {
            existingEntry.IsPinned = originalPinned;
            NormalizeRecentImports(ActiveGame.RecentImports);
            SyncRecentImportsForActiveGame();
            await SettingsService.LogErrorAsync(ex);
            NotificationHelper.ShowError("Recent Imports Error", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ToggleLatestRecentImportPinAsync()
    {
        if (LatestRecentImport is null)
            return;

        await ToggleRecentImportPinAsync(LatestRecentImport);
    }

    [RelayCommand]
    private async Task ClearRecentImportsAsync()
    {
        if (IsBusy || ActiveGame is null || !HasRecentImports)
            return;

        var confirmed = NotificationHelper.Confirm(
            "Clear Recent Imports",
            $"Clear recent imports for {ActiveGame.ShortName}? This only removes the history list.");

        if (!confirmed)
            return;

        IsBusy = true;
        try
        {
            ActiveGame.RecentImports.Clear();
            SyncRecentImportsForActiveGame();
            await _settingsService.SaveGameConfigAsync(ActiveGame);
            ShowToast($"🧹 Cleared recent imports for {ActiveGame.ShortName}.");
        }
        catch (Exception ex)
        {
            await SettingsService.LogErrorAsync(ex);
            NotificationHelper.ShowError("Recent Imports Error", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<BatchImportItemOutcome> ImportPathAsync(string sourcePath, bool showSuccessToast = true)
    {
        var sourceLabel = GetImportDisplayName(sourcePath);

        if (ActiveGame is null)
            return new BatchImportItemOutcome(sourceLabel, BatchImportItemStatus.Failed, "import unavailable");

        if (Directory.Exists(sourcePath))
        {
            var preview = await PreviewImportAsync(
                () => _fileService.PreviewModFolderAsync(sourcePath, ActiveGame.ModLibraryPath, ActiveGame.GameId));
            if (preview is null)
                return new BatchImportItemOutcome(sourceLabel, BatchImportItemStatus.Failed, "preview failed");

            var importMode = GetImportMode(preview);
            if (importMode is null)
                return new BatchImportItemOutcome(sourceLabel, BatchImportItemStatus.Cancelled, "canceled");

            var result = await ImportModAsync(
                () => _fileService.ImportModFolderAsync(sourcePath, ActiveGame.ModLibraryPath, ActiveGame.GameId, importMode.Value),
                sourceLabel: sourceLabel,
                showSuccessToast: showSuccessToast);

            return result is not null
                ? new BatchImportItemOutcome(sourceLabel, result)
                : new BatchImportItemOutcome(sourceLabel, BatchImportItemStatus.Failed, "import failed");
        }

        if (File.Exists(sourcePath) && string.Equals(Path.GetExtension(sourcePath), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            var preview = await PreviewImportAsync(
                () => _fileService.PreviewModArchiveAsync(sourcePath, ActiveGame.ModLibraryPath, ActiveGame.GameId));
            if (preview is null)
                return new BatchImportItemOutcome(sourceLabel, BatchImportItemStatus.Failed, "preview failed");

            var importMode = GetImportMode(preview);
            if (importMode is null)
                return new BatchImportItemOutcome(sourceLabel, BatchImportItemStatus.Cancelled, "canceled");

            var result = await ImportModAsync(
                () => _fileService.ImportModArchiveAsync(sourcePath, ActiveGame.ModLibraryPath, ActiveGame.GameId, importMode.Value),
                sourceLabel: sourceLabel,
                showSuccessToast: showSuccessToast);

            return result is not null
                ? new BatchImportItemOutcome(sourceLabel, result)
                : new BatchImportItemOutcome(sourceLabel, BatchImportItemStatus.Failed, "import failed");
        }

        ShowToast($"Unsupported import source: {Path.GetFileName(sourcePath)}", isError: true);
        return new BatchImportItemOutcome(sourceLabel, BatchImportItemStatus.Unsupported, "unsupported type");
    }

    private async Task<ModImportResult?> ImportModAsync(
        Func<Task<ModImportResult>> importOperation,
        string sourceLabel,
        bool showSuccessToast = true)
    {
        if (ActiveGame is null)
            return null;

        IsBusy = true;
        try
        {
            var result = await importOperation();
            ClearFilters();
            LoadOrderViewActive = false;
            await RefreshAsync();
            SelectedMod = FindModByName(result.ModName);
            await TrackRecentImportAsync(result, sourceLabel);

            var metadataNote = result.CreatedMetadata ? " Metadata was scaffolded." : string.Empty;
            var replaceNote = result.ReplacedExisting ? " Existing library folder was replaced." : string.Empty;
            if (showSuccessToast)
                ShowToast($"📥 Imported {result.ModName} from {sourceLabel} ({result.ImportedFileCount} files).{metadataNote}{replaceNote}");

            return result;
        }
        catch (UnauthorizedAccessException)
        {
            NotificationHelper.ShowError("Permission Error", "Unable to import files. Try running the app as Administrator.");
            await SettingsService.LogErrorAsync(new UnauthorizedAccessException("File permission error during import."));
        }
        catch (Exception ex)
        {
            await SettingsService.LogErrorAsync(ex);
            NotificationHelper.ShowError("Import Error", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }

        return null;
    }

    private async Task<ModImportPreview?> PreviewImportAsync(Func<Task<ModImportPreview>> previewOperation)
    {
        try
        {
            return await previewOperation();
        }
        catch (Exception ex)
        {
            await SettingsService.LogErrorAsync(ex);
            NotificationHelper.ShowError("Import Preview Error", ex.Message);
            return null;
        }
    }

    private ModImportMode? GetImportMode(ModImportPreview preview)
    {
        if (ShowImportPreviewDialog is not null)
            return ShowImportPreviewDialog(preview);

        var icon = preview.Warnings.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Question;
        var result = MessageBox.Show(
            $"Import {preview.ProposedModName} ({preview.ImportedFileCount} files) to the current library?",
            "Import Preview",
            MessageBoxButton.YesNo,
            icon);

        return result == MessageBoxResult.Yes ? ModImportMode.ImportAsCopy : null;
    }

    private async Task RefreshPresetStateAsync(string? preferredPresetName = null)
    {
        if (ActiveGame is null || !ActiveGame.IsConfigured)
        {
            ClearPresetState();
            return;
        }

        var presetNames = await _loadOrderService.GetPresetNamesAsync(ActiveRegistryDir);
        var activePresetName = await _loadOrderService.GetActivePresetNameAsync(ActiveRegistryDir);
        var selectedName = !string.IsNullOrWhiteSpace(preferredPresetName)
            ? preferredPresetName
            : !string.IsNullOrWhiteSpace(SelectedPresetName)
                ? SelectedPresetName
                : activePresetName;

        AvailablePresets.Clear();
        foreach (var presetName in presetNames)
            AvailablePresets.Add(presetName);

        ActivePresetName = activePresetName;
        _activePresetSnapshot = string.IsNullOrWhiteSpace(activePresetName)
            ? null
            : await _loadOrderService.GetPresetAsync(ActiveRegistryDir, activePresetName);
        SelectedPresetName = AvailablePresets.FirstOrDefault(p =>
                                string.Equals(p, selectedName, StringComparison.OrdinalIgnoreCase))
                            ?? string.Empty;
        UpdatePresetDirtyState();
    }

    private void ClearPresetState()
    {
        _activePresetSnapshot = null;
        AvailablePresets.Clear();
        ActivePresetName = string.Empty;
        IsActivePresetDirty = false;
        SelectedPresetName = string.Empty;
        PresetNameInput = string.Empty;
    }

    private ModEntry? FindModByName(string modName)
    {
        return Mods.FirstOrDefault(m => string.Equals(m.ModName, modName, StringComparison.OrdinalIgnoreCase));
    }

    private async Task TrackRecentImportAsync(ModImportResult result, string sourceLabel)
    {
        if (ActiveGame is null)
            return;

        var entries = ActiveGame.RecentImports;
        if (entries is null)
        {
            entries = [];
            ActiveGame.RecentImports = entries;
        }

        bool wasPinned = entries.Any(entry =>
            string.Equals(entry.ModName, result.ModName, StringComparison.OrdinalIgnoreCase) &&
            entry.IsPinned);

        entries.RemoveAll(entry => string.Equals(entry.ModName, result.ModName, StringComparison.OrdinalIgnoreCase));
        entries.Insert(0, new RecentImportEntry
        {
            ModName = result.ModName,
            SourceLabel = sourceLabel,
            FolderPath = result.DestinationFolderPath,
            ImportedFileCount = result.ImportedFileCount,
            ReplacedExisting = result.ReplacedExisting,
            IsPinned = wasPinned,
            ImportedAt = DateTime.Now
        });

        NormalizeRecentImports(entries);

        if (entries.Count > MaxRecentImportsPerGame)
            entries.RemoveRange(MaxRecentImportsPerGame, entries.Count - MaxRecentImportsPerGame);

        SyncRecentImportsForActiveGame();

        try
        {
            await _settingsService.SaveGameConfigAsync(ActiveGame);
        }
        catch (Exception ex)
        {
            await SettingsService.LogErrorAsync(ex);
            ShowToast("Imported mod, but recent import history could not be saved.", isError: true);
        }
    }

    private void SyncRecentImportsForActiveGame()
    {
        PinnedRecentImports.Clear();
        RecentImports.Clear();
        LatestRecentImport = null;

        if (ActiveGame is not null)
        {
            NormalizeRecentImports(ActiveGame.RecentImports);
            LatestRecentImport = ActiveGame.RecentImports
                .OrderByDescending(entry => entry.ImportedAt)
                .FirstOrDefault();

            foreach (var entry in ActiveGame.RecentImports.Where(entry => entry.IsPinned))
                PinnedRecentImports.Add(entry);

            foreach (var entry in ActiveGame.RecentImports.Where(entry => !entry.IsPinned))
                RecentImports.Add(entry);
        }

        PinnedRecentImportCount = PinnedRecentImports.Count;
        RecentImportCount = RecentImports.Count;
        PinnedRecentImportsHeader = PinnedRecentImportCount > 0
            ? $"Pinned ({PinnedRecentImportCount})"
            : "Pinned";
        RecentImportsHeader = RecentImportCount > 0
            ? $"Recent ({RecentImportCount})"
            : "Recent";
        HasPinnedRecentImports = PinnedRecentImports.Count > 0;
        HasUnpinnedRecentImports = RecentImports.Count > 0;
        HasRecentImports = HasPinnedRecentImports || HasUnpinnedRecentImports;
    }

    private RecentImportEntry? FindRecentImportEntry(RecentImportEntry entry)
    {
        if (ActiveGame is null)
            return null;

        return ActiveGame.RecentImports.FirstOrDefault(candidate =>
            string.Equals(candidate.ModName, entry.ModName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.FolderPath, entry.FolderPath, StringComparison.OrdinalIgnoreCase) &&
            candidate.ImportedAt == entry.ImportedAt);
    }

    private static void NormalizeRecentImports(List<RecentImportEntry> entries)
    {
        var orderedEntries = entries
            .OrderByDescending(entry => entry.IsPinned)
            .ThenByDescending(entry => entry.ImportedAt)
            .ToList();

        entries.Clear();
        entries.AddRange(orderedEntries);
    }

    private void SelectConflictMod(string? modName, string roleLabel)
    {
        if (string.IsNullOrWhiteSpace(modName))
            return;

        var mod = FindModByName(modName);
        if (mod is null)
        {
            ShowToast($"Conflict {roleLabel} could not be found.", isError: true);
            return;
        }

        ClearFilters();
        SelectedMod = mod;
        ShowToast($"🔎 Selected {roleLabel}: {mod.ModName}.");
    }

    private void UpdatePresetDirtyState()
    {
        IsActivePresetDirty = !string.IsNullOrWhiteSpace(ActivePresetName) &&
                              _activePresetSnapshot is not null &&
                              !LoadOrderService.DoesPresetMatchState(Mods, _activePresetSnapshot);
    }

    private void UpdateApplyBackupState()
    {
        HasApplyBackupSnapshot = ActiveGame is not null &&
                                 ActiveGame.IsConfigured &&
                                 _backupService.HasLastApplyBackup(ActiveRegistryDir);
    }

    partial void OnActiveGameChanged(GameProfile? value)
    {
        SyncRecentImportsForActiveGame();
        UpdateApplyBackupState();
        OnPropertyChanged(nameof(CanLaunchGame));
        OnPropertyChanged(nameof(CanApplyAndLaunch));
        OnPropertyChanged(nameof(CanRestoreLastApply));
        OnPropertyChanged(nameof(CanManagePresets));
        OnPropertyChanged(nameof(CanSavePreset));
        OnPropertyChanged(nameof(CanLoadPreset));
        OnPropertyChanged(nameof(CanDeletePreset));
        OnPropertyChanged(nameof(CanRenamePreset));
    }

    partial void OnHasApplyBackupSnapshotChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRestoreLastApply));
    }

    partial void OnHasPendingChangesChanged(bool value)
    {
        OnPropertyChanged(nameof(CanApplyAndLaunch));
    }

    partial void OnSelectedPresetNameChanged(string value)
    {
        OnPropertyChanged(nameof(CanSavePreset));
        OnPropertyChanged(nameof(CanLoadPreset));
        OnPropertyChanged(nameof(CanDeletePreset));
        OnPropertyChanged(nameof(CanRenamePreset));
    }

    private enum BatchImportItemStatus
    {
        Imported,
        Cancelled,
        Unsupported,
        Failed
    }

    private sealed class BatchImportItemOutcome
    {
        public BatchImportItemOutcome(string sourceLabel, ModImportResult result)
        {
            SourceLabel = sourceLabel;
            Result = result;
            Status = BatchImportItemStatus.Imported;
            SummaryLabel = "imported";
        }

        public BatchImportItemOutcome(string sourceLabel, BatchImportItemStatus status, string summaryLabel)
        {
            SourceLabel = sourceLabel;
            Status = status;
            SummaryLabel = summaryLabel;
        }

        public string SourceLabel { get; }

        public BatchImportItemStatus Status { get; }

        public string SummaryLabel { get; }

        public ModImportResult? Result { get; }
    }

    partial void OnPresetNameInputChanged(string value)
    {
        OnPropertyChanged(nameof(CanSavePreset));
        OnPropertyChanged(nameof(CanRenamePreset));
    }

    partial void OnActivePresetNameChanged(string value)
    {
        OnPropertyChanged(nameof(ActivePresetDisplayName));
        UpdateStatusBar();
    }

    partial void OnIsActivePresetDirtyChanged(bool value)
    {
        UpdateStatusBar();
    }
}
