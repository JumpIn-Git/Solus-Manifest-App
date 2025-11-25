using SolusManifestApp.Services;

namespace SolusManifestApp.Interfaces
{
    public interface INotificationService
    {
        void ShowNotification(string title, string message, NotificationType type = NotificationType.Info);
        void ShowSuccess(string message, string title = "Success");
        void ShowWarning(string message, string title = "Warning");
        void ShowError(string message, string title = "Error");
        void ShowDownloadComplete(string gameName);
        void ShowInstallComplete(string gameName);
        void ShowUpdateAvailable(string version);
    }
}
