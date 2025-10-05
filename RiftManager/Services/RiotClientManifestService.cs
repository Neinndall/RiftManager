using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes; // Para usar JsonNode para FlattenObject
using Newtonsoft.Json.Linq; // Necesario para JObject.Parse y FlattenObject
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using RiftManager.Utils; // Para ObjectHelper y Crypto

namespace RiftManager.Services
{
    public class RiotClientManifestService
    {
        private readonly JsonFetcherService _jsonFetcherService;
        private readonly AssetDownloader _assetDownloader;
        private readonly LogService _logService;

        public RiotClientManifestService(JsonFetcherService jsonFetcherService, LogService logService, AssetDownloader assetDownloader)
        {
            _jsonFetcherService = jsonFetcherService ?? throw new ArgumentNullException(nameof(jsonFetcherService));
            _assetDownloader = assetDownloader ?? throw new ArgumentNullException(nameof(assetDownloader));
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
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

            _logService.Log("Iniciando procesamiento de Riot Client Manifests...");
            foreach (var url in riotClientManifestUrls)
            {
                await ProcessSingleManifest(url, baseManifestsDownloadDirectory);
            }
            _logService.LogSuccess("Procesamiento completado.");
        }

        private async Task ProcessSingleManifest(string manifestUrl, string baseManifestsDownloadDirectory)
        {
            _logService.LogDebug($"Procesando manifiesto: {manifestUrl}");
            try
            {
                using JsonDocument? document = await _jsonFetcherService.GetJsonDocumentAsync(manifestUrl);
                if (document == null)
                {
                    _logService.LogWarning($"No se pudo obtener el manifiesto: {manifestUrl}");
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
                
                // Convertir JsonDocument a JObject para FlattenObject
                var flattenedManifest = ObjectHelper.FlattenObject(JObject.Parse(document.RootElement.GetRawText()));

                // Filtra solo los valores que son URLs de assets válidos.
                // Usamos .Values para obtener los objetos dentro del diccionario aplanado.
                var validAssets = flattenedManifest.Values
                                    .Select(v => v?.ToString()) // Convertir object a string
                                    .Where(value => !string.IsNullOrWhiteSpace(value) && IsValidAsset(value))
                                    .ToList();

                _logService.Log($"Encontrados {validAssets.Count} assets válidos en el manifiesto.");

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

                    _logService.LogDebug($"Intentando descargar: {fullUrl}");

                    try
                    {
                        // Usar tu AssetDownloader para descargar el archivo
                        await _assetDownloader.DownloadAssetForManifest(fullUrl, targetDir);
                        _logService.Log($"Descargado: {fullUrl}"); 
                    }
                    catch (HttpRequestException httpEx) when (httpEx.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        _logService.LogWarning($"✗ Asset no encontrado (404): {assetPath}");
                    }
                    catch (Exception ex)
                    {
                        _logService.LogError($"✗ Error al descargar asset '{assetPath}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logService.LogError($"Error procesando manifiesto '{manifestUrl}': {ex.Message}");
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