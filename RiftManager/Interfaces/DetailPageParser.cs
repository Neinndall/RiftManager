using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using RiftManager.Models;
using RiftManager.Services;

namespace RiftManager.Interfaces
{
    public class DetailPageParser
    {
        private readonly LogService _logService;
        public const string EmbedUrlIdentifier = "https://embed.rgpub.io/"; // Reutilizamos el identificador

        public DetailPageParser(LogService logService)
        {
            _logService = logService;
        }

        /// <summary>
        /// Extrae URLs de assets adicionales de un documento JSON de página de detalle.
        /// </summary>
        public List<string> ExtractAdditionalAssetsUrls(JToken eventDetailPageData)
        {
            List<string> assetUrls = new List<string>();
            if (eventDetailPageData == null) return assetUrls;

            try
            {
                FindUrlsRecursively(eventDetailPageData, assetUrls);
            }
            catch (Exception e)
            {
                _logService.LogError($"DetailPageParser: Error al extraer assets adicionales de la página de detalle: {e.Message}");
            }
            return assetUrls.Distinct().ToList(); // Eliminar duplicados
        }

        private void FindUrlsRecursively(JToken token, List<string> urls)
        {
            if (token is JObject obj)
            {
                foreach (var property in obj.Properties())
                {
                    if (property.Name == "url" && property.Value.Type == JTokenType.String)
                    {
                        string url = property.Value.ToString();
                        if (url.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                            url.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                            url.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                        {
                            urls.Add(url);
                        }
                    }
                    FindUrlsRecursively(property.Value, urls);
                }
            }
            else if (token is JArray array)
            {
                foreach (var item in array)
                {
                    FindUrlsRecursively(item, urls);
                }
            }
        }

        /// <summary>
        /// Intenta extraer todas las URLs principales del evento y sus MetagameIds desde el documento JSON de la página de detalle.
        /// También asigna el título del evento al objeto EventDetails proporcionado.
        /// </summary>
        /// <param name="eventDetailsToken">El token JSON de la página de detalle del evento.</param>
        /// <param name="eventDetailsToPopulate">El objeto EventDetails a rellenar con el título.</param>
        /// <returns>Una lista de objetos MainEventLink si se encuentran URLs principales, de lo contrario una lista vacía.</returns>
        public List<MainEventLink> GetMainEventUrlsFromDetailPage(JToken eventDetailsToken, EventDetails eventDetailsToPopulate)
        {
            List<MainEventLink> foundMainEventLinks = new List<MainEventLink>(); // Lista para almacenar todas las URLs encontradas

            if (eventDetailsToken == null || eventDetailsToPopulate == null)
            {
                return foundMainEventLinks; // Retorna lista vacía si la entrada es nula
            }

            try
            {
                // Intenta obtener el título (asumiendo que está a nivel raíz)
                if (eventDetailsToken["title"] is JToken titleToken && titleToken.Type == JTokenType.String)
                {
                    eventDetailsToPopulate.Title = titleToken.ToString();
                }

                if (eventDetailsToken["blades"] is JArray bladesArray)
                {
                    foreach (JToken blade in bladesArray)
                    {
                        JToken currentLinksToken = blade.SelectToken("header.links") ?? blade["links"];
                        if (currentLinksToken is JArray linksArray)
                        {
                            foreach (JToken link in linksArray)
                            {
                                JToken actionToken = link["action"];
                                if (actionToken != null && actionToken.Value<string>("type") == "lc_open_metagame")
                                {
                                    JToken payloadToken = actionToken["payload"];
                                    if (payloadToken != null)
                                    {
                                        string currentUrl = payloadToken.Value<string>("url");
                                        // Es importante verificar si 'metagameId' existe antes de intentar obtenerlo
                                        string currentMetagameId = payloadToken.Value<string>("metagameId");
                                        // También obtenemos el título del link
                                        string linkTitle = link.Value<string>("title");

                                        if (currentUrl != null && currentUrl.Contains(EmbedUrlIdentifier, StringComparison.OrdinalIgnoreCase))
                                        {
                                            _logService.LogDebug($"[DetailPageParser] Main metagame URL found: {currentUrl} (MetagameId: {currentMetagameId ?? "N/A"})");
                                            foundMainEventLinks.Add(new MainEventLink(currentUrl)
                                            {
                                                MetagameId = currentMetagameId,
                                                Title = linkTitle // Asignamos el título del link
                                            });
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _logService.LogError($"[DetailPageParser] Error extracting main URLs and MetagameId from the detail page: {e.Message}");
            }

            // Para mantener la compatibilidad con el EventDetails existente que espera UNA URL principal,
            // asignamos la primera URL encontrada (si existe) a las propiedades MainEventUrl y MetagameId.
            // La lista completa de URLs principales se devuelve.
            if (foundMainEventLinks.Any())
            {
                eventDetailsToPopulate.MainEventUrl = foundMainEventLinks.First().Url;
                eventDetailsToPopulate.HasMainEmbedUrl = true;
                eventDetailsToPopulate.MetagameId = foundMainEventLinks.First().MetagameId;
            }
            else
            {
                // Si no se encontró ninguna URL principal, reiniciamos las propiedades.
                eventDetailsToPopulate.MainEventUrl = null;
                eventDetailsToPopulate.HasMainEmbedUrl = false;
                eventDetailsToPopulate.MetagameId = null;
            }

            // Retornamos todas las URLs principales encontradas, eliminando duplicados por la URL misma.
            return foundMainEventLinks.DistinctBy(l => l.Url).ToList();
        }
    }
}