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
