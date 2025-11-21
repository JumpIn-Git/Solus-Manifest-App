using SolusManifestApp.Helpers;
using SolusManifestApp.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SolusManifestApp.Services
{
    public class SteamGamesService
    {
        private readonly SteamService _steamService;

        public SteamGamesService(SteamService steamService)
        {
            _steamService = steamService;
        }

        public List<SteamGame> GetInstalledGames()
        {
            var games = new List<SteamGame>();

            try
            {
                var libraryFolders = GetLibraryFolders();

                foreach (var libraryPath in libraryFolders)
                {
                    var steamappsPath = Path.Combine(libraryPath, "steamapps");
                    if (!Directory.Exists(steamappsPath))
                        continue;

                    // Find all appmanifest files
                    var manifestFiles = Directory.GetFiles(steamappsPath, "appmanifest_*.acf");

                    foreach (var manifestFile in manifestFiles)
                    {
                        try
                        {
                            var game = ParseAppManifest(manifestFile, libraryPath);
                            if (game != null)
                            {
                                games.Add(game);
                            }
                        }
                        catch
                        {
                            // Skip invalid manifests
                        }
                    }
                }
            }
            catch
            {
                // Return empty list on error
            }

            // Remove duplicates by AppId (keep first occurrence)
            return games.GroupBy(g => g.AppId)
                       .Select(g => g.First())
                       .OrderBy(g => g.Name)
                       .ToList();
        }

        private List<string> GetLibraryFolders()
        {
            var folders = new List<string>();

            var steamPath = _steamService.GetSteamPath();
            if (string.IsNullOrEmpty(steamPath))
            {
                throw new Exception("Steam installation not found");
            }

            // Add main Steam folder
            folders.Add(steamPath);

            // Parse libraryfolders.vdf to find additional library locations
            // Try both possible locations (newer Steam uses steamapps, older uses config)
            var libraryFoldersFile = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(libraryFoldersFile))
            {
                libraryFoldersFile = Path.Combine(steamPath, "config", "libraryfolders.vdf");
            }

            if (File.Exists(libraryFoldersFile))
            {
                try
                {
                    var data = VdfParser.Parse(libraryFoldersFile);
                    var libraryFoldersObj = VdfParser.GetObject(data, "libraryfolders");

                    if (libraryFoldersObj != null)
                    {
                        // VDF format: "0" { "path" "C:\\..." }, "1" { "path" ... }, etc.
                        // Sometimes "0" gets flattened, so check if "path" exists directly
                        var directPath = VdfParser.GetValue(libraryFoldersObj, "path");
                        if (!string.IsNullOrEmpty(directPath) && Directory.Exists(directPath))
                        {
                            folders.Add(directPath);
                        }

                        // Also check numbered keys
                        for (int i = 0; i < 10; i++)
                        {
                            var folderData = VdfParser.GetObject(libraryFoldersObj, i.ToString());
                            if (folderData != null)
                            {
                                var path = VdfParser.GetValue(folderData, "path");
                                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                                {
                                    folders.Add(path);
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // If parsing fails, just use main Steam folder
                }
            }

            return folders.Distinct().ToList();
        }

        private SteamGame? ParseAppManifest(string manifestPath, string libraryPath)
        {
            try
            {
                var data = VdfParser.Parse(manifestPath);
                var appState = VdfParser.GetObject(data, "AppState");

                if (appState == null)
                    return null;

                var appId = VdfParser.GetValue(appState, "appid");
                var name = VdfParser.GetValue(appState, "name");
                var installDir = VdfParser.GetValue(appState, "installdir");

                // Try multiple size fields - Steam uses different ones depending on state
                var sizeOnDisk = VdfParser.GetLong(appState, "SizeOnDisk");
                if (sizeOnDisk == 0)
                {
                    sizeOnDisk = VdfParser.GetLong(appState, "BytesDownloaded");
                }
                if (sizeOnDisk == 0)
                {
                    sizeOnDisk = VdfParser.GetLong(appState, "BytesToDownload");
                }

                var lastUpdated = VdfParser.GetLong(appState, "LastUpdated");
                var stateFlags = VdfParser.GetValue(appState, "StateFlags");
                var buildId = VdfParser.GetValue(appState, "buildid");

                if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(name))
                    return null;

                var gamePath = Path.Combine(libraryPath, "steamapps", "common", installDir);

                // If size is still 0 and game is installed, try calculating from folder
                if (sizeOnDisk == 0 && Directory.Exists(gamePath))
                {
                    try
                    {
                        sizeOnDisk = CalculateFolderSize(gamePath);
                    }
                    catch
                    {
                        // Fallback failed, keep 0
                    }
                }

                var game = new SteamGame
                {
                    AppId = appId,
                    Name = name,
                    InstallDir = installDir,
                    SizeOnDisk = sizeOnDisk,
                    LastUpdated = lastUpdated > 0 ? DateTimeOffset.FromUnixTimeSeconds(lastUpdated).DateTime : null,
                    LibraryPath = gamePath,
                    StateFlags = stateFlags,
                    IsFullyInstalled = stateFlags == "4", // StateFlag 4 = Fully Installed
                    BuildId = buildId
                };

                return game;
            }
            catch
            {
                return null;
            }
        }

        public SteamGame? GetGameByAppId(string appId)
        {
            var games = GetInstalledGames();
            return games.FirstOrDefault(g => g.AppId == appId);
        }

        public long GetTotalGamesSize()
        {
            var games = GetInstalledGames();
            return games.Sum(g => g.SizeOnDisk);
        }

        public int GetTotalGamesCount()
        {
            return GetInstalledGames().Count;
        }

        public string? GetLocalIconPath(string appId)
        {
            var steamPath = _steamService.GetSteamPath();
            if (string.IsNullOrEmpty(steamPath))
                return null;

            var appcachePath = Path.Combine(steamPath, "appcache", "librarycache");
            if (!Directory.Exists(appcachePath))
                return null;

            // Try different icon formats Steam uses
            var iconFormats = new[]
            {
                $"{appId}_library_600x900.jpg",
                $"{appId}_library_600x900_2x.jpg",
                $"{appId}_icon.jpg",
                $"{appId}_logo.png",
                $"{appId}_header.jpg"
            };

            foreach (var format in iconFormats)
            {
                var iconPath = Path.Combine(appcachePath, format);
                if (File.Exists(iconPath))
                {
                    return iconPath;
                }
            }

            return null;
        }

        public string GetSteamCdnIconUrl(string appId)
        {
            // Primary format: library_600x900 (vertical poster)
            return $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/library_600x900.jpg";
        }

        public string GetSteamCdnHeaderUrl(string appId)
        {
            // Alternative format: header image (horizontal)
            return $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg";
        }

        public bool UninstallGame(string appId)
        {
            try
            {
                // Use Steam's uninstall protocol - this is safer and lets Steam handle cleanup
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = $"steam://uninstall/{appId}",
                    UseShellExecute = true
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static long CalculateFolderSize(string folderPath)
        {
            if (!Directory.Exists(folderPath))
                return 0;

            long totalSize = 0;

            try
            {
                // Calculate size of all files
                var files = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        totalSize += fileInfo.Length;
                    }
                    catch
                    {
                        // Skip files we can't access
                    }
                }
            }
            catch
            {
                // Return what we have so far
            }

            return totalSize;
        }
    }
}
