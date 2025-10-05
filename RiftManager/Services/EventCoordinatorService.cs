using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using RiftManager.Models;
using RiftManager.Interfaces;
using RiftManager.Services;

namespace RiftManager.Services
{
    public class EventCoordinatorService
    {
        private readonly JsonFetcherService _jsonFetcherService;
        private readonly NavigationParser _navigationParser;
        private readonly DetailPageParser _detailPageParser;
        private readonly WebScraper _webScraper;
        private readonly BundleService _bundleService;
        private readonly LogService _logService;
        private readonly string _baseUrlV2 = "https://content.publishing.riotgames.com/publishing-content/v2.0/public/channel/league_of_legends_client";

        public EventCoordinatorService(
            JsonFetcherService jsonFetcherService,
            NavigationParser NavigationParser,
            DetailPageParser DetailPageParser,
            WebScraper WebScraper,
            BundleService bundleService,
            LogService logService)
        {
            _jsonFetcherService = jsonFetcherService ?? throw new ArgumentNullException(nameof(jsonFetcherService));
            _navigationParser = NavigationParser ?? throw new ArgumentNullException(nameof(NavigationParser));
            _detailPageParser = DetailPageParser ?? throw new ArgumentNullException(nameof(DetailPageParser));
            _webScraper = WebScraper ?? throw new ArgumentNullException(nameof(WebScraper));
            _bundleService = bundleService ?? throw new ArgumentNullException(nameof(bundleService));
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        }

