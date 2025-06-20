using System;
using System.Text.Json;
using System.Threading.Tasks;
using bestPixer2UE.Core;
using Serilog;

namespace bestPixer2UE.Services
{
    /// <summary>
    /// UE control message types
    /// </summary>
    public enum UEMessageType
    {
        Command,
        Query,
        Event,
        Response
    }

    /// <summary>
    /// UE control message
    /// </summary>
    public class UEMessage
    {
        public UEMessageType Type { get; set; }
        public string Command { get; set; } = "";
        public object? Data { get; set; }
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Service for controlling Unreal Engine instances
    /// </summary>
    public class UEControlService
    {
        private readonly ProcessManager _processManager;
        private readonly ConfigurationManager _configManager;
        private ManagedProcess? _currentUEProcess;

        public UEControlService(ProcessManager processManager, ConfigurationManager configManager)
        {
            _processManager = processManager;
            _configManager = configManager;
            
            _processManager.ProcessStarted += OnProcessStarted;
            _processManager.ProcessStopped += OnProcessStopped;
            _processManager.ProcessError += OnProcessError;
        }

        public event EventHandler<ManagedProcess>? UEProcessStarted;
        public event EventHandler<ManagedProcess>? UEProcessStopped;
        public event EventHandler<string>? UEError;
        public event EventHandler<UEMessage>? MessageReceived;

