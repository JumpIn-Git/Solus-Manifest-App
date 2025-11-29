using SolusManifestApp.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SolusManifestApp.Services;
using SolusManifestApp.Views;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace SolusManifestApp.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly SteamService _steamService;
        private readonly SettingsService _settingsService;
        private readonly UpdateService _updateService;
        private readonly NotificationService _notificationService;
        private readonly Dictionary<string, UserControl> _cachedViews = new Dictionary<string, UserControl>();

        [ObservableProperty]
        private object? _currentPage;

        [ObservableProperty]
        private string _currentPageName = "Home";

        public HomeViewModel HomeViewModel { get; }
        public LuaInstallerViewModel LuaInstallerViewModel { get; }
        public LibraryViewModel LibraryViewModel { get; }
        public StoreViewModel StoreViewModel { get; }
        public DownloadsViewModel DownloadsViewModel { get; }
        public ToolsViewModel ToolsViewModel { get; }
        public SettingsViewModel SettingsViewModel { get; }
        public SupportViewModel SupportViewModel { get; }

        public MainViewModel(
            SteamService steamService,
            SettingsService settingsService,
            UpdateService updateService,
            NotificationService notificationService,
            HomeViewModel homeViewModel,
            LuaInstallerViewModel luaInstallerViewModel,
            LibraryViewModel libraryViewModel,
            StoreViewModel storeViewModel,
            DownloadsViewModel downloadsViewModel,
            ToolsViewModel toolsViewModel,
            SettingsViewModel settingsViewModel,
            SupportViewModel supportViewModel)
        {
            _steamService = steamService;
            _settingsService = settingsService;
            _updateService = updateService;
            _notificationService = notificationService;

            HomeViewModel = homeViewModel;
            LuaInstallerViewModel = luaInstallerViewModel;
            LibraryViewModel = libraryViewModel;
            StoreViewModel = storeViewModel;
            DownloadsViewModel = downloadsViewModel;
            ToolsViewModel = toolsViewModel;
            SettingsViewModel = settingsViewModel;
            SupportViewModel = supportViewModel;

            // Start at Home page
            CurrentPage = GetOrCreateView("Home", () => new HomePage { DataContext = HomeViewModel });
            CurrentPageName = "Home";
            HomeViewModel.RefreshMode();
        }

        private UserControl GetOrCreateView(string key, Func<UserControl> createView)
        {
            if (!_cachedViews.ContainsKey(key))
            {
                _cachedViews[key] = createView();
            }
            return _cachedViews[key];
        }

        private bool CanNavigateAway()
        {
            // Check if we're currently on settings page and have unsaved changes
            if (CurrentPageName == "Settings" && SettingsViewModel.HasUnsavedChanges)
            {
                var result = MessageBoxHelper.Show(
                    "You have unsaved changes. Do you want to leave without saving?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                return result == MessageBoxResult.Yes;
            }
            return true;
        }

        // Public method for navigation from external services (like TrayIcon)
        public void NavigateTo(string pageName)
        {
            switch (pageName.ToLower())
            {
                case "home":
                    NavigateToHome();
                    break;
                case "installer":
                    NavigateToInstaller();
                    break;
                case "library":
                    NavigateToLibrary();
                    break;
                case "store":
                    NavigateToStore();
                    break;
                case "downloads":
                    NavigateToDownloads();
                    break;
                case "tools":
                    NavigateToTools();
                    break;
                case "settings":
                    NavigateToSettings();
                    break;
                case "support":
                    NavigateToSupport();
                    break;
            }
        }

        [RelayCommand]
        private void NavigateToHome()
        {
            if (!CanNavigateAway()) return;

            CurrentPage = GetOrCreateView("Home", () => new HomePage { DataContext = HomeViewModel });
            CurrentPageName = "Home";
            HomeViewModel.RefreshMode();
        }

        [RelayCommand]
        private void NavigateToInstaller()
        {
            if (!CanNavigateAway()) return;

            CurrentPage = GetOrCreateView("Installer", () => new LuaInstallerPage { DataContext = LuaInstallerViewModel });
            CurrentPageName = "Installer";
            LuaInstallerViewModel.RefreshMode();
        }

        [RelayCommand]
        private void NavigateToLibrary()
        {
            if (!CanNavigateAway()) return;

            CurrentPage = GetOrCreateView("Library", () => new LibraryPage { DataContext = LibraryViewModel });
            CurrentPageName = "Library";
            // Load from cache async - now properly optimized
            _ = LibraryViewModel.LoadFromCache();
        }

        [RelayCommand]
        private void NavigateToStore()
        {
            if (!CanNavigateAway()) return;

            CurrentPage = GetOrCreateView("Store", () => new StorePage { DataContext = StoreViewModel });
            CurrentPageName = "Store";
            // Check API key when navigating to Store
            StoreViewModel.OnNavigatedTo();
        }

        [RelayCommand]
        private void NavigateToDownloads()
        {
            if (!CanNavigateAway()) return;

            CurrentPage = GetOrCreateView("Downloads", () => new DownloadsPage { DataContext = DownloadsViewModel });
            CurrentPageName = "Downloads";
        }

        [RelayCommand]
        private void NavigateToTools()
        {
            if (!CanNavigateAway()) return;

            CurrentPage = GetOrCreateView("Tools", () => new ToolsPage { DataContext = ToolsViewModel });
            CurrentPageName = "Tools";
        }

        [RelayCommand]
        private void NavigateToSettings()
        {
            if (!CanNavigateAway()) return;

            CurrentPage = GetOrCreateView("Settings", () => new SettingsPage { DataContext = SettingsViewModel });
            CurrentPageName = "Settings";
        }

        [RelayCommand]
        private void NavigateToSupport()
        {
            if (!CanNavigateAway()) return;

            CurrentPage = GetOrCreateView("Support", () => new SupportPage { DataContext = SupportViewModel });
            CurrentPageName = "Support";
        }

        [RelayCommand]
        private void MinimizeWindow(Window window)
        {
            window.WindowState = WindowState.Minimized;
        }

        [RelayCommand]
        private void MaximizeWindow(Window window)
        {
            window.WindowState = window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        [RelayCommand]
        private void CloseWindow(Window window)
        {
            window.Close();
        }

        [RelayCommand]
        private void RestartSteam()
        {
            try
            {
                _steamService.RestartSteam();
                _notificationService.ShowSuccess("Steam is restarting...");
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"Failed to restart Steam: {ex.Message}");
            }
        }

        public async void CheckForUpdates()
        {
            var (hasUpdate, updateInfo) = await _updateService.CheckForUpdatesAsync();
            if (hasUpdate && updateInfo != null)
            {
                var result = MessageBoxHelper.Show(
                    $"A new version ({updateInfo.TagName}) is available!\n\nWould you like to download and install it now?\n\nCurrent version: {_updateService.GetCurrentVersion()}",
                    "Update Available",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    await DownloadAndInstallUpdateAsync(updateInfo);
                }
            }
            else
            {
                MessageBoxHelper.Show(
                    "You are running the latest version!",
                    "No Updates Available",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private async Task DownloadAndInstallUpdateAsync(UpdateInfo updateInfo)
        {
            try
            {
                _notificationService.ShowNotification("Downloading Update", "Downloading the latest version...", NotificationType.Info);

                var progress = new Progress<double>(percent =>
                {
                    _notificationService.ShowNotification("Downloading Update", $"Progress: {percent:F1}%", NotificationType.Info);
                });

                var updatePath = await _updateService.DownloadUpdateAsync(updateInfo, progress);

                if (!string.IsNullOrEmpty(updatePath))
                {
                    var result = MessageBoxHelper.Show(
                        "Update downloaded successfully! The application will now restart to install the update.",
                        "Update Ready",
                        MessageBoxButton.OKCancel,
                        MessageBoxImage.Information);

                    if (result == MessageBoxResult.OK)
                    {
                        _updateService.InstallUpdate(updatePath);
                    }
                }
                else
                {
                    _notificationService.ShowError("Failed to download update. Please try again or download manually from GitHub.");
                }
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"Failed to download update: {ex.Message}");
            }
        }
    }
}
