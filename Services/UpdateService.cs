using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;

namespace SolusManifestApp.Services
{
    public class UpdateInfo
    {
        [JsonProperty("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("body")]
        public string Body { get; set; } = string.Empty;

        [JsonProperty("html_url")]
        public string HtmlUrl { get; set; } = string.Empty;

        [JsonProperty("published_at")]
        public DateTime PublishedAt { get; set; }

        [JsonProperty("assets")]
        public UpdateAsset[] Assets { get; set; } = Array.Empty<UpdateAsset>();
    }

    public class UpdateAsset
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;

        [JsonProperty("size")]
        public long Size { get; set; }
    }

    public class UpdateService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private const string GitHubApiUrl = "https://api.github.com/repos/{owner}/{repo}/releases/latest";
        private const string Owner = "MorrenusGames";
        private const string Repo = "Solus-Manifest-App";

        public UpdateService(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        private HttpClient CreateClient()
        {
            var client = _httpClientFactory.CreateClient("Default");
            if (!client.DefaultRequestHeaders.Contains("User-Agent"))
            {
                client.DefaultRequestHeaders.Add("User-Agent", "SolusManifestApp");
            }
            return client;
        }


        public string GetCurrentVersion()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version?.ToString() ?? "1.0.0";
        }

        public async Task<(bool hasUpdate, UpdateInfo? updateInfo)> CheckForUpdatesAsync()
        {
            try
            {
                var client = CreateClient();
                var url = GitHubApiUrl.Replace("{owner}", Owner).Replace("{repo}", Repo);
                var response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    return (false, null);
                }

                var json = await response.Content.ReadAsStringAsync();
                var updateInfo = JsonConvert.DeserializeObject<UpdateInfo>(json);

                if (updateInfo == null)
                {
                    return (false, null);
                }

                var currentVersion = GetCurrentVersion();
                var latestVersion = updateInfo.TagName.TrimStart('v');

                var hasUpdate = CompareVersions(currentVersion, latestVersion) < 0;

                return (hasUpdate, updateInfo);
            }
            catch
            {
                return (false, null);
            }
        }

        public async Task<string?> DownloadUpdateAsync(UpdateInfo updateInfo, IProgress<double>? progress = null)
        {
            try
            {
                // Find the SolusManifestApp.exe asset (no longer zipped)
                var exeAsset = updateInfo.Assets
                    .FirstOrDefault(a => a.Name.Equals("SolusManifestApp.exe", StringComparison.OrdinalIgnoreCase));

                if (exeAsset == null)
                {
                    // Fallback: try to find any zip file for backward compatibility
                    var zipAsset = updateInfo.Assets
                        .FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

                    if (zipAsset != null)
                    {
                        return await DownloadUpdateFromZipAsync(zipAsset, progress);
                    }

                    return null;
                }

                var tempExePath = Path.Combine(Path.GetTempPath(), "SolusManifestApp_Update.exe");

                // Download EXE directly
                var client = CreateClient();
                using (var response = await client.GetAsync(exeAsset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength ?? 0;
                    var downloadedBytes = 0L;

                    using var fileStream = new FileStream(tempExePath, FileMode.Create, FileAccess.Write, FileShare.None);
                    using var contentStream = await response.Content.ReadAsStreamAsync();

                    var buffer = new byte[8192];
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        downloadedBytes += bytesRead;

                        if (totalBytes > 0 && progress != null)
                        {
                            var progressPercent = (double)downloadedBytes / totalBytes * 100;
                            progress.Report(progressPercent);
                        }
                    }
                }

                return tempExePath;
            }
            catch
            {
                return null;
            }
        }

        // Fallback method for backward compatibility with old zip releases
        private async Task<string?> DownloadUpdateFromZipAsync(UpdateAsset zipAsset, IProgress<double>? progress = null)
        {
            try
            {
                var tempZipPath = Path.Combine(Path.GetTempPath(), "SolusManifestApp_Update.zip");
                var tempExtractPath = Path.Combine(Path.GetTempPath(), "SolusManifestApp_Update_Extract");

                // Download ZIP
                var client = CreateClient();
                using (var response = await client.GetAsync(zipAsset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength ?? 0;
                    var downloadedBytes = 0L;

                    using var fileStream = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    using var contentStream = await response.Content.ReadAsStreamAsync();

                    var buffer = new byte[8192];
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        downloadedBytes += bytesRead;

                        if (totalBytes > 0 && progress != null)
                        {
                            var progressPercent = (double)downloadedBytes / totalBytes * 100;
                            progress.Report(progressPercent);
                        }
                    }
                }

                // Extract ZIP
                if (Directory.Exists(tempExtractPath))
                {
                    Directory.Delete(tempExtractPath, true);
                }
                Directory.CreateDirectory(tempExtractPath);

                ZipFile.ExtractToDirectory(tempZipPath, tempExtractPath);

                // Find the exe in extracted files
                var exePath = Directory.GetFiles(tempExtractPath, "SolusManifestApp.exe", SearchOption.AllDirectories).FirstOrDefault();

                if (string.IsNullOrEmpty(exePath))
                {
                    return null;
                }

                // Move exe to final temp location
                var finalExePath = Path.Combine(Path.GetTempPath(), "SolusManifestApp_Update.exe");
                if (File.Exists(finalExePath))
                {
                    File.Delete(finalExePath);
                }
                File.Move(exePath, finalExePath);

                // Cleanup
                File.Delete(tempZipPath);
                Directory.Delete(tempExtractPath, true);

                return finalExePath;
            }
            catch
            {
                return null;
            }
        }

        public void InstallUpdate(string updatePath)
        {
            try
            {
                var currentExePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(currentExePath))
                    return;

                // Create a batch script to replace the exe after the app closes
                var batchPath = Path.Combine(Path.GetTempPath(), "update_solus.bat");
                var batchContent = $@"
@echo off
timeout /t 2 /nobreak > nul
del ""{currentExePath}""
move /y ""{updatePath}"" ""{currentExePath}""
start """" ""{currentExePath}""
del ""{batchPath}""
";

                File.WriteAllText(batchPath, batchContent);

                // Start the batch file and exit
                Process.Start(new ProcessStartInfo
                {
                    FileName = batchPath,
                    CreateNoWindow = true,
                    UseShellExecute = false
                });

                System.Windows.Application.Current.Shutdown();
            }
            catch
            {
                // Failed to install update
            }
        }

        private int CompareVersions(string current, string latest)
        {
            try
            {
                var currentParts = current.Split('.').Select(int.Parse).ToArray();
                var latestParts = latest.Split('.').Select(int.Parse).ToArray();

                var maxLength = Math.Max(currentParts.Length, latestParts.Length);

                for (int i = 0; i < maxLength; i++)
                {
                    var currentPart = i < currentParts.Length ? currentParts[i] : 0;
                    var latestPart = i < latestParts.Length ? latestParts[i] : 0;

                    if (currentPart < latestPart)
                        return -1;
                    if (currentPart > latestPart)
                        return 1;
                }

                return 0;
            }
            catch
            {
                return 0;
            }
        }
    }
}
