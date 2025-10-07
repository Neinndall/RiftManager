using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using RiftManager.Models;
using RiftManager.Services;

namespace RiftManager.Services
{
    public class EventProcessor
    {
        private readonly AssetDownloader _assetDownloader;
        private readonly LogService _logService;
        private readonly BundleService _bundleService;
        private readonly RiotAudioLoader _riotAudioLoader;
        private readonly EmbedAssetScraperService _embedAssetScraperService;
        
        public EventProcessor(
            AssetDownloader assetDownloader,
            LogService logService,
            BundleService bundleService,
            RiotAudioLoader riotAudioLoader,
            EmbedAssetScraperService embedAssetScraperService)
        {
            _assetDownloader = assetDownloader;
            _logService = logService;
            _bundleService = bundleService;
            _riotAudioLoader = riotAudioLoader;
            _embedAssetScraperService = embedAssetScraperService;
        }

        public async Task ProcessEventAsync(
            EventDetails currentEvent,
            string assetsRootFolderPath,
            MainEventLink selectedMainEventLink = null)
        {
            // Prepare the main URL for processing, which will be the user's choice or the only existing one.
            string urlToProcess = (selectedMainEventLink != null) ? selectedMainEventLink.Url : null;
            string metagameIdToProcess = (selectedMainEventLink != null) ? selectedMainEventLink.MetagameId : null;

            // --- START DETAILED LOG FOR THE SELECTED EVENT ---
            _logService.Log($"Processing Event: {currentEvent.Title} (ID: {currentEvent.NavigationItemId})");
            _logService.Log($"NavigationItemID: {currentEvent.NavigationItemId}");
            _logService.Log($"Title: {currentEvent.Title}");

            // NEW LOGIC: Log the initial MainEventLink with full details if available
            if (currentEvent.MainEventLinks != null && currentEvent.MainEventLinks.Any())
            {
                var firstMainLink = currentEvent.MainEventLinks.First();
                _logService.Log($"Initial Main URL: {firstMainLink.Url}" +
                                (!string.IsNullOrEmpty(firstMainLink.MetagameId) ? $" - Metagame ID: {firstMainLink.MetagameId}" : "") +
                                (!string.IsNullOrEmpty(firstMainLink.Title) ? $" (Link Title: {firstMainLink.Title})": ""));
            }
            else
            {
                _logService.Log($"Initial Main URL: Not available");
            }

            // --- NEW LOGIC: Log secondary main URL if it exists ---
            if (currentEvent.MainEventLinks != null && currentEvent.MainEventLinks.Count >= 2)
            {
                // Access the second element (index 1) directly for the "secondary" URL
                var secondMainLink = currentEvent.MainEventLinks[1]; 
                _logService.Log($"Secondary Main URL: {secondMainLink.Url}" +
                                (!string.IsNullOrEmpty(secondMainLink.MetagameId) ? $" - Metagame ID: {secondMainLink.MetagameId}" : "") +
                                (!string.IsNullOrEmpty(secondMainLink.Title) ? $" (Link Title: {secondMainLink.Title})": ""));
            }
            else
            {
                _logService.Log("Secondary Main URLs: Not available");
            }

            // Logic for Bundles and Audios (Events with Catalog)
            // Preparamos un ID de filtrado más completo, combinando el Metagame ID y la URL del evento.
            var filterKeywordsList = new List<string>();
            if (!string.IsNullOrEmpty(metagameIdToProcess))
            {
                filterKeywordsList.Add(metagameIdToProcess);
            }
            if (!string.IsNullOrEmpty(urlToProcess))
            {
                try
                {
                    var urlPath = new Uri(urlToProcess).AbsolutePath;
                    var urlPart = Path.GetFileNameWithoutExtension(urlPath);
                    if (!string.IsNullOrEmpty(urlPart))
                    {
                        filterKeywordsList.Add(urlPart);
                    }
                }
                catch (UriFormatException)
                {
                    _logService.LogWarning($"[EventProcessor] URL con formato inválido al preparar palabras clave de filtrado: {urlToProcess}");
                }
            }
            var filterKeywords = string.Join("_", filterKeywordsList.Distinct());
            _logService.LogDebug($"[EventProcessor] Combined filtering keywords: {filterKeywords}");

            List<string> fetchedBundleUrls = await _bundleService.GetBundleUrlsFromCatalog(
                    currentEvent.CatalogInformation?.CatalogJsonUrl ?? string.Empty,
                    currentEvent.CatalogInformation?.BaseUrl ?? string.Empty,
                    filterKeywords // Usamos el ID de filtrado combinado
                );

            // --- LOG OF THE MAIN URL TO BE PROCESSED ---
            if (!string.IsNullOrEmpty(urlToProcess))
            {
                // Replace {locale} with en-us if present
                if (urlToProcess.Contains("{locale}"))
                {
                    urlToProcess = urlToProcess.Replace("{locale}", "en-us");
                    _logService.LogWarning($"[EventProcessor] Replaced {{locale}} with en-us in main URL: {urlToProcess}");
                }

                _logService.Log($"Main URL to process: {urlToProcess}" +
                                 (!string.IsNullOrEmpty(metagameIdToProcess) ? $" (Metagame ID: {metagameIdToProcess})": "") +
                                 (!string.IsNullOrEmpty(selectedMainEventLink?.Title) ? $" (Link Title: {selectedMainEventLink.Title})": ""));
            }
            else
            {
                _logService.Log("No main URL will be processed for this event.");
            }
            // --- END OF MAIN URL TO BE PROCESSED LOG ---

            if (fetchedBundleUrls != null && fetchedBundleUrls.Any())
            {
                string bundlesSavePath = Path.Combine(assetsRootFolderPath, currentEvent.NavigationItemId, "Bundles");
                Directory.CreateDirectory(bundlesSavePath);

                foreach (string bundleUrl in fetchedBundleUrls)
                {
                    try
                    {
                        await _assetDownloader.DownloadBundle(bundleUrl, bundlesSavePath);
                    }
                    catch (Exception ex)
                    {
                        _logService.LogError($"[EventProcessor] Error downloading bundle '{bundleUrl}': {ex.Message}");
                    }
                }

                string extractedAssetsPath = Path.Combine(assetsRootFolderPath, currentEvent.NavigationItemId, "ExtractedAssets");
                Directory.CreateDirectory(extractedAssetsPath);
                await _bundleService.ExtractAssetsForEvent(currentEvent.NavigationItemId, assetsRootFolderPath);

                string audioSavePath = Path.Combine(extractedAssetsPath, "Audio");
                await _riotAudioLoader.ProcessAndDownloadAudioUrls(
                    extractedAssetsPath,
                    currentEvent.CatalogInformation?.BaseUrl ?? string.Empty,
                    audioSavePath);
            }
            else // If no bundles were found (either due to no catalog or an empty one)
            {
                // Only if NO bundles were found from the catalog, attempt with the selected main URL
                if (!string.IsNullOrEmpty(urlToProcess)) // Use the selected or only available URL
                {
                    _logService.Log($"[EventProcessor] Attempting to get assets from main URL: {urlToProcess}");
                    string embedScraperTempDir = Path.Combine(assetsRootFolderPath, currentEvent.NavigationItemId, "EmbedScrapedContent");
                    Directory.CreateDirectory(embedScraperTempDir);
                    await _embedAssetScraperService.HandleEmbedEventAsync(urlToProcess, embedScraperTempDir);
                }
                else
                {
                    _logService.LogWarning($"[EventProcessor] Event '{currentEvent.Title}' does not contain catalog.json or valid main URLs to process advanced assets.");
                }
            }

            // --- Save path configuration (ensure they exist) ---
            string eventAssetsFolderPath = Path.Combine(assetsRootFolderPath, currentEvent.NavigationItemId);
            Directory.CreateDirectory(eventAssetsFolderPath);

            // Logic for downloading main assets (background images, icons, additional assets)
            if (!string.IsNullOrEmpty(currentEvent.BackgroundUrl))
            {
                await _assetDownloader.DownloadAsset(currentEvent.BackgroundUrl, eventAssetsFolderPath);
            }

            if (!string.IsNullOrEmpty(currentEvent.IconUrl))
            {
                await _assetDownloader.DownloadAsset(currentEvent.IconUrl, eventAssetsFolderPath);
            }

            if (currentEvent.AdditionalAssetUrls.Any())
            {
                string additionalAssetsDestinationFolder = Path.Combine(eventAssetsFolderPath, "AdditionalAssets");
                Directory.CreateDirectory(additionalAssetsDestinationFolder);
                _logService.Log($"[EventProcessor] Downloading {currentEvent.AdditionalAssetUrls.Count} additional assets...");

                foreach (string assetUrl in currentEvent.AdditionalAssetUrls)
                {
                    try
                    {
                        if (assetUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                            (assetUrl.Contains(".") || assetUrl.StartsWith("https://cmsassets.rgpub.io", StringComparison.OrdinalIgnoreCase)))
                        {
                            await _assetDownloader.DownloadAsset(assetUrl, additionalAssetsDestinationFolder);
                        }
                        else
                        {
                            _logService.LogWarning($"[EventProcessor] Skipped non-downloadable additional asset URL: {assetUrl}");
                        }
                    }
                    catch (UriFormatException)
                    {
                        _logService.LogWarning($"[EventProcessor] Invalid format additional asset URL: {assetUrl}");
                    }
                    catch (Exception ex)
                    {
                        _logService.LogError($"[EventProcessor] Error downloading additional asset '{assetUrl}': {ex.Message}");
                    }
                }
            }
        }
    }
}