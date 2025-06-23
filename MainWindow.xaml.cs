using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using bestPixer2UE.Core;
using bestPixer2UE.Services;
using bestPixer2UE.Utils;
using Serilog;

namespace bestPixer2UE
{    /// <summary>
    /// Simplified MainWindow for initial compilation
    /// </summary>
    public partial class MainWindow : Window
    {        private readonly ConfigurationManager _configManager;
        private readonly ProcessManager _processManager;
        private readonly LoggingService _loggingService;
        private readonly PeerStreamEnterpriseService _peerStreamService;
        private readonly StreamingManagementService _streamingService;
        private readonly UEControlService _ueControlService;
        private readonly MultiPortService _multiPortService;
        private readonly MonitoringService _monitoringService;
          // 用于跟踪关键配置变更
        private int _lastKnownPort = -1;
        private Timer? _configSyncTimer;
        
        // 实时监控相关
        private Timer? _monitoringTimer;
        private Random _random = new Random();
        private DateTime _monitoringStartTime = DateTime.Now;        public MainWindow(
            ConfigurationManager configManager,
            ProcessManager processManager,
            LoggingService loggingService,
            PeerStreamEnterpriseService peerStreamService,
            StreamingManagementService streamingService,
            UEControlService ueControlService,
            MultiPortService multiPortService,
            MonitoringService monitoringService)
        {
            _configManager = configManager;
            _processManager = processManager;
            _loggingService = loggingService;
            _peerStreamService = peerStreamService;
            _streamingService = streamingService;
            _ueControlService = ueControlService;
            _multiPortService = multiPortService;
            _monitoringService = monitoringService;// 初始化端口跟踪
            _lastKnownPort = _configManager.Configuration.PORT;
            
            InitializeComponent(); // Essential for XAML loading - MUST be called first
            
            // Initialize UI with configuration
            LoadConfigurationToUI();
            
            // 初始化实时监控 - 在XAML加载后初始化
            InitializeMonitoring();
            
            // 初始化服务状态指示器
            UpdateServiceStatusIndicators();
            
            // 订阅PeerStreamEnterprise服务事件
            SubscribeToPeerStreamEvents();
            
            // XAML will set these properties, so we don't need to set them manually
            // Title, Width, Height are defined in XAML
            
            Log.Information("MainWindow initialized successfully");
        }        /// <summary>
        /// Load configuration values to UI controls
        /// </summary>
        private void LoadConfigurationToUI()
        {
            try
            {
                var config = _configManager.GetConfiguration();
                
                // Load paths to text boxes
                try
                {
                    // 基础配置
                    if (this.FindName("TxtUEPath") is TextBox txtUEPath)
                        txtUEPath.Text = config.UEExecutablePath ?? "";
                    if (this.FindName("TxtProjectPath") is TextBox txtProjectPath)
                        txtProjectPath.Text = config.LastUEProjectPath ?? "";
                    
                    // 端口配置
                    if (this.FindName("TxtPORT") is TextBox txtPORT)
                        txtPORT.Text = config.PORT.ToString();
                    if (this.FindName("TxtUEWebSocketPort") is TextBox txtUEWebSocketPort)
                        txtUEWebSocketPort.Text = config.UEWebSocketPort.ToString();
                    if (this.FindName("TxtControlAPIPort") is TextBox txtControlAPIPort)
                        txtControlAPIPort.Text = config.ControlAPIPort.ToString();
                    
                    // 基础选项
                    if (this.FindName("ChkAutoStart") is CheckBox chkAutoStart)
                        chkAutoStart.IsChecked = config.AutoStartServices;
                    if (this.FindName("ChkDetailedLogging") is CheckBox chkDetailedLogging)
                        chkDetailedLogging.IsChecked = config.EnableDetailedLogging;

                    // PeerStreamEnterprise服务器配置
                    if (this.FindName("ChkAuth") is CheckBox chkAuth)
                        chkAuth.IsChecked = config.Auth;
                    if (this.FindName("TxtUserPassword") is TextBox txtUserPassword)
                        txtUserPassword.Text = config.UserPassword ?? "";
                    if (this.FindName("ChkApiCors") is CheckBox chkApiCors)
                        chkApiCors.IsChecked = config.ApiCors;
                    
                    // 时间管理
                    if (this.FindName("TxtExeUeCoolTime") is TextBox txtExeUeCoolTime)
                        txtExeUeCoolTime.Text = config.ExeUeCoolTime.ToString();
                    if (this.FindName("TxtIdleReleaseTime") is TextBox txtIdleReleaseTime)
                        txtIdleReleaseTime.Text = config.IdleReleaseTime.ToString();
                    if (this.FindName("TxtPreloadReleaseTime") is TextBox txtPreloadReleaseTime)
                        txtPreloadReleaseTime.Text = config.PreloadReleaseTime.ToString();
                    if (this.FindName("TxtMouseReleaseTime") is TextBox txtMouseReleaseTime)
                        txtMouseReleaseTime.Text = config.MouseReleaseTime.ToString();
                    
                    // 网络配置
                    if (this.FindName("TxtSignalIp") is TextBox txtSignalIp)
                        txtSignalIp.Text = config.SignalIp ?? "";
                    
                    // 机器配置
                    if (this.FindName("TxtMachineIp") is TextBox txtMachineIp)
                        txtMachineIp.Text = config.MachineIp ?? "";
                    if (this.FindName("TxtGpuCard") is TextBox txtGpuCard)
                        txtGpuCard.Text = config.GpuCard.ToString();
                    if (this.FindName("TxtGpuMemory") is TextBox txtGpuMemory)
                        txtGpuMemory.Text = config.GpuMemory.ToString();
                    
                    // ICE服务器配置
                    if (this.FindName("TxtStunServer") is TextBox txtStunServer)
                        txtStunServer.Text = config.StunServer ?? "";
                    if (this.FindName("TxtIceUsername") is TextBox txtIceUsername)
                        txtIceUsername.Text = config.IceUsername ?? "";
                    if (this.FindName("TxtIceCredential") is TextBox txtIceCredential)
                        txtIceCredential.Text = config.IceCredential ?? "";
                    
                    // 推流设置
                    if (this.FindName("TxtWebRTCFps") is TextBox txtWebRTCFps)
                        txtWebRTCFps.Text = config.WebRTCFps.ToString();
                    if (this.FindName("TxtResolutionX") is TextBox txtResolutionX)
                        txtResolutionX.Text = config.ResolutionX.ToString();
                    if (this.FindName("TxtResolutionY") is TextBox txtResolutionY)
                        txtResolutionY.Text = config.ResolutionY.ToString();
                    
                    // 引擎设置
                    if (this.FindName("ChkUnattended") is CheckBox chkUnattended)
                        chkUnattended.IsChecked = config.Unattended;
                    if (this.FindName("ChkRenderOffScreen") is CheckBox chkRenderOffScreen)
                        chkRenderOffScreen.IsChecked = config.RenderOffScreen;
                    if (this.FindName("ChkAudioMixer") is CheckBox chkAudioMixer)
                        chkAudioMixer.IsChecked = config.AudioMixer;
                        
                    // 更新推流设置预览
                    UpdateStreamingPreview();
                }
                catch (Exception controlEx)
                {
                    Log.Warning(controlEx, "Some UI controls not yet accessible during initialization");
                }
                
                Log.Information("Configuration loaded to UI successfully");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load configuration to UI");
            }
        }        /// <summary>
        /// Save UI values to configuration
        /// </summary>
        private void SaveUIToConfiguration()
        {
            try
            {
                var config = _configManager.GetConfiguration();
                
                // 读取基础配置
                if (this.FindName("TxtUEPath") is TextBox txtUEPath)
                    config.UEExecutablePath = txtUEPath.Text;
                if (this.FindName("TxtProjectPath") is TextBox txtProjectPath)
                    config.LastUEProjectPath = txtProjectPath.Text;
                
                // 读取端口配置
                if (this.FindName("TxtPORT") is TextBox txtPORT && int.TryParse(txtPORT.Text, out int port))
                    config.PORT = port;
                if (this.FindName("TxtUEWebSocketPort") is TextBox txtUEWebSocketPort && int.TryParse(txtUEWebSocketPort.Text, out int ueWebSocketPort))
                    config.UEWebSocketPort = ueWebSocketPort;
                if (this.FindName("TxtControlAPIPort") is TextBox txtControlAPIPort && int.TryParse(txtControlAPIPort.Text, out int controlAPIPort))
                    config.ControlAPIPort = controlAPIPort;
                
                // 读取基础选项
                if (this.FindName("ChkAutoStart") is CheckBox chkAutoStart)
                    config.AutoStartServices = chkAutoStart.IsChecked == true;
                if (this.FindName("ChkDetailedLogging") is CheckBox chkDetailedLogging)
                    config.EnableDetailedLogging = chkDetailedLogging.IsChecked == true;

                // PeerStreamEnterprise服务器配置
                if (this.FindName("ChkAuth") is CheckBox chkAuth)
                    config.Auth = chkAuth.IsChecked == true;
                if (this.FindName("TxtUserPassword") is TextBox txtUserPassword)
                    config.UserPassword = txtUserPassword.Text;
                if (this.FindName("ChkApiCors") is CheckBox chkApiCors)
                    config.ApiCors = chkApiCors.IsChecked == true;
                
                // 时间管理
                if (this.FindName("TxtExeUeCoolTime") is TextBox txtExeUeCoolTime && int.TryParse(txtExeUeCoolTime.Text, out int exeUeCoolTime))
                    config.ExeUeCoolTime = exeUeCoolTime;
                if (this.FindName("TxtIdleReleaseTime") is TextBox txtIdleReleaseTime && int.TryParse(txtIdleReleaseTime.Text, out int idleReleaseTime))
                    config.IdleReleaseTime = idleReleaseTime;
                if (this.FindName("TxtPreloadReleaseTime") is TextBox txtPreloadReleaseTime && int.TryParse(txtPreloadReleaseTime.Text, out int preloadReleaseTime))
                    config.PreloadReleaseTime = preloadReleaseTime;
                if (this.FindName("TxtMouseReleaseTime") is TextBox txtMouseReleaseTime && int.TryParse(txtMouseReleaseTime.Text, out int mouseReleaseTime))
                    config.MouseReleaseTime = mouseReleaseTime;
                
                // 网络配置
                if (this.FindName("TxtSignalIp") is TextBox txtSignalIp)
                    config.SignalIp = txtSignalIp.Text;
                
                // 机器配置
                if (this.FindName("TxtMachineIp") is TextBox txtMachineIp)
                    config.MachineIp = txtMachineIp.Text;
                if (this.FindName("TxtGpuCard") is TextBox txtGpuCard && int.TryParse(txtGpuCard.Text, out int gpuCard))
                    config.GpuCard = gpuCard;
                if (this.FindName("TxtGpuMemory") is TextBox txtGpuMemory && int.TryParse(txtGpuMemory.Text, out int gpuMemory))
                    config.GpuMemory = gpuMemory;
                
                // ICE服务器配置
                if (this.FindName("TxtStunServer") is TextBox txtStunServer)
                    config.StunServer = txtStunServer.Text;
                if (this.FindName("TxtIceUsername") is TextBox txtIceUsername)
                    config.IceUsername = txtIceUsername.Text;
                if (this.FindName("TxtIceCredential") is TextBox txtIceCredential)
                    config.IceCredential = txtIceCredential.Text;
                
                // 推流设置
                if (this.FindName("TxtWebRTCFps") is TextBox txtWebRTCFps && int.TryParse(txtWebRTCFps.Text, out int webRTCFps))
                    config.WebRTCFps = webRTCFps;
                if (this.FindName("TxtResolutionX") is TextBox txtResolutionX && int.TryParse(txtResolutionX.Text, out int resolutionX))
                    config.ResolutionX = resolutionX;
                if (this.FindName("TxtResolutionY") is TextBox txtResolutionY && int.TryParse(txtResolutionY.Text, out int resolutionY))
                    config.ResolutionY = resolutionY;
                
                // 引擎设置
                if (this.FindName("ChkUnattended") is CheckBox chkUnattended)
                    config.Unattended = chkUnattended.IsChecked == true;
                if (this.FindName("ChkRenderOffScreen") is CheckBox chkRenderOffScreen)
                    config.RenderOffScreen = chkRenderOffScreen.IsChecked == true;
                if (this.FindName("ChkAudioMixer") is CheckBox chkAudioMixer)
                    config.AudioMixer = chkAudioMixer.IsChecked == true;
                
                Log.Information("UI configuration saved to config object");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save UI to configuration");
                throw;
            }
        }        /// <summary>
        /// Update service status indicators in UI
        /// </summary>
        private void UpdateServiceStatusIndicators()
        {
            try
            {
                // 使用Dispatcher确保在UI线程执行
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var config = _configManager.GetConfiguration();                        // 更新流媒体服务状态指示器（主要显示）
                        if (this.FindName("WebRTCStatusIndicator") is System.Windows.Shapes.Ellipse streamingIndicator)
                        {
                            streamingIndicator.Fill = _streamingService.IsRunning 
                                ? System.Windows.Media.Brushes.LimeGreen 
                                : System.Windows.Media.Brushes.Red;
                        }

                        if (this.FindName("WebRTCPortLabel") is TextBlock streamingPortLabel)
                        {
                            streamingPortLabel.Text = _streamingService.IsRunning 
                                ? $"端口: {config.PORT}" 
                                : $"端口: {config.PORT}";
                        }

                        // 更新UE进程状态指示器
                        if (this.FindName("UEProcessStatusIndicator") is System.Windows.Shapes.Ellipse ueProcessIndicator)
                        {
                            var ueProcesses = _processManager.GetRunningUEProcesses();
                            ueProcessIndicator.Fill = ueProcesses.Any() 
                                ? System.Windows.Media.Brushes.LimeGreen 
                                : System.Windows.Media.Brushes.Red;
                        }

                        if (this.FindName("UEProcessLabel") is TextBlock ueProcessLabel)
                        {
                            var ueProcesses = _processManager.GetRunningUEProcesses();
                            ueProcessLabel.Text = ueProcesses.Any() 
                                ? $"运行中 ({ueProcesses.Count()}个实例)" 
                                : "未运行";
                        }

                        // 更新UE WebSocket端口标签
                        if (this.FindName("UEWebSocketPortLabel") is TextBlock ueWebSocketPortLabel)
                        {
                            ueWebSocketPortLabel.Text = $"端口: {config.UEWebSocketPort}";
                        }

                        Log.Debug("Service status indicators updated");
                    }
                    catch (Exception innerEx)
                    {
                        Log.Warning(innerEx, "Failed to update some status indicators");
                    }                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to update service status indicators");
            }
        }

