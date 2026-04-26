using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using RiftManager.Models;
using RiftManager.Services;
using RiftManager.Interfaces;

namespace RiftManager.Services
{
    public class EventProcessor
    {
        private readonly AssetDownloader _assetDownloader;
        private readonly LogService _logService;
        private readonly BundleService _bundleService;
        private readonly RiotAudioLoader _riotAudioLoader;
        private readonly EmbedAssetScraperService _embedAssetScraperService;
        private readonly WebScraper _webScraper;
        
        public EventProcessor(
            AssetDownloader assetDownloader,
            LogService logService,
            BundleService bundleService,
            RiotAudioLoader riotAudioLoader,
            EmbedAssetScraperService embedAssetScraperService,
            WebScraper webScraper)
        {
            _assetDownloader = assetDownloader;
            _logService = logService;
            _bundleService = bundleService;
            _riotAudioLoader = riotAudioLoader;
            _embedAssetScraperService = embedAssetScraperService;
            _webScraper = webScraper;
        }

        public async Task<string> ProcessEventAsync(
            EventDetails currentEvent,
            string assetsRootFolderPath,
            MainEventLink selectedMainEventLink = null)
        {
            // Prepare the main URL for processing, which will be the user's choice or the only existing one.
            string urlToProcess = (selectedMainEventLink != null) ? selectedMainEventLink.Url : null;
            string metagameIdToProcess = (selectedMainEventLink != null) ? selectedMainEventLink.MetagameId : null;

            // --- FORCE FRESH CATALOG FOR SELECTED LINK ---
            if (!string.IsNullOrEmpty(urlToProcess))
            {
                // Limpiamos el locale para el scraper
                string scrapUrl = urlToProcess.Contains("{locale}") ? urlToProcess.Replace("{locale}", "en-us") : urlToProcess;

                // Si hay un link seleccionado, borramos el catálogo viejo para no heredar basura (ej: del comic1)
                if (selectedMainEventLink != null)
                {
                    _logService.Log($"[EventProcessor] User selected a specific link. Clearing old catalog info and fetching fresh catalog for: {scrapUrl}");
                    currentEvent.CatalogInformation = null;
                }

                // Si no tenemos catálogo (porque lo acabamos de borrar o no existía), buscamos el nuevo.
                if (currentEvent.CatalogInformation == null)
                {
                    string newCatalogUrl = await _webScraper.GetCatalogBaseUrl(scrapUrl, selectedMainEventLink?.Title);
                    if (newCatalogUrl != null)
                    {
                        _logService.LogSuccess($"[EventProcessor] Fresh catalog found: {newCatalogUrl}");
                        string assetBaseUrlForBundles = newCatalogUrl.Replace("catalog.bin", "");
                        currentEvent.CatalogInformation = new Models.CatalogData
                        {
                            BaseUrl = assetBaseUrlForBundles,
                            CatalogJsonUrl = newCatalogUrl
                        };
                    }
                }
            }

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
            // Preparamos un ID de filtrado más completo recolectando metadatos de TODO el evento
            var filterKeywordsList = new List<string>();
            
            // 1. Incluimos palabras del título del evento descompuestas
            if (!string.IsNullOrEmpty(currentEvent.Title))
            {
                var titleWords = Regex.Split(currentEvent.Title, @"[^a-zA-Z0-9]").Where(w => w.Length > 2);
                filterKeywordsList.AddRange(titleWords);
            }

            // 2. Incluimos el ID de navegación descompuesto
            if (!string.IsNullOrEmpty(currentEvent.NavigationItemId))
            {
                var navWords = currentEvent.NavigationItemId.Split('-').Where(w => w.Length > 2);
                filterKeywordsList.AddRange(navWords);
            }

            // 3. ¡IMPORTANTE! Incluimos los Metagame IDs y Títulos de TODOS los links del evento
            if (currentEvent.MainEventLinks != null)
            {
                foreach (var link in currentEvent.MainEventLinks)
                {
                    if (!string.IsNullOrEmpty(link.MetagameId))
                    {
                        filterKeywordsList.Add(link.MetagameId);
                    }
                    if (!string.IsNullOrEmpty(link.Title))
                    {
                        var linkTitleWords = Regex.Split(link.Title, @"[^a-zA-Z0-9]").Where(w => w.Length > 2);
                        filterKeywordsList.AddRange(linkTitleWords);
                    }
                }
            }

            // 4. Incluimos partes de la URL seleccionada
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

            // --- DETERMINE BASE PATH FOR EXTRACCION ---
            // Si hay un metagameId, lo usamos como subcarpeta dentro del navigationItemId
            string eventBaseDir = Path.Combine(assetsRootFolderPath, currentEvent.NavigationItemId);
            if (!string.IsNullOrEmpty(selectedMainEventLink?.MetagameId))
            {
                eventBaseDir = Path.Combine(eventBaseDir, selectedMainEventLink.MetagameId);
                _logService.LogDebug($"[EventProcessor] Using metagame subfolder: {selectedMainEventLink.MetagameId}");
            }
            Directory.CreateDirectory(eventBaseDir);

            List<string> fetchedBundleUrls = await _bundleService.GetBundleUrlsFromCatalog(
                    currentEvent.CatalogInformation?.CatalogJsonUrl ?? string.Empty,
                    currentEvent.CatalogInformation?.BaseUrl ?? string.Empty,
                    filterKeywords // Usamos el ID de filtrado combinado
                );

            // --- LOG OF THE MAIN URL TO BE PROCESSED ---
            if (!string.IsNullOrEmpty(urlToProcess))
            {
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
                string bundlesSavePath = Path.Combine(eventBaseDir, "Bundles");
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

                string extractedAssetsPath = Path.Combine(eventBaseDir, "ExtractedAssets");
                Directory.CreateDirectory(extractedAssetsPath);
                
                await _bundleService.ExtractAssetsForEvent(
                    selectedMainEventLink?.MetagameId ?? currentEvent.NavigationItemId, 
                    bundlesSavePath, 
                    extractedAssetsPath);

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
                    string embedScraperTempDir = Path.Combine(eventBaseDir, "EmbedScrapedContent");
                    Directory.CreateDirectory(embedScraperTempDir);
                    await _embedAssetScraperService.HandleEmbedEventAsync(urlToProcess, embedScraperTempDir);
                }
                else
                {
                    _logService.LogWarning($"[EventProcessor] Event '{currentEvent.Title}' does not contain catalog.bin or valid main URLs to process advanced assets.");
                }
            }

            // Logic for downloading main assets (background images, icons, additional assets)
            if (!string.IsNullOrEmpty(currentEvent.BackgroundUrl))
            {
                await _assetDownloader.DownloadAsset(currentEvent.BackgroundUrl, eventBaseDir);
            }

            if (!string.IsNullOrEmpty(currentEvent.IconUrl))
            {
                await _assetDownloader.DownloadAsset(currentEvent.IconUrl, eventBaseDir);
            }

            if (currentEvent.AdditionalAssetUrls.Any())
            {
                string additionalAssetsDestinationFolder = Path.Combine(eventBaseDir, "AdditionalAssets");
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

            return eventBaseDir;
        }
    }
}