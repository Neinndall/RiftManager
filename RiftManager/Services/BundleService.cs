using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using RiftManager.Models;
using RiftManager.Interfaces;
using RiftManager.Services;
using RiftManager.Utils;
using Newtonsoft.Json.Linq;

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
                _logService.Log("[BundleService] Catalog: Not available");
                return bundleUrls;
            }
            else
            {
                _logService.Log($"[BundleService] Catalog: {catalogJsonUrl}");
            }

            _logService.LogDebug($"[BundleService] Base URL for assets/bundles: {assetBaseUrl}");
            _logService.LogDebug($"[BundleService] Base URL for Bundle downloads: {assetBaseUrl}WebGL/");
            _logService.LogDebug($"[BundleService] Metagame ID received: {metagameId ?? "N/A"}");

            string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            string binPath = Path.Combine(tempDir, "catalog.bin");
            string jsonPath = Path.Combine(tempDir, "catalog.json");
            string exePath = Path.Combine(tempDir, "bintojson.exe");

            try
            {
                // Step 1: Download the .bin file
                using (var httpClient = new HttpClient())
                {
                    var response = await httpClient.GetAsync(catalogJsonUrl);
                    response.EnsureSuccessStatusCode();
                    using (var fs = new FileStream(binPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await response.Content.CopyToAsync(fs);
                    }
                }
                _logService.LogDebug($"[BundleService] Catalog.bin downloaded to: {binPath}");

                // Step 2: Extract bintojson.exe from embedded resource
                var assembly = Assembly.GetExecutingAssembly();
                using (Stream stream = assembly.GetManifestResourceStream("RiftManager.Resources.bintojson.exe"))
                {
                    if (stream == null)
                    {
                        _logService.LogError("[BundleService] Could not find embedded resource 'bintojson.exe'.");
                        return bundleUrls;
                    }
                    using (var fileStream = new FileStream(exePath, FileMode.Create, FileAccess.Write))
                    {
                        await stream.CopyToAsync(fileStream);
                    }
                }
                _logService.LogDebug($"[BundleService] bintojson.exe extracted to: {exePath}");

                // Step 3: Execute conversion
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
                        _logService.LogError($"[BundleService] bintojson.exe failed with code {process.ExitCode}. Error: {error}");
                        return bundleUrls;
                    }
                    _logService.LogDebug($"[BundleService] .bin to .json conversion complete. Output: {output}");
                }

                // Step 4: Process the generated JSON file
                if (!File.Exists(jsonPath))
                {
                    _logService.LogError($"[BundleService] File {jsonPath} was not created by the conversion process.");
                    return bundleUrls;
                }

                string jsonText = await File.ReadAllTextAsync(jsonPath);
                JToken rootToken = JToken.Parse(jsonText);
                bundleUrls = _catalogParser.ParseBundleUrlsFromCatalogJson(rootToken, assetBaseUrl, metagameId);
                _logService.Log($"[BundleService] Found {bundleUrls.Count} valid bundle URLs in the catalog.");
            }
            catch (Exception e)
            {
                _logService.LogError($"[BundleService] An unexpected error occurred in BundleService while fetching bundles: {e.Message}");
            }
            finally
            {
                // Step 5: Clean up temporary files
                if (Directory.Exists(tempDir))
                {
                    try
                    {
                        Directory.Delete(tempDir, true);
                        _logService.LogDebug($"[BundleService] Temporary directory {tempDir} deleted.");
                    }
                    catch (Exception ex)
                    {
                        _logService.LogWarning($"[BundleService] Could not delete temporary directory {tempDir}. Error: {ex.Message}");
                    }
                }
            }

            return bundleUrls;
        }

        public async Task ExtractAssetsForEvent(string eventNavigationItemId, string assetsRootFolderPath)
        {
            string bundlesInputPath = Path.Combine(assetsRootFolderPath, eventNavigationItemId, "Bundles");
            string assetsOutputPath = Path.Combine(assetsRootFolderPath, eventNavigationItemId, "ExtractedAssets");

            if (!Directory.Exists(bundlesInputPath))
            {
                _logService.LogWarning($"[BundleService] No bundles found in {bundlesInputPath}. Skipping asset extraction for {eventNavigationItemId}.");
                return;
            }

            Directory.CreateDirectory(assetsOutputPath);

            string tempToolsDir = Path.Combine(Path.GetTempPath(), "RiftManager", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempToolsDir);
            string assetStudioCliExePath = Path.Combine(tempToolsDir, "AssetStudio", "AssetStudioModCLI.exe");

            try
            {
                _logService.LogDebug("[BundleService] Extracting AssetStudioModCLI from embedded resources...");
                EmbeddedResourceManager.ExtractDirectory("AssetStudio", tempToolsDir);
                _logService.LogDebug($"[BundleService] AssetStudioModCLI extracted to: {tempToolsDir}");

                if (!File.Exists(assetStudioCliExePath))
                {
                    _logService.LogError($"[BundleService] AssetStudio not found at the expected extraction path: {assetStudioCliExePath}.");
                    return;
                }

                _logService.Log($"[BundleService] Starting bundle asset extraction for {eventNavigationItemId}");
                string arguments = $"\"{bundlesInputPath}\" -o \"{assetsOutputPath}\"";
                
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
                            _logService.LogError($"[BundleService] AssetStudio error for {eventNavigationItemId}: {e.Data}");
                        }
                    };

                    process.Start();
                    process.BeginErrorReadLine();
                    await process.WaitForExitAsync();

                    if (process.ExitCode == 0)
                    {
                        _logService.LogSuccess($"BundleService: Bundle asset extraction for '{eventNavigationItemId}' completed successfully.");
                    }
                    else
                    {
                        _logService.LogError($"BundleService: AssetStudio finished with exit code {process.ExitCode} for '{eventNavigationItemId}'. There might be errors.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logService.LogError($"BundleService: Error executing AssetStudio for '{eventNavigationItemId}': {ex.Message}");
            }
            finally
            {
                if (Directory.Exists(tempToolsDir))
                {
                    try
                    {
                        Directory.Delete(tempToolsDir, true);
                        _logService.LogDebug($"[BundleService] Temporary tools directory {tempToolsDir} deleted.");
                    }
                    catch (Exception ex)
                    {
                        _logService.LogWarning($"[BundleService] Could not delete temporary tools directory {tempToolsDir}. Error: {ex.Message}");
                    }
                }
            }
        }
    }
}