using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RiftManager.Models;
using RiftManager.Services;

namespace RiftManager.Services
{
    public class RiotAudioLoader
    {
        private readonly JsonFetcherService _jsonFetcherService;
        private readonly LogService _logService;
        private readonly AssetDownloader _assetDownloader;

        public RiotAudioLoader(JsonFetcherService jsonFetcherService, LogService logService, AssetDownloader assetDownloader)
        {
            _jsonFetcherService = jsonFetcherService ?? throw new ArgumentNullException(nameof(jsonFetcherService));
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
            _assetDownloader = assetDownloader ?? throw new ArgumentNullException(nameof(assetDownloader));
        }

        public async Task ProcessAndDownloadAudioUrls(string extractedAssetsPath, string catalogBaseUrl, string audioSavePath)
        {
            _logService.Log("[RiotAudioLoader] Starting a new deep audio search in JSON assets...");
            Directory.CreateDirectory(audioSavePath);

            string searchPath = Path.Combine(extractedAssetsPath, "Assets", "Prefabs", "Comics");

            if (!Directory.Exists(searchPath))
            {
                _logService.LogWarning($"[RiotAudioLoader] Comic assets directory not found: {searchPath}. Audio extraction skipped.");
                return;
            }

            var audioUrlsToDownload = new HashSet<string>();
            string cleanBaseUrl = CleanBaseUrl(catalogBaseUrl);

            try
            {
                var jsonFiles = Directory.EnumerateFiles(searchPath, "*.json", SearchOption.TopDirectoryOnly);

                foreach (var jsonFile in jsonFiles)
                {
                    string jsonContent = await File.ReadAllTextAsync(jsonFile);
                    JToken root = JToken.Parse(jsonContent);

                    // Buscamos TODAS las ocurrencias de "clipName" en el JSON, sin importar la profundidad
                    var clipNames = root.SelectTokens("..clipName")
                                        .Where(t => t.Type == JTokenType.String)
                                        .Select(t => t.ToString())
                                        .Where(s => !string.IsNullOrEmpty(s))
                                        .Distinct();

                    foreach (var clipName in clipNames)
                    {
                        string folder = "SoundFX"; // Por defecto para SFX y MU
                        
                        if (clipName.Contains("_VO", StringComparison.OrdinalIgnoreCase))
                        {
                            folder = "AudioLocales/en_US";
                        }
                        
                        _logService.LogDebug($"[RiotAudioLoader] Routing clip '{clipName}' to folder: {folder}");
                        
                        string fullUrl = $"{cleanBaseUrl}/{folder}/{clipName}.ogg";
                        audioUrlsToDownload.Add(fullUrl);
                    }
                }

                if (!audioUrlsToDownload.Any())
                {
                    _logService.Log("[RiotAudioLoader] No audio clips found in JSON files.");
                    return;
                }

                _logService.Log($"[RiotAudioLoader] Found {audioUrlsToDownload.Count} unique audio assets. Starting download...");

                foreach (var audioUrl in audioUrlsToDownload)
                {
                    try
                    {
                        await _assetDownloader.DownloadAudio(audioUrl, audioSavePath);
                    }
                    catch (Exception ex)
                    {
                        _logService.LogError($"[RiotAudioLoader] Error downloading {Path.GetFileName(audioUrl)}: {ex.Message}");
                    }
                }
                
                _logService.LogSuccess($"[RiotAudioLoader] Audio download process completed.");
            }
            catch (Exception ex)
            {
                _logService.LogError($"[RiotAudioLoader] Fatal error during audio processing: {ex.Message}");
            }
        }

        private string CleanBaseUrl(string catalogBaseUrl)
        {
            // catalogBaseUrl suele ser .../StreamingAssets/aa/
            // Necesitamos llegar a .../StreamingAssets
            string baseUrl = catalogBaseUrl.TrimEnd('/');
            
            if (baseUrl.EndsWith("/aa", StringComparison.OrdinalIgnoreCase))
            {
                baseUrl = baseUrl[..^"/aa".Length];
            }
            
            return baseUrl;
        }
    }
}
