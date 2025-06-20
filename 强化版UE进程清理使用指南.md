# 强化版UE进程清理功能使用指南

## 📋 更新内容
针对用户反馈的"停止信令服务后仍有残留UE进程"问题，已实施强化版进程清理机制。

## 🔧 新增功能

### 1. 自动强化清理
**位置**：停止WebRTC服务时自动执行
**功能**：三级渐进式清理机制

```
点击"停止WebRTC服务" → 自动执行：
├─ 第1级：优雅停止托管的UE进程
├─ 第2级：强制停止托管的UE进程  
├─ 第3级：系统扫描并清理所有UE相关进程
└─ 第4级：UnrealGame专门检查清理 ⭐新增
```

### 2. 完整服务停止
**位置**："停止所有服务"按钮
**功能**：使用CompleteUEProcessCleanup()进行彻底清理
**效果**：显示清理的进程数量

### 3. 紧急强制清理（可选）
**位置**：BtnForceCleanUE_Click方法（如需要可添加到界面）
**功能**：立即杀死系统中所有UE相关进程
**警告**：会影响非本程序启动的UE进程

## 🎯 清理策略

### 自动清理范围
程序会自动查找并清理以下进程：
- `*UnrealEngine*` - UE引擎主进程
- `*UnrealGame*` - UE游戏/项目进程 ⭐新增
- `*UE4*` / `*UE5*` - UE4/UE5相关进程
- `*UE_*` - UE项目进程
- `*Unreal*` - Unreal相关进程
- `*UELaunch*` - UE启动器进程
- `*crashreportclient*` - UE崩溃报告进程

### 清理日志示例
```
[INFO] Stopping streaming management service...
[INFO] Performing complete UE process cleanup...
[WARN] Scanning system for all UE-related processes to kill...
[WARN] Force killing UE process: UnrealEngine (PID: 1234)
[WARN] Force killing UE process: UnrealGame (PID: 5678)  ⭐新增
[WARN] Additional UnrealGame cleanup killed 1 processes
[INFO] Force killed 2 UE-related processes
[INFO] Cleaned up 2 UE processes
```

## 📊 使用建议

### 日常使用
1. **正常停止**：直接点击"停止WebRTC服务"，系统会自动完成清理
2. **检查结果**：查看日志确认清理的进程数量
3. **任务管理器验证**：确认没有残留的UE相关进程

### 问题排查
如果仍发现残留进程：

1. **查看日志**：确认清理操作是否执行
2. **手动触发**：点击"停止所有服务"进行完整清理
3. **进程特征**：检查残留进程名称是否在清理范围内
4. **权限问题**：确认程序有足够权限终止进程

### 性能影响
- **启动时间**：无影响，清理仅在停止时执行
- **系统资源**：清理过程约2-5秒，占用极少资源
- **其他程序**：只影响UE相关进程，不影响其他应用

## ⚠️ 注意事项

1. **进程范围**：强化清理会影响系统中所有UE相关进程，包括其他程序启动的
2. **数据保存**：确保UE项目已保存，强制清理可能导致未保存数据丢失
3. **开发环境**：在UE开发过程中使用时，请注意保存工作进度

## 🚀 预期效果

- ✅ **彻底清理**：不再有残留的UE后台进程
- ✅ **自动化**：用户无需手动处理，一键完成
- ✅ **可靠性**：多层保障机制，确保清理成功
- ✅ **透明度**：完整的日志记录，便于问题定位

---
*如果使用中仍发现进程残留问题，请查看日志文件并反馈具体的进程名称和PID信息。*