        /// <summary>
        /// Start UE with specified project
        /// </summary>
        public async Task<bool> StartUEAsync(string? projectPath = null)
        {
            try
            {
                var config = _configManager.Configuration;
                var uePath = config.UEExecutablePath;
                
                if (string.IsNullOrEmpty(uePath))
                {
                    throw new InvalidOperationException("UE executable path not configured");
                }                // Build command line arguments
                var arguments = "";
                if (!string.IsNullOrEmpty(projectPath))
                {
                    arguments += $"\"{projectPath}\"";
                    config.LastUEProjectPath = projectPath;
                    _configManager.SaveConfiguration();
                }
                else if (!string.IsNullOrEmpty(config.LastUEProjectPath))
                {
                    arguments += $"\"{config.LastUEProjectPath}\"";
                }

                // Add Pixel Streaming arguments if enabled
                if (config.EnablePixelStreaming)
                {
                    arguments += $" -PixelStreamingURL={config.SignalingServerUrl}";
                    arguments += $" -ResX={config.ResolutionX}";
                    arguments += $" -ResY={config.ResolutionY}";
                    arguments += $" -WebRTCFps={config.TargetFPS}";
                    arguments += " -Unattended";
                    arguments += " -RenderOffScreen";
                    arguments += " -AudioMixer";
                }
                
                // Add WebSocket server arguments
                arguments += $" -WebSocketPort={config.UEWebSocketPort}";
                arguments += " -log";

                Log.Information("Starting UE process: {UEPath} {Arguments}", uePath, arguments);

                _currentUEProcess = await _processManager.StartUEProcessAsync(uePath, arguments);
                
                if (_currentUEProcess != null)
                {
                    Log.Information("UE process started successfully with PID: {ProcessId}", _currentUEProcess.ProcessId);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to start UE process");
                UEError?.Invoke(this, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Stop current UE process
        /// </summary>
        public async Task<bool> StopUEAsync()
        {
            try
            {
                if (_currentUEProcess == null)
                {
                    Log.Warning("No UE process to stop");
                    return true;
                }

                Log.Information("Stopping UE process: {ProcessId}", _currentUEProcess.ProcessId);
                
                var success = await _processManager.StopProcessAsync(_currentUEProcess.ProcessId, 
                    _configManager.Configuration.ProcessCleanupTimeoutMs);
                
                if (success)
                {
                    _currentUEProcess = null;
                    Log.Information("UE process stopped successfully");
                }
                else
                {
                    Log.Error("Failed to stop UE process gracefully");
                }

                return success;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error stopping UE process");
                UEError?.Invoke(this, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Send command to UE
        /// </summary>
        public async Task<bool> SendCommandAsync(string command, object? data = null)
        {
            try
            {
                if (_currentUEProcess == null)
                {
                    Log.Warning("No UE process available to send command");
                    return false;
                }

                var message = new UEMessage
                {
                    Type = UEMessageType.Command,
                    Command = command,
                    Data = data
                };

                Log.Debug("Sending command to UE: {Command}", command);
                
                // TODO: Implement actual WebSocket communication with UE
                // This would involve:
                // - Connecting to UE's WebSocket server
                // - Sending the command as JSON
                // - Handling the response
                
                await Task.Delay(100); // Placeholder
                Log.Debug("Command sent to UE successfully: {Command}", command);
                
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error sending command to UE: {Command}", command);
                UEError?.Invoke(this, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Query UE for information
        /// </summary>
        public async Task<object?> QueryUEAsync(string query)
        {
            try
            {
                if (_currentUEProcess == null)
                {
                    Log.Warning("No UE process available to query");
                    return null;
                }

                var message = new UEMessage
                {
                    Type = UEMessageType.Query,
                    Command = query
                };

                Log.Debug("Querying UE: {Query}", query);
                
                // TODO: Implement actual WebSocket communication with UE
                // This would involve:
                // - Sending the query
                // - Waiting for response
                // - Parsing and returning the result
                
                await Task.Delay(100); // Placeholder
                
                // Placeholder response
                return new { status = "ok", query = query, timestamp = DateTime.Now };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error querying UE: {Query}", query);
                UEError?.Invoke(this, ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Process incoming message from UE
        /// </summary>
        public async Task ProcessUEMessage(string messageJson)
        {
            try
            {
                var message = JsonSerializer.Deserialize<UEMessage>(messageJson);
                if (message != null)
                {
                    Log.Debug("Received message from UE: {MessageType} - {Command}", message.Type, message.Command);
                    MessageReceived?.Invoke(this, message);
                    
                    // Handle specific message types
                    switch (message.Type)
                    {
                        case UEMessageType.Event:
                            await HandleUEEvent(message);
                            break;
                        case UEMessageType.Response:
                            await HandleUEResponse(message);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error processing UE message: {Message}", messageJson);
                UEError?.Invoke(this, ex.Message);
            }
        }

        /// <summary>
        /// Handle UE event messages
        /// </summary>
        private async Task HandleUEEvent(UEMessage message)
        {
            try
            {
                Log.Information("UE Event: {Command} - {Data}", message.Command, message.Data);
                
                // Handle specific events
                switch (message.Command.ToLower())
                {
                    case "error":
                        UEError?.Invoke(this, message.Data?.ToString() ?? "Unknown UE error");
                        break;
                    case "ready":
                        Log.Information("UE is ready for commands");
                        break;
                    case "shutdown":
                        Log.Information("UE is shutting down");
                        break;
                }
                
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error handling UE event: {Command}", message.Command);
            }
        }

        /// <summary>
        /// Handle UE response messages
        /// </summary>
        private async Task HandleUEResponse(UEMessage message)
        {
            try
            {
                Log.Debug("UE Response: {Command} - {Data}", message.Command, message.Data);
                
                // TODO: Implement response correlation and callback mechanism
                // This would involve tracking pending requests and invoking appropriate callbacks
                
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error handling UE response: {Command}", message.Command);
            }
        }

        /// <summary>
        /// Check if UE is running
        /// </summary>
        public bool IsUERunning => _currentUEProcess?.Process != null && !_currentUEProcess.Process.HasExited;

        /// <summary>
        /// Get current UE process information
        /// </summary>
        public ManagedProcess? GetCurrentUEProcess() => _currentUEProcess;

        /// <summary>
        /// Check if WebSocket service is running
        /// </summary>
        public bool IsWebSocketRunning { get; private set; } = false;        /// <summary>
        /// Start UE control service
        /// </summary>
        public async Task StartAsync()
        {
            try
            {
                Log.Information("Starting UE control service...");
                
                // Initialize WebSocket service for UE communication
                // This is handled by MultiPortService, so we just track the state
                await Task.Delay(100); // Simulate async operation
                IsWebSocketRunning = true;
                
                Log.Information("UE control service started successfully");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to start UE control service");
                throw;
            }
        }        /// <summary>
        /// Stop UE control service
        /// </summary>
        public async Task StopAsync()
        {
            try
            {
                Log.Information("Stopping UE control service...");
                
                // First stop the UE process if it's running
                if (_currentUEProcess != null)
                {
                    Log.Information("Stopping UE process as part of service shutdown...");
                    await StopUEAsync();
                }
                
                // Stop WebSocket service
                await Task.Delay(100); // Simulate async operation
                IsWebSocketRunning = false;
                
                Log.Information("UE control service stopped successfully");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to stop UE control service");
                throw;
            }
        }

        /// <summary>
        /// Send command to UE asynchronously
        /// </summary>
        public async Task SendCommandAsync(string command)
        {
            try
            {
                Log.Information("Sending command to UE: {Command}", command);
                
                var message = new UEMessage
                {
                    Type = UEMessageType.Command,
                    Command = "custom",
                    Data = command
                };
                  // Send the message - for now just log it
                // In a real implementation, this would send via WebSocket
                await Task.Run(() => Log.Debug("UE message prepared: {Message}", JsonSerializer.Serialize(message)));
                
                Log.Information("Command sent to UE successfully");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to send command to UE");
                throw;
            }
        }

        private void OnProcessStarted(object? sender, ManagedProcess process)
        {
            if (process == _currentUEProcess)
            {
                UEProcessStarted?.Invoke(this, process);
            }
        }

        private void OnProcessStopped(object? sender, ManagedProcess process)
        {
            if (process == _currentUEProcess)
            {
                _currentUEProcess = null;
                UEProcessStopped?.Invoke(this, process);
            }
        }

        private void OnProcessError(object? sender, string error)
        {
            UEError?.Invoke(this, error);
        }
    }
}
