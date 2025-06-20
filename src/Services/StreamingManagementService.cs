using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using bestPixer2UE.Core;
using Newtonsoft.Json;

namespace bestPixer2UE.Services
{    /// <summary>
    /// 统一的流媒体管理服务
    /// 整合PeerStreamEnterprise和配置管理
    /// </summary>
    public class StreamingManagementService
    {
        private readonly ConfigurationManager _configManager;
        private readonly ProcessManager _processManager;
        private readonly PeerStreamEnterpriseService _peerStreamService;
        private readonly UEControlService _ueControlService;
        
        private bool _isInitialized = false;

        public StreamingManagementService(
            ConfigurationManager configManager, 
            ProcessManager processManager,
            PeerStreamEnterpriseService peerStreamService,
            UEControlService ueControlService)
        {
            _configManager = configManager;
            _processManager = processManager;
            _peerStreamService = peerStreamService;
            _ueControlService = ueControlService;

            // 订阅PeerStreamEnterprise事件
            _peerStreamService.ServiceStarted += OnPeerStreamStarted;
            _peerStreamService.ServiceStopped += OnPeerStreamStopped;
            _peerStreamService.ServiceError += OnPeerStreamError;
        }

        public event EventHandler<string>? ServiceStatusChanged;
        public event EventHandler<string>? ServiceError;
        public event EventHandler<string>? LogMessage;

        /// <summary>
        /// 检查服务是否运行
        /// </summary>
        public bool IsRunning => _peerStreamService.IsRunning;

        /// <summary>
        /// 获取服务状态信息
        /// </summary>
        public ServiceStatus GetServiceStatus()
        {
            var config = _configManager.Configuration;
            
            return new ServiceStatus
            {
                IsRunning = _peerStreamService.IsRunning,
                SignalingPort = config.PORT,
                SignalingUrl = $"http://127.0.0.1:{config.PORT}",
                WebSocketUrl = $"ws://127.0.0.1:{config.PORT}",
                IsNodeJsAvailable = CheckNodeJsAvailability(),
                PeerStreamEnterprisePath = GetPeerStreamPath(),
                ConfigStatus = "Ready",
                LastStartTime = DateTime.Now
            };
        }

