using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Serilog;

namespace bestPixer2UE.Core
{    /// <summary>
    /// Application configuration model - 包含完整的PeerStreamEnterprise配置
    /// </summary>
    public class AppConfiguration
    {
        // WPF应用基础配置
        public string UEExecutablePath { get; set; } = "";
        public int WebRTCPort { get; set; } = 11188;  // 匹配PeerStreamEnterprise默认端口
        public int UEWebSocketPort { get; set; } = 8081;
        public int ControlAPIPort { get; set; } = 8082;
        public string LogLevel { get; set; } = "Information";
        public bool AutoStartServices { get; set; } = false;
        public string LastUEProjectPath { get; set; } = "";
        public bool EnableDetailedLogging { get; set; } = true;
        public int ProcessCleanupTimeoutMs { get; set; } = 10000;
        
        // PeerStreamEnterprise 主要配置
        public int PORT { get; set; } = 11188;
        public bool Auth { get; set; } = false;
        public string UserPassword { get; set; } = "admin:dd2f757773f1fb6c690f3c1305c739bc4e8f35fd3e9eb69c4cdeb98d716f7eec";
        public bool ApiCors { get; set; } = false;
        public int ExeUeCoolTime { get; set; } = 60;
        public int IdleReleaseTime { get; set; } = 120;
        public int PreloadReleaseTime { get; set; } = 15000;
        public int MouseReleaseTime { get; set; } = 0;
        public string SignalIp { get; set; } = "127.0.0.1";
        
        // 全局像素流设置
        public int WebRTCFps { get; set; } = 30;
        public int ResolutionX { get; set; } = 1920;
        public int ResolutionY { get; set; } = 1080;
        public bool Unattended { get; set; } = true;
        public bool RenderOffScreen { get; set; } = true;
        public bool AudioMixer { get; set; } = true;
        
        // 机器配置
        public string MachineIp { get; set; } = "127.0.0.1";
        public int GpuCard { get; set; } = 0;
        public int GpuMemory { get; set; } = 16;
        
        // ICE服务器配置
        public string StunServer { get; set; } = "stun:stun.l.google.com:19302";
        public string IceUsername { get; set; } = "1";
        public string IceCredential { get; set; } = "1";
        
        // 兼容性配置
        public bool EnablePixelStreaming { get; set; } = true;
        public string SignalingServerUrl { get; set; } = "ws://127.0.0.1:11188";  // 匹配PeerStreamEnterprise
        public int TargetFPS { get; set; } = 30;
    }

    /// <summary>
    /// Manages application configuration
    /// </summary>
    public class ConfigurationManager
    {
        private readonly string _configFilePath;
        private AppConfiguration _configuration = new();

        public ConfigurationManager()
        {
            var configDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config");
            Directory.CreateDirectory(configDir);
            _configFilePath = Path.Combine(configDir, "app-config.json");
            
            LoadConfiguration();
        }

        public AppConfiguration Configuration => _configuration;

        /// <summary>
        /// Load configuration from file or create default
        /// </summary>
        private void LoadConfiguration()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    var json = File.ReadAllText(_configFilePath);
                    _configuration = JsonConvert.DeserializeObject<AppConfiguration>(json) ?? new AppConfiguration();
                    Log.Information("Configuration loaded from {FilePath}", _configFilePath);
                }
                else
                {
                    _configuration = new AppConfiguration();
                    SaveConfiguration();
                    Log.Information("Created default configuration at {FilePath}", _configFilePath);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load configuration, using defaults");
                _configuration = new AppConfiguration();
            }
        }

        /// <summary>
        /// Save current configuration to file
        /// </summary>
        public void SaveConfiguration()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_configuration, Formatting.Indented);
                File.WriteAllText(_configFilePath, json);
                Log.Information("Configuration saved to {FilePath}", _configFilePath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save configuration");
                throw;
            }
        }

        /// <summary>
        /// Update configuration values
        /// </summary>
        public void UpdateConfiguration(Action<AppConfiguration> updateAction)
        {
            updateAction(_configuration);
            SaveConfiguration();
        }

        /// <summary>
        /// Get current configuration
        /// </summary>
        public AppConfiguration GetConfiguration()
        {
            return _configuration;
        }

        /// <summary>
        /// Validate configuration asynchronously
        /// </summary>
        public async Task<bool> ValidateConfigurationAsync()
        {
            return await Task.Run(() =>
            {
                var isValid = ValidateConfiguration(out string[] errors);
                
                if (!isValid)
                {
                    foreach (var error in errors)
                    {
                        Log.Warning("Configuration validation error: {Error}", error);
                    }
                }
                
                return isValid;
            });
        }

        /// <summary>
        /// Reset configuration to defaults
        /// </summary>
        public void ResetToDefaults()
        {
            Log.Information("Resetting configuration to defaults");
            _configuration = new AppConfiguration();
            SaveConfiguration();
        }

        /// <summary>
        /// Reload configuration from file
        /// </summary>
        public void ReloadConfiguration()
        {
            LoadConfiguration();
        }

        /// <summary>
        /// Validate configuration
        /// </summary>
        public bool ValidateConfiguration(out string[] errors)
        {
            var errorList = new List<string>();

            // Validate UE executable path
            if (!string.IsNullOrEmpty(_configuration.UEExecutablePath) && 
                !File.Exists(_configuration.UEExecutablePath))
            {
                errorList.Add($"UE executable not found: {_configuration.UEExecutablePath}");
            }

            // Validate ports
            if (_configuration.WebRTCPort <= 0 || _configuration.WebRTCPort > 65535)
            {
                errorList.Add($"Invalid WebRTC port: {_configuration.WebRTCPort}");
            }

            if (_configuration.UEWebSocketPort <= 0 || _configuration.UEWebSocketPort > 65535)
            {
                errorList.Add($"Invalid UE WebSocket port: {_configuration.UEWebSocketPort}");
            }

            if (_configuration.ControlAPIPort <= 0 || _configuration.ControlAPIPort > 65535)
            {
                errorList.Add($"Invalid Control API port: {_configuration.ControlAPIPort}");
            }

            // Check for port conflicts
            var ports = new[] { _configuration.WebRTCPort, _configuration.UEWebSocketPort, _configuration.ControlAPIPort };
            if (ports.Distinct().Count() != ports.Length)
            {
                errorList.Add("Port conflict detected: All ports must be unique");
            }

            errors = errorList.ToArray();
            return errorList.Count == 0;
        }
    }
}
