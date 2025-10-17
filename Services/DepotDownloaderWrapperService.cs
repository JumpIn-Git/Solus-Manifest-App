using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DepotDownloader;

namespace SolusManifestApp.Services
{
    public class DownloadProgressEventArgs : EventArgs
    {
        public string JobId { get; set; } = "";
        public double Progress { get; set; }
        public long DownloadedBytes { get; set; }
        public long TotalBytes { get; set; }
        public double Speed { get; set; }
        public int ProcessedFiles { get; set; }
        public int TotalFiles { get; set; }
        public string CurrentFile { get; set; } = "";
        public long CurrentFileSize { get; set; }
    }

    public class DownloadStatusEventArgs : EventArgs
    {
        public string JobId { get; set; } = "";
        public string Status { get; set; } = "";
        public string Message { get; set; } = "";
    }

    public class DownloadCompletedEventArgs : EventArgs
    {
        public string JobId { get; set; } = "";
        public bool Success { get; set; }
        public string Message { get; set; } = "";
    }

    public class LogMessageEventArgs : EventArgs
    {
        public string Message { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public class DepotDownloaderWrapperService
    {
        private static DepotDownloaderWrapperService? _instance;
        public static DepotDownloaderWrapperService Instance => _instance ??= new DepotDownloaderWrapperService();

        private readonly LoggerService _logger;

        // Events
        public event EventHandler<DownloadProgressEventArgs>? ProgressChanged;
        public event EventHandler<DownloadStatusEventArgs>? StatusChanged;
        public event EventHandler<DownloadCompletedEventArgs>? DownloadCompleted;
        public event EventHandler<LogMessageEventArgs>? LogMessage;

        private bool _isInitialized = false;
        private static bool _configInitialized = false;

        public DepotDownloaderWrapperService()
        {
            _logger = new LoggerService("DepotDownloader");
        }

        public async Task<bool> InitializeAsync(string username = "", string password = "")
        {
            if (_isInitialized)
                return true;

            LogInfo("Initializing Steam session...");

            try
            {
                // Initialize account settings store and config only once
                if (!_configInitialized)
                {
                    var appDataPath = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "SolusManifestApp",
                        "DepotDownloader"
                    );

                    System.IO.Directory.CreateDirectory(appDataPath);

                    // Initialize stores
                    AccountSettingsStore.LoadFromFile(System.IO.Path.Combine(appDataPath, "account.config"));
                    DepotConfigStore.LoadFromFile(System.IO.Path.Combine(appDataPath, "depot.config"));

                    // Initialize ContentDownloader config with defaults
                    ContentDownloader.Config.MaxDownloads = 8;
                    ContentDownloader.Config.CellID = 0;
                    ContentDownloader.Config.DownloadManifestOnly = false;
                    ContentDownloader.Config.RememberPassword = false;
                    ContentDownloader.Config.UseQrCode = false;
                    ContentDownloader.Config.SkipAppConfirmation = true;
                    ContentDownloader.Config.UsingFileList = false;
                    ContentDownloader.Config.FilesToDownload = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    ContentDownloader.Config.FilesToDownloadRegex = new List<System.Text.RegularExpressions.Regex>();

                    _configInitialized = true;
                }

                // Use anonymous login if no credentials provided
                var result = await Task.Run(() => ContentDownloader.InitializeSteam3(
                    string.IsNullOrEmpty(username) ? null : username,
                    string.IsNullOrEmpty(password) ? null : password
                ));

                if (result)
                {
                    _isInitialized = true;
                    LogInfo("Steam session initialized successfully");
                }
                else
                {
                    LogInfo("Failed to initialize Steam session");
                }

                return result;
            }
            catch (Exception ex)
            {
                LogInfo($"Error initializing Steam: {ex.Message}");
                _logger.Error($"DepotDownloader initialization error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> DownloadDepotsAsync(
            uint appId,
            List<(uint depotId, string depotKey, string? manifestFile)> depots,
            string targetDirectory,
            bool verifyFiles = true,
            int maxDownloads = 8,
            CancellationToken cancellationToken = default)
        {
            if (!_isInitialized)
            {
                LogInfo("Steam session not initialized. Please login first.");
                return false;
            }

            try
            {
                LogInfo($"Starting download for App ID: {appId}");
                StatusChanged?.Invoke(this, new DownloadStatusEventArgs
                {
                    JobId = appId.ToString(),
                    Status = "Downloading",
                    Message = $"Preparing to download App {appId}"
                });

                // Configure the download
                ContentDownloader.Config.InstallDirectory = targetDirectory;
                ContentDownloader.Config.VerifyAll = verifyFiles;
                ContentDownloader.Config.MaxDownloads = maxDownloads;
                ContentDownloader.Config.DownloadAllPlatforms = false;
                ContentDownloader.Config.DownloadAllArchs = false;
                ContentDownloader.Config.DownloadAllLanguages = false;

                // Set cancellation token
                ContentDownloader.ExternalCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                // Track depot progress
                int totalDepots = depots.Count;
                int currentDepotIndex = 0;

                // Subscribe to progress events
                EventHandler<DepotDownloader.DownloadProgressEventArgs>? progressHandler = null;
                progressHandler = (sender, e) =>
                {
                    // Calculate overall progress: (completed depots + current depot progress) / total depots
                    double overallProgress = ((currentDepotIndex + (e.Progress / 100.0)) / totalDepots) * 100.0;

                    ProgressChanged?.Invoke(this, new DownloadProgressEventArgs
                    {
                        JobId = appId.ToString(),
                        Progress = overallProgress,
                        DownloadedBytes = (long)e.DownloadedBytes,
                        TotalBytes = (long)e.TotalBytes,
                        ProcessedFiles = e.ProcessedFiles,
                        TotalFiles = e.TotalFiles,
                        CurrentFile = e.CurrentFile ?? ""
                    });
                };

                ContentDownloader.ProgressUpdated += progressHandler;

                try
                {
                    // Load depot keys
                    foreach (var (depotId, depotKey, manifestFile) in depots)
                    {
                        if (!string.IsNullOrEmpty(depotKey))
                        {
                            DepotKeyStore.AddKey($"{depotId};{depotKey}");
                            LogInfo($"Loaded depot key for {depotId}");
                        }
                    }

                    // Download each depot
                    foreach (var (depotId, depotKey, manifestFile) in depots)
                    {
                        LogInfo($"Starting depot {depotId} download ({currentDepotIndex + 1}/{totalDepots})...");

                        var depotList = new List<(uint depotId, ulong manifestId)>
                        {
                            (depotId, ContentDownloader.INVALID_MANIFEST_ID)
                        };

                        // If manifest file is provided, use it
                        if (!string.IsNullOrEmpty(manifestFile) && System.IO.File.Exists(manifestFile))
                        {
                            ContentDownloader.Config.UseManifestFile = true;
                            ContentDownloader.Config.ManifestFile = manifestFile;
                        }

                        await ContentDownloader.DownloadAppAsync(
                            appId,
                            depotList,
                            ContentDownloader.DEFAULT_BRANCH,
                            null, // os
                            null, // arch
                            null, // language
                            false,
                            false
                        );

                        // Reset manifest file config
                        ContentDownloader.Config.UseManifestFile = false;
                        ContentDownloader.Config.ManifestFile = null;

                        // Increment depot index for next depot
                        currentDepotIndex++;
                        LogInfo($"Completed depot {depotId} ({currentDepotIndex}/{totalDepots})");
                    }

                    LogInfo($"Download completed for App ID: {appId}");
                    DownloadCompleted?.Invoke(this, new DownloadCompletedEventArgs
                    {
                        JobId = appId.ToString(),
                        Success = true,
                        Message = "Download completed successfully"
                    });

                    return true;
                }
                finally
                {
                    ContentDownloader.ProgressUpdated -= progressHandler;
                }
            }
            catch (Exception ex)
            {
                LogInfo($"Download failed: {ex.Message}");
                _logger.Error($"DepotDownloader download error: {ex.Message}");

                DownloadCompleted?.Invoke(this, new DownloadCompletedEventArgs
                {
                    JobId = appId.ToString(),
                    Success = false,
                    Message = $"Download failed: {ex.Message}"
                });

                return false;
            }
        }

        private void LogInfo(string message)
        {
            _logger.Info($"[DepotDownloader] {message}");
            LogMessage?.Invoke(this, new LogMessageEventArgs { Message = message });
        }

        public void Shutdown()
        {
            if (_isInitialized)
            {
                ContentDownloader.ShutdownSteam3();
                _isInitialized = false;
                LogInfo("Steam session shutdown");
            }
        }
    }
}
