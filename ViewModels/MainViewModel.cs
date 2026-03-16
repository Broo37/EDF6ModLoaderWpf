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

namespace EDF6ModLoaderWpf.ViewModels;

/// <summary>
/// ViewModel for the main window — mod list, load order management, conflict detection,
/// and multi-game switching.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly ModService _modService;
    private readonly FileService _fileService;
    private readonly SettingsService _settingsService;
    private readonly LoadOrderService _loadOrderService;
    private readonly GameSwitchService _gameSwitchService;

    private AppSettings _settings = new();

    /// <summary>Collection of mods displayed in the DataGrid.</summary>
    public ObservableCollection<ModEntry> Mods { get; } = [];

    /// <summary>Conflict report entries for the info panel.</summary>
    public ObservableCollection<ConflictInfo> ConflictReport { get; } = [];

    /// <summary>Available game profiles for the game selector.</summary>
    public ObservableCollection<GameProfile> AvailableGames { get; } = [];

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

    public System.Windows.Controls.Panel? NotificationPanel { get; set; }

    /// <summary>AppData directory for the active game's registry.</summary>
    private string ActiveRegistryDir =>
        ActiveGame is not null ? SettingsService.GetGameAppDataFolder(ActiveGame.GameId) : string.Empty;

    public MainViewModel(ModService modService, FileService fileService,
        SettingsService settingsService, LoadOrderService loadOrderService,
        GameSwitchService gameSwitchService)
    {
        _modService = modService;
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

        if (!_settings.SetupCompleted)
        {
            NeedsFirstTimeSetup = true;
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
            bool created = _fileService.EnsureModsFolderStructure(ActiveGame.GameRootPath);
            if (created)
                ShowToast("📁 Mods folder structure created / verified.");

            var entries = _fileService.ScanModsLibrary(ActiveGame.ModLibraryPath);

            // Apply persisted load order from registry
            await _loadOrderService.ApplyRegistryToModsAsync(entries, ActiveRegistryDir);

            // Update conflict statuses
            _modService.RefreshStatuses(entries);

            // Rebuild observable collection
            Mods.Clear();
            foreach (var entry in entries.OrderBy(e => e.IsActive ? e.LoadOrder : int.MaxValue))
                Mods.Add(entry);

            RefreshConflictReport();
            UpdateStatusBar();
            HasPendingChanges = false;
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
            _loadOrderService.TryUndo(Mods.ToList());
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
    private void ShowAllModsView()
    {
        LoadOrderViewActive = false;
        _ = RefreshAsync();
    }

    // ── Conflict Panel ───────────────────────────────────────────────

    [RelayCommand]
    private void HideConflictPanel()
    {
        IsConflictPanelVisible = false;
    }

    // ── Folder Commands ──────────────────────────────────────────────

    [RelayCommand]
    private void OpenGameFolder()
    {
        if (ActiveGame is not null && Directory.Exists(ActiveGame.GameRootPath))
            Process.Start("explorer.exe", ActiveGame.GameRootPath);
    }

    [RelayCommand]
    private void OpenModsLibrary()
    {
        if (ActiveGame is not null && Directory.Exists(ActiveGame.ModLibraryPath))
            Process.Start("explorer.exe", ActiveGame.ModLibraryPath);
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        var settingsWindow = new Views.SettingsWindow(_settingsService);
        if (settingsWindow.ShowDialog() == true)
        {
            await ReloadSettingsAsync();
        }
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
        if (ActiveGame is null) return;
        IsBusy = true;

        try
        {
            var progress = new Progress<string>(msg => StatusText = msg);
            await _modService.ApplyAllModsAsync(Mods.ToList(), ActiveGame.GameRootPath, ActiveRegistryDir, progress);
            RefreshConflictReport();
            UpdateStatusBar();
            HasPendingChanges = false;
            ShowToast("🚀 Mods applied successfully!");
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
            IsBusy = false;
        }
    }

    /// <summary>
    /// Marks that in-memory state has changed and refreshes conflict / status UI.
    /// Does NOT touch the filesystem — call ApplyModsAsync to deploy.
    /// </summary>
    private void MarkPendingChanges()
    {
        _modService.RefreshStatuses(Mods.ToList());
        RefreshConflictReport();
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

        var sb = new StringBuilder();
        if (ActiveGame is not null)
            sb.Append($"🎮 {ActiveGame.ShortName} | ");
        sb.Append($"✅ {activeCount} mod(s) active");
        if (conflictCount > 0)
            sb.Append($" | ⚠️ {conflictCount} conflict(s) detected");

        StatusText = sb.ToString();
    }

    private void ShowToast(string message, bool isError = false)
    {
        if (NotificationPanel is not null)
            NotificationHelper.ShowToast(NotificationPanel, message, isError);
    }
}
