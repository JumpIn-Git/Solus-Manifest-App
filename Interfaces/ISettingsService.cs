using SolusManifestApp.Models;

namespace SolusManifestApp.Interfaces
{
    public interface ISettingsService
    {
        AppSettings LoadSettings();
        void SaveSettings(AppSettings settings);
        void AddApiKeyToHistory(string apiKey);
    }
}
