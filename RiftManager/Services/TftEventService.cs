using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RiftManager.Models;

namespace RiftManager.Services
{
    public class TftEventService
    {
        private readonly JsonFetcherService _jsonFetcherService;
        private readonly LogService _logService;
        private const string TftConfigUrl = "https://clientconfig.rpg.riotgames.com/api/v1/config/public";

        public TftEventService(JsonFetcherService jsonFetcherService, LogService logService)
        {
            _jsonFetcherService = jsonFetcherService;
            _logService = logService;
        }

        public async Task<List<EventDetails>> GetTftEventsAsync()
        {
            List<EventDetails> tftEvents = new List<EventDetails>();
            _logService.LogDebug("[TftEventService] Fetching TFT events from Client Config API.");

            try
            {
                JToken config = await _jsonFetcherService.GetJTokenAsync(TftConfigUrl, suppressConsoleOutput: true);
                if (config == null)
                {
                    _logService.LogWarning("[TftEventService] Could not retrieve TFT configuration from Client Config.");
                    return tftEvents;
                }

                JToken tftEventsToken = config["lol.client_settings.tft.tft_events"];
                if (tftEventsToken == null)
                {
                    _logService.LogDebug("[TftEventService] 'lol.client_settings.tft.tft_events' not found in configuration.");
                    return tftEvents;
                }

                JToken subNavTabs = tftEventsToken["subNavTabs"];
                if (subNavTabs != null && subNavTabs.Type == JTokenType.Array)
                {
                    foreach (JToken tab in subNavTabs)
                    {
                        bool enabled = tab.Value<bool>("enabled");
                        if (!enabled) continue;

                        string eventId = tab.Value<string>("eventId");
                        string url = tab.Value<string>("url");

                        if (!string.IsNullOrEmpty(eventId) && !string.IsNullOrEmpty(url))
                        {
                            // Use eventId as both title and navigationItemId for these events
                            EventDetails tftEvent = new EventDetails(eventId, eventId);

                            // Replace {bcplocale} with en-us as requested by user
                            string finalUrl = url.Replace("{bcplocale}", "en-us");
                            
                            tftEvent.MainEventUrl = finalUrl;
                            tftEvent.HasMainEmbedUrl = true;
                            
                            // Add the URL to the main event links list
                            tftEvent.MainEventLinks.Add(new MainEventLink(finalUrl)
                            {
                                Title = eventId
                            });

                            tftEvents.Add(tftEvent);
                            _logService.LogDebug($"[TftEventService] Found TFT event: {eventId} - {finalUrl}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logService.LogError($"[TftEventService] Error processing TFT events: {ex.Message}");
            }

            return tftEvents;
        }
    }
}