        public async Task<Dictionary<string, EventDetails>> TrackEvents(string navigationUrl)
        {
            Dictionary<string, EventDetails> eventData = new Dictionary<string, EventDetails>();

            using JsonDocument document = await _jsonFetcherService.GetJsonDocumentAsync(navigationUrl, suppressConsoleOutput: true);
            if (document == null)
            {
                _logService.LogWarning("[EventCoordinatorService] No se pudo obtener el documento JSON de navegación. Asegúrate de que la URL sea correcta o haya conexión.");
                return eventData;
            }

            if (document.RootElement.TryGetProperty("data", out JsonElement dataElement) && dataElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement eventElement in dataElement.EnumerateArray())
                {
                    if (eventElement.TryGetProperty("navigationItemID", out JsonElement navigationItemIdElement) && navigationItemIdElement.ValueKind == JsonValueKind.String
                        && eventElement.TryGetProperty("title", out JsonElement titleElement) && titleElement.ValueKind == JsonValueKind.String)
                    {
                        string navigationItemId = navigationItemIdElement.GetString()!;
                        string eventTitle = titleElement.GetString()!;

                        EventDetails currentEvent = new EventDetails(eventTitle, navigationItemId);

                        string fullCatalogJsonUrl = null;

                        // 1. Obtener la MainEventUrl del propio elemento de navegación inicial (si es de tipo 'iframed')
                        string navMainUrl = _navigationParser.GetMainEventUrlFromNavigationItem(eventElement);
                        if (navMainUrl != null)
                        {
                            currentEvent.MainEventUrl = navMainUrl;
                            currentEvent.HasMainEmbedUrl = true;

                            // --- CAMBIO APLICADO AQUÍ ---
                            // Añade la URL principal encontrada en la navegación a la lista MainEventLinks.
                            currentEvent.MainEventLinks.Add(new MainEventLink(navMainUrl)
                            {
                                Title = eventTitle, // Usa el título del evento como título predeterminado
                                MetagameId = null // No hay MetagameId en este nivel de la navegación
                            });
                            // ----------------------------

                            fullCatalogJsonUrl = await _webScraper.GetCatalogBaseUrl(currentEvent.MainEventUrl, eventTitle);
                            if (fullCatalogJsonUrl != null)
                            {
                                string assetBaseUrlForBundles = fullCatalogJsonUrl.Replace("catalog.bin", "");
                                currentEvent.CatalogInformation = new Models.CatalogData
                                {
                                    BaseUrl = assetBaseUrlForBundles,
                                    CatalogJsonUrl = fullCatalogJsonUrl
                                };
                            }
                            else
                            {
                                currentEvent.CatalogInformation = null; // Asegurarse de que sea null si no se encuentra un catálogo válido
                            }
                        }

                        if (eventElement.TryGetProperty("background", out JsonElement backgroundElement) && backgroundElement.ValueKind == JsonValueKind.Object
                            && backgroundElement.TryGetProperty("url", out JsonElement backgroundUrlElement) && backgroundUrlElement.ValueKind == JsonValueKind.String)
                        {
                            currentEvent.BackgroundUrl = backgroundUrlElement.GetString();
                        }

                        if (eventElement.TryGetProperty("icon", out JsonElement iconElement) && iconElement.ValueKind == JsonValueKind.Object
                            && iconElement.TryGetProperty("url", out JsonElement iconUrlElement) && iconUrlElement.ValueKind == JsonValueKind.String)
                        {
                            currentEvent.IconUrl = iconUrlElement.GetString();
                        }

                        bool requiresDetailPageFetch = true;
                        if (navigationItemId.Equals("info-hub", StringComparison.OrdinalIgnoreCase) ||
                            navigationItemId.Equals("lol-patch-notes", StringComparison.OrdinalIgnoreCase))
                        {
                            requiresDetailPageFetch = false;
                        }

                        if (requiresDetailPageFetch)
                        {
                            string eventDataUrl = $"{_baseUrlV2}/page/{navigationItemId}";
                            JsonDocument eventDetailsDocument = await _jsonFetcherService.GetJsonDocumentAsync(eventDataUrl, suppressConsoleOutput: true);
                            if (eventDetailsDocument != null)
                            {
                                // --- CAMBIO ADICIONAL APLICADO AQUÍ ---
                                // Asegúrate de que DetailPageParser SIEMPRE se llame para buscar enlaces adicionales,
                                // sin importar si ya encontramos uno en la navegación inicial.
                                List<MainEventLink> detailPageMainLinks = _detailPageParser.GetMainEventUrlsFromDetailPage(eventDetailsDocument, currentEvent);

                                // Agrega los enlaces de la página de detalle, evitando duplicados.
                                foreach (var link in detailPageMainLinks)
                                {
                                    if (!currentEvent.MainEventLinks.Any(l => l.Url.Equals(link.Url, StringComparison.OrdinalIgnoreCase)))
                                    {
                                        currentEvent.MainEventLinks.Add(link);
                                    }
                                }
                                // -------------------------------------

                                // Si después de ambos chequeos (navegación y detalle) hay enlaces, actualiza HasMainEmbedUrl
                                currentEvent.HasMainEmbedUrl = currentEvent.MainEventLinks.Any();

                                // Si MainEventUrl aún no está establecido y hay enlaces en la lista, usa el primero.
                                if (currentEvent.MainEventUrl == null && currentEvent.MainEventLinks.Any())
                                {
                                    currentEvent.MainEventUrl = currentEvent.MainEventLinks.First().Url;
                                }

                                if (fullCatalogJsonUrl == null && currentEvent.MainEventUrl != null)
                                {
                                    // Pasa el título del primer MainEventLink si existe, de lo contrario null.
                                    string titleForCatalog = currentEvent.MainEventLinks.Any() ? currentEvent.MainEventLinks.First().Title : null;
                                    fullCatalogJsonUrl = await _webScraper.GetCatalogBaseUrl(currentEvent.MainEventUrl, titleForCatalog);
                                    if (fullCatalogJsonUrl != null)
                                    {
                                        string assetBaseUrlForBundles = fullCatalogJsonUrl.Replace("catalog.bin", "");
                                        currentEvent.CatalogInformation = new Models.CatalogData
                                        {
                                            BaseUrl = assetBaseUrlForBundles,
                                            CatalogJsonUrl = fullCatalogJsonUrl
                                        };
                                    }
                                }

                                currentEvent.AdditionalAssetUrls.AddRange(_detailPageParser.ExtractAdditionalAssetsUrls(eventDetailsDocument));
                            }
                        }

                        eventData.Add(navigationItemId, currentEvent);
                    }
                }
            }
            else
            {
                _logService.LogWarning("[EventCoordinatorService] El documento JSON de navegación no contiene la propiedad 'data' como un array. No se pudieron cargar los eventos.");
            }
            return eventData;
        }
    }
}