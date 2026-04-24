using System.Windows;
using EDF6ModLoaderWpf.Helpers;
using EDF6ModLoaderWpf.Services;
using EDF6ModLoaderWpf.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace EDF6ModLoaderWpf
{
    /// <summary>
    /// Application entry point — configures DI and launches the main window.
    /// </summary>
    public partial class App : Application
    {
        private static IServiceProvider _serviceProvider = null!;

        /// <summary>
        /// Resolves a service from the DI container.
        /// </summary>
        public static T GetService<T>() where T : notnull
            => _serviceProvider.GetRequiredService<T>();

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                // Build the DI container
                var services = new ServiceCollection();
                ConfigureServices(services);
                _serviceProvider = services.BuildServiceProvider();

                // Load saved font so it's stored for all windows
                var settingsService = _serviceProvider.GetRequiredService<SettingsService>();
                var settings = await settingsService.LoadAsync();
                FontHelper.ApplyFont(settings.FontFamily);

                // Create and show the main window
                var mainWindow = new MainWindow();
                FontHelper.ApplyCurrentFont(mainWindow);
                var viewModel = _serviceProvider.GetRequiredService<MainViewModel>();

                mainWindow.Show();
                await mainWindow.InitializeAsync(viewModel);
            }
            catch (Exception ex)
            {
                await SettingsService.LogErrorAsync(ex);
                MessageBox.Show(
                    $"The application failed to start:\n\n{ex.Message}",
                    "Startup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        /// <summary>
        /// Registers all services and view models with the DI container.
        /// </summary>
        private static void ConfigureServices(IServiceCollection services)
        {
            // Services (singleton — stateless utilities)
            services.AddSingleton<SettingsService>();
            services.AddSingleton<FileService>();
            services.AddSingleton<ConflictService>();
            services.AddSingleton<LoadOrderService>();
            services.AddSingleton<ModService>();
            services.AddSingleton<GameSwitchService>();

            // ViewModels (transient — one per window)
            services.AddTransient<MainViewModel>();
            services.AddTransient<SettingsViewModel>();
        }
    }
}
