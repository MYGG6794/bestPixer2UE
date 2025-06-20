using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace bestPixer2UE.Utils
{
    /// <summary>
    /// Centralized logging service with log collection capabilities
    /// </summary>
    public class LoggingService
    {
        private static readonly string LogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        private static readonly string LogFileName = "bestPixer2UE.log";
        private static readonly string LogFilePath = Path.Combine(LogDirectory, LogFileName);

        /// <summary>
        /// Initialize logging configuration
        /// </summary>
        public static void Initialize(LogEventLevel logLevel = LogEventLevel.Information)
        {
            // Ensure log directory exists
            Directory.CreateDirectory(LogDirectory);

            // Configure Serilog
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Is(logLevel)
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithThreadId()
                .Enrich.WithMachineName()
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
                .WriteTo.File(
                    LogFilePath,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] [{ThreadId}] {Message:lj} {Properties:j}{NewLine}{Exception}",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    shared: true,
                    flushToDiskInterval: TimeSpan.FromSeconds(1))
                .CreateLogger();

            Log.Information("Logging initialized. Log directory: {LogDirectory}", LogDirectory);
        }

        /// <summary>
        /// Create a compressed log package for error reporting
        /// </summary>
        public static string CreateLogPackage(DateTime? fromDate = null, DateTime? toDate = null)
        {
            try
            {
                var packageDate = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var packageFileName = $"bestPixer2UE_logs_{packageDate}.zip";
                var packagePath = Path.Combine(LogDirectory, packageFileName);

                // Set default date range if not provided
                fromDate ??= DateTime.Now.AddDays(-7); // Last 7 days
                toDate ??= DateTime.Now;

                Log.Information("Creating log package: {PackagePath} for period {FromDate} to {ToDate}", 
                    packagePath, fromDate, toDate);

                using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);

                // Add log files within date range
                var logFiles = Directory.GetFiles(LogDirectory, "*.log*")
                    .Where(file => 
                    {
                        var fileInfo = new FileInfo(file);
                        return fileInfo.CreationTime >= fromDate && fileInfo.CreationTime <= toDate;
                    })
                    .ToArray();

                foreach (var logFile in logFiles)
                {
                    var fileName = Path.GetFileName(logFile);
                    archive.CreateEntryFromFile(logFile, fileName);
                    Log.Debug("Added log file to package: {FileName}", fileName);
                }

                // Add system information
                var systemInfoEntry = archive.CreateEntry("system-info.txt");
                using var systemInfoStream = systemInfoEntry.Open();
                using var writer = new StreamWriter(systemInfoStream);
                
                writer.WriteLine($"Package Created: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                writer.WriteLine($"Application: bestPixer2UE");
                writer.WriteLine($"Machine Name: {Environment.MachineName}");
                writer.WriteLine($"OS Version: {Environment.OSVersion}");
                writer.WriteLine($"CLR Version: {Environment.Version}");
                writer.WriteLine($"Working Directory: {Environment.CurrentDirectory}");
                writer.WriteLine($"Process ID: {Environment.ProcessId}");
                writer.WriteLine($"User: {Environment.UserName}");

                Log.Information("Log package created successfully: {PackagePath} ({FileCount} files)", 
                    packagePath, logFiles.Length);

                return packagePath;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to create log package");
                throw;
            }
        }

        /// <summary>
        /// Clean up old log files
        /// </summary>
        public static void CleanupOldLogs(int daysToKeep = 30)
        {
            try
            {
                var cutoffDate = DateTime.Now.AddDays(-daysToKeep);
                var logFiles = Directory.GetFiles(LogDirectory, "*.*")
                    .Where(file => new FileInfo(file).CreationTime < cutoffDate)
                    .ToArray();

                foreach (var file in logFiles)
                {
                    try
                    {
                        File.Delete(file);
                        Log.Debug("Deleted old log file: {FileName}", Path.GetFileName(file));
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Failed to delete old log file: {FileName}", Path.GetFileName(file));
                    }
                }

                if (logFiles.Length > 0)
                {
                    Log.Information("Cleaned up {Count} old log files", logFiles.Length);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during log cleanup");
            }
        }

        /// <summary>
        /// Get current log file size
        /// </summary>
        public static long GetCurrentLogSize()
        {
            try
            {
                if (File.Exists(LogFilePath))
                {
                    return new FileInfo(LogFilePath).Length;
                }
                return 0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Get log directory path
        /// </summary>
        public static string GetLogDirectory() => LogDirectory;

        /// <summary>
        /// Set log level dynamically
        /// </summary>
        public static void SetLogLevel(LogEventLevel logLevel)
        {
            var levelSwitch = new LoggingLevelSwitch(logLevel);
            
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.ControlledBy(levelSwitch)
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithThreadId()
                .Enrich.WithMachineName()
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
                .WriteTo.File(
                    LogFilePath,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] [{ThreadId}] {Message:lj} {Properties:j}{NewLine}{Exception}",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    shared: true,
                    flushToDiskInterval: TimeSpan.FromSeconds(1))
                .CreateLogger();

            Log.Information("Log level changed to: {LogLevel}", logLevel);
        }

        /// <summary>
        /// Get recent log entries from file
        /// </summary>
        public List<string> GetRecentLogs(int maxLines = 1000)
        {
            try
            {
                if (!File.Exists(LogFilePath))
                {
                    return new List<string>();
                }

                var lines = File.ReadAllLines(LogFilePath);
                var recentLines = lines.Skip(Math.Max(0, lines.Length - maxLines)).ToList();
                
                return recentLines;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to read recent logs");
                return new List<string> { $"Error reading logs: {ex.Message}" };
            }
        }

        /// <summary>
        /// Create log package (instance method for DI)
        /// </summary>
        public string CreateLogPackage()
        {
            return CreateLogPackage(DateTime.Now.AddDays(-7), DateTime.Now);
        }

        /// <summary>
        /// Collect system information for troubleshooting
        /// </summary>
        public Dictionary<string, string> CollectSystemInformation()
        {
            var systemInfo = new Dictionary<string, string>();

            try
            {
                systemInfo["操作系统"] = Environment.OSVersion.ToString();
                systemInfo["机器名称"] = Environment.MachineName;
                systemInfo["用户名"] = Environment.UserName;
                systemInfo["CPU 核心数"] = Environment.ProcessorCount.ToString();
                systemInfo["内存使用"] = $"{GC.GetTotalMemory(false) / 1024 / 1024} MB";
                systemInfo["工作目录"] = Environment.CurrentDirectory;
                systemInfo[".NET 版本"] = Environment.Version.ToString();
                systemInfo["运行时间"] = Environment.TickCount64.ToString() + " ms";
                
                // Add application-specific info
                systemInfo["应用程序版本"] = "2.0.0";
                systemInfo["构建日期"] = "2025-06-19";
                systemInfo["日志目录"] = LogDirectory;
                
                Log.Information("System information collected successfully");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to collect system information");
                systemInfo["错误"] = $"收集系统信息时发生错误: {ex.Message}";
            }

            return systemInfo;
        }
    }
}
