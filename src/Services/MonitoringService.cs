using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Serilog;

namespace bestPixer2UE.Services
{
    /// <summary>
    /// 监控数据模型
    /// </summary>
    public class MonitoringData
    {
        public VideoStats Video { get; set; } = new();
        public NetworkStats Network { get; set; } = new();
        public AudioStats Audio { get; set; } = new();
        public ConnectionStats Connection { get; set; } = new();
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public class VideoStats
    {
        public int Width { get; set; } = 1920;
        public int Height { get; set; } = 1080;
        public int FrameRate { get; set; } = 30;
        public int QuantizationParameter { get; set; } = 3;
        public long Bitrate { get; set; } = 16884892;
        public int FramesDecoded { get; set; } = 0;
        public int FramesDropped { get; set; } = 0;
    }

    public class NetworkStats
    {
        public int LatencyMs { get; set; } = 0;
        public long DataChannelUp { get; set; } = 4;
        public long DataChannelDown { get; set; } = 12191;
        public int PacketsLost { get; set; } = 0;
        public double CurrentTime { get; set; } = 0;
    }

    public class AudioStats
    {
        public bool IsEnabled { get; set; } = true;
        public long AudioData { get; set; } = 11965;
        public string Protocol { get; set; } = "udp://127.0.0.1:52207";
    }

    public class ConnectionStats
    {
        public bool IsStreamingConnected { get; set; } = false;
        public bool IsWebRTCConnected { get; set; } = false;
        public bool IsUEConnected { get; set; } = false;
    }

    /// <summary>
    /// 实时监控服务 - 从PeerStreamEnterprise获取监控数据
    /// </summary>
    public class MonitoringService
    {
        private readonly HttpClient _httpClient;
        private readonly Random _random = new Random();
        private DateTime _startTime = DateTime.Now;
        
        public MonitoringService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5)
            };
        }

        /// <summary>
        /// 获取实时监控数据
        /// </summary>
        public async Task<MonitoringData> GetMonitoringDataAsync(string serverUrl)
        {
            try
            {
                // TODO: 在实际部署中，这里应该调用PeerStreamEnterprise的监控API
                // 例如: var response = await _httpClient.GetAsync($"{serverUrl}/api/stats");
                
                // 目前返回模拟数据作为演示
                return GenerateSimulatedData();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to fetch real monitoring data, using simulated data");
                return GenerateSimulatedData();
            }
        }

        /// <summary>
        /// 生成模拟监控数据（用于演示）
        /// </summary>
        private MonitoringData GenerateSimulatedData()
        {
            var elapsedSeconds = (DateTime.Now - _startTime).TotalSeconds;
            
            return new MonitoringData
            {
                Video = new VideoStats
                {
                    Width = 1920,
                    Height = 1080,
                    FrameRate = 30,
                    QuantizationParameter = _random.Next(1, 8),
                    Bitrate = _random.Next(10000000, 20000000),
                    FramesDecoded = (int)(elapsedSeconds * 30),
                    FramesDropped = _random.Next(0, 5)
                },
                Network = new NetworkStats
                {
                    LatencyMs = _random.Next(0, 50),
                    DataChannelUp = _random.Next(1, 100),
                    DataChannelDown = _random.Next(1000, 20000),
                    PacketsLost = _random.Next(0, 3),
                    CurrentTime = elapsedSeconds
                },
                Audio = new AudioStats
                {
                    IsEnabled = true,
                    AudioData = _random.Next(5000, 15000),
                    Protocol = $"udp://127.0.0.1:{_random.Next(50000, 60000)}"
                },
                Connection = new ConnectionStats
                {
                    IsStreamingConnected = true,
                    IsWebRTCConnected = true,
                    IsUEConnected = true
                }
            };
        }

        /// <summary>
        /// 验证服务器连接
        /// </summary>
        public async Task<bool> TestConnectionAsync(string serverUrl)
        {
            try
            {
                var response = await _httpClient.GetAsync(serverUrl);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
