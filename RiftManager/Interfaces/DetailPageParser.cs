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
                JToken bladesToken = eventDetailPageData["blades"];
                if (bladesToken != null && bladesToken.Type == JTokenType.Array)
                {
                    foreach (JToken blade in bladesToken)
                    {
                        // Nueva lógica para backdrop.background.url (cuando es una imagen directa)
                        JToken backgroundToken = blade.SelectToken("backdrop.background");
                        if (backgroundToken != null)
                        {
                            // AÑADIDO: Si background tiene una propiedad "url" y es de tipo string, es una imagen directa.
                            if (backgroundToken["url"] is JToken backgroundUrlToken && backgroundUrlToken.Type == JTokenType.String)
                            {
                                string backgroundImageUrl = backgroundUrlToken.ToString();
                                if (!string.IsNullOrEmpty(backgroundImageUrl))
                                {
                                    assetUrls.Add(backgroundImageUrl);
                                }
                            }

                            // Lógica existente para backdrop.background.sources (video)
                            if (backgroundToken["sources"] is JArray sourcesArray)
                            {
                                foreach (var source in sourcesArray)
                                {
                                    if (source["src"] is JToken srcToken && srcToken.Type == JTokenType.String)
                                    {
                                        assetUrls.Add(srcToken.ToString());
                                    }
                                }
                            }
                            // Lógica existente para backdrop.background.thumbnail (thumbnail de video)
                            if (backgroundToken.SelectToken("thumbnail.url") is JToken thumbnailUrlToken && thumbnailUrlToken.Type == JTokenType.String)
                            {
                                assetUrls.Add(thumbnailUrlToken.ToString());
                            }
                        }

                        // header.media (imagen del encabezado)
                        if (blade.SelectToken("header.media.url") is JToken mediaUrlToken && mediaUrlToken.Type == JTokenType.String)
                        {
                            assetUrls.Add(mediaUrlToken.ToString());
                        }

                        // leagueClientTabContentGroups.ctas.media (imágenes de skins, etc.)
                        if (blade["leagueClientTabContentGroups"] is JArray tabGroupsArray)
                        {
                            foreach (JToken tabGroup in tabGroupsArray)
                            {
                                if (tabGroup["ctas"] is JArray ctasArray)
                                {
                                    foreach (JToken cta in ctasArray)
                                    {
                                        if (cta.SelectToken("media.url") is JToken ctaMediaUrlToken && ctaMediaUrlToken.Type == JTokenType.String)
                                        {
                                            assetUrls.Add(ctaMediaUrlToken.ToString());
                                        }
                                    }
                                }
                            }
                        }

                        // links.media (imágenes de cinematic, motion comics secundarios, etc.) y iframes
                        JToken currentLinksToken = blade.SelectToken("header.links") ?? blade["links"];
                        if (currentLinksToken is JArray linksArray)
                        {
                            foreach (JToken link in linksArray)
                            {
                                if (link.SelectToken("media.url") is JToken linkMediaUrlToken && linkMediaUrlToken.Type == JTokenType.String)
                                {
                                    assetUrls.Add(linkMediaUrlToken.ToString());
                                }
                                // Buscar URLs de iframes que podrían ser assets o contenido relevante
                                JToken actionToken = link["action"];
                                if (actionToken != null && actionToken.Value<string>("type") == "open_iframe")
                                {
                                    if (actionToken.SelectToken("payload.url") is JToken iframeUrlToken && iframeUrlToken.Type == JTokenType.String)
                                    {
                                        assetUrls.Add(iframeUrlToken.ToString());
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _logService.LogError($"DetailPageParser: Error al extraer assets adicionales de la página de detalle: {e.Message}");
            }
            return assetUrls.Distinct().ToList(); // Eliminar duplicados
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