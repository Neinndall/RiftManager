using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using RiftManager.Utils;

namespace RiftManager.Services
{
    public class AssetDownloader
    {
        private readonly HttpClient _httpClient;
        private readonly LogService _logService;
        private readonly DirectoriesCreator _directoriesCreator;

        public AssetDownloader(HttpClient httpClient, LogService logService, DirectoriesCreator directoriesCreator)
        {
            _httpClient = httpClient;
            _logService = logService;
            _directoriesCreator = directoriesCreator;
        }
        
        // --- Method to Download Bundles ---
        public async Task DownloadBundle(string bundleUrl, string destinationFolder)
        {
            await DownloadFileCoreAsync(bundleUrl, destinationFolder, "bundle");
        }
 
        // --- Method to Download Normal Event Assets ---
        public async Task DownloadAsset(string assetUrl, string destinationFolder)
        {
            await DownloadFileCoreAsync(assetUrl, destinationFolder, "asset");
        }

        /// <summary>
        /// Downloads a distribution file (like a main JS or CSS) to a destination folder.
        /// </summary>
        /// <param name="distUrl">The full URL of the distribution file.</param>
        /// <param name="destinationFolder">The folder where the file will be saved.</param>
        public async Task DownloadDistFile(string distUrl, string destinationFolder)
        {
            await DownloadFileCoreAsync(distUrl, destinationFolder, "dist");
        }


        /// <summary>
        /// v1.2.0: single download core. Why: DownloadBundle/DownloadAsset/
        /// DownloadDistFile were 3 copies of the same 40-line block; fixes had to
        /// be applied 3 times. Behavior preserved (same logs/behavior per label,
        /// same throw-on-error contract relied upon by EventProcessor).
        /// </summary>
        private async Task DownloadFileCoreAsync(string fileUrl, string destinationFolder, string label)
        {
            string fileName = Path.GetFileName(new Uri(fileUrl).AbsolutePath);
            // Sanitize against invalid Windows filename chars from odd URLs.
            foreach (char c in Path.GetInvalidFileNameChars())
                fileName = fileName.Replace(c, '_');
            string fullDestinationPath = Path.Combine(destinationFolder, fileName);

            string directory = Path.GetDirectoryName(fullDestinationPath);
            _directoriesCreator.EnsureDirectoryExists(directory);

            if (File.Exists(fullDestinationPath))
            {
                _logService.LogWarning($"File {fileName} already exists, skipping download.");
                return;
            }

            _logService.Log(label == "dist" ? $"Downloading dist: {fileName}"
                : label == "bundle" ? $"Downloading bundle: {fileName}"
                : $"Downloading asset: {fileName}");
            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                using (Stream contentStream = await response.Content.ReadAsStreamAsync())
                using (FileStream fileStream = new FileStream(fullDestinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await contentStream.CopyToAsync(fileStream);
                }
            }
            catch (HttpRequestException httpEx)
            {
                if (httpEx.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logService.LogWarning($"{fileName} not found at {fileUrl}");
                }
                else
                {
                    _logService.LogError($"✗ HTTP Error for {fileName} ({(int?)httpEx.StatusCode}): {httpEx.Message}");
                }
                throw;
            }
            catch (Exception ex)
            {
                _logService.LogError($"✗ Unexpected error for {fileName}: {ex.GetType().Name}");
                throw;
            }
        }

        /// <summary>
        /// Downloads an asset from a manifest to a destination folder.
        /// </summary>
        /// <param name="assetUrl">The full URL of the manifest asset.</param>
        /// <param name="destinationDirectoryForGame">The base directory for the game (e.g., Assets/RiotClientAssets/arcane/).</param>
        public async Task DownloadAssetForManifest(string assetUrl, string destinationDirectoryForGame)
        {
            string fileName = Path.GetFileName(new Uri(assetUrl).AbsolutePath);
            string fullDestinationPath = Path.Combine(destinationDirectoryForGame, fileName);

            string directory = Path.GetDirectoryName(fullDestinationPath);
            _directoriesCreator.EnsureDirectoryExists(directory);
            _logService.Log($"Downloaded: {fileName}");
            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync(assetUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode(); 

                using (Stream contentStream = await response.Content.ReadAsStreamAsync())
                using (FileStream fileStream = new FileStream(fullDestinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await contentStream.CopyToAsync(fileStream);
                }
            }
            catch (HttpRequestException httpEx)
            {
                if (httpEx.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logService.LogDebug($"{fileName} not found at {assetUrl}");
                }
                else
                {
                    _logService.LogError($"✗ HTTP Error for {fileName} ({(int?)httpEx.StatusCode}): {httpEx.Message}");
                    throw; 
                }
            }
            catch (Exception ex)
            {
                _logService.LogError($"✗ Unexpected error for {fileName}: {ex.GetType().Name}");
                throw; 
            }
        }

        public async Task DownloadAudio(string audioUrl, string audioSavePath)
        {
            string fileName = Path.GetFileName(new Uri(audioUrl).AbsolutePath);
            string fullDestinationPath = Path.Combine(audioSavePath, fileName);

            string directory = Path.GetDirectoryName(fullDestinationPath);
            _directoriesCreator.EnsureDirectoryExists(directory);

            if (File.Exists(fullDestinationPath))
            {
                _logService.LogWarning($"Audio file {fileName} already exists, skipping download");
                return;
            }
            
            _logService.Log($"Downloading audio: {fileName}");
            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync(audioUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                using (Stream contentStream = await response.Content.ReadAsStreamAsync())
                using (FileStream fileStream = new FileStream(fullDestinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await contentStream.CopyToAsync(fileStream);
                }
            }
            catch (HttpRequestException httpEx)
            {
                if (httpEx.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logService.LogWarning($"{fileName} not found at {audioUrl}");
                }
                else
                {
                    _logService.LogError($"✗ HTTP Error for {fileName} ({(int?)httpEx.StatusCode}): {httpEx.Message}");
                }
                throw; 
            }
            catch (Exception ex)
            {
                _logService.LogError($"✗ Unexpected error for {fileName}: {ex.GetType().Name}");
                throw; 
            }
        }

    }
}