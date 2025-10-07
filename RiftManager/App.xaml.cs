using System;
using System.Net.Http;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using RiftManager.Interfaces;
using RiftManager.Services;
using RiftManager.Utils;

namespace RiftManager
{
    public partial class App : Application
    {
        public static IServiceProvider ServiceProvider { get; private set; }

        public App()
        {
            ServiceProvider = ConfigureServices();
        }

        private IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            // Add services to the container
            services.AddSingleton<LogService>();
            services.AddSingleton<HttpClient>();
            services.AddSingleton<DirectoriesCreator>();

            // Services with interfaces or specific logic
            services.AddTransient<NavigationParser>();
            services.AddTransient<DetailPageParser>();
            services.AddTransient<CatalogParser>();
            services.AddTransient<JsonFetcherService>();
            services.AddTransient<WebScraper>();
            services.AddTransient<AssetDownloader>();
            services.AddTransient<BundleService>();
            services.AddTransient<RiotAudioLoader>();
            services.AddSingleton<RiotClientManifestService>();
            services.AddTransient<EmbedAssetScraperService>();
            services.AddTransient<EventCoordinatorService>();
            services.AddTransient<EventProcessor>();

            // Register MainWindow
            services.AddTransient<MainWindow>();

            return services.BuildServiceProvider();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var logService = ServiceProvider.GetRequiredService<LogService>();
            logService.Log("Application starting up.");

            // Setup global exception handlers
            AppDomain.CurrentDomain.UnhandledException += (sender, args) => LogUnhandledException(ServiceProvider.GetRequiredService<LogService>(), (Exception)args.ExceptionObject, "AppDomain");
            
            this.DispatcherUnhandledException += (sender, args) => 
            {
                LogUnhandledException(ServiceProvider.GetRequiredService<LogService>(), args.Exception, "Dispatcher");
                args.Handled = true; // Prevent application crash
            };

            var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }

        private void LogUnhandledException(LogService logger, Exception ex, string context)
        {
            logger.LogError($"[CRITICAL UNHANDLED EXCEPTION - {context}] Message: {ex.Message}\nStackTrace: {ex.StackTrace}");
            MessageBox.Show($"An unhandled application error occurred in {context}: {ex.Message}\nSee application.log for details.", "Application Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Environment.Exit(1); // Forcefully terminate
        }

        protected override void OnExit(ExitEventArgs e)
        {
            var logService = ServiceProvider?.GetService<LogService>();
            logService?.Log("Application exiting.");
            logService?.Dispose(); // Ensure logs are flushed and resources released

            base.OnExit(e);
        }
    }
}
