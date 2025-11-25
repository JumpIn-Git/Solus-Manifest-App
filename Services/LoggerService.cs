using SolusManifestApp.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SolusManifestApp.Services
{
    public class LoggerService : ILoggerService
    {
        private static readonly object _lock = new object();
        private readonly string _logFilePath;
        private const long MAX_LOG_SIZE = 8 * 1024 * 1024; // 8MB
        private const long TRIM_TO_SIZE = 6 * 1024 * 1024; // Trim to 6MB when rotating

        public LoggerService(string logName = "SolusManifestApp")
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SolusManifestApp"
            );
            Directory.CreateDirectory(appDataPath);

            // Use simple named log file (no timestamp)
            _logFilePath = Path.Combine(appDataPath, $"{logName}.log");

            Log("INFO", "Logger initialized");
            Log("INFO", $"Log file: {_logFilePath}");
        }

        public void Log(string level, string message)
        {
            lock (_lock)
            {
                try
                {
                    // Check if log file needs trimming
                    if (File.Exists(_logFilePath))
                    {
                        var fileInfo = new FileInfo(_logFilePath);
                        if (fileInfo.Length >= MAX_LOG_SIZE)
                        {
                            TrimLogFile();
                        }
                    }

                    var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    var logEntry = $"[{timestamp}] [{level}] {message}";

                    File.AppendAllText(_logFilePath, logEntry + Environment.NewLine);

                    // Also write to debug output for convenience
                    System.Diagnostics.Debug.WriteLine(logEntry);
                }
                catch
                {
                    // Silently fail if logging fails
                }
            }
        }

        private void TrimLogFile()
        {
            try
            {
                // Read all lines from the log file
                var allLines = File.ReadAllLines(_logFilePath);

                // Calculate how many lines to keep (approximate based on average line length)
                var currentSize = new FileInfo(_logFilePath).Length;
                var averageLineSize = currentSize / allLines.Length;
                var linesToKeep = (int)(TRIM_TO_SIZE / averageLineSize);

                // Keep only the newest lines
                var linesToWrite = allLines.Skip(Math.Max(0, allLines.Length - linesToKeep)).ToArray();

                // Write back the trimmed content
                File.WriteAllLines(_logFilePath, linesToWrite);
            }
            catch
            {
                // If trimming fails, try to at least clear the file
                try
                {
                    File.WriteAllText(_logFilePath, "");
                }
                catch
                {
                    // Silently fail
                }
            }
        }

        public void Info(string message) => Log("INFO", message);
        public void Debug(string message) => Log("DEBUG", message);
        public void Warning(string message) => Log("WARN", message);
        public void Error(string message) => Log("ERROR", message);

        public string GetLogFilePath() => _logFilePath;

        public string GetLogsFolderPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SolusManifestApp"
            );
        }

        public void OpenLogsFolder()
        {
            var logsFolderPath = GetLogsFolderPath();
            if (Directory.Exists(logsFolderPath))
            {
                System.Diagnostics.Process.Start("explorer.exe", logsFolderPath);
            }
        }

        public void ClearOldLogs()
        {
            try
            {
                var logsFolderPath = GetLogsFolderPath();
                if (!Directory.Exists(logsFolderPath))
                {
                    return;
                }

                // Clean up old timestamp-based log files from previous versions
                var oldLogFiles = Directory.GetFiles(logsFolderPath, "solus_*.log");
                foreach (var logFile in oldLogFiles)
                {
                    try
                    {
                        File.Delete(logFile);
                        Info($"Deleted old timestamp log file: {Path.GetFileName(logFile)}");
                    }
                    catch (Exception ex)
                    {
                        Error($"Failed to delete old log file {Path.GetFileName(logFile)}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Error($"Failed to clear old logs: {ex.Message}");
            }
        }
    }
}
