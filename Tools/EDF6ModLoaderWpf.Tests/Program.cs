using EDF6ModLoaderWpf.Models;
using EDF6ModLoaderWpf.Services;
using EDF6ModLoaderWpf.ViewModels;

var tests = new (string Name, Action Test)[]
{
    ("ToggleConflictPanelCommand shows hidden radar when conflicts exist", ToggleConflictPanelCommandShowsHiddenRadarWhenConflictsExist),
    ("ToggleConflictPanelCommand hides visible radar when conflicts exist", ToggleConflictPanelCommandHidesVisibleRadarWhenConflictsExist),
    ("ToggleConflictPanelCommand cannot run without conflicts", ToggleConflictPanelCommandCannotRunWithoutConflicts)
};

var failed = 0;
foreach (var (name, test) in tests)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"FAIL {name}");
        Console.WriteLine(ex.Message);
    }
}

return failed == 0 ? 0 : 1;

static void ToggleConflictPanelCommandShowsHiddenRadarWhenConflictsExist()
{
    var viewModel = CreateViewModelWithConflict();
    viewModel.IsConflictPanelVisible = false;

    Require(viewModel.ToggleConflictPanelCommand.CanExecute(null), "Expected toggle command to be enabled with conflict report items.");
    viewModel.ToggleConflictPanelCommand.Execute(null);

    Require(viewModel.IsConflictPanelVisible, "Expected conflict radar to be visible after toggling from hidden.");
}

static void ToggleConflictPanelCommandHidesVisibleRadarWhenConflictsExist()
{
    var viewModel = CreateViewModelWithConflict();
    viewModel.IsConflictPanelVisible = true;

    Require(viewModel.ToggleConflictPanelCommand.CanExecute(null), "Expected toggle command to be enabled with conflict report items.");
    viewModel.ToggleConflictPanelCommand.Execute(null);

    Require(!viewModel.IsConflictPanelVisible, "Expected conflict radar to be hidden after toggling from visible.");
}

static void ToggleConflictPanelCommandCannotRunWithoutConflicts()
{
    var viewModel = CreateViewModel();
    viewModel.IsConflictPanelVisible = false;

    Require(!viewModel.ToggleConflictPanelCommand.CanExecute(null), "Expected toggle command to be disabled without conflict report items.");
    viewModel.ToggleConflictPanelCommand.Execute(null);

    Require(!viewModel.IsConflictPanelVisible, "Expected conflict radar to remain hidden without conflicts.");
}

static MainViewModel CreateViewModelWithConflict()
{
    var viewModel = CreateViewModel();
    viewModel.ConflictReport.Add(new ConflictInfo
    {
        FileName = "weapon_data.bin",
        SubFolder = "Weapon",
        RelativePath = "Weapon/weapon_data.bin",
        WinnerModName = "Winner Mod",
        LoserModName = "Loser Mod",
        WinnerLoadOrder = 2,
        LoserLoadOrder = 1
    });

    return viewModel;
}

static MainViewModel CreateViewModel()
{
    var fileService = new FileService();
    var backupService = new BackupService(fileService);
    var conflictService = new ConflictService();
    var loadOrderService = new LoadOrderService(fileService);
    var modService = new ModService(backupService, fileService, conflictService, loadOrderService);
    var settingsService = new SettingsService();
    var gameSwitchService = new GameSwitchService(settingsService);

    return new MainViewModel(modService, backupService, fileService, settingsService, loadOrderService, gameSwitchService);
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