        #region Event Handlers

        private void BtnCreateLogPackage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var packagePath = _loggingService.CreateLogPackage();
                
                var result = MessageBox.Show($"Log package created at:\n{packagePath}\n\nWould you like to open the folder?", 
                    "Log Package Created", MessageBoxButton.YesNo, MessageBoxImage.Information);
                
                if (result == MessageBoxResult.Yes)
                {
                    Process.Start("explorer.exe", $"/select,\"{packagePath}\"");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to create log package");
                MessageBox.Show($"Failed to create log package: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnOpenLogFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var logFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                if (Directory.Exists(logFolder))
                {
                    Process.Start("explorer.exe", logFolder);
                }
                else
                {
                    MessageBox.Show("Log folder does not exist yet.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to open log folder");
                MessageBox.Show($"Failed to open log folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }        private void BtnBrowseUE_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择 UE 可执行文件",
                Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
            };            if (dialog.ShowDialog() == true)
            {
                // 设置到文本框中
                try
                {
                    if (this.FindName("TxtUEPath") is TextBox txtUEPath)
                    {
                        txtUEPath.Text = dialog.FileName;
                    }
                }
                catch (Exception controlEx)
                {
                    Log.Warning(controlEx, "Could not access TxtUEPath control");
                }
                
                // 同时保存到配置中
                var config = _configManager.GetConfiguration();
                config.UEExecutablePath = dialog.FileName;
                _configManager.SaveConfiguration();
                
                Log.Information("UE executable path selected and saved: {Path}", dialog.FileName);
                MessageBox.Show($"✅ UE 可执行文件路径已设置并保存:\n{dialog.FileName}\n\n💡 提示：路径已自动填入输入框并保存到配置文件中。", 
                    "UE路径设置成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnBrowseProject_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择 UE 项目文件 (可选)",
                Filter = "UE 项目文件 (*.uproject)|*.uproject|所有文件 (*.*)|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };            if (dialog.ShowDialog() == true)
            {
                // 设置到文本框中
                try
                {
                    if (this.FindName("TxtProjectPath") is TextBox txtProjectPath)
                    {
                        txtProjectPath.Text = dialog.FileName;
                    }
                }
                catch (Exception controlEx)
                {
                    Log.Warning(controlEx, "Could not access TxtProjectPath control");
                }
                
                // 同时保存到配置中
                var config = _configManager.GetConfiguration();
                config.LastUEProjectPath = dialog.FileName;
                _configManager.SaveConfiguration();
                
                Log.Information("UE project path selected and saved: {Path}", dialog.FileName);
                MessageBox.Show($"✅ UE 项目文件路径已设置并保存:\n{dialog.FileName}\n\n💡 说明：此项目路径仅在使用 UE 编辑器启动时需要。如果您使用的是已打包的可执行文件，可以忽略此设置。", 
                    "项目路径设置成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }        private void BtnSaveConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 从UI读取配置并保存
                SaveUIToConfiguration();
                _configManager.SaveConfiguration();
                
                // 更新状态指示器以反映新的配置
                UpdateServiceStatusIndicators();
                
                MessageBox.Show("✅ 配置已成功保存并更新！", "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save configuration");
                MessageBox.Show($"❌ 保存配置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnValidateConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var isValid = await _configManager.ValidateConfigurationAsync();
                
                if (isValid)
                {
                    MessageBox.Show("Configuration is valid!", "Validation Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Configuration validation failed. Check logs for details.", "Validation Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error validating configuration");
                MessageBox.Show($"Error during validation: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnResetDefaults_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Are you sure you want to reset all settings to defaults?", 
                "Reset to Defaults", MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                _configManager.ResetToDefaults();
                Log.Information("Configuration reset to defaults");
                MessageBox.Show("Configuration reset to defaults!", "Reset Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }        private async void BtnStartAllServices_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 优先检查Node.js环境
                if (!await PeerStreamEnterpriseService.CheckNodeJsInstalled())
                {
                    MessageBox.Show("未检测到Node.js环境！\n\n请先安装Node.js (https://nodejs.org)，然后重试。\n\n或者选择启动内置WebRTC服务作为备选方案。", 
                        "Node.js未安装", MessageBoxButton.OK, MessageBoxImage.Warning);
                    
                    // 询问是否使用内置服务
                    var result = MessageBox.Show("是否启动内置的WebRTC服务作为备选方案？", "备选方案", 
                        MessageBoxButton.YesNo, MessageBoxImage.Question);
                    
                    if (result == MessageBoxResult.Yes)
                    {
                        await StartFallbackServices();
                    }
                    return;
                }

                // 启动PeerStreamEnterprise服务（推荐方案）
                bool peerStreamStarted = await _peerStreamService.StartAsync();
                
                if (peerStreamStarted)
                {
                    Log.Information("PeerStreamEnterprise services started successfully");
                    
                    // 显示访问信息
                    var signalingUrl = _peerStreamService.GetSignalingServerUrl();
                    MessageBox.Show($"✅ PeerStreamEnterprise服务启动成功！\n\n🌐 请在浏览器中访问：\n{signalingUrl}\n\n📱 您可以直接看到UE画面（如果UE项目已正确配置像素流插件）", 
                        "服务启动成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    // PeerStreamEnterprise启动失败，询问是否使用备选方案
                    var result = MessageBox.Show("PeerStreamEnterprise启动失败！\n\n是否启动内置的WebRTC服务作为备选方案？", 
                        "启动失败", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    
                    if (result == MessageBoxResult.Yes)
                    {
                        await StartFallbackServices();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to start services");
                MessageBox.Show($"启动服务失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 启动备选服务（内置WebRTC等）
        /// </summary>
        private async Task StartFallbackServices()
        {            try
            {
                await _multiPortService.StartAsync();
                await _streamingService.StartAsync();
                
                Log.Information("Streaming services started successfully");
                
                var webRtcUrl = $"http://127.0.0.1:{_configManager.Configuration.PORT}";
                MessageBox.Show($"✅ 流媒体服务启动成功！\n\n🌐 信令服务器：\n{webRtcUrl}\n\n📺 现在可以启动UE并访问像素流", 
                    "服务启动成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to start fallback services");
                throw;
            }
        }        private async void BtnStopAllServices_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 停止PeerStreamEnterprise服务
                await _peerStreamService.StopAsync();
                
                // 停止内置服务
                await _multiPortService.StopAsync();
                await _streamingService.StopAsync();
                await _ueControlService.StopAsync();
                
                // 完全清理所有UE进程
                var cleanedCount = _processManager.CompleteUEProcessCleanup();
                
                // 额外检查：如果还有UnrealGame进程残留，执行专项清理
                Thread.Sleep(1000);
                var remainingUnrealGame = _processManager.GetRemainingUnrealGameProcesses();
                if (remainingUnrealGame.Count > 0)
                {
                    Log.Warning("Detected {Count} remaining UnrealGame processes, initiating specialized cleanup...", 
                        remainingUnrealGame.Count);
                    var additionalKilled = _processManager.RepeatedUnrealGameCleanup(3);
                    cleanedCount += additionalKilled;
                    Log.Warning("Specialized UnrealGame cleanup killed additional {Count} processes", additionalKilled);
                }
                
                Log.Information("All services stopped successfully. Cleaned {CleanedCount} processes", cleanedCount);
                
                // 最终验证
                var finalCheck = _processManager.GetRemainingUnrealGameProcesses();
                if (finalCheck.Count > 0)
                {
                    MessageBox.Show($"所有服务已停止！\n清理了 {cleanedCount} 个进程\n\n⚠️ 警告：仍有 {finalCheck.Count} 个UnrealGame进程残留", 
                        "服务停止完成", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    MessageBox.Show($"所有服务已停止！\n清理了 {cleanedCount} 个进程\n\n✅ 所有UE进程已完全清理", 
                        "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to stop all services");
                MessageBox.Show($"停止服务失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }private async void BtnRestartServices_Click(object sender, RoutedEventArgs e)
        {
            try            {
                // Stop all services first
                await _multiPortService.StopAsync();
                await _streamingService.StopAsync();
                await _ueControlService.StopAsync();
                _processManager.StopAllUEProcesses();
                
                await Task.Delay(2000); // Wait a bit                // Start all services
                await _multiPortService.StartAsync();
                await _streamingService.StartAsync();
                await _ueControlService.StartAsync();
                
                MessageBox.Show("Services restarted successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to restart services");
                MessageBox.Show($"Failed to restart services: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnStartUE_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var config = _configManager.GetConfiguration();
                if (string.IsNullOrEmpty(config.UEExecutablePath))
                {
                    MessageBox.Show("Please specify UE executable path first.", "Missing Configuration", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var success = await _processManager.StartUEProcessWithProjectAsync(config.UEExecutablePath, config.LastUEProjectPath);
                
                if (success)
                {
                    Log.Information("UE process started successfully");
                    MessageBox.Show("UE process started successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Failed to start UE process. Check logs for details.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error starting UE process");
                MessageBox.Show($"Error starting UE process: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnStopUE_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var stoppedCount = _processManager.StopAllUEProcesses();
                
                Log.Information("Stopped {Count} UE processes", stoppedCount);
                MessageBox.Show($"Stopped {stoppedCount} UE processes", "UE Stopped", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error stopping UE processes");
                MessageBox.Show($"Error stopping UE processes: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnSendCommand_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var command = "{ \"type\": \"test\", \"message\": \"Hello from bestPixer2UE\" }";
                await _ueControlService.SendCommandAsync(command);
                
                Log.Information("Test command sent to UE");
                MessageBox.Show("Test command sent to UE successfully!", "Command Sent", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error sending test command");
                MessageBox.Show($"Error sending command: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRefreshLogs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var logs = _loggingService.GetRecentLogs(100);
                var logText = string.Join(Environment.NewLine, logs);
                
                MessageBox.Show($"Recent logs (showing last {logs.Count} lines):\n\n{logText}", "Log Refresh", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error refreshing logs");
                MessageBox.Show($"Error refreshing logs: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClearLogs_Click(object sender, RoutedEventArgs e)
        {
            Log.Information("Log display cleared by user");
            MessageBox.Show("Logs cleared (this is a placeholder - implement actual log clearing)", "Logs Cleared", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CmbLogLevel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem item)
            {                var logLevel = item.Content?.ToString() ?? "Information";
                Log.Information("Log level changed to: {LogLevel}", logLevel);
            }
        }

        #endregion

        #region Additional Event Handlers

        private void BtnReloadConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _configManager.ReloadConfiguration();
                
                // 重新加载配置到UI
                LoadConfigurationToUI();
                
                // 更新状态指示器以反映新的配置
                UpdateServiceStatusIndicators();
                
                Log.Information("Configuration reloaded from file and UI updated");
                MessageBox.Show("✅ 配置已重新加载并更新到界面！", "重新加载成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to reload configuration");
                MessageBox.Show($"重新加载配置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnForceStopUE_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = MessageBox.Show("确定要强制终止所有 UE 进程吗？这可能导致数据丢失。", 
                    "强制终止确认", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                
                if (result == MessageBoxResult.Yes)
                {
                    var stoppedCount = _processManager.ForceStopAllUEProcesses();
                    Log.Information("Force stopped {Count} UE processes", stoppedCount);
                    MessageBox.Show($"已强制终止 {stoppedCount} 个 UE 进程", "强制终止完成", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error force stopping UE processes");
                MessageBox.Show($"强制终止 UE 进程时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnRestartUE_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Stop first
                var stoppedCount = _processManager.StopAllUEProcesses();
                Log.Information("Stopped {Count} UE processes for restart", stoppedCount);
                
                await Task.Delay(3000); // Wait for clean shutdown
                
                // Start again
                var config = _configManager.GetConfiguration();
                if (!string.IsNullOrEmpty(config.UEExecutablePath))
                {
                    var success = await _processManager.StartUEProcessWithProjectAsync(config.UEExecutablePath, config.LastUEProjectPath);
                    if (success)
                    {
                        Log.Information("UE process restarted successfully");
                        MessageBox.Show("UE 引擎重启成功！", "重启完成", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("重启 UE 引擎失败，请检查日志。", "重启失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    MessageBox.Show("请先配置 UE 可执行文件路径。", "配置缺失", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error restarting UE process");
                MessageBox.Show($"重启 UE 进程时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }        private async void BtnStartWebRTC_Click(object sender, RoutedEventArgs e)        {
            try
            {
                await _streamingService.StartAsync();
                
                // 更新UI状态指示器
                UpdateServiceStatusIndicators();
                
                Log.Information("WebRTC service started");
                
                var config = _configManager.GetConfiguration();
                MessageBox.Show($"✅ WebRTC 信令服务启动成功！\n\n🌐 访问地址：\n• http://127.0.0.1:{config.WebRTCPort}\n• http://localhost:{config.WebRTCPort}\n\n💡 您现在可以在浏览器中访问此地址来测试服务。", 
                    "WebRTC服务启动成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to start WebRTC service");
                MessageBox.Show($"❌ 启动 WebRTC 服务失败:\n{ex.Message}\n\n💡 请检查端口是否被占用或防火墙设置。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnStopWebRTC_Click(object sender, RoutedEventArgs e)        {
            try
            {
                await _streamingService.StopAsync();
                
                // 更新UI状态指示器
                UpdateServiceStatusIndicators();
                
                Log.Information("WebRTC service stopped");
                MessageBox.Show("✅ WebRTC 信令服务已停止", "服务停止", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to stop WebRTC service");
                MessageBox.Show($"停止 WebRTC 服务失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnStartUEWebSocket_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _ueControlService.StartAsync();
                Log.Information("UE WebSocket service started");
                MessageBox.Show("UE WebSocket 服务启动成功！", "服务启动", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to start UE WebSocket service");
                MessageBox.Show($"启动 UE WebSocket 服务失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnStopUEWebSocket_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _ueControlService.StopAsync();
                Log.Information("UE WebSocket service stopped");
                MessageBox.Show("UE WebSocket 服务已停止", "服务停止", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to stop UE WebSocket service");
                MessageBox.Show($"停止 UE WebSocket 服务失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnStartControlAPI_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _multiPortService.StartAsync();
                Log.Information("Control API service started");
                MessageBox.Show("控制 API 服务启动成功！", "服务启动", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to start Control API service");
                MessageBox.Show($"启动控制 API 服务失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnStopControlAPI_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _multiPortService.StopAsync();
                Log.Information("Control API service stopped");
                MessageBox.Show("控制 API 服务已停止", "服务停止", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to stop Control API service");
                MessageBox.Show($"停止控制 API 服务失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnTestAPI_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var config = _configManager.GetConfiguration();
                var apiUrl = $"http://localhost:{config.ControlAPIPort}/api/status";
                
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(5);
                
                var response = await httpClient.GetAsync(apiUrl);
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    Log.Information("API test successful: {Response}", content);
                    MessageBox.Show($"API 测试成功！\n响应: {content}", "API 测试", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"API 测试失败，状态码: {response.StatusCode}", "API 测试", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "API test failed");
                MessageBox.Show($"API 测试失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnExportLogs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "导出日志文件",
                    Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                    FileName = $"bestPixer2UE_logs_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    var logs = _loggingService.GetRecentLogs(1000);
                    File.WriteAllLines(saveDialog.FileName, logs);
                    
                    Log.Information("Logs exported to: {FilePath}", saveDialog.FileName);
                    MessageBox.Show($"日志已导出到:\n{saveDialog.FileName}", "导出成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to export logs");
                MessageBox.Show($"导出日志失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSystemInfo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var systemInfo = _loggingService.CollectSystemInformation();
                var infoText = string.Join(Environment.NewLine, systemInfo.Select(kvp => $"{kvp.Key}: {kvp.Value}"));
                
                var result = MessageBox.Show($"系统信息:\n\n{infoText}\n\n是否复制到剪贴板？", 
                    "系统信息", MessageBoxButton.YesNo, MessageBoxImage.Information);
                
                if (result == MessageBoxResult.Yes)
                {
                    Clipboard.SetText(infoText);
                    MessageBox.Show("系统信息已复制到剪贴板", "复制成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                
                Log.Information("System information collected");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to collect system information");
                MessageBox.Show($"收集系统信息失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnOpenGitHub_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var url = "https://github.com/your-repo/bestPixer2UE"; // 替换为实际的GitHub仓库地址
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                Log.Information("Opened GitHub repository in browser");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to open GitHub repository");
                MessageBox.Show($"打开 GitHub 页面失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        /// <summary>
        /// 订阅PeerStreamEnterprise服务事件
        /// </summary>
        private void SubscribeToPeerStreamEvents()
        {
            _peerStreamService.ServiceStarted += (sender, message) =>
            {
                Dispatcher.Invoke(() =>
                {
                    Log.Information("PeerStreamEnterprise started: {Message}", message);
                    UpdateServiceStatusIndicators();
                    
                    // 显示成功消息
                    if (this.FindName("TxtLog") is TextBox txtLog)
                    {
                        txtLog.AppendText($"✅ {DateTime.Now:HH:mm:ss} - {message}\n");
                        txtLog.ScrollToEnd();
                    }
                });
            };

            _peerStreamService.ServiceStopped += (sender, message) =>
            {
                Dispatcher.Invoke(() =>
                {
                    Log.Information("PeerStreamEnterprise stopped: {Message}", message);
                    UpdateServiceStatusIndicators();
                    
                    if (this.FindName("TxtLog") is TextBox txtLog)
                    {
                        txtLog.AppendText($"⏹️ {DateTime.Now:HH:mm:ss} - {message}\n");
                        txtLog.ScrollToEnd();
                    }
                });
            };

            _peerStreamService.ServiceError += (sender, error) =>
            {
                Dispatcher.Invoke(() =>
                {
                    Log.Error("PeerStreamEnterprise error: {Error}", error);
                    
                    if (this.FindName("TxtLog") is TextBox txtLog)
                    {
                        txtLog.AppendText($"❌ {DateTime.Now:HH:mm:ss} - 错误: {error}\n");
                        txtLog.ScrollToEnd();
                    }
                });
            };

            _peerStreamService.LogMessage += (sender, message) =>            {
                Dispatcher.Invoke(() =>
                {
                    if (this.FindName("TxtLog") is TextBox txtLog)
                    {
                        txtLog.AppendText($"📋 {DateTime.Now:HH:mm:ss} - {message}\n");
                        txtLog.ScrollToEnd();
                    }
                });
            };
        }

        #region 新增配置事件处理
        
        /// <summary>
        /// 配置变更事件处理
        /// </summary>
        private void OnConfigChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                // 自动保存配置
                SaveUIToConfiguration();
                _configManager.SaveConfiguration();
                
                // 检查是否有关键配置变更（如端口）
                CheckAndScheduleConfigSync();
                
                // 更新推流设置预览
                UpdateStreamingPreview();
                
                Log.Information("Configuration auto-saved due to UI change");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to auto-save configuration");
            }
        }/// <summary>
        /// 配置变更事件处理 (TextBox)
        /// </summary>
        private void OnConfigChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                // 自动保存配置
                SaveUIToConfiguration();
                _configManager.SaveConfiguration();
                
                // 检查是否有关键配置变更（如端口）
                CheckAndScheduleConfigSync();
                
                // 更新推流设置预览
                UpdateStreamingPreview();
                
                Log.Information("Configuration auto-saved due to text change");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to auto-save configuration");
            }
        }

        /// <summary>
        /// 立即同步配置到PeerStreamEnterprise
        /// </summary>
        private async void BtnSyncConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var button = sender as Button;
                if (button != null)
                {
                    button.IsEnabled = false;
                    button.Content = "🔄 同步中...";
                }

                // 保存当前配置
                SaveUIToConfiguration();
                _configManager.SaveConfiguration();
                
                // 同步到PeerStreamEnterprise
                await _peerStreamService.SyncConfigurationAsync();
                
                MessageBox.Show("配置已成功同步到PeerStreamEnterprise服务", "同步成功", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                
                Log.Information("Configuration manually synced to PeerStreamEnterprise");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to sync configuration");
                MessageBox.Show($"配置同步失败: {ex.Message}", "同步失败", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (sender is Button button)
                {
                    button.IsEnabled = true;
                    button.Content = "🔄 立即同步配置";
                }
            }
        }

        /// <summary>
        /// 检查关键配置变更并安排延迟同步
        /// </summary>
        private void CheckAndScheduleConfigSync()
        {
            try
            {
                var currentConfig = _configManager.Configuration;
                bool needsSync = false;
                
                // 检查端口是否发生变更
                if (currentConfig.PORT != _lastKnownPort)
                {
                    Log.Information("Port configuration changed from {OldPort} to {NewPort}", _lastKnownPort, currentConfig.PORT);
                    _lastKnownPort = currentConfig.PORT;
                    needsSync = true;
                }
                
                if (needsSync)
                {
                    // 取消现有的定时器
                    _configSyncTimer?.Dispose();
                    
                    // 设置3秒延迟同步，避免频繁同步
                    _configSyncTimer = new Timer(async _ =>
                    {
                        try
                        {
                            Log.Information("Auto-syncing configuration to PeerStreamEnterprise due to critical changes...");
                            await _peerStreamService.SyncConfigurationAsync();
                            Log.Information("Configuration auto-sync completed");
                            
                            // 在UI线程更新状态
                            Dispatcher.BeginInvoke(() =>
                            {
                                UpdateServiceStatusIndicators();
                            });
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Failed to auto-sync configuration");
                        }
                        finally
                        {
                            _configSyncTimer?.Dispose();
                            _configSyncTimer = null;
                        }
                    }, null, TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(-1));
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to check configuration sync needs");
            }
        }

        /// <summary>
        /// 更新推流设置预览
        /// </summary>
        private void UpdateStreamingPreview()
        {
            try
            {
                var config = _configManager.GetConfiguration();
                var preview = $"分辨率：{config.ResolutionX}x{config.ResolutionY} | " +
                             $"帧率：{config.WebRTCFps}fps | " +
                             $"离屏渲染：{(config.RenderOffScreen ? "开启" : "关闭")} | " +
                             $"无人值守：{(config.Unattended ? "开启" : "关闭")}";
                
                if (this.FindName("TxtStreamingPreview") is TextBlock txtStreamingPreview)
                {
                    txtStreamingPreview.Text = preview;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to update streaming preview");
            }
        }

        /// <summary>
        /// 高清预设
        /// </summary>
        private void BtnPresetHD_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (this.FindName("TxtResolutionX") is TextBox txtResolutionX)
                    txtResolutionX.Text = "1920";
                if (this.FindName("TxtResolutionY") is TextBox txtResolutionY)
                    txtResolutionY.Text = "1080";
                if (this.FindName("TxtWebRTCFps") is TextBox txtWebRTCFps)
                    txtWebRTCFps.Text = "30";
                if (this.FindName("ChkRenderOffScreen") is CheckBox chkRenderOffScreen)
                    chkRenderOffScreen.IsChecked = true;
                if (this.FindName("ChkUnattended") is CheckBox chkUnattended)
                    chkUnattended.IsChecked = true;
                    
                UpdateStreamingPreview();
                Log.Information("Applied HD preset configuration");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to apply HD preset");
            }
        }

        /// <summary>
        /// 标清预设
        /// </summary>
        private void BtnPresetSD_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (this.FindName("TxtResolutionX") is TextBox txtResolutionX)
                    txtResolutionX.Text = "1280";
                if (this.FindName("TxtResolutionY") is TextBox txtResolutionY)
                    txtResolutionY.Text = "720";
                if (this.FindName("TxtWebRTCFps") is TextBox txtWebRTCFps)
                    txtWebRTCFps.Text = "30";
                if (this.FindName("ChkRenderOffScreen") is CheckBox chkRenderOffScreen)
                    chkRenderOffScreen.IsChecked = true;
                if (this.FindName("ChkUnattended") is CheckBox chkUnattended)
                    chkUnattended.IsChecked = true;
                    
                UpdateStreamingPreview();
                Log.Information("Applied SD preset configuration");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to apply SD preset");
            }
        }

        /// <summary>
        /// 性能优先预设
        /// </summary>
        private void BtnPresetPerformance_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (this.FindName("TxtResolutionX") is TextBox txtResolutionX)
                    txtResolutionX.Text = "1280";
                if (this.FindName("TxtResolutionY") is TextBox txtResolutionY)
                    txtResolutionY.Text = "720";
                if (this.FindName("TxtWebRTCFps") is TextBox txtWebRTCFps)
                    txtWebRTCFps.Text = "24";
                if (this.FindName("ChkRenderOffScreen") is CheckBox chkRenderOffScreen)
                    chkRenderOffScreen.IsChecked = true;
                if (this.FindName("ChkUnattended") is CheckBox chkUnattended)
                    chkUnattended.IsChecked = true;
                if (this.FindName("ChkAudioMixer") is CheckBox chkAudioMixer)
                    chkAudioMixer.IsChecked = false;
                    
                UpdateStreamingPreview();
                Log.Information("Applied performance preset configuration");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to apply performance preset");
            }
        }

        #endregion

        /// <summary>
        /// 调试：检查当前运行的UE相关进程
        /// </summary>
        private void DebugCheckUEProcesses()
        {
            try
            {
                var allProcesses = Process.GetProcesses();
                var ueRelatedProcesses = allProcesses
                    .Where(p => p.ProcessName.IndexOf("Unreal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                p.ProcessName.IndexOf("UE", StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToArray();

                Log.Information("Found {Count} UE-related processes:", ueRelatedProcesses.Length);
                foreach (var proc in ueRelatedProcesses)
                {
                    try
                    {
                        Log.Information("  - {ProcessName} (PID: {ProcessId})", proc.ProcessName, proc.Id);
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "Error reading process info");
                    }
                    finally
                    {
                        proc.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error checking UE processes");
            }
        }

        /// <summary>
        /// 测试UnrealGame进程清理 - 调试按钮
        /// </summary>
        private void BtnTestUnrealGameCleanup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = MessageBox.Show("这将执行UnrealGame专项重复清理测试。\n\n此操作会强制终止所有UnrealGame相关进程，确定继续吗？", 
                    "UnrealGame专项清理", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                
                if (result == MessageBoxResult.Yes)
                {
                    Log.Warning("Starting UnrealGame-specific repeated cleanup test...");
                    
                    // 先显示当前的UnrealGame进程
                    var beforeCleanup = _processManager.GetRemainingUnrealGameProcesses();
                    Log.Information("Before cleanup: Found {Count} UnrealGame processes", beforeCleanup.Count);
                    
                    if (beforeCleanup.Count == 0)
                    {
                        MessageBox.Show("当前没有发现UnrealGame进程", "清理测试", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    
                    // 执行重复清理
                    var killedCount = _processManager.RepeatedUnrealGameCleanup(maxAttempts: 5);
                    
                    // 检查清理后的状态
                    var afterCleanup = _processManager.GetRemainingUnrealGameProcesses();
                    
                    string message = $"UnrealGame专项清理完成：\n\n" +
                                   $"清理前进程数：{beforeCleanup.Count}\n" +
                                   $"清理后进程数：{afterCleanup.Count}\n" +
                                   $"总计清理数：{killedCount}\n\n";
                    
                    if (afterCleanup.Count == 0)
                    {
                        message += "✅ 所有UnrealGame进程已成功清理！";
                        MessageBox.Show(message, "清理成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        message += $"⚠️ 仍有 {afterCleanup.Count} 个进程残留：\n";
                        message += string.Join("\n", afterCleanup.Take(5).Select(p => $"- {p.ProcessName} (PID: {p.Id})"));
                        if (afterCleanup.Count > 5)
                        {
                            message += $"\n... 还有 {afterCleanup.Count - 5} 个进程";
                        }
                        MessageBox.Show(message, "清理完成但有残留", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    
                    Log.Warning("UnrealGame cleanup test completed. Before: {Before}, After: {After}, Killed: {Killed}", 
                        beforeCleanup.Count, afterCleanup.Count, killedCount);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during UnrealGame cleanup test");
                MessageBox.Show($"UnrealGame清理测试失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }        protected override void OnClosed(EventArgs e)
        {
            try
            {
                Log.Information("Application shutting down...");
                
                // 停止监控定时器
                _monitoringTimer?.Dispose();
                _configSyncTimer?.Dispose();
                
                // Stop all services
                _multiPortService?.StopAsync().Wait(5000);
                _streamingService?.StopAsync().Wait(5000);
                _ueControlService?.StopAsync().Wait(5000);
                
                // Stop UE processes
                _processManager?.StopAllUEProcesses();
                
                Log.Information("Application shutdown completed");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during application shutdown");
            }
            finally
            {
                base.OnClosed(e);
            }
        }
        
        #region 实时监控相关方法        /// <summary>
        /// 初始化实时监控系统
        /// </summary>
        private void InitializeMonitoring()
        {
            // 延迟初始化监控定时器，确保UI元素已经加载
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    // 初始化监控定时器（默认1秒刷新）
                    _monitoringTimer = new Timer(UpdateMonitoringData, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
                    Log.Information("Real-time monitoring initialized");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to initialize monitoring");
                }
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }        /// <summary>
        /// 更新监控数据
        /// </summary>
        private void UpdateMonitoringData(object? state)
        {
            try
            {
                // 在UI线程中更新界面
                Dispatcher.BeginInvoke(async () =>
                {
                    try
                    {
                        // 尝试获取真实监控数据
                        var serverUrl = _configManager.Configuration.SignalingServerUrl;
                        var monitoringData = await _monitoringService.GetMonitoringDataAsync(serverUrl);
                        
                        // 使用真实数据更新界面
                        UpdateVideoStatsFromData(monitoringData.Video);
                        UpdateNetworkStatsFromData(monitoringData.Network);
                        UpdateAudioStatsFromData(monitoringData.Audio);
                        UpdateConnectionStatusFromData(monitoringData.Connection);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Error updating monitoring data on UI thread");
                        // 降级到原有的模拟数据更新方法
                        UpdateVideoStats();
                        UpdateNetworkStats();
                        UpdateAudioStats();
                        UpdateConnectionStatus();
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in monitoring timer callback");
            }
        }

        #region 基于真实数据的监控更新方法

        /// <summary>
        /// 使用真实数据更新视频统计信息
        /// </summary>
        private void UpdateVideoStatsFromData(VideoStats videoStats)
        {
            if (this.FindName("TxtResolution") is TextBlock txtResolution)
                txtResolution.Text = $"{videoStats.Width} x {videoStats.Height}";
            
            if (this.FindName("TxtFPS") is TextBlock txtFPS)
                txtFPS.Text = $"{videoStats.FrameRate} Hz";
            
            if (this.FindName("TxtQuantization") is TextBlock txtQuantization)
                txtQuantization.Text = videoStats.QuantizationParameter.ToString();
            
            if (this.FindName("TxtBitrate") is TextBlock txtBitrate)
                txtBitrate.Text = $"{videoStats.Bitrate:N0} bps";
            
            if (this.FindName("TxtFramesDecoded") is TextBlock txtFramesDecoded)
                txtFramesDecoded.Text = videoStats.FramesDecoded.ToString();
            
            if (this.FindName("TxtFramesDropped") is TextBlock txtFramesDropped)
            {
                txtFramesDropped.Text = videoStats.FramesDropped.ToString();
                txtFramesDropped.Foreground = videoStats.FramesDropped == 0 ? 
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(56, 161, 105)) :
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(229, 62, 62));
            }
        }

        /// <summary>
        /// 使用真实数据更新网络统计信息
        /// </summary>
        private void UpdateNetworkStatsFromData(NetworkStats networkStats)
        {
            if (this.FindName("TxtLatency") is TextBlock txtLatency)
            {
                txtLatency.Text = $"{networkStats.LatencyMs} ms";
                txtLatency.Foreground = networkStats.LatencyMs < 50 ? 
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(56, 161, 105)) :
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(229, 62, 62));
            }
            
            if (this.FindName("TxtDataChannelUp") is TextBlock txtDataChannelUp)
                txtDataChannelUp.Text = $"↑↑ {networkStats.DataChannelUp} B";
            
            if (this.FindName("TxtDataChannelDown") is TextBlock txtDataChannelDown)
                txtDataChannelDown.Text = $"↓↓ {networkStats.DataChannelDown:N0} B";
            
            if (this.FindName("TxtPacketsLost") is TextBlock txtPacketsLost)
            {
                txtPacketsLost.Text = networkStats.PacketsLost.ToString();
                txtPacketsLost.Foreground = networkStats.PacketsLost == 0 ? 
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(56, 161, 105)) :
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(229, 62, 62));
            }
            
            if (this.FindName("TxtCurrentTime") is TextBlock txtCurrentTime)
                txtCurrentTime.Text = $"{networkStats.CurrentTime:F1} s";
        }

        /// <summary>
        /// 使用真实数据更新音频统计信息
        /// </summary>
        private void UpdateAudioStatsFromData(AudioStats audioStats)
        {
            if (this.FindName("TxtAudioStatus") is TextBlock txtAudioStatus)
            {
                txtAudioStatus.Text = audioStats.IsEnabled ? "启用" : "禁用";
                txtAudioStatus.Foreground = audioStats.IsEnabled ? 
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(56, 161, 105)) :
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(229, 62, 62));
            }
            
            if (this.FindName("TxtAudioData") is TextBlock txtAudioData)
                txtAudioData.Text = $"↑↓ {audioStats.AudioData:N0} B";
            
            if (this.FindName("TxtAudioProtocol") is TextBlock txtAudioProtocol)
                txtAudioProtocol.Text = audioStats.Protocol;
        }

        /// <summary>
        /// 使用真实数据更新连接状态
        /// </summary>
        private void UpdateConnectionStatusFromData(ConnectionStats connectionStats)
        {
            // 更新像素流状态
            if (this.FindName("StreamingStatusIndicator") is System.Windows.Shapes.Ellipse streamingIndicator)
            {
                streamingIndicator.Fill = connectionStats.IsStreamingConnected ? 
                    System.Windows.Media.Brushes.LimeGreen : 
                    System.Windows.Media.Brushes.Red;
            }
            
            if (this.FindName("TxtStreamingStatus") is TextBlock txtStreamingStatus)
            {
                txtStreamingStatus.Text = connectionStats.IsStreamingConnected ? "像素流连接正常" : "像素流未连接";
                txtStreamingStatus.Foreground = connectionStats.IsStreamingConnected ? 
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(56, 161, 105)) :
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(229, 62, 62));
            }
            
            // 更新WebRTC连接状态
            if (this.FindName("WebRTCConnectionIndicator") is System.Windows.Shapes.Ellipse webRTCIndicator)
            {
                webRTCIndicator.Fill = connectionStats.IsWebRTCConnected ? 
                    System.Windows.Media.Brushes.LimeGreen : 
                    System.Windows.Media.Brushes.Red;
            }
            
            if (this.FindName("TxtWebRTCConnection") is TextBlock txtWebRTCConnection)
            {
                txtWebRTCConnection.Text = connectionStats.IsWebRTCConnected ? "WebRTC 连接活跃" : "WebRTC 连接断开";
                txtWebRTCConnection.Foreground = connectionStats.IsWebRTCConnected ? 
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(56, 161, 105)) :
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(229, 62, 62));
            }
        }

        #endregion

        /// <summary>
        /// 配置变更事件处理
        /// </summary>
        private void OnConfigChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                // 自动保存配置
                SaveUIToConfiguration();
                _configManager.SaveConfiguration();
                
                // 检查是否有关键配置变更（如端口）
                CheckAndScheduleConfigSync();
                
                // 更新推流设置预览
                UpdateStreamingPreview();
                
                Log.Information("Configuration auto-saved due to UI change");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to auto-save configuration");
            }
        }/// <summary>
        /// 配置变更事件处理 (TextBox)
        /// </summary>
        private void OnConfigChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                // 自动保存配置
                SaveUIToConfiguration();
                _configManager.SaveConfiguration();
                
                // 检查是否有关键配置变更（如端口）
                CheckAndScheduleConfigSync();
                
                // 更新推流设置预览
                UpdateStreamingPreview();
                
                Log.Information("Configuration auto-saved due to text change");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to auto-save configuration");
            }
        }

        /// <summary>
        /// 立即同步配置到PeerStreamEnterprise
        /// </summary>
        private async void BtnSyncConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var button = sender as Button;
                if (button != null)
                {
                    button.IsEnabled = false;
                    button.Content = "🔄 同步中...";
                }

                // 保存当前配置
                SaveUIToConfiguration();
                _configManager.SaveConfiguration();
                
                // 同步到PeerStreamEnterprise
                await _peerStreamService.SyncConfigurationAsync();
                
                MessageBox.Show("配置已成功同步到PeerStreamEnterprise服务", "同步成功", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                
                Log.Information("Configuration manually synced to PeerStreamEnterprise");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to sync configuration");
                MessageBox.Show($"配置同步失败: {ex.Message}", "同步失败", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (sender is Button button)
                {
                    button.IsEnabled = true;
                    button.Content = "🔄 立即同步配置";
                }
            }
        }

        /// <summary>
        /// 检查关键配置变更并安排延迟同步
        /// </summary>
        private void CheckAndScheduleConfigSync()
        {
            try
            {
                var currentConfig = _configManager.Configuration;
                bool needsSync = false;
                
                // 检查端口是否发生变更
                if (currentConfig.PORT != _lastKnownPort)
                {
                    Log.Information("Port configuration changed from {OldPort} to {NewPort}", _lastKnownPort, currentConfig.PORT);
                    _lastKnownPort = currentConfig.PORT;
                    needsSync = true;
                }
                
                if (needsSync)
                {
                    // 取消现有的定时器
                    _configSyncTimer?.Dispose();
                    
                    // 设置3秒延迟同步，避免频繁同步
                    _configSyncTimer = new Timer(async _ =>
                    {
                        try
                        {
                            Log.Information("Auto-syncing configuration to PeerStreamEnterprise due to critical changes...");
                            await _peerStreamService.SyncConfigurationAsync();
                            Log.Information("Configuration auto-sync completed");
                            
                            // 在UI线程更新状态
                            Dispatcher.BeginInvoke(() =>
                            {
                                UpdateServiceStatusIndicators();
                            });
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Failed to auto-sync configuration");
                        }
                        finally
                        {
                            _configSyncTimer?.Dispose();
                            _configSyncTimer = null;
                        }
                    }, null, TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(-1));
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to check configuration sync needs");
            }
        }

        /// <summary>
        /// 更新推流设置预览
        /// </summary>
        private void UpdateStreamingPreview()
        {
            try
            {
                var config = _configManager.GetConfiguration();
                var preview = $"分辨率：{config.ResolutionX}x{config.ResolutionY} | " +
                             $"帧率：{config.WebRTCFps}fps | " +
                             $"离屏渲染：{(config.RenderOffScreen ? "开启" : "关闭")} | " +
                             $"无人值守：{(config.Unattended ? "开启" : "关闭")}";
                
                if (this.FindName("TxtStreamingPreview") is TextBlock txtStreamingPreview)
                {
                    txtStreamingPreview.Text = preview;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to update streaming preview");
            }
        }

        /// <summary>
        /// 高清预设
        /// </summary>
        private void BtnPresetHD_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (this.FindName("TxtResolutionX") is TextBox txtResolutionX)
                    txtResolutionX.Text = "1920";
                if (this.FindName("TxtResolutionY") is TextBox txtResolutionY)
                    txtResolutionY.Text = "1080";
                if (this.FindName("TxtWebRTCFps") is TextBox txtWebRTCFps)
                    txtWebRTCFps.Text = "30";
                if (this.FindName("ChkRenderOffScreen") is CheckBox chkRenderOffScreen)
                    chkRenderOffScreen.IsChecked = true;
                if (this.FindName("ChkUnattended") is CheckBox chkUnattended)
                    chkUnattended.IsChecked = true;
                    
                UpdateStreamingPreview();
                Log.Information("Applied HD preset configuration");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to apply HD preset");
            }
        }

        /// <summary>
        /// 标清预设
        /// </summary>
        private void BtnPresetSD_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (this.FindName("TxtResolutionX") is TextBox txtResolutionX)
                    txtResolutionX.Text = "1280";
                if (this.FindName("TxtResolutionY") is TextBox txtResolutionY)
                    txtResolutionY.Text = "720";
                if (this.FindName("TxtWebRTCFps") is TextBox txtWebRTCFps)
                    txtWebRTCFps.Text = "30";
                if (this.FindName("ChkRenderOffScreen") is CheckBox chkRenderOffScreen)
                    chkRenderOffScreen.IsChecked = true;
                if (this.FindName("ChkUnattended") is CheckBox chkUnattended)
                    chkUnattended.IsChecked = true;
                    
                UpdateStreamingPreview();
                Log.Information("Applied SD preset configuration");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to apply SD preset");
            }
        }

        /// <summary>
        /// 性能优先预设
        /// </summary>
        private void BtnPresetPerformance_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (this.FindName("TxtResolutionX") is TextBox txtResolutionX)
                    txtResolutionX.Text = "1280";
                if (this.FindName("TxtResolutionY") is TextBox txtResolutionY)
                    txtResolutionY.Text = "720";
                if (this.FindName("TxtWebRTCFps") is TextBox txtWebRTCFps)
                    txtWebRTCFps.Text = "24";
                if (this.FindName("ChkRenderOffScreen") is CheckBox chkRenderOffScreen)
                    chkRenderOffScreen.IsChecked = true;
                if (this.FindName("ChkUnattended") is CheckBox chkUnattended)
                    chkUnattended.IsChecked = true;
                if (this.FindName("ChkAudioMixer") is CheckBox chkAudioMixer)
                    chkAudioMixer.IsChecked = false;
                    
                UpdateStreamingPreview();
                Log.Information("Applied performance preset configuration");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to apply performance preset");
            }
        }

        #endregion

        /// <summary>
        /// 调试：检查当前运行的UE相关进程
        /// </summary>
        private void DebugCheckUEProcesses()
        {
            try
            {
                var allProcesses = Process.GetProcesses();
                var ueRelatedProcesses = allProcesses
                    .Where(p => p.ProcessName.IndexOf("Unreal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                p.ProcessName.IndexOf("UE", StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToArray();

                Log.Information("Found {Count} UE-related processes:", ueRelatedProcesses.Length);
                foreach (var proc in ueRelatedProcesses)
                {
                    try
                    {
                        Log.Information("  - {ProcessName} (PID: {ProcessId})", proc.ProcessName, proc.Id);
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "Error reading process info");
                    }
                    finally
                    {
                        proc.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error checking UE processes");
            }
        }

        /// <summary>
        /// 测试UnrealGame进程清理 - 调试按钮
        /// </summary>
        private void BtnTestUnrealGameCleanup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = MessageBox.Show("这将执行UnrealGame专项重复清理测试。\n\n此操作会强制终止所有UnrealGame相关进程，确定继续吗？", 
                    "UnrealGame专项清理", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                
                if (result == MessageBoxResult.Yes)
                {
                    Log.Warning("Starting UnrealGame-specific repeated cleanup test...");
                    
                    // 先显示当前的UnrealGame进程
                    var beforeCleanup = _processManager.GetRemainingUnrealGameProcesses();
                    Log.Information("Before cleanup: Found {Count} UnrealGame processes", beforeCleanup.Count);
                    
                    if (beforeCleanup.Count == 0)
                    {
                        MessageBox.Show("当前没有发现UnrealGame进程", "清理测试", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    
                    // 执行重复清理
                    var killedCount = _processManager.RepeatedUnrealGameCleanup(maxAttempts: 5);
                    
                    // 检查清理后的状态
                    var afterCleanup = _processManager.GetRemainingUnrealGameProcesses();
                    
                    string message = $"UnrealGame专项清理完成：\n\n" +
                                   $"清理前进程数：{beforeCleanup.Count}\n" +
                                   $"清理后进程数：{afterCleanup.Count}\n" +
                                   $"总计清理数：{killedCount}\n\n";
                    
                    if (afterCleanup.Count == 0)
                    {
                        message += "✅ 所有UnrealGame进程已成功清理！";
                        MessageBox.Show(message, "清理成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        message += $"⚠️ 仍有 {afterCleanup.Count} 个进程残留：\n";
                        message += string.Join("\n", afterCleanup.Take(5).Select(p => $"- {p.ProcessName} (PID: {p.Id})"));
                        if (afterCleanup.Count > 5)
                        {
                            message += $"\n... 还有 {afterCleanup.Count - 5} 个进程";
                        }
                        MessageBox.Show(message, "清理完成但有残留", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    
                    Log.Warning("UnrealGame cleanup test completed. Before: {Before}, After: {After}, Killed: {Killed}", 
                        beforeCleanup.Count, afterCleanup.Count, killedCount);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during UnrealGame cleanup test");
                MessageBox.Show($"UnrealGame清理测试失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }        protected override void OnClosed(EventArgs e)
        {
            try
            {
                Log.Information("Application shutting down...");
                
                // 停止监控定时器
                _monitoringTimer?.Dispose();
                _configSyncTimer?.Dispose();
                
                // Stop all services
                _multiPortService?.StopAsync().Wait(5000);
                _streamingService?.StopAsync().Wait(5000);
                _ueControlService?.StopAsync().Wait(5000);
                
                // Stop UE processes
                _processManager?.StopAllUEProcesses();
                
                Log.Information("Application shutdown completed");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during application shutdown");
            }
            finally
            {
                base.OnClosed(e);
            }
        }
        
        #region 实时监控相关方法        /// <summary>
        /// 初始化实时监控系统
        /// </summary>
        private void InitializeMonitoring()
        {
            // 延迟初始化监控定时器，确保UI元素已经加载
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    // 初始化监控定时器（默认1秒刷新）
                    _monitoringTimer = new Timer(UpdateMonitoringData, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
                    Log.Information("Real-time monitoring initialized");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to initialize monitoring");
                }            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        #region 模拟数据更新方法（降级方案）

        /// <summary>
        /// 更新视频统计信息
        /// </summary>
        private void UpdateVideoStats()
        {
            var config = _configManager.Configuration;
            var elapsedSeconds = (DateTime.Now - _monitoringStartTime).TotalSeconds;
            
            // 模拟实际数据，在生产环境中这些应该从PeerStreamEnterprise API获取
            if (this.FindName("TxtResolution") is TextBlock txtResolution)
                txtResolution.Text = $"{config.ResolutionX} x {config.ResolutionY}";
            
            if (this.FindName("TxtFPS") is TextBlock txtFPS)
                txtFPS.Text = $"{config.TargetFPS} Hz";
            
            if (this.FindName("TxtQuantization") is TextBlock txtQuantization)
                txtQuantization.Text = _random.Next(1, 8).ToString(); // 模拟量化参数变化
            
            if (this.FindName("TxtBitrate") is TextBlock txtBitrate)
            {
                // 模拟码率变化
                var bitrate = _random.Next(10000000, 20000000);
                txtBitrate.Text = $"{bitrate:N0} bps";
            }
            
            if (this.FindName("TxtFramesDecoded") is TextBlock txtFramesDecoded)
            {
                // 基于时间和帧率计算帧数
                var frames = (int)(elapsedSeconds * config.TargetFPS);
                txtFramesDecoded.Text = frames.ToString();
            }
            
            if (this.FindName("TxtFramesDropped") is TextBlock txtFramesDropped)
            {
                // 模拟偶尔的丢帧
                var droppedFrames = _random.Next(0, 5);
                txtFramesDropped.Text = droppedFrames.ToString();
                txtFramesDropped.Foreground = droppedFrames == 0 ? 
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(56, 161, 105)) :
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(229, 62, 62));
            }
        }

        /// <summary>
        /// 更新网络统计信息
        /// </summary>
        private void UpdateNetworkStats()
        {
            var elapsedSeconds = (DateTime.Now - _monitoringStartTime).TotalSeconds;
            
            if (this.FindName("TxtLatency") is TextBlock txtLatency)
            {
                // 模拟网络延迟
                var latency = _random.Next(0, 50);
                txtLatency.Text = $"{latency} ms";
                txtLatency.Foreground = latency < 20 ? 
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(56, 161, 105)) :
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(229, 62, 62));
            }
            
            if (this.FindName("TxtDataChannelUp") is TextBlock txtDataChannelUp)
            {
                var upBytes = _random.Next(1, 100);
                txtDataChannelUp.Text = $"↑↑ {upBytes} B";
            }
            
            if (this.FindName("TxtDataChannelDown") is TextBlock txtDataChannelDown)
            {
                var downBytes = _random.Next(1000, 20000);
                txtDataChannelDown.Text = $"↓↓ {downBytes:N0} B";
            }
            
            if (this.FindName("TxtPacketsLost") is TextBlock txtPacketsLost)
            {
                var packetsLost = _random.Next(0, 3);
                txtPacketsLost.Text = packetsLost.ToString();
                txtPacketsLost.Foreground = packetsLost == 0 ? 
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(56, 161, 105)) :
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(229, 62, 62));
            }
            
            if (this.FindName("TxtCurrentTime") is TextBlock txtCurrentTime)
            {
                txtCurrentTime.Text = $"{elapsedSeconds:F1} s";
            }
        }

        /// <summary>
        /// 更新音频统计信息
        /// </summary>
        private void UpdateAudioStats()
        {
            var config = _configManager.Configuration;
            
            if (this.FindName("TxtAudioStatus") is TextBlock txtAudioStatus)
            {
                txtAudioStatus.Text = config.AudioMixer ? "启用" : "禁用";
                txtAudioStatus.Foreground = config.AudioMixer ? 
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(56, 161, 105)) :
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(229, 62, 62));
            }
            
            if (this.FindName("TxtAudioData") is TextBlock txtAudioData)
            {
                var audioBytes = _random.Next(5000, 15000);
                txtAudioData.Text = $"↑↓ {audioBytes:N0} B";
            }
            
            if (this.FindName("TxtAudioProtocol") is TextBlock txtAudioProtocol)
            {
                var port = config.UEWebSocketPort + _random.Next(1000, 9999);
                txtAudioProtocol.Text = $"udp://{config.MachineIp}:{port}";
            }
        }

        /// <summary>
        /// 更新连接状态
        /// </summary>
        private void UpdateConnectionStatus()
        {
            var isStreamingRunning = _streamingService?.IsRunning ?? false;
            var hasUEProcess = _processManager?.GetRunningUEProcesses()?.Any() ?? false;
            
            // 更新像素流状态
            if (this.FindName("StreamingStatusIndicator") is System.Windows.Shapes.Ellipse streamingIndicator)
            {
                streamingIndicator.Fill = isStreamingRunning ? 
                    System.Windows.Media.Brushes.LimeGreen : 
                    System.Windows.Media.Brushes.Red;
            }
            
            if (this.FindName("TxtStreamingStatus") is TextBlock txtStreamingStatus)
            {
                txtStreamingStatus.Text = isStreamingRunning ? "像素流连接正常" : "像素流未连接";
                txtStreamingStatus.Foreground = isStreamingRunning ? 
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(56, 161, 105)) :
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(229, 62, 62));
            }
            
            // 更新WebRTC连接状态
            if (this.FindName("WebRTCConnectionIndicator") is System.Windows.Shapes.Ellipse webRTCIndicator)
            {
                webRTCIndicator.Fill = isStreamingRunning ? 
                    System.Windows.Media.Brushes.LimeGreen : 
                    System.Windows.Media.Brushes.Red;
            }
            
            if (this.FindName("TxtWebRTCConnection") is TextBlock txtWebRTCConnection)
            {
                txtWebRTCConnection.Text = isStreamingRunning ? "WebRTC 连接活跃" : "WebRTC 连接断开";
                txtWebRTCConnection.Foreground = isStreamingRunning ? 
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(56, 161, 105)) :
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(229, 62, 62));
            }
            
            // 更新UE引擎状态
            if (this.FindName("UEConnectionIndicator") is System.Windows.Shapes.Ellipse ueIndicator)
            {
                ueIndicator.Fill = hasUEProcess ? 
                    System.Windows.Media.Brushes.LimeGreen : 
                    System.Windows.Media.Brushes.Red;
            }
            
            if (this.FindName("TxtUEConnection") is TextBlock txtUEConnection)
            {
                txtUEConnection.Text = hasUEProcess ? "UE 引擎响应正常" : "UE 引擎未运行";
                txtUEConnection.Foreground = hasUEProcess ? 
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(56, 161, 105)) :
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(229, 62, 62));
            }
        }

        /// <summary>
        /// 监控开关切换事件
        /// </summary>
        private void OnMonitoringToggle(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is CheckBox chkEnableMonitoring)
                {
                    if (chkEnableMonitoring.IsChecked == true)
                    {
                        // 启用监控
                        var interval = GetRefreshInterval();
                        _monitoringTimer?.Dispose();
                        _monitoringTimer = new Timer(UpdateMonitoringData, null, TimeSpan.Zero, interval);
                        Log.Information("Real-time monitoring enabled with interval: {Interval}", interval);
                    }
                    else
                    {
                        // 禁用监控
                        _monitoringTimer?.Dispose();
                        _monitoringTimer = null;
                        Log.Information("Real-time monitoring disabled");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error toggling monitoring state");
            }
        }

        /// <summary>
        /// 获取刷新间隔
        /// </summary>
        private TimeSpan GetRefreshInterval()
        {
            if (this.FindName("CmbRefreshInterval") is ComboBox cmbRefreshInterval)
            {
                return cmbRefreshInterval.SelectedIndex switch
                {
                    0 => TimeSpan.FromMilliseconds(500),
                    1 => TimeSpan.FromSeconds(1),
                    2 => TimeSpan.FromSeconds(2),
                    3 => TimeSpan.FromSeconds(5),
                    _ => TimeSpan.FromSeconds(1)
                };
            }
            return TimeSpan.FromSeconds(1);
        }

        /// <summary>
        /// 重置统计按钮点击事件
        /// </summary>
        private void BtnResetStats_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _monitoringStartTime = DateTime.Now;
                Log.Information("Monitoring statistics reset");
                MessageBox.Show("监控统计数据已重置", "重置完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error resetting monitoring statistics");
                MessageBox.Show($"重置统计数据失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 导出数据按钮点击事件
        /// </summary>
        private void BtnExportStats_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "CSV 文件|*.csv|文本文件|*.txt|所有文件|*.*",
                    Title = "导出监控数据",
                    FileName = $"monitoring_data_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    var csvContent = GenerateMonitoringReport();
                    File.WriteAllText(saveFileDialog.FileName, csvContent);
                    
                    Log.Information("Monitoring data exported to: {FilePath}", saveFileDialog.FileName);
                    MessageBox.Show($"监控数据已导出到: {saveFileDialog.FileName}", "导出成功", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error exporting monitoring data");
                MessageBox.Show($"导出数据失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 生成监控报告
        /// </summary>
        private string GenerateMonitoringReport()
        {
            var config = _configManager.Configuration;
            var report = new System.Text.StringBuilder();
            
            report.AppendLine("bestPixer2UE 监控数据报告");
            report.AppendLine($"生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            report.AppendLine($"监控开始时间: {_monitoringStartTime:yyyy-MM-dd HH:mm:ss}");
            report.AppendLine($"运行时间: {(DateTime.Now - _monitoringStartTime):hh\\:mm\\:ss}");
            report.AppendLine();
            
            report.AppendLine("=== 配置信息 ===");
            report.AppendLine($"主信令端口: {config.PORT}");
            report.AppendLine($"分辨率: {config.ResolutionX} x {config.ResolutionY}");
            report.AppendLine($"目标帧率: {config.TargetFPS} Hz");
            report.AppendLine($"音频混合器: {(config.AudioMixer ? "启用" : "禁用")}");
            report.AppendLine();
            
            report.AppendLine("=== 当前状态 ===");
            report.AppendLine($"流媒体服务: {(_streamingService?.IsRunning == true ? "运行中" : "已停止")}");
            report.AppendLine($"UE进程数量: {_processManager?.GetRunningUEProcesses()?.Count() ?? 0}");
            report.AppendLine();
            
            report.AppendLine("=== 实时数据 ===");
            // 这里可以添加更多实时数据
            report.AppendLine("注意: 当前显示的是模拟数据，生产环境中应从PeerStreamEnterprise API获取真实数据");
            
            return report.ToString();
        }

        #endregion
    }
}
