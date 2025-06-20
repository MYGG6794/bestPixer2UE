using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using bestPixer2UE.Core;

namespace bestPixer2UE.Services
{
    /// <summary>
    /// Service type enumeration
    /// </summary>
    public enum ServiceType
    {
        WebRTC,
        UEWebSocket,
        ControlAPI
    }

    /// <summary>
    /// Service endpoint information
    /// </summary>
    public class ServiceEndpoint
    {
        public ServiceType Type { get; set; }
        public int Port { get; set; }
        public string Name { get; set; } = "";
        public bool IsRunning { get; set; }
        public IHost? Host { get; set; }
    }

    /// <summary>
    /// Multi-port service manager with isolated endpoints
    /// </summary>
    public class MultiPortService : IDisposable
    {
        private readonly ConfigurationManager _configManager;
        private readonly ConcurrentDictionary<ServiceType, ServiceEndpoint> _services = new();
        private readonly ConcurrentDictionary<string, WebSocket> _webSocketConnections = new();
        private bool _disposed = false;

        public MultiPortService(ConfigurationManager configManager)
        {
            _configManager = configManager;
        }

        public event EventHandler<ServiceEndpoint>? ServiceStarted;
        public event EventHandler<ServiceEndpoint>? ServiceStopped;
        public event EventHandler<string>? ServiceError;

        /// <summary>
        /// Check if service is running
        /// </summary>
        public bool IsRunning => _services.Values.Any(s => s.IsRunning);

        /// <summary>
        /// Start all services asynchronously
        /// </summary>
        public async Task StartAsync()
        {
            await StartAllServicesAsync();
        }

        /// <summary>
        /// Stop all services asynchronously
        /// </summary>
        public async Task StopAsync()
        {
            await Task.Run(() => StopAllServices());
        }

        /// <summary>
        /// Get active connection count
        /// </summary>
        public int GetActiveConnectionCount()
        {
            return _webSocketConnections.Count;
        }

        /// <summary>
        /// Start all configured services
        /// </summary>
        public async Task StartAllServicesAsync()
        {
            var config = _configManager.GetConfiguration();

            // Start WebRTC service
            await StartServiceAsync(ServiceType.WebRTC, config.WebRTCPort);
            
            // Start UE WebSocket service
            await StartServiceAsync(ServiceType.UEWebSocket, config.UEWebSocketPort);
            
            // Start Control API service
            await StartServiceAsync(ServiceType.ControlAPI, config.ControlAPIPort);

            Log.Information("All services started successfully");
        }

