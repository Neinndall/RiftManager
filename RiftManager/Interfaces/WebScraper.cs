using HtmlAgilityPack;
using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using RiftManager.Services;

namespace RiftManager.Interfaces
{
    public class WebScraper
    {
        private readonly HttpClient _httpClient;
        private readonly LogService _logService;

        public WebScraper(HttpClient httpClient, LogService logService)
        {
            _httpClient = httpClient;
            _logService = logService;
        }

        /// <summary>
        /// Obtiene el contenido HTML de una URL.
        /// Este método se usa principalmente por EmbedAssetScraperService para eventos seleccionados,
        /// donde los logs DEBUG son aceptables.
        /// </summary>
        /// <param name="url">La URL de la que obtener el contenido.</param>
        /// <returns>El contenido de la URL como string.</returns>
        public async Task<string> GetContentFromUrl(string url)
        {
            _logService.LogDebug($"WebScraper: Getting content from URL: {url}");
            string content = await _httpClient.GetStringAsync(url);
            _logService.LogDebug($"WebScraper: URL content {url} obtained.");
            return content;
        }

        /// <summary>
        /// Intenta obtener la URL base del catálogo de assets de una página de evento.
        /// Este método es llamado durante la fase inicial de *rastreo* de eventos,
        /// por lo tanto, evita logs de depuración/progreso, solo reporta errores críticos.
        /// </summary>
        /// <param name="eventUrl">La URL del evento a scrapear.</param>
        /// <param name="linkTitle">El título del link principal del evento, usado para diferenciar tipos de eventos (e.g., cómics vs. metajuegos).</param>
        /// <returns>La URL base del catálogo si se encuentra, de lo contrario null.</returns>
        public async Task<string> GetCatalogBaseUrl(string eventUrl, string linkTitle = null)
        {
            try
            {
                string html = await _httpClient.GetStringAsync(eventUrl);

                HtmlDocument doc = new HtmlDocument();
                doc.LoadHtml(html);

                // Busca el elemento <link> con rel="preload", as="font" y que contenga "woff2"
                HtmlNode linkNode = doc.DocumentNode.SelectSingleNode("//link[@rel='preload' and @as='font' and contains(@href, 'woff2')]");

                if (linkNode != null)
                {
                    string href = linkNode.GetAttributeValue("href", string.Empty);
                    if (!string.IsNullOrEmpty(href))
                    {
                        // 1. Extrae la parte de la URL que necesitas.
                        int endIndex = href.IndexOf("_next/static/media/");
                        if (endIndex != -1)
                        {
                            string baseUrl = href.Substring(0, endIndex);

                            // Determina el sufijo del path del catálogo basado en el linkTitle
                            string catalogPathSuffix = GetCatalogJsonPathSuffix(linkTitle);

                            // 2. Añade la cadena fija.
                            return baseUrl + catalogPathSuffix;
                        }
                        // No loguear detalles internos si no se encuentra un patrón. Simplemente se retorna null.
                    }
                }

                // No loguear detalles internos si no se encuentra un nodo. Simplemente se retorna null.
                return null; // No se encontró la URL base o el patrón no coincidió
            }
            catch (HttpRequestException e)
            {
                // Este es un error crítico al intentar acceder a la URL, debe ser logueado.
                _logService.LogError($"WebScraper: Error getting HTML from {eventUrl}: {e.Message}");
                return null;
            }
            catch (Exception e)
            {
                // Este es un error inesperado durante el procesamiento del HTML, debe ser logueado.
                _logService.LogError($"WebScraper: Error processing HTML from {eventUrl}: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Determina el sufijo del path para el catalog.json basado en el título del link.
        /// </summary>
        /// <param name="linkTitle">El título del link del evento.</param>
        /// <returns>El sufijo del path para el catalog.json.</returns>
        private string GetCatalogJsonPathSuffix(string linkTitle)
        {
            if (linkTitle != null && (linkTitle.Contains("play", StringComparison.OrdinalIgnoreCase) || linkTitle.Contains("minigame", StringComparison.OrdinalIgnoreCase)))
            {
                return "WebGLBuild/StreamingAssets/aa/catalog.bin";
            }
            else if (linkTitle != null && linkTitle.Contains("comic", StringComparison.OrdinalIgnoreCase))
            {
                return "Comic/WebGLBuild/StreamingAssets/aa/catalog.bin";
            }
            // Por defecto, si no se puede determinar, asumimos el comportamiento original (Comic).
            return "Comic/WebGLBuild/StreamingAssets/aa/catalog.bin";
        }
    }
}