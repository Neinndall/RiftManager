using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using RiftManager.Models; // Asegúrate de agregar esta directiva using!
using RiftManager.Services; // Necesario para LogService

namespace RiftManager.Interfaces
{
    public class DetailPageParser
    {
        private readonly LogService _logService;
        public const string EmbedUrlIdentifier = "https://embed.rgpub.io/"; // Reutilizamos el identificador

        public DetailPageParser(LogService logService)
        {
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        }

        /// <summary>
        /// Extrae URLs de assets adicionales de un documento JSON de página de detalle.
        /// </summary>
        public List<string> ExtractAdditionalAssetsUrls(JsonDocument? eventDetailPageData)
        {
            List<string> assetUrls = new List<string>();
            if (eventDetailPageData == null)
            {
                return assetUrls;
            }

            try
            {
                if (eventDetailPageData.RootElement.TryGetProperty("blades", out JsonElement bladesElement) && bladesElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement blade in bladesElement.EnumerateArray())
                    {
                        // Nueva lógica para backdrop.background.url (cuando es una imagen directa)
                        if (blade.TryGetProperty("backdrop", out JsonElement backdropElement) && backdropElement.ValueKind == JsonValueKind.Object &&
                            backdropElement.TryGetProperty("background", out JsonElement backgroundElement) && backgroundElement.ValueKind == JsonValueKind.Object)
                        {
                            // AÑADIDO: Si background tiene una propiedad "url" y es de tipo string, es una imagen directa.
                            if (backgroundElement.TryGetProperty("url", out JsonElement backgroundUrlElement) && backgroundUrlElement.ValueKind == JsonValueKind.String)
                            {
                                string? backgroundImageUrl = backgroundUrlElement.GetString();
                                if (!string.IsNullOrEmpty(backgroundImageUrl))
                                {
                                    assetUrls.Add(backgroundImageUrl);
                                }
                            }

                            // Lógica existente para backdrop.background.sources (video)
                            if (backgroundElement.TryGetProperty("sources", out JsonElement sourcesElement) && sourcesElement.ValueKind == JsonValueKind.Array)
                            {
                                foreach (JsonElement source in sourcesElement.EnumerateArray())
                                {
                                    if (source.TryGetProperty("src", out JsonElement srcElement) && srcElement.ValueKind == JsonValueKind.String)
                                    {
                                        assetUrls.Add(srcElement.GetString()!);
                                    }
                                }
                            }
                            // Lógica existente para backdrop.background.thumbnail (thumbnail de video)
                            if (backgroundElement.TryGetProperty("thumbnail", out JsonElement thumbnailElement) && thumbnailElement.ValueKind == JsonValueKind.Object &&
                                thumbnailElement.TryGetProperty("url", out JsonElement thumbnailUrlElement) && thumbnailUrlElement.ValueKind == JsonValueKind.String)
                            {
                                assetUrls.Add(thumbnailUrlElement.GetString()!);
                            }
                        }

                        // header.media (imagen del encabezado)
                        if (blade.TryGetProperty("header", out JsonElement headerElement) && headerElement.ValueKind == JsonValueKind.Object &&
                            headerElement.TryGetProperty("media", out JsonElement mediaElement) && mediaElement.ValueKind == JsonValueKind.Object &&
                            mediaElement.TryGetProperty("url", out JsonElement mediaUrlElement) && mediaUrlElement.ValueKind == JsonValueKind.String)
                        {
                            assetUrls.Add(mediaUrlElement.GetString()!);
                        }

                        // leagueClientTabContentGroups.ctas.media (imágenes de skins, etc.)
                        if (blade.TryGetProperty("leagueClientTabContentGroups", out JsonElement tabGroupsElement) && tabGroupsElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (JsonElement tabGroup in tabGroupsElement.EnumerateArray())
                            {
                                if (tabGroup.TryGetProperty("ctas", out JsonElement ctasElement) && ctasElement.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (JsonElement cta in ctasElement.EnumerateArray())
                                    {
                                        if (cta.TryGetProperty("media", out JsonElement ctaMediaElement) && ctaMediaElement.ValueKind == JsonValueKind.Object &&
                                            ctaMediaElement.TryGetProperty("url", out JsonElement ctaMediaUrlElement) && ctaMediaUrlElement.ValueKind == JsonValueKind.String)
                                        {
                                            assetUrls.Add(ctaMediaUrlElement.GetString()!);
                                        }
                                    }
                                }
                            }
                        }

                        // links.media (imágenes de cinematic, motion comics secundarios, etc.) y iframes
                        JsonElement currentLinksElement;

                        // Priorizar links dentro de header si existen, si no, buscar en links directos del blade
                        if (blade.TryGetProperty("header", out JsonElement headerBladeElement) && headerBladeElement.ValueKind == JsonValueKind.Object &&
                            headerBladeElement.TryGetProperty("links", out JsonElement headerLinksElement) && headerLinksElement.ValueKind == JsonValueKind.Array)
                        {
                            currentLinksElement = headerLinksElement;
                        }
                        else if (blade.TryGetProperty("links", out JsonElement directLinksElement) && directLinksElement.ValueKind == JsonValueKind.Array)
                        {
                            currentLinksElement = directLinksElement;
                        }
                        else
                        {
                            continue; // No hay links en este blade
                        }

                        foreach (JsonElement link in currentLinksElement.EnumerateArray())
                        {
                            if (link.TryGetProperty("media", out JsonElement linkMediaElement) && linkMediaElement.ValueKind == JsonValueKind.Object &&
                                linkMediaElement.TryGetProperty("url", out JsonElement linkMediaUrlElement) && linkMediaUrlElement.ValueKind == JsonValueKind.String)
                            {
                                assetUrls.Add(linkMediaUrlElement.GetString()!);
                            }
                            // Buscar URLs de iframes que podrían ser assets o contenido relevante
                            if (link.TryGetProperty("action", out JsonElement actionElement) && actionElement.ValueKind == JsonValueKind.Object &&
                                actionElement.TryGetProperty("type", out JsonElement typeElement) && typeElement.GetString() == "open_iframe" &&
                                actionElement.TryGetProperty("payload", out JsonElement payloadElement) && payloadElement.ValueKind == JsonValueKind.Object &&
                                payloadElement.TryGetProperty("url", out JsonElement iframeUrlElement) && iframeUrlElement.ValueKind == JsonValueKind.String)
                            {
                                assetUrls.Add(iframeUrlElement.GetString()!);
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
        /// <param name="eventDetailsDocument">El documento JSON de la página de detalle del evento.</param>
        /// <param name="eventDetailsToPopulate">El objeto EventDetails a rellenar con el título.</param>
        /// <returns>Una lista de objetos MainEventLink si se encuentran URLs principales, de lo contrario una lista vacía.</returns>
        public List<MainEventLink> GetMainEventUrlsFromDetailPage(JsonDocument? eventDetailsDocument, EventDetails eventDetailsToPopulate) // ¡Cambio de retorno!
        {
            List<MainEventLink> foundMainEventLinks = new List<MainEventLink>(); // Lista para almacenar todas las URLs encontradas

            if (eventDetailsDocument == null || eventDetailsToPopulate == null)
            {
                return foundMainEventLinks; // Retorna lista vacía si la entrada es nula
            }

            try
            {
                // Intenta obtener el título (asumiendo que está a nivel raíz)
                if (eventDetailsDocument.RootElement.TryGetProperty("title", out JsonElement titleElement) && titleElement.ValueKind == JsonValueKind.String)
                {
                    eventDetailsToPopulate.Title = titleElement.GetString()!;
                }

                if (eventDetailsDocument.RootElement.TryGetProperty("blades", out JsonElement bladesElement) && bladesElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement blade in bladesElement.EnumerateArray())
                    {
                        JsonElement currentLinksElement;

                        // Priorizar links dentro de header si existen, si no, buscar en links directos del blade
                        if (blade.TryGetProperty("header", out JsonElement headerBladeElement) && headerBladeElement.ValueKind == JsonValueKind.Object &&
                            headerBladeElement.TryGetProperty("links", out JsonElement headerLinksElement) && headerLinksElement.ValueKind == JsonValueKind.Array)
                        {
                            currentLinksElement = headerLinksElement;
                        }
                        else if (blade.TryGetProperty("links", out JsonElement directLinksElement) && directLinksElement.ValueKind == JsonValueKind.Array)
                        {
                            currentLinksElement = directLinksElement;
                        }
                        else
                        {
                            continue; // No hay links relevantes en este blade, pasar al siguiente
                        }

                        foreach (JsonElement link in currentLinksElement.EnumerateArray())
                        {
                            if (link.TryGetProperty("action", out JsonElement actionElement) && actionElement.ValueKind == JsonValueKind.Object &&
                                actionElement.TryGetProperty("type", out JsonElement typeElement) && typeElement.GetString() == "lc_open_metagame" &&
                                actionElement.TryGetProperty("payload", out JsonElement payloadElement) && payloadElement.ValueKind == JsonValueKind.Object &&
                                payloadElement.TryGetProperty("url", out JsonElement urlElement) && urlElement.ValueKind == JsonValueKind.String)
                            {
                                string? currentUrl = urlElement.GetString();
                                // Es importante verificar si 'metagameId' existe antes de intentar obtenerlo
                                string? currentMetagameId = payloadElement.TryGetProperty("metagameId", out JsonElement metagameIdElement) && metagameIdElement.ValueKind == JsonValueKind.String
                                                            ? metagameIdElement.GetString() : null;
                                // También obtenemos el título del link
                                string? linkTitle = link.TryGetProperty("title", out JsonElement linkTitleElement) && linkTitleElement.ValueKind == JsonValueKind.String
                                                    ? linkTitleElement.GetString() : null;

                                if (currentUrl != null && currentUrl.Contains(EmbedUrlIdentifier, StringComparison.OrdinalIgnoreCase))
                                {
                                    Console.WriteLine($"DEBUG: URL principal de metajuego encontrada: {currentUrl} (MetagameId: {currentMetagameId ?? "N/A"})");
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
            catch (Exception e)
            {
                _logService.LogError($"DetailPageParser: Error al extraer URLs principales y MetagameId de la página de detalle: {e.Message}");
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