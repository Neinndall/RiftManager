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
            
            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            }
        }

        public async Task<string> GetContentFromUrl(string url)
        {
            return await _httpClient.GetStringAsync(url);
        }

        public async Task<string> GetCatalogBaseUrl(string eventUrl, string linkTitle = null)
        {
            try
            {
                string targetUrl = eventUrl.Replace("{locale}", "en-us");
                string html = await _httpClient.GetStringAsync(targetUrl);

                HtmlDocument doc = new HtmlDocument();
                doc.LoadHtml(html);

                // Obtener la BASE URL del CDN mediante el link de la fuente (contiene el hash de despliegue)
                HtmlNode linkNode = doc.DocumentNode.SelectSingleNode("//link[@rel='preload' and @as='font' and contains(@href, 'woff2')]");
                if (linkNode != null)
                {
                    string href = linkNode.GetAttributeValue("href", string.Empty);
                    if (!string.IsNullOrEmpty(href))
                    {
                        int endIndex = href.IndexOf("_next/static/media/");
                        if (endIndex != -1)
                        {
                            string baseUrl = href.Substring(0, endIndex);
                            string catalogPathSuffix = GetCatalogJsonPathSuffix(linkTitle, eventUrl);

                            return baseUrl + catalogPathSuffix;
                        }
                    }
                }

                return null;
            }
            catch (Exception e)
            {
                _logService.LogError($"WebScraper: Error processing {eventUrl}: {e.Message}");
                return null;
            }
        }

        private string GetCatalogJsonPathSuffix(string linkTitle, string eventUrl)
        {
            var comicMatch = Regex.Match(eventUrl, @"comic(\d+)", RegexOptions.IgnoreCase);
            string comicNumber = comicMatch.Success ? comicMatch.Groups[1].Value : null;

            if (linkTitle != null && linkTitle.Contains("comic", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(comicNumber))
            {
                return $"comics-pipeline-{comicNumber}/WebGLBuild/StreamingAssets/aa/catalog.bin";
            }
            
            if (linkTitle != null && (linkTitle.Contains("play", StringComparison.OrdinalIgnoreCase) || linkTitle.Contains("minigame", StringComparison.OrdinalIgnoreCase)))
            {
                return "WebGLBuild/StreamingAssets/aa/catalog.bin";
            }

            return "Comic/WebGLBuild/StreamingAssets/aa/catalog.bin";
        }
    }
}
