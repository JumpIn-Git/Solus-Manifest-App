using SolusManifestApp.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SolusManifestApp.Models;
using SolusManifestApp.Services;
using SolusManifestApp.Views.Dialogs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace SolusManifestApp.ViewModels
{
    public partial class LibraryViewModel : ObservableObject
    {
        private readonly FileInstallService _fileInstallService;
        private readonly SteamService _steamService;
        private readonly SteamGamesService _steamGamesService;
        private readonly ManifestApiService _manifestApiService;
        private readonly SettingsService _settingsService;
        private readonly CacheService _cacheService;
        private readonly NotificationService _notificationService;
        private readonly LuaFileManager _luaFileManager;
        private readonly ArchiveExtractionService _archiveExtractor;
        private readonly SteamApiService _steamApiService;
        private readonly LoggerService _logger;

        private List<LibraryItem> _allItems = new();

        [ObservableProperty]
        private ObservableCollection<LibraryItem> _displayedItems = new();

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private string _statusMessage = "No items";

        [ObservableProperty]
        private string _searchQuery = string.Empty;

        [ObservableProperty]
        private string _selectedFilter = "All";

        [ObservableProperty]
        private string _selectedSort = "Name";

        [ObservableProperty]
        private int _totalLua;

        [ObservableProperty]
        private int _totalSteamGames;

        [ObservableProperty]
        private int _totalGreenLuma;

        [ObservableProperty]
        private long _totalSize;

        [ObservableProperty]
        private bool _showLua = true;

        [ObservableProperty]
        private bool _showSteamGames = true;

        [ObservableProperty]
        private bool _isSelectMode;

        [ObservableProperty]
        private bool _isSteamToolsMode;

        [ObservableProperty]
        private bool _isGreenLumaMode;

        [ObservableProperty]
        private ObservableCollection<string> _filterOptions = new();

        public List<string> SortOptions { get; } = new() { "Name", "Size", "Install Date", "Last Updated" };

        public LibraryViewModel(
            FileInstallService fileInstallService,
            SteamService steamService,
            SteamGamesService steamGamesService,
            ManifestApiService manifestApiService,
            SettingsService settingsService,
            CacheService cacheService,
            NotificationService notificationService,
            LoggerService logger)
        {
            _fileInstallService = fileInstallService;
            _steamService = steamService;
            _logger = logger;
            _steamGamesService = steamGamesService;
            _manifestApiService = manifestApiService;
            _settingsService = settingsService;
            _cacheService = cacheService;
            _notificationService = notificationService;

            // Initialize new services
            var stpluginPath = _steamService.GetStPluginPath() ?? "";
            _luaFileManager = new LuaFileManager(stpluginPath);
            _archiveExtractor = new ArchiveExtractionService();
            _steamApiService = new SteamApiService(_cacheService);
        }

        partial void OnSearchQueryChanged(string value)
        {
            ApplyFilters();
        }

        partial void OnSelectedFilterChanged(string value)
        {
            UpdateVisibilityFilters();
            ApplyFilters();
        }

        partial void OnSelectedSortChanged(string value)
        {
            ApplyFilters();
        }

        private void UpdateVisibilityFilters()
        {
            ShowLua = SelectedFilter is "All" or "Lua Only" or "GreenLuma Only";
            ShowSteamGames = SelectedFilter is "All" or "Steam Games Only";
        }

        [RelayCommand]
        public async Task RefreshLibrary()
        {
            IsLoading = true;
            StatusMessage = "Loading library...";

            // Check mode for UI visibility
            var settings = _settingsService.LoadSettings();
            IsSteamToolsMode = settings.Mode == ToolMode.SteamTools;
            IsGreenLumaMode = settings.Mode == ToolMode.GreenLuma;

            // Update filter options based on mode
            FilterOptions.Clear();
            FilterOptions.Add("All");
            if (IsGreenLumaMode)
            {
                FilterOptions.Add("GreenLuma Only");
            }
            else
            {
                FilterOptions.Add("Lua Only");
            }
            FilterOptions.Add("Steam Games Only");

            // Reset filter to "All" if current filter doesn't exist in new options
            if (!FilterOptions.Contains(SelectedFilter))
            {
                SelectedFilter = "All";
            }

            try
            {
                _allItems.Clear();

                // Load Steam games to get actual sizes
                var steamGames = await Task.Run(() => _steamGamesService.GetInstalledGames());
                var steamGameDict = steamGames.ToDictionary(g => g.AppId, g => g);

                // Load Steam app list once (cached for 7 days, very fast)
                var steamAppList = await _steamApiService.GetAppListAsync();

                // Load lua files (only in Lua/SteamTools mode)
                if (settings.Mode != ToolMode.GreenLuma)
                {
                    var luaGames = await Task.Run(() => _fileInstallService.GetInstalledGames());

                    // Quick enrichment - use cache first, then Steam app list for names
                    foreach (var mod in luaGames)
                    {
                        // Try cache first
                        var cachedManifest = _cacheService.GetCachedManifest(mod.AppId);
                        if (cachedManifest != null)
                        {
                            mod.Name = cachedManifest.Name;
                            mod.Description = cachedManifest.Description;
                            mod.Version = cachedManifest.Version;
                            mod.IconUrl = cachedManifest.IconUrl;
                        }
                        else
                        {
                            // Get name from Steam app list (fast, no API call)
                            mod.Name = _steamApiService.GetGameName(mod.AppId, steamAppList);
                        }

                        // Check if this game is actually installed via Steam
                        if (steamGameDict.TryGetValue(mod.AppId, out var steamGame))
                        {
                            // Use actual Steam game size
                            mod.SizeBytes = steamGame.SizeOnDisk;
                        }
                        else
                        {
                            // Game not installed, show 0 bytes
                            mod.SizeBytes = 0;
                        }

                        var item = LibraryItem.FromGame(mod);
                        _allItems.Add(item);
                    }
                }

                // Load icons in background with throttling
                _ = Task.Run(async () =>
                {
                    var semaphore = new System.Threading.SemaphoreSlim(5, 5); // Limit to 5 concurrent downloads
                    var tasks = _allItems.Where(i => i.ItemType == LibraryItemType.Lua).Select(async item =>
                    {
                        await semaphore.WaitAsync();
                        try
                        {
                            _logger.Info($"Loading icon for {item.Name} (AppId: {item.AppId})");
                            var cdnIconUrl = _steamGamesService.GetSteamCdnIconUrl(item.AppId);
                            _logger.Debug($"Using CDN URL: {cdnIconUrl}");

                            var iconPath = await _cacheService.GetSteamGameIconAsync(item.AppId, null, cdnIconUrl);

                            if (!string.IsNullOrEmpty(iconPath))
                            {
                                _logger.Info($"✓ Icon loaded successfully for {item.Name}: {iconPath}");
                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    item.CachedIconPath = iconPath;
                                });
                            }
                            else
                            {
                                _logger.Warning($"✗ Failed to load icon for {item.Name} - No path returned");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Error($"✗ Exception loading icon for {item.Name}: {ex.Message}");
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    });

                    await Task.WhenAll(tasks);
                });

                // Load GreenLuma games (only in GreenLuma mode)
                if (settings.Mode == ToolMode.GreenLuma)
                {
                    try
                    {
                        string? customAppListPath = null;
                        if (settings.GreenLumaSubMode == GreenLumaMode.StealthAnyFolder)
                        {
                            var injectorDir = Path.GetDirectoryName(settings.DLLInjectorPath);
                            if (!string.IsNullOrEmpty(injectorDir))
                            {
                                customAppListPath = Path.Combine(injectorDir, "AppList");
                            }
                        }

                        var greenLumaGames = await Task.Run(() => _fileInstallService.GetGreenLumaGames(customAppListPath));

                        // Get list of AppIds already loaded (lua files)
                        var existingAppIds = _allItems.Select(i => i.AppId).ToHashSet();

                        foreach (var glGame in greenLumaGames)
                        {
                            // Skip if already have a lua entry for this game
                            if (!existingAppIds.Contains(glGame.AppId))
                            {
                                // Enrich with name from Steam app list if needed (if name is missing, generic, or just the AppID)
                                if (string.IsNullOrEmpty(glGame.Name) ||
                                    glGame.Name.StartsWith("App ") ||
                                    glGame.Name == glGame.AppId)
                                {
                                    glGame.Name = _steamApiService.GetGameName(glGame.AppId, steamAppList);
                                }

                                var item = LibraryItem.FromGreenLumaGame(glGame);
                                _allItems.Add(item);
                            }
                        }

                        // Load GreenLuma game icons in background
                        _ = Task.Run(async () =>
                        {
                            var semaphore = new System.Threading.SemaphoreSlim(5, 5);
                            var tasks = _allItems.Where(i => i.ItemType == LibraryItemType.GreenLuma).Select(async item =>
                            {
                                await semaphore.WaitAsync();
                                try
                                {
                                    var cdnIconUrl = _steamGamesService.GetSteamCdnIconUrl(item.AppId);
                                    var iconPath = await _cacheService.GetSteamGameIconAsync(item.AppId, null, cdnIconUrl);

                                    if (!string.IsNullOrEmpty(iconPath))
                                    {
                                        Application.Current.Dispatcher.Invoke(() =>
                                        {
                                            item.CachedIconPath = iconPath;
                                        });
                                    }
                                }
                                catch { }
                                finally
                                {
                                    semaphore.Release();
                                }
                            });

                            await Task.WhenAll(tasks);
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Failed to load GreenLuma games: {ex.Message}");
                    }
                }

                // Add Steam games that don't have lua files
                try
                {
                    if (steamGames.Count == 0)
                    {
                        StatusMessage = "No Steam games found. Check Steam installation.";
                    }

                    // Get list of AppIds that already have lua files or GreenLuma entries
                    var luaAppIds = _allItems.Where(i => i.ItemType == LibraryItemType.Lua || i.ItemType == LibraryItemType.GreenLuma)
                                             .Select(i => i.AppId)
                                             .ToHashSet();

                    // Only add Steam games that don't already have lua files
                    foreach (var steamGame in steamGames)
                    {
                        if (!luaAppIds.Contains(steamGame.AppId))
                        {
                            var item = LibraryItem.FromSteamGame(steamGame);
                            _allItems.Add(item);
                        }
                    }

                    // Load Steam game icons in background with throttling
                    _ = Task.Run(async () =>
                    {
                        var semaphore = new System.Threading.SemaphoreSlim(5, 5);
                        var tasks = _allItems.Where(i => i.ItemType == LibraryItemType.SteamGame).Select(async item =>
                        {
                            await semaphore.WaitAsync();
                            try
                            {
                                var localIconPath = _steamGamesService.GetLocalIconPath(item.AppId);
                                var cdnIconUrl = _steamGamesService.GetSteamCdnIconUrl(item.AppId);
                                var iconPath = await _cacheService.GetSteamGameIconAsync(item.AppId, localIconPath, cdnIconUrl);

                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    item.CachedIconPath = iconPath;
                                });
                            }
                            catch { }
                            finally
                            {
                                semaphore.Release();
                            }
                        });

                        await Task.WhenAll(tasks);
                    });
                }
                catch (Exception ex)
                {
                    _notificationService.ShowError($"Failed to load Steam games: {ex.Message}");
                }

                // Update statistics
                TotalLua = _allItems.Count(i => i.ItemType == LibraryItemType.Lua);
                TotalSteamGames = _allItems.Count(i => i.ItemType == LibraryItemType.SteamGame);
                TotalGreenLuma = _allItems.Count(i => i.ItemType == LibraryItemType.GreenLuma);
                TotalSize = _allItems.Sum(i => i.SizeBytes);

                ApplyFilters();

                StatusMessage = $"{_allItems.Count} item(s) loaded";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading library: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void ApplyFilters()
        {
            var filtered = _allItems.AsEnumerable();

            // Filter by type
            if (!ShowLua)
                filtered = filtered.Where(i => i.ItemType != LibraryItemType.Lua && i.ItemType != LibraryItemType.GreenLuma);
            if (!ShowSteamGames)
                filtered = filtered.Where(i => i.ItemType != LibraryItemType.SteamGame);

            // Search filter
            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                var query = SearchQuery.ToLower();
                filtered = filtered.Where(i =>
                    i.Name.ToLower().Contains(query) ||
                    i.AppId.ToLower().Contains(query) ||
                    i.Description.ToLower().Contains(query));
            }

            // Sort
            filtered = SelectedSort switch
            {
                "Size" => filtered.OrderByDescending(i => i.SizeBytes),
                "Install Date" => filtered.OrderByDescending(i => i.InstallDate),
                "Last Updated" => filtered.OrderByDescending(i => i.LastUpdated),
                _ => filtered.OrderBy(i => i.Name)
            };

            DisplayedItems = new ObservableCollection<LibraryItem>(filtered);
            StatusMessage = $"{DisplayedItems.Count} of {_allItems.Count} item(s)";
        }

        [RelayCommand]
        private async Task UninstallItem(LibraryItem item)
        {
            var itemType = item.ItemType switch
            {
                LibraryItemType.Lua => "lua file",
                LibraryItemType.GreenLuma => "GreenLuma game",
                _ => "Steam game"
            };

            var message = item.ItemType switch
            {
                LibraryItemType.Lua => "This will remove the lua file from your system.",
                LibraryItemType.GreenLuma => "This will remove ALL related files:\n- All AppList entries (main app + DLC depots)\n- ACF file\n- Depot keys from Config.VDF\n- .lua file (if exists)",
                _ => "This will delete the game files and remove it from Steam."
            };

            var result = MessageBoxHelper.Show(
                $"Are you sure you want to uninstall {item.Name}?\n\n{message}",
                "Confirm Uninstall",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    bool success = false;

                    if (item.ItemType == LibraryItemType.Lua)
                    {
                        success = await Task.Run(() => _fileInstallService.UninstallGame(item.AppId));
                    }
                    else if (item.ItemType == LibraryItemType.SteamGame)
                    {
                        success = await Task.Run(() => _steamGamesService.UninstallGame(item.AppId));
                    }
                    else if (item.ItemType == LibraryItemType.GreenLuma)
                    {
                        var settings = _settingsService.LoadSettings();
                        string? customAppListPath = null;
                        if (settings.GreenLumaSubMode == GreenLumaMode.StealthAnyFolder)
                        {
                            var injectorDir = Path.GetDirectoryName(settings.DLLInjectorPath);
                            if (!string.IsNullOrEmpty(injectorDir))
                            {
                                customAppListPath = Path.Combine(injectorDir, "AppList");
                            }
                        }

                        success = await _fileInstallService.UninstallGreenLumaGameAsync(item.AppId, customAppListPath);
                    }

                    if (success)
                    {
                        _allItems.Remove(item);
                        ApplyFilters();

                        // Update statistics
                        TotalLua = _allItems.Count(i => i.ItemType == LibraryItemType.Lua);
                        TotalSteamGames = _allItems.Count(i => i.ItemType == LibraryItemType.SteamGame);
                        TotalGreenLuma = _allItems.Count(i => i.ItemType == LibraryItemType.GreenLuma);
                        TotalSize = _allItems.Sum(i => i.SizeBytes);

                        _notificationService.ShowSuccess($"{item.Name} uninstalled successfully");
                    }
                    else
                    {
                        _notificationService.ShowError($"Failed to uninstall {item.Name}");
                    }
                }
                catch (Exception ex)
                {
                    _notificationService.ShowError($"Failed to uninstall: {ex.Message}");
                }
            }
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

        [RelayCommand]
        private void ToggleSelectMode()
        {
            IsSelectMode = !IsSelectMode;
            if (!IsSelectMode)
            {
                // Deselect all
                foreach (var item in _allItems)
                {
                    item.IsSelected = false;
                }
            }
        }

        [RelayCommand]
        private void SelectAll()
        {
            foreach (var item in DisplayedItems)
            {
                item.IsSelected = true;
            }
        }

        [RelayCommand]
        private void DeselectAll()
        {
            foreach (var item in DisplayedItems)
            {
                item.IsSelected = false;
            }
        }

        [RelayCommand]
        private async Task UninstallSelected()
        {
            var selected = DisplayedItems.Where(i => i.IsSelected).ToList();
            if (!selected.Any())
            {
                MessageBoxHelper.Show("No items selected", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var luaCount = selected.Count(i => i.ItemType == LibraryItemType.Lua);
            var gameCount = selected.Count(i => i.ItemType == LibraryItemType.SteamGame);
            var message = luaCount > 0 && gameCount > 0
                ? $"Are you sure you want to uninstall {luaCount} lua file(s) and {gameCount} Steam game(s)?"
                : luaCount > 0
                    ? $"Are you sure you want to uninstall {luaCount} lua file(s)?"
                    : $"Are you sure you want to uninstall {gameCount} Steam game(s)?";

            var result = MessageBoxHelper.Show(
                message,
                "Confirm Batch Uninstall",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                int successCount = 0;
                foreach (var item in selected)
                {
                    try
                    {
                        bool success = false;

                        if (item.ItemType == LibraryItemType.Lua)
                        {
                            success = await Task.Run(() => _fileInstallService.UninstallGame(item.AppId));
                        }
                        else if (item.ItemType == LibraryItemType.SteamGame)
                        {
                            success = await Task.Run(() => _steamGamesService.UninstallGame(item.AppId));
                        }

                        if (success)
                        {
                            _allItems.Remove(item);
                            successCount++;
                        }
                    }
                    catch { }
                }

                ApplyFilters();
                TotalLua = _allItems.Count(i => i.ItemType == LibraryItemType.Lua);
                TotalSteamGames = _allItems.Count(i => i.ItemType == LibraryItemType.SteamGame);
                TotalGreenLuma = _allItems.Count(i => i.ItemType == LibraryItemType.GreenLuma);
                TotalSize = _allItems.Sum(i => i.SizeBytes);

                _notificationService.ShowSuccess($"{successCount} item(s) uninstalled successfully");
                IsSelectMode = false;
            }
        }

        [RelayCommand]
        private void OpenInExplorer(LibraryItem item)
        {
            try
            {
                string? pathToOpen = null;

                // Try to find the path based on item type
                if (!string.IsNullOrEmpty(item.LocalPath) && (File.Exists(item.LocalPath) || Directory.Exists(item.LocalPath)))
                {
                    pathToOpen = item.LocalPath;
                }
                else if (item.ItemType == LibraryItemType.Lua)
                {
                    // Try to find the .lua file
                    var stpluginPath = _steamService.GetStPluginPath();
                    if (!string.IsNullOrEmpty(stpluginPath))
                    {
                        var luaFile = Path.Combine(stpluginPath, $"{item.AppId}.lua");
                        if (File.Exists(luaFile))
                        {
                            pathToOpen = luaFile;
                        }
                        else
                        {
                            var luaFileDisabled = Path.Combine(stpluginPath, $"{item.AppId}.lua.disabled");
                            if (File.Exists(luaFileDisabled))
                            {
                                pathToOpen = luaFileDisabled;
                            }
                        }
                    }
                }
                else if (item.ItemType == LibraryItemType.SteamGame)
                {
                    // Try to find the Steam game folder
                    var steamGames = _steamGamesService.GetInstalledGames();
                    var steamGame = steamGames.FirstOrDefault(g => g.AppId == item.AppId);
                    if (steamGame != null && !string.IsNullOrEmpty(steamGame.LibraryPath) && Directory.Exists(steamGame.LibraryPath))
                    {
                        pathToOpen = steamGame.LibraryPath;
                    }
                }

                if (!string.IsNullOrEmpty(pathToOpen))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = File.Exists(pathToOpen) ? $"/select,\"{pathToOpen}\"" : $"\"{pathToOpen}\"",
                        UseShellExecute = true
                    });
                }
                else
                {
                    _notificationService.ShowWarning($"Could not find local path for {item.Name}");
                }
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"Failed to open explorer: {ex.Message}");
            }
        }

        [RelayCommand]
        private void ShowDetails(LibraryItem item)
        {
            try
            {
                // This will open a details window - to be implemented
                var details = $"Name: {item.Name}\n" +
                             $"App ID: {item.AppId}\n" +
                             $"Type: {item.TypeBadge}\n" +
                             $"Size: {item.SizeFormatted}\n" +
                             $"Install Date: {item.InstallDate?.ToString("yyyy-MM-dd HH:mm") ?? "Unknown"}\n" +
                             $"Last Updated: {item.LastUpdated?.ToString("yyyy-MM-dd HH:mm") ?? "Unknown"}\n" +
                             $"Path: {(string.IsNullOrEmpty(item.LocalPath) ? "Not available" : item.LocalPath)}";

                MessageBoxHelper.Show(details, $"Details: {item.Name}", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"Failed to show details: {ex.Message}");
            }
        }

        public string GetStatisticsSummary()
        {
            return $"Lua: {TotalLua} | Steam Games: {TotalSteamGames} | Total Size: {FormatBytes(TotalSize)}";
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        [RelayCommand]
        private async Task PatchAll()
        {
            try
            {
                var result = MessageBoxHelper.Show(
                    "This will patch all .lua files by commenting out setManifestid lines.\n\nContinue?",
                    "Patch All .lua Files",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                    return;

                IsLoading = true;
                StatusMessage = "Patching .lua files...";

                var (luaFiles, _) = _luaFileManager.FindLuaFiles();
                int patchedCount = 0;

                foreach (var luaFile in luaFiles)
                {
                    var patchResult = _luaFileManager.PatchLuaFile(luaFile);
                    if (patchResult == "patched")
                    {
                        patchedCount++;
                    }
                }

                _notificationService.ShowSuccess($"Patched {patchedCount} file(s). Restart Steam for changes to take effect.");
                await RefreshLibrary();
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"Failed to patch files: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task EnableGame(string appId)
        {
            try
            {
                var (success, message) = _luaFileManager.EnableGame(appId);
                if (success)
                {
                    _notificationService.ShowSuccess(message);
                    await RefreshLibrary();
                }
                else
                {
                    _notificationService.ShowError(message);
                }
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"Failed to enable game: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task DisableGame(string appId)
        {
            try
            {
                var (success, message) = _luaFileManager.DisableGame(appId);
                if (success)
                {
                    _notificationService.ShowSuccess(message);
                    await RefreshLibrary();
                }
                else
                {
                    _notificationService.ShowError(message);
                }
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"Failed to disable game: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task DeleteLua(string appId)
        {
            try
            {
                var result = MessageBoxHelper.Show(
                    $"Are you sure you want to permanently delete the .lua file for App ID {appId}?\n\nThis cannot be undone!",
                    "Confirm Delete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                    return;

                var (success, message) = _luaFileManager.DeleteLuaFile(appId);
                if (success)
                {
                    _notificationService.ShowSuccess(message);
                    await RefreshLibrary();
                }
                else
                {
                    _notificationService.ShowError(message);
                }
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"Failed to delete lua file: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task ProcessDroppedFiles(string[] filePaths)
        {
            try
            {
                var luaFiles = new List<string>();
                var tempDirs = new List<string>();

                foreach (var filePath in filePaths)
                {
                    if (filePath.ToLower().EndsWith(".lua"))
                    {
                        if (ArchiveExtractionService.IsValidLuaFilename(Path.GetFileName(filePath)))
                        {
                            luaFiles.Add(filePath);
                        }
                    }
                    else if (filePath.ToLower().EndsWith(".zip"))
                    {
                        var (archiveLuaFiles, tempDir) = _archiveExtractor.ExtractLuaFromArchive(filePath);
                        luaFiles.AddRange(archiveLuaFiles);
                        if (!string.IsNullOrEmpty(tempDir))
                        {
                            tempDirs.Add(tempDir);
                        }
                    }
                }

                if (luaFiles.Count == 0)
                {
                    _notificationService.ShowWarning("No valid .lua files found");
                    return;
                }

                // Copy files to stplug-in directory
                var stpluginPath = _steamService.GetStPluginPath();
                if (string.IsNullOrEmpty(stpluginPath))
                {
                    _notificationService.ShowError("Could not find Steam stplug-in directory");
                    return;
                }

                int copiedCount = 0;
                foreach (var luaFile in luaFiles)
                {
                    var fileName = Path.GetFileName(luaFile);
                    var destPath = Path.Combine(stpluginPath, fileName);

                    // Remove existing files
                    if (File.Exists(destPath))
                        File.Delete(destPath);
                    if (File.Exists(destPath + ".disabled"))
                        File.Delete(destPath + ".disabled");

                    File.Copy(luaFile, destPath, true);
                    copiedCount++;
                }

                // Cleanup temp directories
                foreach (var tempDir in tempDirs)
                {
                    _archiveExtractor.CleanupTempDirectory(tempDir);
                }

                _notificationService.ShowSuccess($"Successfully added {copiedCount} file(s)! Restart Steam for changes to take effect.");
                await RefreshLibrary();
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"Failed to process files: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task EnableAutoUpdates()
        {
            // Check if in SteamTools mode
            var settings = _settingsService.LoadSettings();
            if (settings.Mode != ToolMode.SteamTools)
            {
                _notificationService.ShowWarning("Auto-Update Enabler is only available in SteamTools mode");
                return;
            }

            try
            {
                // Get all .lua files
                var (luaFiles, _) = _luaFileManager.FindLuaFiles();
                if (luaFiles.Count == 0)
                {
                    _notificationService.ShowWarning("No .lua files found");
                    return;
                }

                // Build list of apps
                var selectableApps = new List<SelectableApp>();
                foreach (var luaFile in luaFiles)
                {
                    var appId = _luaFileManager.ExtractAppId(luaFile);
                    var name = await _steamApiService.GetGameNameAsync(appId) ?? $"App {appId}";

                    selectableApps.Add(new SelectableApp
                    {
                        AppId = appId,
                        Name = name,
                        IsSelected = false
                    });
                }

                // Show dialog
                var dialog = new UpdateEnablerDialog(selectableApps);
                var result = dialog.ShowDialog();

                if (result == true && dialog.SelectedApps.Count > 0)
                {
                    // Enable updates for selected apps
                    int successCount = 0;
                    int failCount = 0;

                    foreach (var app in dialog.SelectedApps)
                    {
                        var (success, _) = _luaFileManager.EnableAutoUpdatesForApp(app.AppId);
                        if (success)
                            successCount++;
                        else
                            failCount++;
                    }

                    if (failCount == 0)
                    {
                        _notificationService.ShowSuccess($"Successfully enabled auto-updates for {successCount} app(s)");
                    }
                    else
                    {
                        _notificationService.ShowWarning($"Enabled auto-updates for {successCount} app(s), {failCount} failed");
                    }
                }
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"Failed to enable auto-updates: {ex.Message}");
            }
        }
    }
}
