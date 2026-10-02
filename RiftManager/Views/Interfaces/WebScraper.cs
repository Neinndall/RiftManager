using HtmlAgilityPack;
using System;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using RiftManager.Services;
using RiftManager.Utils;

namespace RiftManager.Views.Interfaces
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
            string normalized = UrlNormalizer.NormalizeEmbedUrl(url);
            return await _httpClient.GetStringAsync(normalized);
        }

        /// <summary>
        /// Discovery-based catalog lookup (v1.2.0). Why the rewrite: v1.1.x derived
        /// the CDN base ONLY from a woff2 preload link, but current Nuxt embeds
        /// (HoL 2026) have no woff2 preload - they expose the base via
        /// window.__NUXT__.config._app.cdnURL plus as="script|style" preloads.
        /// Result: GetCatalogBaseUrl always returned null for EmbedWeb and the app
        /// fell through to the embed scraper by accident instead of by decision.
        /// New order: 1) explicit catalog.bin/StreamingAssets/WebGLBuild refs in
        /// HTML, 2) cdnBase + legacy suffix guess (comics/play/minigame), else null
        /// (caller treats null as "no Unity catalog", i.e. EmbedWeb or Normal).
        /// </summary>
        public async Task<string> GetCatalogBaseUrl(string eventUrl, string linkTitle = null)
        {
            try
            {
                string targetUrl = UrlNormalizer.NormalizeEmbedUrl(eventUrl);
                string html = await _httpClient.GetStringAsync(targetUrl);

                // 1. Direct reference wins (robust against title-based guessing).
                string direct = FindCatalogUrlInHtml(html, targetUrl);
                if (!string.IsNullOrEmpty(direct))
                    return direct;

                // 2. Derive CDN base and probe legacy suffixes (comics history).
                string cdnBase = GetCdnBaseUrlFromHtml(html);
                if (string.IsNullOrEmpty(cdnBase))
                    return null; // EmbedWeb without Unity: not an error.

                string suffix = GetCatalogJsonPathSuffix(linkTitle, eventUrl);
                string candidate = cdnBase.TrimEnd('/') + "/" + suffix.TrimStart('/');

                // Avoid a wasted BinToJson cycle: HEAD-check the candidate.
                try
                {
                    using var head = new HttpRequestMessage(HttpMethod.Head, candidate);
                    using var resp = await _httpClient.SendAsync(head);
                    if (resp.IsSuccessStatusCode)
                        return candidate;
                }
                catch { /* fall through to null */ }

                return null;
            }
            catch (Exception e)
            {
                _logService.LogError($"WebScraper: Error processing {eventUrl}: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Extracts the asset CDN base from raw HTML. Public so the embed scraper
        /// reuses the exact same base instead of re-parsing differently.
        /// Priority: __NUXT__.cdnURL &gt; as=script/style preload &gt; script src &gt; legacy woff2.
        /// </summary>
        public string GetCdnBaseUrlFromHtml(string html)
        {
            if (string.IsNullOrEmpty(html)) return null;

            // 1. Nuxt runtime config: cdnURL:"https://assetcdn.rgpub.io/.../hash/"
            var nuxt = Regex.Match(html, "cdnURL\\s*:\\s*\"(?<u>https://assetcdn\\.rgpub\\.io/[^\"]+)\"");
            if (nuxt.Success)
                return UrlNormalizer.EnsureTrailingSlash(nuxt.Groups["u"].Value);

            try
            {
                HtmlDocument doc = new HtmlDocument();
                doc.LoadHtml(html);

                // 2. Any preload/script/stylesheet pointing at assetcdn.
                var nodes = doc.DocumentNode.SelectNodes(
                    "//link[@rel='preload' and contains(@href,'assetcdn.rgpub.io')] | " +
                    "//script[contains(@src,'assetcdn.rgpub.io')] | " +
                    "//link[contains(@href,'assetcdn.rgpub.io')]");
                if (nodes != null)
                {
                    foreach (var n in nodes)
                    {
                        string href = n.GetAttributeValue("href", null) ?? n.GetAttributeValue("src", null);
                        if (string.IsNullOrEmpty(href)) continue;
                        int lastSlash = href.LastIndexOf('/');
                        if (lastSlash > "https://".Length)
                            return UrlNormalizer.EnsureTrailingSlash(href.Substring(0, lastSlash));
                    }
                }

                // 3. Legacy woff2 preload (old comics deploys).
                HtmlNode linkNode = doc.DocumentNode.SelectSingleNode("//link[@rel='preload' and @as='font' and contains(@href, 'woff2')]");
                if (linkNode != null)
                {
                    string href = linkNode.GetAttributeValue("href", string.Empty);
                    int endIndex = href.IndexOf("_next/static/media/");
                    if (endIndex != -1)
                        return href.Substring(0, endIndex);
                }
            }
            catch { /* regex path already tried; return null below */ }

            return null;
        }

        private static string FindCatalogUrlInHtml(string html, string pageUrl)
        {
            // Absolute catalog.bin reference.
            var abs = Regex.Match(html, @"https://[^\s""']*?catalog\.bin", RegexOptions.IgnoreCase);
            if (abs.Success) return abs.Value;

            // Relative StreamingAssets/WebGLBuild reference -> resolve against CDN base.
            var rel = Regex.Match(html, @"(?<p>(?:[\w\-./]*WebGLBuild/StreamingAssets/aa/catalog\.bin|StreamingAssets/aa/catalog\.bin))", RegexOptions.IgnoreCase);
            if (rel.Success)
            {
                // Prefer resolving against the page URL directory; caller HEAD-checks.
                try
                {
                    var baseUri = new Uri(pageUrl);
                    return new Uri(baseUri, rel.Groups["p"].Value).ToString();
                }
                catch { return rel.Groups["p"].Value; }
            }

            return null;
        }

        private string GetCatalogJsonPathSuffix(string linkTitle, string eventUrl)
        {
            var comicMatch = Regex.Match(eventUrl ?? string.Empty, @"comic(\d+)", RegexOptions.IgnoreCase);
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
