using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using RiftManager.Utils;

namespace RiftManager.Services
{
    public class RiotClientManifestService
    {
        private readonly JsonFetcherService _jsonFetcherService;
        private readonly AssetDownloader _assetDownloader;
        private readonly LogService _logService;
        private readonly string _lastManifestDownloadFilePath;
        private bool _isManifestDownloadRunning = false;

        public event Action<bool, string> StateChanged;

        public RiotClientManifestService(JsonFetcherService jsonFetcherService, LogService logService, AssetDownloader assetDownloader)
        {
            _jsonFetcherService = jsonFetcherService;
            _assetDownloader = assetDownloader;
            _logService = logService;
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appFolder = Path.Combine(appDataPath, "RiftManager");
            Directory.CreateDirectory(appFolder);
            _lastManifestDownloadFilePath = Path.Combine(appFolder, "last_manifest_download.txt");
        }

        public async Task UpdateManifestButtonStateAsync()
        {
            bool isButtonEnabled = true;
            string toolTipText = "Download Riot Client asset manifests.";

            if (File.Exists(_lastManifestDownloadFilePath))
            {
                try
                {
                    string[] lines = await File.ReadAllLinesAsync(_lastManifestDownloadFilePath);
                    if (lines.Length >= 2)
                    {
                        string storedDateStr = lines[0];
                        string storedHash = lines[1];
                        string expectedHash = Crypto.Md5HashEncode(storedDateStr);

                        if (storedHash == expectedHash && DateTime.TryParse(storedDateStr, out DateTime lastDownloadDate))
                        {
                            if (lastDownloadDate.Year == DateTime.Now.Year && lastDownloadDate.Month == DateTime.Now.Month)
                            {
                                isButtonEnabled = false;
                                DateTime nextAvailableDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1).AddMonths(1);
                                toolTipText = $"This has already been used this month. Next available on {nextAvailableDate:MM/dd/yyyy}.";
                            }
                        }
                        else
                        {
                            _logService.LogWarning("Manifest download timestamp has been tampered with. Resetting...");
                            File.Delete(_lastManifestDownloadFilePath);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logService.LogError($"Error checking manifest download timestamp: {ex.Message}");
                }
            }

            StateChanged?.Invoke(isButtonEnabled, toolTipText);
        }

        public async Task ProcessRiotClientManifestsWithButtonLogicAsync(string baseManifestsDownloadDirectory)
        {
            if (_isManifestDownloadRunning)
            {
                _logService.LogWarning("Manifest download is already in progress.");
                return;
            }

            _isManifestDownloadRunning = true;
            StateChanged?.Invoke(false, "Download Riot Client asset manifests.");

            try
            {
                await ProcessRiotClientManifests(baseManifestsDownloadDirectory);

                // Save timestamp on success
                string currentDate = DateTime.Now.ToString("o");
                string dateHash = Crypto.Md5HashEncode(currentDate);
                await File.WriteAllLinesAsync(_lastManifestDownloadFilePath, new[] { currentDate, dateHash });
                await UpdateManifestButtonStateAsync();
            }
            catch (Exception ex)
            {
                _logService.LogError($"Error downloading Riot manifests: {ex.Message}");
                StateChanged?.Invoke(true, "Download Riot Client asset manifests."); // Re-enable on failure
            }
            finally
            {
                _isManifestDownloadRunning = false;
            }
        }

        public async Task ProcessRiotClientManifests(string baseManifestsDownloadDirectory)
        {
            List<string> riotClientManifestUrls = new List<string>
            {
                "https://lol.secure.dyn.riotcdn.net/channels/public/rccontent/tft/theme/manifest.json",
                "https://riot-client.secure.dyn.riotcdn.net/channels/public/rccontent/arcane/theme/manifest.json",
                "https://wildrift.secure.dyn.riotcdn.net/channels/public/rccontent/theme/manifest.json",
                "https://valorant.secure.dyn.riotcdn.net/channels/public/rccontent/theme/03/manifest.json",
                "https://bacon.secure.dyn.riotcdn.net/channels/public/rccontent/theme/manifest.json",
                "https://lol.secure.dyn.riotcdn.net/channels/public/rccontent/theme/manifest_default.json",
                "https://riot-client.secure.dyn.riotcdn.net/channels/public/rccontent/theme/manifest_live.json",
            };

            Directory.CreateDirectory(baseManifestsDownloadDirectory);
            _logService.Log("Starting processing Riot Client Manifests...");
            foreach (var url in riotClientManifestUrls)
            {
                await ProcessSingleManifest(url, baseManifestsDownloadDirectory);
            }
            _logService.LogInteractiveSuccess("Downloaded completed: ", "Assets from Manifests", baseManifestsDownloadDirectory);
        }

        private async Task ProcessSingleManifest(string manifestUrl, string baseManifestsDownloadDirectory)
        {
            _logService.LogDebug($"Processing manifest: {manifestUrl}");
            try
            {
                JToken document = await _jsonFetcherService.GetJTokenAsync(manifestUrl);
                if (document == null)
                {
                    _logService.LogWarning($"The manifest could not be obtained: {manifestUrl}");
                    return;
                }

                // Obtener la URL base de forma similar a tu código antiguo
                // Esto recorta la parte final (ej. "manifest.json" o "03/manifest.json")
                var baseUrl = manifestUrl.Contains("manifest") 
                                ? manifestUrl.Substring(0, manifestUrl.LastIndexOf('/')) 
                                : manifestUrl;

                // Si la baseUrl no termina en '/', añadirlo. Esto es importante para la concatenación.
                if (!baseUrl.EndsWith("/"))
                {
                    baseUrl += "/";
                }
                
                // Aplanar el JToken directamente
                var flattenedManifest = ObjectHelper.FlattenObject((JObject)document);

                // Filtra solo los valores que son URLs de assets válidos.
                // Usamos .Values para obtener los objetos dentro del diccionario aplanado.
                var validAssets = flattenedManifest.Values
                                    .Select(v => v?.ToString()) // Convertir object a string
                                    .Where(value => !string.IsNullOrWhiteSpace(value) && IsValidAsset(value))
                                    .ToList();

                _logService.Log($"Found {validAssets.Count} valid assets in the manifest.");

                foreach (var assetPath in validAssets)
                {
                    // Aseguramos que assetPath no sea null antes de usarlo
                    if (assetPath == null) continue; 

                    // Construir la URL completa concatenando directamente, como en el código antiguo
                    // Asumimos que el HttpClient o el CDN de Riot resolverán los ".."
                    string fullUrl = $"{baseUrl}{assetPath}";

                    // Determinar el subdirectorio para guardar el archivo
                    string gameSubDir = GetSubDirectoryForUrl(manifestUrl);
                    string targetDir = Path.Combine(baseManifestsDownloadDirectory, gameSubDir);
                    
                    // Asegurarse de que el directorio de destino existe
                    Directory.CreateDirectory(targetDir);

                    _logService.LogDebug($"Trying to download: {fullUrl}");

                    try
                    {
                        // Usar tu AssetDownloader para descargar el archivo
                        await _assetDownloader.DownloadAssetForManifest(fullUrl, targetDir);
                        _logService.LogDebug($"Downloaded from: {fullUrl}"); 
                    }
                    catch (HttpRequestException httpEx) when (httpEx.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        _logService.LogWarning($"✗ Asset not found: {assetPath}");
                    }
                    catch (Exception ex)
                    {
                        _logService.LogError($"✗ Error downloading asset '{assetPath}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logService.LogError($"Error processing manifest '{manifestUrl}': {ex.Message}");
            }
        }

        private bool IsValidAsset(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            
            // Si el assetPath ya es una URL completa (empieza con http), y no queremos assets externos, retornar false.
            if (value.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return false; 

            // Lista de extensiones válidas
            string[] validExtensions = {    
                ".png", ".jpg", ".jpeg", ".webp",    
                ".svg", ".ico", ".webm", ".mp4",    
                ".mp3", ".ogg", ".wav", ".json" // Mantengo .json por si hay manifiestos que referencien otros JSONs
            };
            
            // Buscar cualquier extensión válida al final de la cadena
            return validExtensions.Any(ext =>    
                value.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
        }

        // Este método toma la URL COMPLETA del manifiesto, no solo la baseUrl.
        private string GetSubDirectoryForUrl(string url)
        {
            if (url.Contains("tft", StringComparison.OrdinalIgnoreCase)) return "tft";
            if (url.Contains("arcane", StringComparison.OrdinalIgnoreCase)) return "arcane";
            if (url.Contains("wildrift", StringComparison.OrdinalIgnoreCase)) return "wr"; // 'wr' para wildrift
            if (url.Contains("valorant", StringComparison.OrdinalIgnoreCase)) return "val"; // 'val' para valorant
            if (url.Contains("bacon", StringComparison.OrdinalIgnoreCase)) return "lor";
            if (url.Contains("manifest_default.json", StringComparison.OrdinalIgnoreCase)) return "lol";
            if (url.Contains("manifest_live.json", StringComparison.OrdinalIgnoreCase)) return "riot-client";
            return "unknown"; // 'unknown' en lugar de 'otros' para consistencia con tu antiguo log
        }
    }
}