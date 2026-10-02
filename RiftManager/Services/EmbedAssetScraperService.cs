using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using RiftManager.Utils;
using RiftManager.Views.Interfaces;

namespace RiftManager.Services
{
    public class EmbedAssetScraperService
    {
        private readonly HttpClient _httpClient;
        private readonly AssetDownloader _assetDownloader;
        private readonly LogService _logService;
        private readonly WebScraper _webScraper;

        // Known main files for frontpages (kept for compat; no longer throws).
        public static readonly List<string> KnownMainFiles = new List<string> { "app", "app.css" };

        // Known patterns for main files (using regex).
        public static readonly List<string> KnownMainFilePatterns = new List<string>
        {
            @"^\d+-[a-f0-9]{8,}\.js$",  // Para archivos 686- .js
            @"^[a-f0-9]{8,}\.css$"    // Para archivos .css como 44939c99c1f6ea56.css
        };

        // v1.2.0: strict allow-list for JS-derived relative assets. Anything that
        // does not look like a webpack-emitted file (n.p+"images/..."/" lib-embed/..."
        // or name.hash.ext) is JS code noise, not a downloadable file.
        private static readonly Regex WebpackPublicPathRegex =
            new Regex("n\\.p\\s*\\+\\s*[\"'](?<p>[^\"']+\\.(?:png|jpe?g|gif|webm|webp|svg|ogg|mp3|mp4|json|woff2?))[^\"']*[\"']",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex AbsoluteAssetcdnRegex =
            new Regex("https://assetcdn\\.rgpub\\.io/[^\\s\"'`)\\]]+?\\.(?:png|jpe?g|gif|webm|webp|svg|ogg|mp3|mp4|json|woff2?)(?:\\?[^\\s\"'`)\\]]*)?",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex GenericAssetRegex =
            new Regex("\\.?(?<path>[\\w.\\/-]*\\.(?:jpg|jpeg|png|gif|webm|svg|webp|ogg|mp3|mp4|json|woff2?))",
                RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

        // Basenames that are JS artifacts, never files (log 10:37:18-10:37:48 404s).
        private static readonly HashSet<string> JsNoiseBasenames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "this", "e", "t", "n", "r", "window", "image", "self", "global"
        };

        public EmbedAssetScraperService(HttpClient httpClient, AssetDownloader assetDownloader, LogService logService, WebScraper webScraper)
        {
            _httpClient = httpClient;
            _assetDownloader = assetDownloader;
            _logService = logService;
            _webScraper = webScraper;
        }

        // Check for known main files (kept; now advisory only).
        private bool IsMainFile(string fileName)
        {
            return KnownMainFiles.Any(file =>
                fileName.Equals(file) ||
                fileName.StartsWith(file + ".")) ||
                KnownMainFilePatterns.Any(pattern =>
                Regex.IsMatch(fileName, pattern));
        }

        /// <summary>
        /// Saves the inline HTML SVGs.
        /// </summary>
        /// <param name="foundSvgs">An array of found inline HTML SVGs.</param>
        /// <param name="tmpDir">The directory to save the files.</param>
        private async Task SaveSvgs(List<string> svgContents, string tmpDir)
        {
            string exportDir = Path.Combine(tmpDir, "svg");
            Directory.CreateDirectory(exportDir);

            int svgsSavedSuccessfully = 0; // Solo un contador para los éxitos

            foreach (var svgContent in svgContents)
            {
                if (svgContent.Contains("<svg>\"+r+\"</svg>"))
                {
                    _logService.LogWarning("Ignoring SVG containing '<svg>\"+r+\"</svg>'.");
                    continue;
                }

                string hash = Crypto.Md5HashEncode(svgContent);
                string fileName = hash + ".svg";
                string savePath = Path.Combine(exportDir, fileName);

                try
                {
                    await File.WriteAllTextAsync(savePath, svgContent);
                    svgsSavedSuccessfully++; // Solo incrementamos si se guardó
                }
                catch (Exception ex)
                {
                    _logService.LogError($"Error saving SVG file {fileName}: {ex.Message}"); // Los errores sí se loguean
                }
            }

            // Log de resumen final
            if (svgsSavedSuccessfully > 0)
            {
                _logService.LogSuccess($"Finished saving inline SVGs. Successfully saved {svgsSavedSuccessfully} files.");
            }
            else if (svgContents.Count > 0)
            {
                _logService.LogWarning($"Finished saving inline SVGs, but no files were successfully saved.");
            }
            else
            {
                _logService.Log($"No inline SVGs found to save.");
            }
        }

        private async Task DownloadJsAssets(string content, string distURL, string tmpDir, HashSet<string> downloadedAssets)
        {
            _logService.Log($"Starting asset discovery and download from JS file (distURL: {distURL}).");

            var candidates = new List<string>();

            // Phase A: webpack public-path emissions (n.p+"images/x.HASH.jpg") - always real.
            foreach (Match m in WebpackPublicPathRegex.Matches(content))
                candidates.Add(m.Groups["p"].Value);

            // Phase B: absolute assetcdn URLs embedded in JS.
            foreach (Match m in AbsoluteAssetcdnRegex.Matches(content))
                candidates.Add(m.Value);

            // Phase C: generic relative paths, strictly filtered (see IsDownloadableJsPath).
            foreach (Match m in GenericAssetRegex.Matches(content))
            {
                string p = m.Groups["path"].Value;
                if (IsDownloadableJsPath(p, content, m))
                    candidates.Add(p);
            }

            // Eliminar duplicados para evitar descargas redundantes.
            var uniqueAssetPaths = candidates.Distinct().ToList();

            _logService.Log($"Found {uniqueAssetPaths.Count} unique potential asset paths in the JS file.");

            // Buscar cuantos .svgs se encuentran
            var foundSvgs = await Finder.FindSvgs(tmpDir, content, _logService);

            // Derivar la URL base para la descarga de estos assets.
            string downloadBaseUrl = (Path.GetDirectoryName(distURL) ?? string.Empty)
                                         .Replace("\\", "/") // Normalizar barras
                                         .Replace("https:/", "https://"); // Asegurarse de tener el doble slash

            // Ruta del archivo para registrar los assets descargados (ej. files.txt).
            string filesPath = Path.Combine(tmpDir, "files.txt");

            // Descargar cada asset encontrado.
            foreach (var assetRelativePath in uniqueAssetPaths)
            {
                if (assetRelativePath.Contains("/fe/"))
                    continue;

                // NEW: Ignore absolute URLs or external domains like lolesports
                if (assetRelativePath.StartsWith("//") || assetRelativePath.Contains("lolesports.com"))
                {
                    _logService.LogWarning($"Skipping external or absolute asset path: {assetRelativePath}");
                    continue;
                }

                string fullAssetUrl;
                if (assetRelativePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    fullAssetUrl = assetRelativePath;
                }
                else
                {
                    // Construir la URL completa para la descarga.
                    fullAssetUrl = $"{downloadBaseUrl}/{assetRelativePath.TrimStart('/')}";
                }

                // Normalizar el nombre para el registro de assets descargados.
                var normalizedName = ObjectHelper.NormalizeAssetName(fullAssetUrl);

                // Verificar si el asset ya fue descargado en esta sesión.
                if (downloadedAssets.Contains(normalizedName))
                {
                    _logService.LogWarning($"Skipping download of {Path.GetFileName(assetRelativePath)}, already downloaded (normalized: {normalizedName}).");
                    continue;
                }

                downloadedAssets.Add(normalizedName); // Marcar como descargado

                // Determinar el directorio de exportación manteniendo la estructura de directorios relativa.
                string assetFileName = Path.GetFileName(assetRelativePath);
                string rawAssetFileDirectory = Path.GetDirectoryName(assetRelativePath);
                string assetFileDirectory = rawAssetFileDirectory == null ? "" : rawAssetFileDirectory.Replace("\\", "/");
                assetFileDirectory = assetFileDirectory.Replace("_/lib-embed/", "lib-embed/");

                string finalExportDir = Path.Combine(tmpDir, assetFileDirectory).Replace("\\", "/");

                // Crear las carpetas necesarias.
                Directory.CreateDirectory(finalExportDir);

                try
                {
                    await _assetDownloader.DownloadAsset(fullAssetUrl, finalExportDir);

                    // Registrar el archivo descargado en "files.txt".
                    await File.AppendAllTextAsync(filesPath, Path.Combine(assetFileDirectory, assetFileName) + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    _logService.LogError($"Failed to download asset {assetFileName} from {fullAssetUrl}: {ex.Message}");
                }
            }

            // Saves the found SVGs.
            await SaveSvgs(foundSvgs, tmpDir);
        }

        /// <summary>
        /// Strict gate for generic JS regex hits. Why: the loose pattern also matches
        /// JS code fragments ("this.svg", "e.svg", "window.webp", ".svg", ".json",
        /// "image.jpg", "./button-x.svg" module-map keys) which produced every 404 in
        /// the HoL log. Webpack module keys ("./x.svg":1234) are inline SVGs already
        /// handled by Finder.FindSvgs, never HTTP files.
        /// </summary>
        private static bool IsDownloadableJsPath(string path, string fullContent, Match match)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (path.StartsWith("//") || path.Contains("lolesports.com") || path.Contains("youtube.com"))
                return false;
            if (path.Contains("/fe/")) return false;
            if (path.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("blob:", StringComparison.OrdinalIgnoreCase))
                return false;

            string fileName = path.Split('/').Last();
            string baseName = fileName.Contains('.') ? fileName.Substring(0, fileName.LastIndexOf('.')) : fileName;
            if (string.IsNullOrEmpty(baseName) || baseName.Length < 4) return false; // ".svg"/".json"
            if (JsNoiseBasenames.Contains(baseName)) return false; // this/e/window/image

            // Webpack inline-SVG module map: "./icon-x.svg":1234 -> skip (inline, not HTTP).
            if (path.StartsWith("./") && path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                return false;

            // Leading-slash router paths without asset folder or hash ("/bg-x.jpg") are
            // Vue routes, not files; real files live under images/ or carry a hash.
            bool hasAssetFolder = path.Contains("images/", StringComparison.OrdinalIgnoreCase)
                || path.Contains("lib-embed", StringComparison.OrdinalIgnoreCase)
                || path.Contains("assets/", StringComparison.OrdinalIgnoreCase);
            bool hasHash = Regex.IsMatch(fileName, @"\.[0-9a-fA-F]{5,16}\.[a-zA-Z0-9]+$");
            bool hasSubdir = path.Contains('/');
            if (!hasAssetFolder && !hasHash && !hasSubdir) return false;

            return true;
        }

        private async Task DownloadCssAssets(string content, string distURL, string tmpDir, HashSet<string> downloadedAssets)
        {
            // v1.2.0: match absolute AND relative url(...) refs; skip data:/blob:.
            var urlRegex = new Regex("url\\((['\"]?)(?<url>(?!data:|blob:)[^'\"\\)]+?\\.((?:jpg|jpeg|png|gif|webm|svg|webp|ogg|mp3|mp4|json|woff2?))(?:\\?[^'\"\\)]*)?)\\1\\)", RegexOptions.IgnoreCase);
            var matches = urlRegex.Matches(content).Cast<Match>()
                .Select(m => m.Groups["url"].Value.Trim())
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Distinct()
                .ToList();

            // Resolve relative refs against the CSS file directory.
            string cssDir = (Path.GetDirectoryName(distURL) ?? string.Empty).Replace("\\", "/").Replace("https:/", "https://");
            var assetUrls = matches.Select(u =>
                u.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? u : $"{cssDir}/{u.TrimStart('/')}"
            ).Distinct().ToList();

            _logService.Log($"Found {assetUrls.Count} asset URLs in CSS.");

            string filesPath = Path.Combine(tmpDir, "files.txt");

            foreach (var assetUrl in assetUrls)
            {
                var normalizedName = ObjectHelper.NormalizeAssetName(assetUrl);
                if (downloadedAssets.Contains(normalizedName))
                {
                    _logService.Log($"Skipping download of {normalizedName}, already downloaded.");
                    continue;
                }

                downloadedAssets.Add(normalizedName);

                // --- Lógica para preservar la estructura de directorios ---
                string rawBasePath = Path.GetDirectoryName(distURL);
                string basePath = (rawBasePath ?? string.Empty)
                    .Replace("\\", "/")
                    .Replace("https:/", "https://")
                    .Replace("_next/static/media", "")
                    .Replace("_next/static/chunks", "")
                    .Replace("_next/static/css", ""); // Añadido para CSS

                string relativePath = assetUrl
                    .Replace(basePath ?? string.Empty, string.Empty)
                    .Replace("_/lib-embed/", "lib-embed/")
                    .Replace("_next/static/", "");

                string tempDirName = Path.GetDirectoryName(relativePath);
                string fileDirectory;
                if (tempDirName == null)
                {
                    fileDirectory = string.Empty;
                }
                else
                {
                    string nonNullableTempDirName = tempDirName;
                    fileDirectory = nonNullableTempDirName.Replace("\\", "/").TrimStart('/');
                }
                string exportDir = Path.Combine(tmpDir, fileDirectory).Replace("\\", "/");

                Directory.CreateDirectory(exportDir);

                string fileName = Path.GetFileName(new Uri(assetUrl.Split('?')[0]).AbsolutePath) ?? string.Empty;

                try
                {
                    await _assetDownloader.DownloadAsset(assetUrl, exportDir);
                    await File.AppendAllTextAsync(filesPath, Path.Combine(fileDirectory, fileName) + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    _logService.LogError($"Failed to download asset {fileName} from {assetUrl}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Main function to handle scraping of embed event URLs (rgpub.io).
        /// v1.2.0: processes root app.* dists only (same scope as v1.1.x).
        /// Per-event dedup set replaces the old instance field (which leaked
        /// across events on error).
        /// </summary>
        /// <param name="embedUrl">The embed URL (e.g., https://embed.rgpub.io/wwpub-hall-of-legends-embed-2025/en-us/).</param>
        /// <param name="tmpDir">The temporary directory to save assets.</param>
        public async Task HandleEmbedEventAsync(string embedUrl, string tmpDir)
        {
            var downloadedAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string normalizedEmbedUrl = UrlNormalizer.NormalizeEmbedUrl(embedUrl);

                // Primero, descargamos el HTML de la URL principal
                string htmlContent;
                try
                {
                    htmlContent = await _webScraper.GetContentFromUrl(normalizedEmbedUrl);
                }
                catch (Exception ex)
                {
                    _logService.LogError($"EmbedAssetScraperService: Failed to download HTML content from {normalizedEmbedUrl}: {ex.Message}");
                    return;
                }

                // Root app.* dists only (same scope as v1.1.x; vendors/commons/runtime
                // verified asset-free on HoL 2026).
                HashSet<string> uniqueDistUrls = DiscoverDistUrls(htmlContent);

                if (uniqueDistUrls.Count == 0)
                {
                    _logService.LogError($"Could not find any main JS/CSS dist files in {normalizedEmbedUrl}.");
                    return;
                }

                _logService.Log($"Found {uniqueDistUrls.Count} unique main dist files in {normalizedEmbedUrl}.");

                // Ahora iteramos sobre las URLs únicas de los archivos dist
                foreach (string distURL in uniqueDistUrls)
                {
                    _logService.Log($"Processing main dist file: {distURL}");
                    string fileName = (Path.GetFileName(new Uri(distURL).AbsolutePath) ?? string.Empty).Split('?')[0];

                    _logService.Log($"Validating main file: {fileName}");
                    if (!IsMainFile(fileName) && !fileName.EndsWith(".js", StringComparison.OrdinalIgnoreCase) && !fileName.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
                    {
                        // v1.2.0: warn-and-skip instead of throwing (old throw aborted the whole event).
                        _logService.LogWarning($"Skipping non-dist file '{fileName}'.");
                        continue;
                    }
                    Directory.CreateDirectory(tmpDir); // Asegurarse de que el directorio temporal exista

                    // Descargar el archivo principal (JS o CSS)
                    await _assetDownloader.DownloadDistFile(distURL, tmpDir);

                    // Contenido del archivo principal descargado (este 'content' es local al bucle).
                    string content = await File.ReadAllTextAsync(Path.Combine(tmpDir, fileName), Encoding.UTF8);

                    // Determinar si es un archivo CSS o JS y llamar a la lógica correspondiente
                    if (fileName.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
                    {
                        await DownloadCssAssets(content, distURL, tmpDir, downloadedAssets);
                    }
                    else if (fileName.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
                    {
                        await DownloadJsAssets(content, distURL, tmpDir, downloadedAssets);
                    }
                }
            }
            finally
            {
                downloadedAssets.Clear();
            }
        }

        private HashSet<string> DiscoverDistUrls(string html)
        {
            // v1.2.0 (corrected): ONLY root app.* dists. Why not preload-everything:
            // verified against HoL 2026 HTML+files - vendors/app.* (1.7 MB), commons/
            // and runtime contain ZERO downloadable game assets (no n.p emissions,
            // no assetcdn file refs, vendor.css has 0 url()). Worse, vendor.js holds
            // hundreds of /lol-game-data/... and /fe/... client-data strings that
            // must never resolve against the CDN (404 storm). Game assets live in
            // root app.HASH.js (webpack modules) and app.HASH.css (images-direct/).
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(html))
            {
                var distUrlRegex = new Regex(@"https://assetcdn\.rgpub\.io/public/live/bundle-offload/[^/]+/[^/]+/app\.[a-f0-9]+\.(?:js|css)", RegexOptions.IgnoreCase);
                foreach (Match m in distUrlRegex.Matches(html))
                    found.Add(m.Value);
            }

            return found;
        }
    }
}
