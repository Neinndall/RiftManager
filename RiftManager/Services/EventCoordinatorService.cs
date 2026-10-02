using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RiftManager.Views.Models;
using RiftManager.Views.Interfaces;
using RiftManager.Services;

namespace RiftManager.Services
{
    public class EventCoordinatorService
    {
        private readonly JsonFetcherService _jsonFetcherService;
        private readonly NavigationParser _navigationParser;
        private readonly DetailPageParser _detailPageParser;
        private readonly WebScraper _webScraper;
        private readonly TftEventService _tftEventService;
        private readonly BundleService _bundleService;
        private readonly LogService _logService;
        private readonly string _baseUrlV2 = "https://content.publishing.riotgames.com/publishing-content/v2.0/public/channel/league_of_legends_client";

        public EventCoordinatorService(
            JsonFetcherService jsonFetcherService,
            NavigationParser NavigationParser,
            DetailPageParser DetailPageParser,
            WebScraper WebScraper,
            TftEventService tftEventService,
            BundleService bundleService,
            LogService logService)
        {
            _jsonFetcherService = jsonFetcherService;
            _navigationParser = NavigationParser;
            _detailPageParser = DetailPageParser;
            _webScraper = WebScraper;
            _tftEventService = tftEventService;
            _bundleService = bundleService;
            _logService = logService;
        }

        public async Task<Dictionary<string, EventDetails>> TrackEvents(string navigationUrl)
        {
            var eventData = new ConcurrentDictionary<string, EventDetails>();

            // 1. First, get the standard events
            JToken document = await _jsonFetcherService.GetJTokenAsync(navigationUrl, suppressConsoleOutput: true);
            if (document != null)
            {
                JToken dataToken = document["data"];
                if (dataToken != null && dataToken.Type == JTokenType.Array)
                {
                    // v1.2.0: fetch detail pages in parallel (was sequential: N x RTT
                    // on startup). Catalog scraping stays lazy in EventProcessor, so
                    // no catalog HTTP happens here at all (old code fetched it twice:
                    // once here, once on download).
                    var semaphore = new SemaphoreSlim(6);
                    var tasks = new List<Task>();
                    foreach (JToken eventElement in dataToken)
                    {
                        tasks.Add(ProcessNavigationItemAsync(eventElement, eventData, semaphore));
                    }
                    await Task.WhenAll(tasks);
                }
                else
                {
                    _logService.LogWarning("[EventCoordinatorService] The navigation JSON document does not contain the 'data' property as an array. Normal events could not be loaded.");
                }
            }
            else
            {
                _logService.LogWarning("[EventCoordinatorService] The navigation JSON document could not be retrieved. Normal events could not be loaded.");
            }

            // 2. Second, get TFT events from Client Config
            var tftEvents = await _tftEventService.GetTftEventsAsync();
            foreach (var tftEvent in tftEvents)
            {
                if (!eventData.ContainsKey(tftEvent.NavigationItemId))
                {
                    eventData.TryAdd(tftEvent.NavigationItemId, tftEvent);
                }
            }

            return new Dictionary<string, EventDetails>(eventData);
        }

        private async Task ProcessNavigationItemAsync(JToken eventElement, ConcurrentDictionary<string, EventDetails> eventData, SemaphoreSlim semaphore)
        {
            await semaphore.WaitAsync();
            try
            {
                string navigationItemId = eventElement.Value<string>("navigationItemID");
                string eventTitle = eventElement.Value<string>("title");

                if (string.IsNullOrEmpty(navigationItemId) || string.IsNullOrEmpty(eventTitle))
                    return;

                EventDetails currentEvent = new EventDetails(eventTitle, navigationItemId);

                // 1. Obtener la MainEventUrl del propio elemento de navegación inicial (si es de tipo 'iframed')
                string navMainUrl = _navigationParser.GetMainEventUrlFromNavigationItem(eventElement);
                if (navMainUrl != null)
                {
                    currentEvent.MainEventUrl = navMainUrl;
                    currentEvent.HasMainEmbedUrl = true;

                    currentEvent.MainEventLinks.Add(new MainEventLink(navMainUrl)
                    {
                        Title = eventTitle,
                        MetagameId = null
                    });

                    // v1.2.0: catalog stays null here (lazy in EventProcessor).
                    currentEvent.CatalogInformation = null;
                }

                currentEvent.BackgroundUrl = eventElement.SelectToken("background.url")?.ToString();
                currentEvent.IconUrl = eventElement.SelectToken("icon.url")?.ToString();

                bool requiresDetailPageFetch = true;
                if (navigationItemId.Equals("info-hub", StringComparison.OrdinalIgnoreCase) ||
                    navigationItemId.Equals("lol-patch-notes", StringComparison.OrdinalIgnoreCase))
                {
                    requiresDetailPageFetch = false;
                }

                if (requiresDetailPageFetch)
                {
                    string eventDataUrl = $"{_baseUrlV2}/page/{navigationItemId}";
                    JToken eventDetailsToken = await _jsonFetcherService.GetJTokenAsync(eventDataUrl, suppressConsoleOutput: true);
                    if (eventDetailsToken != null)
                    {
                        List<MainEventLink> detailPageMainLinks = _detailPageParser.GetMainEventUrlsFromDetailPage(eventDetailsToken, currentEvent);

                        foreach (var link in detailPageMainLinks)
                        {
                            if (!currentEvent.MainEventLinks.Any(l => l.Url.Equals(link.Url, StringComparison.OrdinalIgnoreCase)))
                            {
                                currentEvent.MainEventLinks.Add(link);
                            }
                        }

                        currentEvent.HasMainEmbedUrl = currentEvent.MainEventLinks.Any();

                        if (currentEvent.MainEventUrl == null && currentEvent.MainEventLinks.Any())
                        {
                            currentEvent.MainEventUrl = currentEvent.MainEventLinks.First().Url;
                        }

                        currentEvent.AdditionalAssetUrls.AddRange(_detailPageParser.ExtractAdditionalAssetsUrls(eventDetailsToken));
                    }
                }

                // v1.2.0: explicit routing (Normal = cmsassets only, EmbedWeb = JS/CSS scraper).
                currentEvent.RefreshType();
                eventData.TryAdd(navigationItemId, currentEvent);
            }
            finally
            {
                semaphore.Release();
            }
        }
    }
}
