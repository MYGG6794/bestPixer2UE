using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using bestPixer2UE.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace bestPixer2UE.Services
{
    /// <summary>
    /// PeerStreamEnterprise 服务管理器
    /// 负责启动和管理 signal.js 和 execue.js 进程
    /// </summary>
    public class PeerStreamEnterpriseService
    {
        private readonly ConfigurationManager _configManager;
        private readonly ProcessManager _processManager;
        
        private Process? _signalProcess;
        private Process? _execueProcess;
        private bool _isRunning = false;

        public event EventHandler<string>? ServiceStarted;
        public event EventHandler<string>? ServiceStopped;
        public event EventHandler<string>? ServiceError;
        public event EventHandler<string>? LogMessage;

        public PeerStreamEnterpriseService(ConfigurationManager configManager, ProcessManager processManager)
        {
            _configManager = configManager;
            _processManager = processManager;
        }

        /// <summary>
        /// 检查是否正在运行
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// 获取信令服务器URL
        /// </summary>
        public string GetSignalingServerUrl()
        {
            var config = _configManager.Configuration;
            return $"http://127.0.0.1:{config.WebRTCPort}";
        }

        /// <summary>
        /// 启动PeerStreamEnterprise服务
        /// </summary>
        public async Task<bool> StartAsync()
        {
            try
            {
                if (_isRunning)
                {
                    Log.Warning("PeerStreamEnterprise service is already running");
                    return true;
                }

                var peerStreamPath = GetPeerStreamPath();
                if (!Directory.Exists(peerStreamPath))
                {
                    throw new DirectoryNotFoundException($"PeerStreamEnterprise directory not found: {peerStreamPath}");
                }

                // 确保配置文件存在并正确配置
                await EnsureConfigurationFiles();

                // 启动signal.js
                if (!await StartSignalService())
                {
                    return false;
                }

                // 等待signal服务启动
                await Task.Delay(2000);

                // 启动execue.js
                if (!await StartExecueService())
                {
                    await StopSignalService();
                    return false;
                }

                _isRunning = true;
                ServiceStarted?.Invoke(this, "PeerStreamEnterprise services started successfully");
                Log.Information("PeerStreamEnterprise services started successfully");
                
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to start PeerStreamEnterprise services");
                ServiceError?.Invoke(this, ex.Message);
                await StopAsync();
                return false;
            }
        }

        /// <summary>
        /// 停止PeerStreamEnterprise服务
        /// </summary>
        public async Task StopAsync()
        {
            try
            {
                Log.Information("Stopping PeerStreamEnterprise services...");

                await StopExecueService();
                await StopSignalService();

                _isRunning = false;
                ServiceStopped?.Invoke(this, "PeerStreamEnterprise services stopped");
                Log.Information("PeerStreamEnterprise services stopped successfully");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error stopping PeerStreamEnterprise services");
                ServiceError?.Invoke(this, ex.Message);
            }
        }        /// <summary>
        /// 启动signal.js服务
        /// </summary>
        private Task<bool> StartSignalService()
        {
            try
            {
                var peerStreamPath = GetPeerStreamPath();
                var signalPath = Path.Combine(peerStreamPath, "signal.js");

                if (!File.Exists(signalPath))
                {
                    throw new FileNotFoundException($"signal.js not found: {signalPath}");
                }

                _signalProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "node",
                        Arguments = "signal.js",
                        WorkingDirectory = peerStreamPath,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                _signalProcess.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Log.Information("[Signal] {Message}", e.Data);
                        LogMessage?.Invoke(this, $"[Signal] {e.Data}");
                    }
                };

                _signalProcess.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Log.Warning("[Signal Error] {Message}", e.Data);
                        LogMessage?.Invoke(this, $"[Signal Error] {e.Data}");
                    }
                };

                _signalProcess.Start();
                _signalProcess.BeginOutputReadLine();
                _signalProcess.BeginErrorReadLine();

                Log.Information("Signal service started with PID: {ProcessId}", _signalProcess.Id);
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to start signal service");
                return Task.FromResult(false);
            }
        }        /// <summary>
        /// 启动execue.js服务
        /// </summary>
        private Task<bool> StartExecueService()
        {
            try
            {
                var peerStreamPath = GetPeerStreamPath();
                var execuePath = Path.Combine(peerStreamPath, "execue.js");

                if (!File.Exists(execuePath))
                {
                    throw new FileNotFoundException($"execue.js not found: {execuePath}");
                }

                _execueProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "node",
                        Arguments = "execue.js",
                        WorkingDirectory = peerStreamPath,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                _execueProcess.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Log.Information("[Execue] {Message}", e.Data);
                        LogMessage?.Invoke(this, $"[Execue] {e.Data}");
                    }
                };

                _execueProcess.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Log.Warning("[Execue Error] {Message}", e.Data);
                        LogMessage?.Invoke(this, $"[Execue Error] {e.Data}");
                    }
                };

                _execueProcess.Start();
                _execueProcess.BeginOutputReadLine();
                _execueProcess.BeginErrorReadLine();

                Log.Information("Execue service started with PID: {ProcessId}", _execueProcess.Id);
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to start execue service");
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// 停止signal服务
        /// </summary>
        private async Task StopSignalService()
        {
            if (_signalProcess != null && !_signalProcess.HasExited)
            {
                try
                {
                    _signalProcess.Kill();
                    await _signalProcess.WaitForExitAsync();
                    Log.Information("Signal service stopped");
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error stopping signal service");
                }
                finally
                {
                    _signalProcess?.Dispose();
                    _signalProcess = null;
                }
            }
        }

        /// <summary>
        /// 停止execue服务
        /// </summary>
        private async Task StopExecueService()
        {
            if (_execueProcess != null && !_execueProcess.HasExited)
            {
                try
                {
                    _execueProcess.Kill();
                    await _execueProcess.WaitForExitAsync();
                    Log.Information("Execue service stopped");
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error stopping execue service");
                }
                finally
                {
                    _execueProcess?.Dispose();
                    _execueProcess = null;
                }
            }
        }

        /// <summary>
        /// 获取PeerStreamEnterprise目录路径
        /// </summary>
        private string GetPeerStreamPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PeerStreamEnterprise-main");
        }

        /// <summary>
        /// 确保配置文件存在并正确配置
        /// </summary>
        private async Task EnsureConfigurationFiles()
        {
            var peerStreamPath = GetPeerStreamPath();
            var config = _configManager.Configuration;

            // 配置signal.json
            await ConfigureSignalJson(peerStreamPath, config);
            
            // 配置execue.json
            await ConfigureExecueJson(peerStreamPath, config);
        }

        /// <summary>
        /// 配置signal.json文件
        /// </summary>
        private async Task ConfigureSignalJson(string peerStreamPath, AppConfiguration config)
        {
            var signalJsonPath = Path.Combine(peerStreamPath, "signal.json");
            
            // 读取现有配置或创建默认配置
            var signalConfig = new
            {
                PORT = config.WebRTCPort,
                auth = false,
                userpwd = "admin:dd2f757773f1fb6c690f3c1305c739bc4e8f35fd3e9eb69c4cdeb98d716f7eec",
                apiCors = false,
                exeUeCoolTime = 60,
                idleReleaseTime = 120,
                preloadReleaseTime = 15000,
                mouseReleaseTime = 0,
                SignalIp = "127.0.0.1",
                globlesetting = new
                {
                    WebRTCFps = config.TargetFPS,
                    ResX = config.ResolutionX,
                    ResY = config.ResolutionY,
                    Unattended = true,
                    RenderOffScreen = true,
                    AudioMixer = true
                },
                machine = new[]
                {
                    new
                    {
                        ip = "127.0.0.1",
                        gpu = new[]
                        {
                            new { gpucard = 0, gpumemory = 16 }
                        }
                    }
                },
                ueprogram = new[]
                {
                    new
                    {
                        name = "bestPixer2UE",
                        path = config.UEExecutablePath,
                        urlprefix = "main",
                        gpumemory = 8,
                        preload = false,
                        param = ""
                    }
                },
                iceServers = new[]
                {
                    new
                    {
                        urls = new[] { "stun:stun.l.google.com:19302" },
                        username = "1",
                        credential = "1"
                    }
                }
            };

            var json = System.Text.Json.JsonSerializer.Serialize(signalConfig, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(signalJsonPath, json);
            Log.Information("Updated signal.json configuration");
        }

        /// <summary>
        /// 配置execue.json文件
        /// </summary>
        private async Task ConfigureExecueJson(string peerStreamPath, AppConfiguration config)
        {
            var execueJsonPath = Path.Combine(peerStreamPath, "execue.json");
            
            var execueConfig = new
            {
                signalPort = config.WebRTCPort,
                signalIp = "127.0.0.1",
                execueIp = "127.0.0.1"
            };

            var json = System.Text.Json.JsonSerializer.Serialize(execueConfig, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(execueJsonPath, json);
            Log.Information("Updated execue.json configuration");
        }

        /// <summary>
        /// 检查Node.js是否已安装
        /// </summary>
        public static async Task<bool> CheckNodeJsInstalled()
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
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                return process.ExitCode == 0 && !string.IsNullOrEmpty(output);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 动态配置UE程序信息
        /// </summary>
        public class UEProgramConfig
        {
            public string Name { get; set; } = "";
            public string Path { get; set; } = "";
            public string UrlPrefix { get; set; } = "";
            public int GpuMemory { get; set; } = 8;
            public bool Preload { get; set; } = false;
            public string Param { get; set; } = "";
        }

        /// <summary>
        /// 获取当前UE程序配置列表
        /// </summary>
        public async Task<List<UEProgramConfig>> GetUEProgramsAsync()
        {
            try
            {
                var peerStreamPath = GetPeerStreamPath();
                var configPath = Path.Combine(peerStreamPath, "signal.json");
                
                if (!File.Exists(configPath))
                {
                    return new List<UEProgramConfig>();
                }

                var configContent = await File.ReadAllTextAsync(configPath);
                var config = JsonConvert.DeserializeObject<dynamic>(configContent);
                
                var programs = new List<UEProgramConfig>();
                if (config?.ueprogram != null)
                {
                    foreach (var program in config.ueprogram)
                    {
                        programs.Add(new UEProgramConfig
                        {
                            Name = program.name?.ToString() ?? "",
                            Path = program.path?.ToString() ?? "",
                            UrlPrefix = program.urlprefix?.ToString() ?? "",
                            GpuMemory = int.TryParse(program.gpumemory?.ToString(), out int gpu) ? gpu : 8,
                            Preload = bool.TryParse(program.preload?.ToString(), out bool preload) ? preload : false,
                            Param = program.param?.ToString() ?? ""
                        });
                    }
                }
                
                return programs;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to get UE programs configuration");
                return new List<UEProgramConfig>();
            }
        }

        /// <summary>
        /// 更新UE程序配置并重启服务
        /// </summary>
        public async Task<bool> UpdateUEProgramsAsync(List<UEProgramConfig> programs)
        {
            try
            {
                var peerStreamPath = GetPeerStreamPath();
                var configPath = Path.Combine(peerStreamPath, "signal.json");
                  // 读取现有配置
                var configContent = await File.ReadAllTextAsync(configPath);
                var config = JsonConvert.DeserializeObject<JObject>(configContent);
                
                if (config == null)
                {
                    Log.Error("Failed to parse configuration file");
                    return false;
                }
                
                // 更新ueprogram部分
                var programArray = programs.Select(p => new
                {
                    name = p.Name,
                    path = p.Path,
                    urlprefix = p.UrlPrefix,
                    gpumemory = p.GpuMemory,
                    preload = p.Preload,
                    param = p.Param
                }).ToArray();
                
                config["ueprogram"] = JArray.FromObject(programArray);
                
                // 保存配置
                var updatedConfig = JsonConvert.SerializeObject(config, Formatting.Indented);
                await File.WriteAllTextAsync(configPath, updatedConfig);
                
                Log.Information("UE programs configuration updated successfully");
                
                // 如果服务正在运行，重启以应用新配置
                if (_isRunning)
                {
                    Log.Information("Restarting PeerStreamEnterprise services to apply new configuration...");
                    await StopAsync();
                    await Task.Delay(2000); // 等待完全停止
                    return await StartAsync();
                }
                
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to update UE programs configuration");
                ServiceError?.Invoke(this, $"配置更新失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 添加单个UE程序配置
        /// </summary>
        public async Task<bool> AddUEProgramAsync(UEProgramConfig program)
        {
            var programs = await GetUEProgramsAsync();
            
            // 检查是否已存在相同的urlprefix
            if (programs.Any(p => p.UrlPrefix.Equals(program.UrlPrefix, StringComparison.OrdinalIgnoreCase)))
            {
                ServiceError?.Invoke(this, $"URL前缀 '{program.UrlPrefix}' 已存在");
                return false;
            }
            
            programs.Add(program);
            return await UpdateUEProgramsAsync(programs);
        }

        /// <summary>
        /// 删除UE程序配置
        /// </summary>
        public async Task<bool> RemoveUEProgramAsync(string urlPrefix)
        {
            var programs = await GetUEProgramsAsync();
            var programToRemove = programs.FirstOrDefault(p => p.UrlPrefix.Equals(urlPrefix, StringComparison.OrdinalIgnoreCase));
            
            if (programToRemove == null)
            {
                ServiceError?.Invoke(this, $"未找到URL前缀为 '{urlPrefix}' 的程序配置");
                return false;
            }
            
            programs.Remove(programToRemove);
            return await UpdateUEProgramsAsync(programs);
        }

        /// <summary>
        /// 生成完整的signal.json配置文件
        /// </summary>
        private async Task GenerateSignalConfigAsync(string peerStreamPath)
        {
            try
            {
                var config = _configManager.Configuration;
                var configPath = Path.Combine(peerStreamPath, "signal.json");
                
                // 获取当前UE程序配置
                var uePrograms = await GetUEProgramsAsync();
                
                var signalConfig = new
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
                    ueprogram = uePrograms.Select(p => new
                    {
                        name = p.Name,
                        path = p.Path,
                        urlprefix = p.UrlPrefix,
                        gpumemory = p.GpuMemory,
                        preload = p.Preload,
                        param = p.Param
                    }).ToArray(),
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
                
                var jsonContent = JsonConvert.SerializeObject(signalConfig, Formatting.Indented);
                await File.WriteAllTextAsync(configPath, jsonContent);
                
                Log.Information("Generated signal.json configuration file");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to generate signal.json configuration");
                throw;
            }
        }

        /// <summary>
        /// 从WPF配置同步到PeerStreamEnterprise配置
        /// </summary>
        public async Task<bool> SyncConfigurationAsync()
        {
            try
            {
                var peerStreamPath = GetPeerStreamPath();
                await GenerateSignalConfigAsync(peerStreamPath);
                
                // 如果服务正在运行，重启以应用新配置
                if (_isRunning)
                {
                    Log.Information("Restarting PeerStreamEnterprise services to apply configuration changes...");
                    await StopAsync();
                    await Task.Delay(2000);
                    return await StartAsync();
                }
                
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to sync configuration");
                ServiceError?.Invoke(this, $"配置同步失败: {ex.Message}");
                return false;
            }
        }
    }
}