        /// <summary>
        /// Start a specific service
        /// </summary>
        public async Task StartServiceAsync(ServiceType serviceType, int port)
        {
            try
            {
                if (_services.ContainsKey(serviceType))
                {
                    await StopServiceAsync(serviceType);
                }

                Log.Information("Starting {ServiceType} service on port {Port}", serviceType, port);

                var hostBuilder = Host.CreateDefaultBuilder()
                    .ConfigureWebHostDefaults(webBuilder =>
                    {
                        webBuilder.UseUrls($"http://localhost:{port}");
                        webBuilder.ConfigureServices(services =>
                        {
                            services.AddCors();
                        });
                        webBuilder.Configure(app =>
                        {
                            ConfigureService(app, serviceType);
                        });
                    });

                var host = hostBuilder.Build();
                await host.StartAsync();

                var serviceEndpoint = new ServiceEndpoint
                {
                    Type = serviceType,
                    Port = port,
                    Name = serviceType.ToString(),
                    IsRunning = true,
                    Host = host
                };

                _services[serviceType] = serviceEndpoint;
                ServiceStarted?.Invoke(this, serviceEndpoint);

                Log.Information("{ServiceType} service started successfully on port {Port}", serviceType, port);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to start {ServiceType} service on port {Port}", serviceType, port);
                ServiceError?.Invoke(this, $"Failed to start {serviceType} service: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Configure service endpoints based on type
        /// </summary>
        private void ConfigureService(IApplicationBuilder app, ServiceType serviceType)
        {
            app.UseCors(builder => builder
                .AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader());            switch (serviceType)
            {
                case ServiceType.WebRTC:
                    // 配置基础的管理接口，实际信令由PeerStreamEnterprise处理
                    ConfigureManagementService(app);
                    break;
                case ServiceType.UEWebSocket:
                    ConfigureUEWebSocketService(app);
                    break;
                case ServiceType.ControlAPI:
                    ConfigureControlAPIService(app);
                    break;
            }
        }        /// <summary>
        /// Configure management interface service (replacing WebRTC signaling)
        /// </summary>
        private void ConfigureManagementService(IApplicationBuilder app)
        {
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                // 管理接口 - 服务状态
                endpoints.MapGet("/status", async context =>
                {
                    var response = new
                    {
                        Status = "running",
                        Message = "Service management interface is active. Signaling handled by PeerStreamEnterprise.",
                        PeerStreamEnterprisePort = _configManager.Configuration.PORT,
                        Timestamp = DateTime.Now
                    };
                    
                    await context.Response.WriteAsync(JsonSerializer.Serialize(response));
                });

                // 重定向到PeerStreamEnterprise
                endpoints.MapGet("/", async context =>
                {
                    var peerStreamUrl = $"http://127.0.0.1:{_configManager.Configuration.PORT}";
                    var html = $@"
<!DOCTYPE html>
<html>
<head>
    <title>bestPixer2UE - Management Interface</title>
</head>
<body>
    <h1>bestPixer2UE Management Interface</h1>
    <p>This is a management interface. For pixel streaming, please use:</p>
    <p><a href='{peerStreamUrl}' target='_blank'>PeerStreamEnterprise Signaling Server ({peerStreamUrl})</a></p>
    <h3>Service Status</h3>
    <ul>
        <li>Management Interface: ✅ Running</li>
        <li>PeerStreamEnterprise: <span id='peerStreamStatus'>🔄 Checking...</span></li>
    </ul>
    <script>
        fetch('{peerStreamUrl}/status').then(r => {{
            document.getElementById('peerStreamStatus').textContent = '✅ Running';
        }}).catch(e => {{
            document.getElementById('peerStreamStatus').textContent = '❌ Not Available';
        }});
    </script>
</body>
</html>";
                    
                    context.Response.ContentType = "text/html";
                    await context.Response.WriteAsync(html);
                });
            });
        }

        /// <summary>
        /// Configure WebRTC signaling service
        /// </summary>
        private void ConfigureWebRTCService(IApplicationBuilder app)
        {
            app.UseWebSockets();
            
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                // WebRTC signaling endpoint
                endpoints.MapGet("/ws", async context =>
                {
                    if (context.WebSockets.IsWebSocketRequest)
                    {
                        var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                        await HandleWebRTCWebSocket(context, webSocket);
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                    }
                });

                // Health check
                endpoints.MapGet("/health", async context =>
                {
                    await context.Response.WriteAsync("WebRTC service is running");
                });
            });
        }

