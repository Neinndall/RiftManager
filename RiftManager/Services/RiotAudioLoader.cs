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
            _logService.Log("[RiotAudioLoader] Iniciando nuevo proceso de búsqueda de audios en archivos MotionComic...");
            Directory.CreateDirectory(audioSavePath);

            string searchPath = Path.Combine(extractedAssetsPath, "Assets", "Prefabs", "Comics");

            if (!Directory.Exists(searchPath))
            {
                _logService.LogWarning($"[RiotAudioLoader] El directorio específico de cómics no existe: {searchPath}. No se buscarán audios.");
                return;
            }

            var letteringAudioUrls = new List<string>();
            var panelAudioUrls = new List<string>();
            string cleanBaseUrl = CleanBaseUrl(catalogBaseUrl);

            try
            {
                var jsonFiles = Directory.EnumerateFiles(searchPath, "*.json", SearchOption.TopDirectoryOnly);

                foreach (var jsonFile in jsonFiles)
                {
                    var fileName = Path.GetFileName(jsonFile);

                    try
                    {
                        string jsonContent = await File.ReadAllTextAsync(jsonFile);
                        JToken root = JToken.Parse(jsonContent);

                        if (fileName.StartsWith("MotionComicLettering", StringComparison.OrdinalIgnoreCase))
                        {
                            JToken clipNameToken = root.SelectToken("letteringSfx.clipName");
                            if (clipNameToken != null && clipNameToken.Type == JTokenType.String)
                            {
                                string clipName = clipNameToken.ToString();
                                if (!string.IsNullOrEmpty(clipName))
                                {
                                    letteringAudioUrls.Add($"{cleanBaseUrl}/AudioLocales/en_US/{clipName}.ogg");
                                }
                            }
                        }
                        else if (fileName.StartsWith("MotionComicPanel", StringComparison.OrdinalIgnoreCase))
                        {
                            JToken panelClipNameToken = root.SelectToken("panelSfx.clipName");
                            if (panelClipNameToken != null && panelClipNameToken.Type == JTokenType.String)
                            {
                                string clipName = panelClipNameToken.ToString();
                                if (!string.IsNullOrEmpty(clipName))
                                {
                                    panelAudioUrls.Add($"{cleanBaseUrl}/SoundFX/{clipName}.ogg");
                                }
                            }

                            if (root["audioEvents"] is JArray audioEvents)
                            {
                                foreach (var audioEvent in audioEvents)
                                {
                                    if (audioEvent["clipName"] is JToken eventClipNameElement)
                                    {
                                        string clipName = eventClipNameElement.ToString();
                                        if (!string.IsNullOrEmpty(clipName))
                                        {
                                            panelAudioUrls.Add($"{cleanBaseUrl}/SoundFX/{clipName}.ogg");
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (JsonException jsonEx)
                    {
                        _logService.LogWarning($"[RiotAudioLoader] Error al parsear el archivo JSON '{fileName}': {jsonEx.Message}. Saltando archivo.");
                    }
                    catch (Exception fileEx)
                    {
                        _logService.LogError($"[RiotAudioLoader] Error inesperado al procesar el archivo '{fileName}': {fileEx.Message}. Saltando archivo.");
                    }
                }

                if (letteringAudioUrls.Any())
                {
                    _logService.Log($"[RiotAudioLoader] Audios encontrados en MotionComicLettering: {letteringAudioUrls.Count}");
                }
                if (panelAudioUrls.Any())
                {
                    _logService.Log($"[RiotAudioLoader] Audios encontrados en MotionComicPanel: {panelAudioUrls.Count}");
                }

                var audioUrlsToDownload = new HashSet<string>(letteringAudioUrls.Concat(panelAudioUrls));

                if (!audioUrlsToDownload.Any())
                {
                    _logService.Log("[RiotAudioLoader] No se encontraron audios en ningún archivo MotionComic.");
                    return;
                }

                _logService.Log($"[RiotAudioLoader] Se encontraron {audioUrlsToDownload.Count} URLs de audio únicas en total. Iniciando descarga...");

                foreach (var audioUrl in audioUrlsToDownload)
                {
                    try
                    {
                        await _assetDownloader.DownloadAudio(audioUrl, audioSavePath);
                    }
                    catch (HttpRequestException httpEx)
                    {
                        _logService.LogError($"[RiotAudioLoader] Error HTTP ({(int?)httpEx.StatusCode}) al descargar {Path.GetFileName(audioUrl)}: {httpEx.Message}");
                    }
                    catch (Exception ex)
                    {
                        _logService.LogError($"[RiotAudioLoader] Error inesperado al descargar {Path.GetFileName(audioUrl)}: {ex.GetType().Name} - {ex.Message}");
                    }
                    await Task.Delay(100);
                }
                
                _logService.LogSuccess($"[RiotAudioLoader] Proceso de descarga de audios completado.");
            }
            catch (Exception ex)
            {
                _logService.LogError($"[RiotAudioLoader] Error fatal durante la búsqueda de archivos de audio: {ex.Message}");
            }
        }

        private string CleanBaseUrl(string catalogBaseUrl)
        {
            string baseUrl = catalogBaseUrl.TrimEnd('/');
            
            if (baseUrl.EndsWith("/WebGL", StringComparison.OrdinalIgnoreCase))
            {
                baseUrl = baseUrl[..^"/WebGL".Length];
            }
            
            if (baseUrl.EndsWith("/aa", StringComparison.OrdinalIgnoreCase))
            {
                baseUrl = baseUrl[..^"/aa".Length];
            }

            return baseUrl;
        }
    }
}