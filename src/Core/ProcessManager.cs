using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace bestPixer2UE.Core
{
    /// <summary>
    /// Managed process information
    /// </summary>
    public class ManagedProcess
    {
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = "";
        public string ExecutablePath { get; set; } = "";
        public DateTime StartTime { get; set; }
        public Process? Process { get; set; }
    }

    /// <summary>
    /// Manages UE and related processes with enhanced cleanup capabilities
    /// </summary>
    public class ProcessManager : IDisposable
    {
        private readonly List<ManagedProcess> _managedProcesses = new();
        private readonly object _lockObject = new();
        private bool _disposed = false;

        public event EventHandler<ManagedProcess>? ProcessStarted;
        public event EventHandler<ManagedProcess>? ProcessStopped;
        public event EventHandler<string>? ProcessError;        /// <summary>
        /// Start UE process with monitoring
        /// </summary>
        public Task<ManagedProcess?> StartUEProcessAsync(string executablePath, string arguments = "", CancellationToken cancellationToken = default)
        {
            return Task.Run(() => StartUEProcess(executablePath, arguments), cancellationToken);
        }

        /// <summary>
        /// Start UE process with monitoring (synchronous)
        /// </summary>
        private ManagedProcess? StartUEProcess(string executablePath, string arguments = "")
        {
            try
            {
                if (string.IsNullOrEmpty(executablePath) || !File.Exists(executablePath))
                {
                    throw new FileNotFoundException($"UE executable not found: {executablePath}");
                }

                Log.Information("Starting UE process: {ExecutablePath} with arguments: {Arguments}", executablePath, arguments);

                var startInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = false,
                    WorkingDirectory = Path.GetDirectoryName(executablePath) ?? ""
                };

                var process = new Process { StartInfo = startInfo };
                
                // Set up output handlers
                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Log.Debug("UE Output: {Data}", e.Data);
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Log.Warning("UE Error: {Data}", e.Data);
                        ProcessError?.Invoke(this, e.Data);
                    }
                };

                process.EnableRaisingEvents = true;
                process.Exited += OnProcessExited;

                if (!process.Start())
                {
                    throw new InvalidOperationException("Failed to start UE process");
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var managedProcess = new ManagedProcess
                {
                    ProcessId = process.Id,
                    ProcessName = process.ProcessName,
                    ExecutablePath = executablePath,
                    StartTime = DateTime.Now,
                    Process = process
                };

                lock (_lockObject)
                {
                    _managedProcesses.Add(managedProcess);
                }

                ProcessStarted?.Invoke(this, managedProcess);
                Log.Information("UE process started successfully. PID: {ProcessId}", process.Id);

                return managedProcess;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to start UE process");
                throw;
            }
        }        /// <summary>
        /// Start UE process with project path
        /// </summary>
        public async Task<bool> StartUEProcessWithProjectAsync(string executablePath, string projectPath = "")
        {
            try
            {
                string arguments = "";
                if (!string.IsNullOrEmpty(projectPath) && File.Exists(projectPath))
                {
                    arguments = $"\"{projectPath}\"";
                }

                var managedProcess = await StartUEProcessAsync(executablePath, arguments);
                return managedProcess != null;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to start UE process with project path");
                return false;
            }
        }

        /// <summary>
        /// Stop specific process gracefully
        /// </summary>
        public async Task<bool> StopProcessAsync(int processId, int timeoutMs = 10000)
        {
            try
            {
                ManagedProcess? managedProcess;
                lock (_lockObject)
                {
                    managedProcess = _managedProcesses.FirstOrDefault(p => p.ProcessId == processId);
                }

                if (managedProcess?.Process == null)
                {
                    Log.Warning("Process {ProcessId} not found in managed processes", processId);
                    return false;
                }

                return await StopProcessAsync(managedProcess, timeoutMs);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error stopping process {ProcessId}", processId);
                return false;
            }
        }

        /// <summary>
        /// Stop specific managed process gracefully
        /// </summary>
        private async Task<bool> StopProcessAsync(ManagedProcess managedProcess, int timeoutMs = 10000)
        {
            try
            {
                var process = managedProcess.Process;
                if (process == null || process.HasExited)
                {
                    Log.Information("Process {ProcessId} already exited", managedProcess.ProcessId);
                    return true;
                }

                Log.Information("Stopping process {ProcessId} ({ProcessName})", managedProcess.ProcessId, managedProcess.ProcessName);

                // Try graceful shutdown first
                if (!process.CloseMainWindow())
                {
                    Log.Warning("Failed to close main window for process {ProcessId}", managedProcess.ProcessId);
                }

                // Wait for graceful exit
                var cts = new CancellationTokenSource(timeoutMs);
                try
                {
                    await process.WaitForExitAsync(cts.Token);
                    Log.Information("Process {ProcessId} exited gracefully", managedProcess.ProcessId);
                    return true;
                }
                catch (OperationCanceledException)
                {
                    Log.Warning("Process {ProcessId} did not exit gracefully within {TimeoutMs}ms, forcing termination", 
                        managedProcess.ProcessId, timeoutMs);
                }

                // Force termination
                if (!process.HasExited)
                {
                    await TerminateProcessTreeAsync(managedProcess.ProcessId);
                    
                    // Wait a bit more for forced termination
                    await Task.Delay(2000);
                    
                    if (!process.HasExited)
                    {
                        Log.Error("Failed to terminate process {ProcessId}", managedProcess.ProcessId);
                        return false;
                    }
                }

                Log.Information("Process {ProcessId} terminated successfully", managedProcess.ProcessId);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during process termination for {ProcessId}", managedProcess.ProcessId);
                return false;
            }
        }

        /// <summary>
        /// Terminate process tree using WMI
        /// </summary>
        private async Task TerminateProcessTreeAsync(int parentProcessId)
        {
            try
            {
                await Task.Run(() =>
                {
                    var searcher = new ManagementObjectSearcher($"SELECT * FROM Win32_Process WHERE ParentProcessId={parentProcessId}");
                    var managementObjects = searcher.Get();

                    foreach (var managementObject in managementObjects.Cast<ManagementObject>())
                    {
                        var childProcessId = Convert.ToInt32(managementObject["ProcessId"]);
                        Log.Debug("Terminating child process {ChildProcessId} of parent {ParentProcessId}", childProcessId, parentProcessId);
                        
                        // Recursively terminate child processes
                        TerminateProcessTreeAsync(childProcessId).Wait();
                        
                        try
                        {
                            var childProcess = Process.GetProcessById(childProcessId);
                            childProcess.Kill();
                        }
                        catch (Exception ex)
                        {
                            Log.Debug(ex, "Failed to kill child process {ChildProcessId}", childProcessId);
                        }
                    }

                    // Kill the parent process
                    try
                    {
                        var parentProcess = Process.GetProcessById(parentProcessId);
                        parentProcess.Kill();
                        Log.Debug("Killed parent process {ParentProcessId}", parentProcessId);
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "Failed to kill parent process {ParentProcessId}", parentProcessId);
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error terminating process tree for {ParentProcessId}", parentProcessId);
            }
        }

        /// <summary>
        /// Get all currently managed processes
        /// </summary>
        public ManagedProcess[] GetManagedProcesses()
        {
            lock (_lockObject)
            {
                return _managedProcesses.ToArray();
            }
        }

        /// <summary>
        /// Cleanup all managed processes
        /// </summary>
        public void CleanupAllProcesses()
        {
            Log.Information("Starting cleanup of all managed processes...");
            
            ManagedProcess[] processes;
            lock (_lockObject)
            {
                processes = _managedProcesses.ToArray();
            }

            var tasks = processes.Select(p => StopProcessAsync(p)).ToArray();
            
            try
            {
                Task.WaitAll(tasks, TimeSpan.FromSeconds(30));
                Log.Information("All processes cleanup completed");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during process cleanup");
            }

            lock (_lockObject)
            {
                _managedProcesses.Clear();
            }
        }

        /// <summary>
        /// Handle process exit event
        /// </summary>
        private void OnProcessExited(object? sender, EventArgs e)
        {
            if (sender is Process process)
            {
                ManagedProcess? managedProcess;
                lock (_lockObject)
                {
                    managedProcess = _managedProcesses.FirstOrDefault(p => p.ProcessId == process.Id);
                    if (managedProcess != null)
                    {
                        _managedProcesses.Remove(managedProcess);
                    }
                }

                if (managedProcess != null)
                {
                    Log.Information("Process {ProcessId} ({ProcessName}) exited with code {ExitCode}", 
                        process.Id, managedProcess.ProcessName, process.ExitCode);
                    ProcessStopped?.Invoke(this, managedProcess);
                }

                process.Dispose();
            }
        }

        /// <summary>
        /// Check if any UE processes are running
        /// </summary>
        public bool HasActiveProcesses()
        {
            lock (_lockObject)
            {
                return _managedProcesses.Any(p => p.Process != null && !p.Process.HasExited);
            }
        }

        /// <summary>
        /// Check if any UE process is currently running
        /// </summary>
        public bool IsUEProcessRunning()
        {
            lock (_lockObject)
            {
                return _managedProcesses.Any(p => p.Process != null && !p.Process.HasExited);
            }
        }

        /// <summary>
        /// Get list of currently running UE processes
        /// </summary>
        public List<ManagedProcess> GetRunningUEProcesses()
        {
            lock (_lockObject)
            {
                return _managedProcesses.Where(p => p.Process != null && !p.Process.HasExited).ToList();
            }
        }

        /// <summary>
        /// Stop all UE processes and return count of stopped processes
        /// </summary>
        public int StopAllUEProcesses()
        {
            Log.Information("Stopping all UE processes...");
            
            ManagedProcess[] processes;
            lock (_lockObject)
            {
                processes = _managedProcesses.Where(p => p.Process != null && !p.Process.HasExited).ToArray();
            }

            if (processes.Length == 0)
            {
                Log.Information("No UE processes to stop");
                return 0;
            }

            var tasks = processes.Select(async p =>
            {
                try
                {
                    await StopProcessAsync(p);
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to stop process {ProcessId}", p.ProcessId);
                    return false;
                }
            }).ToArray();

            try
            {
                Task.WaitAll(tasks, TimeSpan.FromSeconds(30));
                var stoppedCount = tasks.Count(t => t.Result);
                Log.Information("Stopped {StoppedCount} out of {TotalCount} UE processes", stoppedCount, processes.Length);
                return stoppedCount;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during UE processes shutdown");
                return 0;
            }
        }

        /// <summary>
        /// Force stop all UE processes immediately and return count of stopped processes
        /// </summary>
        public int ForceStopAllUEProcesses()
        {
            Log.Warning("Force stopping all UE processes...");
            
            ManagedProcess[] processes;
            lock (_lockObject)
            {
                processes = _managedProcesses.Where(p => p.Process != null && !p.Process.HasExited).ToArray();
            }

            if (processes.Length == 0)
            {
                Log.Information("No UE processes to force stop");
                return 0;
            }

            int stoppedCount = 0;
            foreach (var managedProcess in processes)
            {
                try
                {
                    if (managedProcess.Process != null && !managedProcess.Process.HasExited)
                    {
                        // Try to kill the process immediately
                        managedProcess.Process.Kill(true); // Kill entire process tree
                        managedProcess.Process.WaitForExit(5000); // Wait max 5 seconds
                        
                        Log.Warning("Force stopped UE process {ProcessId}", managedProcess.ProcessId);
                        stoppedCount++;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to force stop process {ProcessId}", managedProcess.ProcessId);
                }
            }

            // Clean up the managed processes list
            lock (_lockObject)
            {
                _managedProcesses.RemoveAll(p => p.Process == null || p.Process.HasExited);
            }

            Log.Warning("Force stopped {Count} UE processes", stoppedCount);
            return stoppedCount;
        }

        /// <summary>
        /// Kill all UE-related processes by name pattern (nuclear option)
        /// This will kill ALL UE processes, not just the ones we started
        /// </summary>
        public int KillAllUEProcessesByName()
        {
            Log.Warning("Scanning system for all UE-related processes to kill...");
              string[] ueProcessPatterns = {
                "*UnrealEngine*",
                "*UnrealGame*",     // 添加UnrealGame进程模式
                "*UE4*", 
                "*UE5*",
                "*UE_*",
                "*Unreal*",
                "*UELaunch*",
                "*crashreportclient*"
            };

            int killedCount = 0;
            
            foreach (var pattern in ueProcessPatterns)
            {
                try
                {
                    var processes = Process.GetProcesses()
                        .Where(p => IsPatternMatch(p.ProcessName, pattern.Replace("*", "")))
                        .ToArray();

                    foreach (var process in processes)
                    {
                        try
                        {
                            if (!process.HasExited)
                            {
                                Log.Warning("Force killing UE process: {ProcessName} (PID: {ProcessId})", 
                                    process.ProcessName, process.Id);
                                process.Kill(true); // Kill entire process tree
                                process.WaitForExit(3000);
                                killedCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Debug(ex, "Failed to kill process {ProcessName} (PID: {ProcessId})", 
                                process.ProcessName, process.Id);
                        }
                        finally
                        {
                            process.Dispose();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error scanning for processes matching pattern {Pattern}", pattern);
                }
            }

            Log.Warning("Force killed {Count} UE-related processes", killedCount);
            return killedCount;
        }

        /// <summary>
        /// Check if process name matches pattern (simple contains check)
        /// </summary>
        private bool IsPatternMatch(string processName, string pattern)
        {
            return processName.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;
        }        /// <summary>
        /// Complete UE process cleanup - tries everything with multiple passes
        /// </summary>
        public int CompleteUEProcessCleanup()
        {
            Log.Warning("Starting complete UE process cleanup with enhanced strategy...");
            
            int totalKilled = 0;
            
            // Step 1: Try to stop our managed processes gracefully
            try
            {
                totalKilled += StopAllUEProcesses();
                Thread.Sleep(2000); // Wait a bit
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in graceful UE process stop");
            }

            // Step 2: Force stop our managed processes
            try
            {
                totalKilled += ForceStopAllUEProcesses();
                Thread.Sleep(1000); // Wait a bit
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in force UE process stop");
            }

            // Step 3: Multiple passes of aggressive cleanup
            for (int pass = 1; pass <= 3; pass++)
            {
                Log.Warning("Starting cleanup pass {Pass}/3", pass);
                
                // Step 3a: Nuclear option - kill all UE processes by name
                try
                {
                    var killed = KillAllUEProcessesByName();
                    totalKilled += killed;
                    if (killed > 0)
                    {
                        Log.Warning("Pass {Pass}: Killed {Count} UE processes by name", pass, killed);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error in nuclear UE process cleanup on pass {Pass}", pass);
                }

                // Step 3b: Specific UnrealGame cleanup
                try
                {
                    var unrealGameKilled = KillUnrealGameProcesses();
                    totalKilled += unrealGameKilled;
                    if (unrealGameKilled > 0)
                    {
                        Log.Warning("Pass {Pass}: Additional UnrealGame cleanup killed {Count} processes", pass, unrealGameKilled);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error in UnrealGame specific cleanup on pass {Pass}", pass);
                }

                // Step 3c: WMI-based cleanup for stubborn processes
                try
                {
                    var wmiKilled = KillUEProcessesViaWMI();
                    totalKilled += wmiKilled;
                    if (wmiKilled > 0)
                    {
                        Log.Warning("Pass {Pass}: WMI cleanup killed {Count} processes", pass, wmiKilled);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error in WMI cleanup on pass {Pass}", pass);
                }

                // Check if any UE processes remain
                var remainingProcesses = GetRemainingUEProcesses();
                if (remainingProcesses.Count == 0)
                {
                    Log.Information("All UE processes cleared after pass {Pass}", pass);
                    break;
                }
                else
                {
                    Log.Warning("Pass {Pass}: {Count} UE processes still running: {Processes}", 
                        pass, remainingProcesses.Count, string.Join(", ", remainingProcesses.Select(p => $"{p.ProcessName}({p.Id})")));
                    
                    if (pass < 3)
                    {
                        Thread.Sleep(1500); // Wait before next pass
                    }
                }
            }

            // Final verification
            var finalCheck = GetRemainingUEProcesses();
            if (finalCheck.Count > 0)
            {
                Log.Warning("WARNING: {Count} UE processes still remain after complete cleanup: {Processes}",
                    finalCheck.Count, string.Join(", ", finalCheck.Select(p => $"{p.ProcessName}({p.Id})")));
            }
            else
            {
                Log.Information("SUCCESS: All UE processes have been eliminated");
            }

            Log.Warning("Complete UE cleanup finished. Total processes affected: {TotalKilled}", totalKilled);
            return totalKilled;
        }

        /// <summary>
        /// 获取系统中剩余的UE相关进程
        /// </summary>
        public List<Process> GetRemainingUEProcesses()
        {
            List<Process> ueProcesses = new();
            
            try
            {
                string[] ueProcessPatterns = {
                    "UnrealEngine",
                    "UnrealGame",
                    "UE4",
                    "UE5", 
                    "UE_",
                    "Unreal",
                    "UELaunch",
                    "crashreportclient"
                };

                var allProcesses = Process.GetProcesses();
                
                foreach (var process in allProcesses)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            foreach (var pattern in ueProcessPatterns)
                            {
                                if (process.ProcessName.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    ueProcesses.Add(process);
                                    break; // 避免重复添加同一个进程
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "Error checking process {ProcessName}", process.ProcessName);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error getting remaining UE processes");
            }

            return ueProcesses;
        }

        /// <summary>
        /// 使用WMI强制终止UE进程 - 更强力的清理方法
        /// </summary>
        public int KillUEProcessesViaWMI()
        {
            Log.Warning("Using WMI to kill stubborn UE processes...");
            
            int killedCount = 0;
            
            try
            {
                string[] ueProcessPatterns = {
                    "UnrealEngine",
                    "UnrealGame", 
                    "UE4",
                    "UE5",
                    "UE_",
                    "Unreal",
                    "UELaunch",
                    "crashreportclient"
                };

                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Process"))
                {
                    using (var results = searcher.Get())
                    {
                        foreach (ManagementObject process in results)
                        {
                            try
                            {
                                var processName = process["Name"]?.ToString() ?? "";
                                var processId = process["ProcessId"]?.ToString() ?? "";
                                
                                // 检查是否匹配UE进程模式
                                bool isUEProcess = ueProcessPatterns.Any(pattern => 
                                    processName.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0);
                                
                                if (isUEProcess)
                                {
                                    Log.Warning("WMI terminating process: {ProcessName} (PID: {ProcessId})", 
                                        processName, processId);
                                    
                                    // 使用WMI终止进程
                                    var terminateResult = process.InvokeMethod("Terminate", null);
                                    if (terminateResult != null && terminateResult.ToString() == "0")
                                    {
                                        killedCount++;
                                        Log.Warning("WMI successfully terminated {ProcessName} (PID: {ProcessId})", 
                                            processName, processId);
                                    }
                                    else
                                    {
                                        Log.Warning("WMI termination failed for {ProcessName} (PID: {ProcessId}), result: {Result}", 
                                            processName, processId, terminateResult);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Debug(ex, "Error processing WMI object");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in WMI-based process cleanup");
            }

            Log.Warning("WMI cleanup completed. Killed {Count} processes", killedCount);
            return killedCount;
        }

        /// <summary>
        /// 专门检查和清理UnrealGame进程 - 增强版本
        /// </summary>
        public int KillUnrealGameProcesses()
        {
            Log.Warning("Scanning for UnrealGame processes with enhanced detection...");
            
            int killedCount = 0;
            
            try
            {
                // 使用多种方式查找UnrealGame进程
                var processes = Process.GetProcesses();
                var unrealGameProcesses = new List<Process>();
                
                foreach (var process in processes)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            // 检查进程名称
                            if (process.ProcessName.IndexOf("UnrealGame", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                unrealGameProcesses.Add(process);
                                continue;
                            }
                            
                            // 检查可执行文件路径（如果可访问）
                            try
                            {
                                var mainModule = process.MainModule;
                                if (mainModule?.FileName?.IndexOf("UnrealGame", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    unrealGameProcesses.Add(process);
                                }
                            }
                            catch (Exception)
                            {
                                // 某些进程可能无法访问MainModule，跳过
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "Error checking process {ProcessId} for UnrealGame", process.Id);
                    }
                }

                // 去重
                var uniqueProcesses = unrealGameProcesses.GroupBy(p => p.Id).Select(g => g.First()).ToList();
                
                foreach (var process in uniqueProcesses)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            Log.Warning("Force killing UnrealGame process: {ProcessName} (PID: {ProcessId})", 
                                process.ProcessName, process.Id);
                            
                            // 尝试多种终止方法
                            bool killed = false;
                            
                            // 方法1: Kill with process tree
                            try
                            {
                                process.Kill(true);
                                process.WaitForExit(2000);
                                if (process.HasExited)
                                {
                                    killed = true;
                                    killedCount++;
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Debug(ex, "Kill method 1 failed for process {ProcessId}", process.Id);
                            }
                            
                            // 方法2: 如果还没终止，尝试WMI方式
                            if (!killed && !process.HasExited)
                            {
                                try
                                {
                                    using (var searcher = new ManagementObjectSearcher($"SELECT * FROM Win32_Process WHERE ProcessId = {process.Id}"))
                                    {
                                        using (var results = searcher.Get())
                                        {
                                            foreach (ManagementObject wmiProcess in results)
                                            {
                                                var result = wmiProcess.InvokeMethod("Terminate", null);
                                                if (result?.ToString() == "0")
                                                {
                                                    killed = true;
                                                    killedCount++;
                                                    Log.Warning("WMI terminated UnrealGame process {ProcessId}", process.Id);
                                                }
                                                break;
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Log.Debug(ex, "WMI kill method failed for process {ProcessId}", process.Id);
                                }
                            }
                            
                            if (!killed)
                            {
                                Log.Error("Failed to terminate UnrealGame process {ProcessId} with all methods", process.Id);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to kill UnrealGame process {ProcessName} (PID: {ProcessId})", 
                            process.ProcessName, process.Id);
                    }
                    finally
                    {
                        try
                        {
                            process.Dispose();
                        }
                        catch { }
                    }
                }

                Log.Warning("Enhanced UnrealGame cleanup killed {Count} processes", killedCount);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in enhanced UnrealGame process scanning");
            }
            
            return killedCount;
        }

        /// <summary>
        /// 专门针对UnrealGame进程的重复强力清理
        /// 用于调试和确保UnrealGame彻底清理
        /// </summary>
        public int RepeatedUnrealGameCleanup(int maxAttempts = 5)
        {
            Log.Warning("Starting repeated UnrealGame cleanup with {MaxAttempts} attempts...", maxAttempts);
            
            int totalKilled = 0;
            
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                Log.Warning("UnrealGame cleanup attempt {Attempt}/{MaxAttempts}", attempt, maxAttempts);
                
                // 先检查是否还有UnrealGame进程
                var remainingProcesses = GetRemainingUnrealGameProcesses();
                if (remainingProcesses.Count == 0)
                {
                    Log.Information("No UnrealGame processes found on attempt {Attempt}", attempt);
                    break;
                }
                
                Log.Warning("Found {Count} UnrealGame processes on attempt {Attempt}: {Processes}", 
                    remainingProcesses.Count, attempt, 
                    string.Join(", ", remainingProcesses.Select(p => $"{p.ProcessName}({p.Id})")));
                
                // 方法1: 标准清理
                try
                {
                    var killed1 = KillUnrealGameProcesses();
                    totalKilled += killed1;
                    if (killed1 > 0)
                    {
                        Log.Warning("Attempt {Attempt}: Standard cleanup killed {Count}", attempt, killed1);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Standard cleanup failed on attempt {Attempt}", attempt);
                }
                
                Thread.Sleep(500); // 等待进程退出
                
                // 方法2: WMI清理
                try
                {
                    var killed2 = KillUnrealGameViaWMI();
                    totalKilled += killed2;
                    if (killed2 > 0)
                    {
                        Log.Warning("Attempt {Attempt}: WMI cleanup killed {Count}", attempt, killed2);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "WMI cleanup failed on attempt {Attempt}", attempt);
                }
                
                Thread.Sleep(500); // 等待进程退出
                
                // 方法3: CMD强杀
                try
                {
                    var killed3 = KillUnrealGameViaCmd();
                    totalKilled += killed3;
                    if (killed3 > 0)
                    {
                        Log.Warning("Attempt {Attempt}: CMD cleanup killed {Count}", attempt, killed3);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "CMD cleanup failed on attempt {Attempt}", attempt);
                }
                
                // 检查结果
                Thread.Sleep(1000);
                var afterCleanup = GetRemainingUnrealGameProcesses();
                if (afterCleanup.Count == 0)
                {
                    Log.Information("All UnrealGame processes eliminated after attempt {Attempt}", attempt);
                    break;
                }
                else if (attempt < maxAttempts)
                {
                    Log.Warning("Still {Count} UnrealGame processes remaining after attempt {Attempt}, trying again...", 
                        afterCleanup.Count, attempt);
                    Thread.Sleep(1000);
                }
            }
            
            // 最终检查
            var finalRemaining = GetRemainingUnrealGameProcesses();
            if (finalRemaining.Count > 0)
            {
                Log.Error("FAILED: {Count} UnrealGame processes still remain after {MaxAttempts} attempts: {Processes}",
                    finalRemaining.Count, maxAttempts, 
                    string.Join(", ", finalRemaining.Select(p => $"{p.ProcessName}({p.Id})")));
            }
            else
            {
                Log.Information("SUCCESS: All UnrealGame processes eliminated after repeated cleanup");
            }
            
            Log.Warning("Repeated UnrealGame cleanup completed. Total killed: {TotalKilled}", totalKilled);
            return totalKilled;
        }

        /// <summary>
        /// 获取系统中剩余的UnrealGame进程
        /// </summary>
        public List<Process> GetRemainingUnrealGameProcesses()
        {
            List<Process> unrealGameProcesses = new();
            
            try
            {
                var allProcesses = Process.GetProcesses();
                
                foreach (var process in allProcesses)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            // 检查进程名称
                            if (process.ProcessName.IndexOf("UnrealGame", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                unrealGameProcesses.Add(process);
                                continue;
                            }
                            
                            // 检查可执行文件路径（如果可访问）
                            try
                            {
                                var mainModule = process.MainModule;
                                if (mainModule?.FileName?.IndexOf("UnrealGame", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    unrealGameProcesses.Add(process);
                                }
                            }
                            catch (Exception)
                            {
                                // 某些进程可能无法访问MainModule，跳过
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "Error checking process {ProcessId} for UnrealGame", process.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error getting remaining UnrealGame processes");
            }

            return unrealGameProcesses;
        }

        /// <summary>
        /// 使用WMI专门清理UnrealGame进程
        /// </summary>
        public int KillUnrealGameViaWMI()
        {
            Log.Warning("Using WMI to kill UnrealGame processes...");
            
            int killedCount = 0;
            
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Process WHERE Name LIKE '%UnrealGame%'"))
                {
                    using (var results = searcher.Get())
                    {
                        foreach (ManagementObject process in results)
                        {
                            try
                            {
                                var processName = process["Name"]?.ToString() ?? "";
                                var processId = process["ProcessId"]?.ToString() ?? "";
                                
                                Log.Warning("WMI terminating UnrealGame process: {ProcessName} (PID: {ProcessId})", 
                                    processName, processId);
                                
                                var terminateResult = process.InvokeMethod("Terminate", null);
                                if (terminateResult != null && terminateResult.ToString() == "0")
                                {
                                    killedCount++;
                                    Log.Warning("WMI successfully terminated {ProcessName} (PID: {ProcessId})", 
                                        processName, processId);
                                }
                                else
                                {
                                    Log.Warning("WMI termination failed for {ProcessName} (PID: {ProcessId}), result: {Result}", 
                                        processName, processId, terminateResult);
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Error(ex, "Error processing WMI UnrealGame object");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in WMI-based UnrealGame cleanup");
            }

            Log.Warning("WMI UnrealGame cleanup completed. Killed {Count} processes", killedCount);
            return killedCount;
        }

        /// <summary>
        /// 使用CMD命令强杀UnrealGame进程
        /// </summary>
        public int KillUnrealGameViaCmd()
        {
            Log.Warning("Using CMD taskkill to kill UnrealGame processes...");
            
            int killedCount = 0;
            
            try
            {
                // 使用taskkill命令强制终止UnrealGame进程
                var startInfo = new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = "/f /im UnrealGame* /t",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process != null)
                    {
                        process.WaitForExit(5000); // 等待最多5秒
                        
                        var output = process.StandardOutput.ReadToEnd();
                        var error = process.StandardError.ReadToEnd();
                        
                        Log.Warning("CMD taskkill output: {Output}", output);
                        if (!string.IsNullOrEmpty(error))
                        {
                            Log.Warning("CMD taskkill error: {Error}", error);
                        }
                        
                        // 简单计数（如果输出包含"SUCCESS"字样）
                        var successCount = output.Split(new[] { "SUCCESS" }, StringSplitOptions.None).Length - 1;
                        killedCount = Math.Max(0, successCount);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error using CMD to kill UnrealGame processes");
            }

            Log.Warning("CMD UnrealGame cleanup completed. Killed approximately {Count} processes", killedCount);
            return killedCount;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                CleanupAllProcesses();
                _disposed = true;
            }
        }
    }
}
