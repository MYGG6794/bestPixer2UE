# bestPixer2UE

> 🎮 基于 .NET 8 WPF 的 Unreal Engine 像素流管理工具

[![Build Status](https://img.shields.io/badge/build-passing-brightgreen.svg)]()
[![.NET](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/download)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

## 📖 项目简介

bestPixer2UE 是一个专业的 UE 像素流管理应用程序，提供了完整的 WebRTC 信令服务器集成、UE 进程管理和友好的图形用户界面。通过集成成熟的 PeerStreamEnterprise 解决方案，为 UE 像素流应用提供稳定可靠的服务。

**项目状态**：✅ 开发完成，可用于生产环境

## ✨ 主要特性

### 🎯 核心功能
- **🎮 UE 进程管理**：一键启动/停止 UE 项目，自动配置像素流参数
- **📡 信令服务器**：集成 PeerStreamEnterprise，提供完整的 WebRTC 信令服务
- **⚙️ 配置管理**：统一的配置界面，自动同步到信令服务器
- **📊 状态监控**：实时显示服务状态，完善的错误处理和日志记录
- **🔧 环境检查**：启动前自动验证 Node.js 和依赖文件
- **🔄 自动化部署**：一键部署和启动所有服务

### 🏗️ 技术架构
```
StreamingManagementService (统一管理层)
├── PeerStreamEnterpriseService (Node.js 信令服务器)
├── UEControlService (UE 进程管理)
├── ConfigurationManager (配置管理)
└── ProcessManager (进程生命周期管理)
```

- **.NET 8 WPF**：现代化的桌面应用程序框架
- **PeerStreamEnterprise**：成熟的 Node.js WebRTC 信令服务器
- **依赖注入**：清晰的服务层架构，便于测试和维护
- **Serilog**：结构化日志记录，支持多种输出格式
- **自动配置同步**：WPF 配置自动生成 signal.json

## 🚀 快速开始

### 📋 环境要求
- **Windows 10/11** 64位系统
- **.NET 8 Runtime** 或更高版本
- **Node.js** v16+ （用于信令服务器）
- **Unreal Engine** 项目（启用 PixelStreaming 插件）

### 📦 安装步骤

1. **克隆项目**
```bash
git clone https://github.com/your-repo/bestPixer2UE.git
cd bestPixer2UE
```

2. **编译项目**
```bash
dotnet build
```

3. **运行程序**
```bash
dotnet run
# 或直接运行编译后的 bestPixer2UE.exe
```

### ⚙️ 配置说明

#### 基础配置
1. **UE 可执行文件路径**：设置 UE 引擎的可执行文件位置
2. **UE 项目文件**：选择要运行的 .uproject 文件
3. **信令端口**：默认 11188，可根据需要修改
4. **分辨率设置**：配置像素流的输出分辨率

#### 自动配置功能
- 程序会自动将 WPF 界面的配置同步到 `signal.json`
- 启动时自动检查 Node.js 环境和 PeerStreamEnterprise 文件
- UE 启动参数自动生成，包含完整的像素流配置

## 🎮 使用指南

### 1. 启动服务
1. 运行 `bestPixer2UE.exe`
2. 点击 **"启动PeerStreamEnterprise服务"** 按钮
3. 等待状态指示器变为绿色

### 2. 配置 UE 项目
确保您的 UE 项目：
- ✅ 启用了 **PixelStreaming** 插件
- ✅ 配置了正确的信令服务器 URL

**DefaultEngine.ini 示例配置：**
```ini
[/Script/PixelStreaming.PixelStreamingSettings]
SignallingServerURL=ws://127.0.0.1:11188
StartOnBoot=true

[/Script/Engine.Engine]
GameEngine=/Script/PixelStreaming.PixelStreamingGameEngine
```

### 3. 启动 UE
1. 在界面中设置 UE 可执行文件路径
2. 选择 UE 项目文件（.uproject）
3. 点击 **"启动UE"** 按钮
4. UE 会自动连接到信令服务器

### 4. 访问像素流
浏览器访问：`http://127.0.0.1:11188`

## 🏗️ 项目架构

### 核心组件
```
bestPixer2UE
├── StreamingManagementService     # 统一流媒体服务管理
├── PeerStreamEnterpriseService    # Node.js 信令服务器集成
├── UEControlService              # UE 进程控制
├── ConfigurationManager          # 配置管理
└── ProcessManager               # 进程生命周期管理
```

### 数据流
```
WPF界面 → ConfigurationManager → StreamingManagementService 
                                        ↓
                                PeerStreamEnterpriseService
                                        ↓
                                Node.js信令服务器
                                        ↓
                                UE进程 ↔ 浏览器客户端
```

## 📊 当前配置

根据最新配置，系统当前设置为：
- **信令端口**：11188
- **分辨率**：1000x1000
- **帧率**：30 FPS
- **UE项目**：BoaoPanda.exe
- **URL前缀**：main

## 🔧 故障排除

### 常见问题

#### 1. Node.js 未找到
**错误**：`Node.js not found`
**解决**：
```bash
# 安装 Node.js
https://nodejs.org/
# 验证安装
node --version
```

#### 2. 端口被占用或配置不生效
**错误**：`Port 11188 already in use` 或端口配置变更后不生效
**解决**：
- 在配置中修改 PORT 设置，系统会自动同步配置（延迟3秒）
- 如果服务正在运行，配置变更会自动重启服务以应用新端口
- 或使用任务管理器停止占用端口的进程
- 使用"同步配置"按钮手动强制同步配置到 PeerStreamEnterprise

**注意**：端口配置变更现已支持实时同步，无需手动重启服务

#### 3. UE 连接失败
**错误**：UE 无法连接信令服务器
**解决**：
1. 检查 UE 项目是否启用 PixelStreaming 插件
2. 确认 UE 启动参数包含正确的信令 URL
3. 检查防火墙设置
4. 验证信令服务器是否正常运行

#### 4. 编译错误
**错误**：编译时出现依赖问题
**解决**：
```bash
# 清理并重新构建
dotnet clean
dotnet restore
dotnet build
```

## 📝 开发状态

### ✅ 已完成功能
- [x] WPF 用户界面
- [x] PeerStreamEnterprise 集成
- [x] 配置管理系统
- [x] UE 进程控制
- [x] 自动配置同步
- [x] 环境依赖检查
- [x] 日志记录系统
- [x] 错误处理机制
- [x] **实时监控系统** - 专业的性能监控界面

### 📊 实时监控功能 **[NEW]**
- **视频流监控**: 分辨率、帧率、码率、丢帧统计
- **网络状态**: 延迟、数据流量、丢包率监控
- **音频监控**: 音频状态、流量、协议信息
- **连接状态**: 像素流、WebRTC、UE引擎状态
- **智能控制**: 监控开关、刷新频率、数据导出

### 🔄 进行中
- [ ] UI 美化和优化
- [ ] 多实例支持

### 📋 计划功能
- [ ] 配置模板系统
- [ ] 插件扩展支持
- [ ] 云部署支持
- [ ] 自动化测试

## 🤝 贡献指南

欢迎提交 Issue 和 Pull Request！

### 开发环境设置
1. 安装 .NET 8 SDK
2. 安装 Node.js v16+
3. 克隆项目并安装依赖

### 代码规范
- 使用 C# 编码规范
- 添加适当的注释和文档
- 遵循现有的架构模式

## 📄 许可证

本项目采用 MIT 许可证。详见 [LICENSE](LICENSE) 文件。

## 📞 联系方式

如有问题或建议，请通过以下方式联系：
- 提交 GitHub Issue
- 发送邮件至项目维护者

---

> 💡 **提示**：首次使用建议阅读 [架构优化完成报告.md](架构优化完成报告.md) 了解项目的详细架构和实现原理。