        /// <summary>
        /// Configure UE WebSocket service
        /// </summary>
        private void ConfigureUEWebSocketService(IApplicationBuilder app)
        {
            app.UseWebSockets();
            
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                // UE control endpoint
                endpoints.MapGet("/ue", async context =>
                {
                    if (context.WebSockets.IsWebSocketRequest)
                    {
                        var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                        await HandleUEWebSocket(context, webSocket);
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                    }
                });

                // Health check
                endpoints.MapGet("/health", async context =>
                {
                    await context.Response.WriteAsync("UE WebSocket service is running");
                });
            });
        }

        /// <summary>
        /// Configure Control API service
        /// </summary>
        private void ConfigureControlAPIService(IApplicationBuilder app)
        {
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                // Status endpoint
                endpoints.MapGet("/api/status", async context =>
                {
                    var status = new
                    {
                        services = _services.Values.Select(s => new { s.Type, s.Port, s.IsRunning }),
                        connections = _webSocketConnections.Count,
                        timestamp = DateTime.UtcNow
                    };
                    
                    var json = JsonSerializer.Serialize(status);
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(json);
                });

                // Health check
                endpoints.MapGet("/health", async context =>
                {
                    await context.Response.WriteAsync("Control API service is running");
                });
            });
        }

        /// <summary>
        /// Handle WebRTC WebSocket connections
        /// </summary>
        private async Task HandleWebRTCWebSocket(HttpContext context, WebSocket webSocket)
        {
            var connectionId = Guid.NewGuid().ToString();
            _webSocketConnections[connectionId] = webSocket;

            Log.Information("WebRTC WebSocket connection established: {ConnectionId}", connectionId);

            try
            {
                var buffer = new byte[4096];
                while (webSocket.State == WebSocketState.Open)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        Log.Debug("WebRTC message received from {ConnectionId}: {Message}", connectionId, message);
                        
                        // Process WebRTC signaling message
                        await ProcessWebRTCMessage(connectionId, message, webSocket);
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in WebRTC WebSocket connection {ConnectionId}", connectionId);
            }
            finally
            {
                _webSocketConnections.TryRemove(connectionId, out _);
                Log.Information("WebRTC WebSocket connection closed: {ConnectionId}", connectionId);
            }
        }

        /// <summary>
        /// Handle UE WebSocket connections
        /// </summary>
        private async Task HandleUEWebSocket(HttpContext context, WebSocket webSocket)
        {
            var connectionId = Guid.NewGuid().ToString();
            _webSocketConnections[connectionId] = webSocket;

            Log.Information("UE WebSocket connection established: {ConnectionId}", connectionId);

            try
            {
                var buffer = new byte[4096];
                while (webSocket.State == WebSocketState.Open)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        Log.Debug("UE message received from {ConnectionId}: {Message}", connectionId, message);
                        
                        // Process UE control message
                        await ProcessUEMessage(connectionId, message, webSocket);
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in UE WebSocket connection {ConnectionId}", connectionId);
            }
            finally
            {
                _webSocketConnections.TryRemove(connectionId, out _);
                Log.Information("UE WebSocket connection closed: {ConnectionId}", connectionId);
            }
        }

        /// <summary>
        /// Process WebRTC signaling messages
        /// </summary>
        private async Task ProcessWebRTCMessage(string connectionId, string message, WebSocket webSocket)
        {
            try
            {
                // Basic WebRTC signaling relay - extend as needed
                var responseMessage = $"{{\"type\":\"response\",\"connectionId\":\"{connectionId}\",\"timestamp\":\"{DateTime.UtcNow:O}\"}}";
                var responseBytes = Encoding.UTF8.GetBytes(responseMessage);
                
                await webSocket.SendAsync(
                    new ArraySegment<byte>(responseBytes),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error processing WebRTC message from {ConnectionId}", connectionId);
            }
        }

        /// <summary>
        /// Process UE control messages
        /// </summary>
        private async Task ProcessUEMessage(string connectionId, string message, WebSocket webSocket)
        {
            try
            {
                // Basic UE command processing - extend as needed
                var responseMessage = $"{{\"type\":\"ack\",\"connectionId\":\"{connectionId}\",\"timestamp\":\"{DateTime.UtcNow:O}\"}}";
                var responseBytes = Encoding.UTF8.GetBytes(responseMessage);
                
                await webSocket.SendAsync(
                    new ArraySegment<byte>(responseBytes),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error processing UE message from {ConnectionId}", connectionId);
            }
        }

        /// <summary>
        /// Stop a specific service
        /// </summary>
        public async Task StopServiceAsync(ServiceType serviceType)
        {
            if (_services.TryGetValue(serviceType, out var service) && service.Host != null)
            {
                try
                {
                    Log.Information("Stopping {ServiceType} service", serviceType);
                    
                    await service.Host.StopAsync(TimeSpan.FromSeconds(10));
                    service.Host.Dispose();
                    
                    service.IsRunning = false;
                    ServiceStopped?.Invoke(this, service);
                    
                    Log.Information("{ServiceType} service stopped successfully", serviceType);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error stopping {ServiceType} service", serviceType);
                    throw;
                }
                finally
                {
                    _services.TryRemove(serviceType, out _);
                }
            }
        }

        /// <summary>
        /// Stop all services
        /// </summary>
        public void StopAllServices()
        {
            Log.Information("Stopping all services...");
            
            var stopTasks = new List<Task>();
            foreach (var serviceType in _services.Keys)
            {
                stopTasks.Add(StopServiceAsync(serviceType));
            }

            try
            {
                Task.WaitAll(stopTasks.ToArray(), TimeSpan.FromSeconds(30));
                Log.Information("All services stopped successfully");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error stopping some services");
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                StopAllServices();
                _disposed = true;
            }
        }
    }
}
