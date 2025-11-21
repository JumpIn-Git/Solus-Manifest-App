using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SolusManifestApp.Models
{
    public enum LibraryItemType
    {
        Lua,
        SteamGame,
        GreenLuma
    }

    public class LibraryItem : INotifyPropertyChanged
    {
        public string AppId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public DateTime? InstallDate { get; set; }
        public DateTime? LastUpdated { get; set; }
        public string IconUrl { get; set; } = string.Empty;

        private string? _cachedIconPath;
        public string? CachedIconPath
        {
            get => _cachedIconPath;
            set
            {
                if (_cachedIconPath != value)
                {
                    _cachedIconPath = value;
                    OnPropertyChanged();
                }
            }
        }

        private System.Windows.Media.Imaging.BitmapImage? _cachedBitmapImage;
        public System.Windows.Media.Imaging.BitmapImage? CachedBitmapImage
        {
            get => _cachedBitmapImage;
            set
            {
                if (_cachedBitmapImage != value)
                {
                    _cachedBitmapImage = value;
                    OnPropertyChanged();
                }
            }
        }

        public string LocalPath { get; set; } = string.Empty;
        public LibraryItemType ItemType { get; set; }
        public string Version { get; set; } = string.Empty;
        public bool IsSelected { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public string SizeFormatted => FormatBytes(SizeBytes);
        public string TypeBadge => ItemType switch
        {
            LibraryItemType.Lua => "LUA",
            LibraryItemType.GreenLuma => "GL",
            _ => "GAME"
        };

        private static string FormatBytes(long bytes)
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

        public static LibraryItem FromGame(Game game)
        {
            return new LibraryItem
            {
                AppId = game.AppId,
                Name = game.Name,
                Description = game.Description,
                SizeBytes = game.SizeBytes,
                InstallDate = game.InstallDate,
                LastUpdated = game.LastUpdated,
                IconUrl = game.IconUrl,
                LocalPath = game.LocalPath,
                ItemType = LibraryItemType.Lua,
                Version = game.Version
            };
        }

        public static LibraryItem FromSteamGame(SteamGame steamGame)
        {
            return new LibraryItem
            {
                AppId = steamGame.AppId,
                Name = steamGame.Name,
                SizeBytes = steamGame.SizeOnDisk,
                LastUpdated = steamGame.LastUpdated,
                LocalPath = steamGame.LibraryPath,
                ItemType = LibraryItemType.SteamGame
            };
        }

        public static LibraryItem FromGreenLumaGame(GreenLumaGame greenLumaGame)
        {
            return new LibraryItem
            {
                AppId = greenLumaGame.AppId,
                Name = greenLumaGame.Name,
                SizeBytes = greenLumaGame.SizeBytes,
                InstallDate = greenLumaGame.InstallDate,
                LastUpdated = greenLumaGame.LastUpdated,
                LocalPath = greenLumaGame.AcfPath,
                ItemType = LibraryItemType.GreenLuma
            };
        }
    }
}
