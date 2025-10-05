using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using RiftManager.Models;
using RiftManager.Interfaces;
using RiftManager.Services;

namespace RiftManager.Services
{
    public class BundleService
    {
        private readonly LogService _logService;
        private readonly JsonFetcherService _jsonFetcherService;
        private readonly CatalogParser _catalogParser;

        public BundleService(
            JsonFetcherService jsonFetcherService,
            LogService logService,
            CatalogParser catalogParser)
        {
            _jsonFetcherService = jsonFetcherService ?? throw new ArgumentNullException(nameof(jsonFetcherService));
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
            _catalogParser = catalogParser ?? throw new ArgumentNullException(nameof(catalogParser));
        }
        
        public async Task<List<string>> GetBundleUrlsFromCatalog(string catalogJsonUrl, string assetBaseUrl, string metagameId = null)
        {
            List<string> bundleUrls = new List<string>();

            if (string.IsNullOrEmpty(catalogJsonUrl) || string.IsNullOrEmpty(assetBaseUrl))
            {
                _logService.Log("[BundleService] Catalog: No disponible");
                return bundleUrls;
            }
            else
            {
                _logService.Log($"[BundleService] Catalog: {catalogJsonUrl}");
            }

            _logService.LogDebug($"[BundleService] Base de URL para assets/bundles: {assetBaseUrl}");
            _logService.LogDebug($"[BundleService] Base Url para descarga de Bundles: {assetBaseUrl}WebGL/");
            _logService.LogDebug($"[BundleService] Metagame ID recibido: {metagameId ?? "N/A"}");

            string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            string binPath = Path.Combine(tempDir, "catalog.bin");
            string jsonPath = Path.Combine(tempDir, "catalog.json");
            string exePath = Path.Combine(tempDir, "bintojson.exe");

            try
            {
                // Paso 1: Descargar el archivo .bin
                using (var httpClient = new HttpClient())
                {
                    var response = await httpClient.GetAsync(catalogJsonUrl);
                    response.EnsureSuccessStatusCode();
                    using (var fs = new FileStream(binPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await response.Content.CopyToAsync(fs);
                    }
                }
                _logService.LogDebug($"[BundleService] Catalog.bin descargado en: {binPath}");

                // Paso 2: Extraer bintojson.exe del recurso embebido
                var assembly = Assembly.GetExecutingAssembly();
                using (Stream stream = assembly.GetManifestResourceStream("RiftManager.Resources.bintojson.exe"))
                {
                    if (stream == null)
                    {
                        _logService.LogError("[BundleService] No se pudo encontrar el recurso embebido 'bintojson.exe'.");
                        return bundleUrls;
                    }
                    using (var fileStream = new FileStream(exePath, FileMode.Create, FileAccess.Write))
                    {
                        await stream.CopyToAsync(fileStream);
                    }
                }
                _logService.LogDebug($"[BundleService] bintojson.exe extraído en: {exePath}");

                // Paso 3: Ejecutar la conversión
                using (Process process = new Process())
                {
                    process.StartInfo.FileName = exePath;
                    process.StartInfo.Arguments = $"convert \"{binPath}\" \"{jsonPath}\"";
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.CreateNoWindow = true;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true;

                    process.Start();
                    string output = await process.StandardOutput.ReadToEndAsync();
                    string error = await process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync();

                    if (process.ExitCode != 0)
                    {
                        _logService.LogError($"[BundleService] bintojson.exe falló con código {process.ExitCode}. Error: {error}");
                        return bundleUrls;
                    }
                    _logService.LogDebug($"[BundleService] Conversión de .bin a .json completada. Salida: {output}");
                }

                // Paso 4: Procesar el archivo JSON generado
                if (!File.Exists(jsonPath))
                {
                    _logService.LogError($"[BundleService] El archivo {jsonPath} no fue creado por el proceso de conversión.");
                    return bundleUrls;
                }

                using (var stream = File.OpenRead(jsonPath))
                {
                    using (JsonDocument document = await JsonDocument.ParseAsync(stream))
                    {
                        bundleUrls = _catalogParser.ParseBundleUrlsFromCatalogJson(document, assetBaseUrl, metagameId);
                        _logService.Log($"[BundleService] Se encontraron {bundleUrls.Count} URLs de bundles válidas en el catálogo.");
                    }
                }
            }
            catch (Exception e)
            {
                _logService.LogError($"[BundleService] Un error inesperado ocurrió en BundleService al obtener bundles: {e.Message}");
            }
            finally
            {
                // Paso 5: Limpieza de archivos temporales
                if (Directory.Exists(tempDir))
                {
                    try
                    {
                        Directory.Delete(tempDir, true);
                        _logService.LogDebug($"[BundleService] Directorio temporal {tempDir} eliminado.");
                    }
                    catch (Exception ex)
                    {
                        _logService.LogWarning($"[BundleService] No se pudo eliminar el directorio temporal {tempDir}. Error: {ex.Message}");
                    }
                }
            }

            return bundleUrls;
        }

        // ... (el resto del código de ExtractAssetsForEvent permanece igual)
        public async Task ExtractAssetsForEvent(string eventNavigationItemId, string assetsRootFolderPath)
        {
            string bundlesInputPath = Path.Combine(assetsRootFolderPath, eventNavigationItemId, "Bundles");
            string assetsOutputPath = Path.Combine(assetsRootFolderPath, eventNavigationItemId, "ExtractedAssets");

            if (!Directory.Exists(bundlesInputPath))
            {
                _logService.LogWarning($"[BundleService] No se encontraron bundles en {bundlesInputPath}. Saltando la extracción de assets para {eventNavigationItemId}.");
                return;
            }

            Directory.CreateDirectory(assetsOutputPath);

            string assetStudioCliExePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "AssetStudioModCLI.exe");

            if (!File.Exists(assetStudioCliExePath))
            {
                _logService.LogError("[BundleService] AssetStudio no encontrado en la ruta esperada. Asegúrate de que esté en la carpeta 'Tools'.");
                return;
            }

            _logService.Log($"[BundleService] Iniciando extracción de assets de bundles para {eventNavigationItemId}");
            string arguments = $"\"{bundlesInputPath}\" -o \"{assetsOutputPath}\"";
            try
            {
                using (Process process = new Process())
                {
                    process.StartInfo.FileName = assetStudioCliExePath;
                    process.StartInfo.Arguments = arguments;
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.RedirectStandardError = true;
                    process.StartInfo.CreateNoWindow = true;

                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            _logService.LogError($"[BundleService] Error de AssetStudio para {eventNavigationItemId}: {e.Data}");
                        }
                    };

                    process.Start();
                    process.BeginErrorReadLine();
                    await process.WaitForExitAsync();

                    if (process.ExitCode == 0)
                    {
                        _logService.LogSuccess($"BundleService: Extracción de assets de bundles para '{eventNavigationItemId}' completada exitosamente.");
                    }
                    else
                    {
                        _logService.LogError($"BundleService: AssetStudio terminó con código de salida {process.ExitCode} para '{eventNavigationItemId}'. Puede haber errores.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logService.LogError($"BundleService: Error al ejecutar AssetStudio para '{eventNavigationItemId}': {ex.Message}");
            }
        }
    }
}