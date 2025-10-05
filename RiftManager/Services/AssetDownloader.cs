// RiftManager.Services/AssetDownloader.cs
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace RiftManager.Services
{
    public class AssetDownloader
    {
        private readonly HttpClient _httpClient;
        private readonly LogService _logService;

        public AssetDownloader(HttpClient httpClient, LogService logService)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        }
        
        // --- Method to Download Bundles ---
        public async Task DownloadBundle(string bundleUrl, string destinationFolder)
        {
            string fileName = Path.GetFileName(new Uri(bundleUrl).AbsolutePath);
            string fullDestinationPath = Path.Combine(destinationFolder, fileName);

            string? directory = Path.GetDirectoryName(fullDestinationPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            if (File.Exists(fullDestinationPath))
            {
                _logService.LogWarning($"File {fileName} already exists, skipping download.");
                return;
            }

            _logService.Log($"Downloading bundle: {fileName}");
            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync(bundleUrl, HttpCompletionOption.ResponseHeadersRead);
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
                    _logService.LogWarning($"{fileName} not found at {bundleUrl}");
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
 
        // --- Method to Download Normal Event Assets ---
        public async Task DownloadAsset(string assetUrl, string destinationFolder)
        {
            string fileName = Path.GetFileName(new Uri(assetUrl).AbsolutePath);
            string fullDestinationPath = Path.Combine(destinationFolder, fileName);

            string? directory = Path.GetDirectoryName(fullDestinationPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            if (File.Exists(fullDestinationPath))
            {
                _logService.LogWarning($"File {fileName} already exists, skipping download.");
                return;
            }

            _logService.Log($"Downloading asset: {fileName}");
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
                    _logService.LogWarning($"{fileName} not found at {assetUrl}");
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
        /// Downloads a distribution file (like a main JS or CSS) to a destination folder.
        /// </summary>
        /// <param name="distUrl">The full URL of the distribution file.</param>
        /// <param name="destinationFolder">The folder where the file will be saved.</param>
        public async Task DownloadDistFile(string distUrl, string destinationFolder)
        {
            string fileName = Path.GetFileName(new Uri(distUrl).AbsolutePath);
            string fullDestinationPath = Path.Combine(destinationFolder, fileName);

            string? directory = Path.GetDirectoryName(fullDestinationPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            if (File.Exists(fullDestinationPath))
            {
                _logService.LogWarning($"DIST file '{fileName}' already exists, skipping download.");
                return;
            }

            _logService.Log($"Downloading dist: {fileName}");
            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync(distUrl, HttpCompletionOption.ResponseHeadersRead);
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
                    _logService.LogWarning($"DIST file '{fileName}' not found at {distUrl}");
                }
                else
                {
                    _logService.LogError($"✗ HTTP Error downloading DIST '{fileName}' ({(int?)httpEx.StatusCode}): {httpEx.Message}");
                }
                throw; 
            }
            catch (Exception ex)
            {
                _logService.LogError($"✗ Unexpected error downloading DIST '{fileName}': {ex.GetType().Name}");
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

            string? directory = Path.GetDirectoryName(fullDestinationPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            _logService.Log($"Downloading manifest: {fileName}");
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
                    _logService.LogWarning($"{fileName} not found at {assetUrl}");
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

        public async Task DownloadAudio(string audioUrl, string audioSavePath)
        {
            string fileName = Path.GetFileName(new Uri(audioUrl).AbsolutePath);
            string fullDestinationPath = Path.Combine(audioSavePath, fileName);

            string? directory = Path.GetDirectoryName(fullDestinationPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

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