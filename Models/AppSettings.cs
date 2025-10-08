using System.Collections.Generic;

namespace SolusManifestApp.Models
{
    public enum ToolMode
    {
        SteamTools,
        GreenLuma
    }

    public enum GreenLumaMode
    {
        Normal,
        StealthAnyFolder,
        StealthUser32
    }

    public enum AppTheme
    {
        Default,
        Dark,
        Light,
        Cherry,
        Sunset,
        Forest,
        Grape,
        Cyberpunk
    }

    public enum AutoUpdateMode
    {
        Disabled,
        CheckOnly,
        AutoDownloadAndInstall
    }

    public class AppSettings
    {
        public string SteamPath { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string DownloadsPath { get; set; } = string.Empty;
        public bool AutoCheckUpdates { get; set; } = true; // Legacy - kept for compatibility
        public AutoUpdateMode AutoUpdate { get; set; } = AutoUpdateMode.CheckOnly;
        public bool MinimizeToTray { get; set; } = true;
        public bool AutoInstallAfterDownload { get; set; } = false;
        public bool ShowNotifications { get; set; } = true;
        public List<string> ApiKeyHistory { get; set; } = new List<string>();
        public ToolMode Mode { get; set; } = ToolMode.SteamTools;
        public GreenLumaMode GreenLumaSubMode { get; set; } = GreenLumaMode.Normal;
        public string AppListPath { get; set; } = string.Empty;
        public string DLLInjectorPath { get; set; } = string.Empty;
        public bool UseDefaultInstallLocation { get; set; } = true;
        public string SelectedLibraryFolder { get; set; } = string.Empty;
        public AppTheme Theme { get; set; } = AppTheme.Default;
        public double WindowWidth { get; set; } = 1400;
        public double WindowHeight { get; set; } = 850;
        public string ConfigVdfPath { get; set; } = string.Empty;
        public string CombinedKeysPath { get; set; } = string.Empty;
    }
}
