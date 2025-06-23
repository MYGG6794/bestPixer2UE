using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using bestPixer2UE.Core;
using bestPixer2UE.Services;
using bestPixer2UE.Utils;
using Serilog;

namespace bestPixer2UE
{
    /// <summary>
    /// Main application entry point
    /// </summary>
    public partial class App : Application
    {
        private IHost? _host;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Initialize logging
            LoggingService.Initialize();
            
            Log.Information("Application starting up...");            // Build host
            _host = Host.CreateDefaultBuilder()                .ConfigureServices((context, services) =>
                {
                    // Register services
                    services.AddSingleton<bestPixer2UE.Core.ConfigurationManager>();
                    services.AddSingleton<ProcessManager>();
                    services.AddSingleton<LoggingService>();
                    services.AddSingleton<MonitoringService>();
                    services.AddSingleton<PeerStreamEnterpriseService>();
                    services.AddSingleton<StreamingManagementService>();
                    services.AddSingleton<UEControlService>();
                    services.AddSingleton<MultiPortService>();
                    
                    // Register main window
                    services.AddTransient<MainWindow>();
                })
                .Build();

            try
            {
                _host.Start();
                
                // Show main window
                var mainWindow = _host.Services.GetRequiredService<MainWindow>();
                mainWindow.Show();
                
                Log.Information("Application started successfully");
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application failed to start");
                MessageBox.Show($"Application failed to start: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(1);
            }

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                Log.Information("Application shutting down...");
                
                // Cleanup services
                var processManager = _host?.Services.GetService<ProcessManager>();
                processManager?.CleanupAllProcesses();
                
                var multiPortService = _host?.Services.GetService<MultiPortService>();
                multiPortService?.StopAllServices();
                
                _host?.StopAsync().Wait(TimeSpan.FromSeconds(10));
                _host?.Dispose();
                
                Log.Information("Application shutdown completed");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during application shutdown");
            }
            finally
            {
                Log.CloseAndFlush();
            }

            base.OnExit(e);
        }
    }
}