        /// <summary>
        /// 启动完整的流媒体服务栈
        /// </summary>
        public async Task<bool> StartAsync()
        {
            try
            {
                Log.Information("Starting streaming management service...");

                // 1. 检查环境依赖
                if (!await CheckEnvironmentAsync())
                {
                    return false;
                }

                // 2. 更新配置文件
                await UpdatePeerStreamConfigurationAsync();

                // 3. 启动PeerStreamEnterprise服务
                var success = await _peerStreamService.StartAsync();
                
                if (success)
                {
                    _isInitialized = true;
                    ServiceStatusChanged?.Invoke(this, "Streaming services started successfully");
                    Log.Information("Streaming management service started successfully");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to start streaming management service");
                ServiceError?.Invoke(this, ex.Message);
                return false;
            }
        }        /// <summary>
        /// 停止流媒体服务
        /// </summary>
        public async Task StopAsync()
        {
            try
            {
                Log.Information("Stopping streaming management service...");
                
                // Stop UE process first if it's running
                if (_ueControlService != null)
                {
                    Log.Information("Stopping UE control service...");
                    await _ueControlService.StopAsync();
                    
                    // Additional safety: Complete UE process cleanup
                    Log.Information("Performing complete UE process cleanup...");
                    var cleanedCount = _processManager.CompleteUEProcessCleanup();
                    if (cleanedCount > 0)
                    {
                        Log.Information("Cleaned up {CleanedCount} UE processes", cleanedCount);
                    }
                }
                
                // Then stop PeerStreamEnterprise
                await _peerStreamService.StopAsync();
                
                _isInitialized = false;
                ServiceStatusChanged?.Invoke(this, "Streaming services stopped");
                Log.Information("Streaming management service stopped successfully");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error stopping streaming management service");
                ServiceError?.Invoke(this, ex.Message);
                
                // Final nuclear option: Kill all UE processes even if there was an error
                try
                {
                    Log.Warning("Attempting nuclear UE cleanup due to error...");
                    var killedCount = _processManager.KillAllUEProcessesByName();
                    Log.Warning("Nuclear cleanup killed {KilledCount} processes", killedCount);
                }
                catch (Exception nuclearEx)
                {
                    Log.Error(nuclearEx, "Failed to perform nuclear UE cleanup");
                }
            }
        }/// <summary>
        /// 检查环境依赖
        /// </summary>
        private Task<bool> CheckEnvironmentAsync()
        {
            // 检查Node.js
            if (!CheckNodeJsAvailability())
            {
                var message = "Node.js not found. Please install Node.js to use PeerStreamEnterprise.";
                Log.Error(message);
                ServiceError?.Invoke(this, message);
                return Task.FromResult(false);
            }

            // 检查PeerStreamEnterprise文件
            var peerStreamPath = GetPeerStreamPath();
            if (!Directory.Exists(peerStreamPath))
            {
                var message = $"PeerStreamEnterprise directory not found: {peerStreamPath}";
                Log.Error(message);
                ServiceError?.Invoke(this, message);
                return Task.FromResult(false);
            }

            var signalJsPath = Path.Combine(peerStreamPath, "signal.js");
            var execueJsPath = Path.Combine(peerStreamPath, "execue.js");
            
            if (!File.Exists(signalJsPath) || !File.Exists(execueJsPath))
            {
                var message = "PeerStreamEnterprise files (signal.js, execue.js) not found.";
                Log.Error(message);
                ServiceError?.Invoke(this, message);
                return Task.FromResult(false);
            }

            return Task.FromResult(true);
        }

        /// <summary>
        /// 更新PeerStreamEnterprise配置文件
        /// </summary>
        private async Task UpdatePeerStreamConfigurationAsync()
        {
            try
            {
                var config = _configManager.Configuration;
                var peerStreamPath = GetPeerStreamPath();
                var signalConfigPath = Path.Combine(peerStreamPath, "signal.json");

                // 构建PeerStreamEnterprise配置
                var peerStreamConfig = new
                {
                    PORT = config.PORT,
                    auth = config.Auth,
                    userpwd = config.UserPassword,
                    apiCors = config.ApiCors,
                    exeUeCoolTime = config.ExeUeCoolTime,
                    idleReleaseTime = config.IdleReleaseTime,
                    preloadReleaseTime = config.PreloadReleaseTime,
                    mouseReleaseTime = config.MouseReleaseTime,
                    SignalIp = config.SignalIp,
                    globlesetting = new
                    {
                        WebRTCFps = config.WebRTCFps,
                        ResX = config.ResolutionX,
                        ResY = config.ResolutionY,
                        Unattended = config.Unattended,
                        RenderOffScreen = config.RenderOffScreen,
                        AudioMixer = config.AudioMixer
                    },
                    machine = new[]
                    {
                        new
                        {
                            ip = config.MachineIp,
                            gpu = new[]
                            {
                                new
                                {
                                    gpucard = config.GpuCard,
                                    gpumemory = config.GpuMemory
                                }
                            }
                        }
                    },
                    ueprogram = new[]
                    {
                        new
                        {
                            name = "bestPixer2UE",
                            path = config.UEExecutablePath ?? "",
                            urlprefix = "main",
                            gpumemory = Math.Min(config.GpuMemory - 2, 8), // 保留一些GPU内存
                            preload = false,
                            param = BuildUEParameters(config)
                        }
                    },
                    iceServers = new[]
                    {
                        new
                        {
                            urls = new[] { config.StunServer },
                            username = config.IceUsername,
                            credential = config.IceCredential
                        }
                    }
                };

                var json = JsonConvert.SerializeObject(peerStreamConfig, Formatting.Indented);
                await File.WriteAllTextAsync(signalConfigPath, json);
                
                Log.Information("Updated PeerStreamEnterprise configuration: {ConfigPath}", signalConfigPath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to update PeerStreamEnterprise configuration");
                throw;
            }
        }

        /// <summary>
        /// 构建UE启动参数
        /// </summary>
        private string BuildUEParameters(AppConfiguration config)
        {
            var parameters = new List<string>();
            
            if (!string.IsNullOrEmpty(config.LastUEProjectPath))
            {
                parameters.Add($"\"{config.LastUEProjectPath}\"");
            }
            
            // 基础参数会由PeerStreamEnterprise自动添加
            // 这里只添加额外的自定义参数
            parameters.Add("-log");
            
            return string.Join(" ", parameters);
        }

        /// <summary>
        /// 检查Node.js可用性
        /// </summary>
        private bool CheckNodeJsAvailability()
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "node",
                        Arguments = "--version",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                
                process.Start();
                process.WaitForExit();
                
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 获取PeerStreamEnterprise路径
        /// </summary>
        private string GetPeerStreamPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PeerStreamEnterprise-main");
        }        /// <summary>
        /// 检查是否有UE进程在运行
        /// </summary>
        public bool HasRunningUEProcesses()
        {
            return _processManager.GetRunningUEProcesses().Any();
        }

        /// <summary>
        /// 获取运行中的UE进程数量
        /// </summary>
        public int GetRunningUEProcessCount()
        {
            return _processManager.GetRunningUEProcesses().Count;
        }

        private void OnPeerStreamStarted(object? sender, string message)
        {
            ServiceStatusChanged?.Invoke(this, $"PeerStreamEnterprise: {message}");
            LogMessage?.Invoke(this, $"[PeerStream] {message}");
        }

        private void OnPeerStreamStopped(object? sender, string message)
        {
            ServiceStatusChanged?.Invoke(this, $"PeerStreamEnterprise: {message}");
            LogMessage?.Invoke(this, $"[PeerStream] {message}");
        }

        private void OnPeerStreamError(object? sender, string message)
        {
            ServiceError?.Invoke(this, $"PeerStreamEnterprise Error: {message}");
            LogMessage?.Invoke(this, $"[PeerStream Error] {message}");
        }
    }

    /// <summary>
    /// 服务状态信息
    /// </summary>
    public class ServiceStatus
    {
        public bool IsRunning { get; set; }
        public int SignalingPort { get; set; }
        public string SignalingUrl { get; set; } = "";
        public string WebSocketUrl { get; set; } = "";
        public bool IsNodeJsAvailable { get; set; }
        public string PeerStreamEnterprisePath { get; set; } = "";
        public string ConfigStatus { get; set; } = "";
        public DateTime LastStartTime { get; set; }
    }
}
