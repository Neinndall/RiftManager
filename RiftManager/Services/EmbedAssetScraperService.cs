// RiftManager.Services/EmbedAssetScraperService.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

// Importa tus utilidades
using RiftManager.Utils;
using RiftManager.Interfaces;

namespace RiftManager.Services
{
    public class EmbedAssetScraperService
    {
        private readonly HttpClient _httpClient;
        private readonly AssetDownloader _assetDownloader;
        private readonly LogService _logService; // Ahora sí, inyectamos tu LogService
        private readonly WebScraper _webScraper; // Esta instancia ya debería estar inyectada desde el cambio anterior

        // Almacenar assets ya descargados para evitar repeticion, ahora como campo de instancia
        private readonly HashSet<string> _downloadedAssets = new HashSet<string>();

        // Known main files for frontpages.
        public static readonly List<string> KnownMainFiles = new List<string> { "app", "app.css" };

        // Known patterns for main files (using regex).
        public static readonly List<string> KnownMainFilePatterns = new List<string>
        {
            @"^\d+-[a-f0-9]{8,}\.js$",  // Para archivos 686- .js
            @"^[a-f0-9]{8,}\.css$"    // Para archivos .css como 44939c99c1f6ea56.css
        };


        public EmbedAssetScraperService(HttpClient httpClient, AssetDownloader assetDownloader, LogService logService, WebScraper webScraper)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _assetDownloader = assetDownloader ?? throw new ArgumentNullException(nameof(assetDownloader));
            _logService = logService ?? throw new ArgumentNullException(nameof(logService)); // Inyectamos LogService
            _webScraper = webScraper ?? throw new ArgumentNullException(nameof(webScraper));
        }


        // Check for known main files
        private void CheckForMainFile(string fileName)
        {
            bool foundMainFile = KnownMainFiles.Any(file =>
                                                            fileName.Equals(file) ||
                                                            fileName.StartsWith(file + ".")) ||
                                                            KnownMainFilePatterns.Any(pattern =>
                                                            Regex.IsMatch(fileName, pattern));

            if (!foundMainFile)
            {
                throw new Exception("Please provide the dist file.");
            }
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

        private async Task DownloadJsAssets(string content, string distURL, string tmpDir)
        {
            _logService.Log($"Starting asset discovery and download from JS file (distURL: {distURL}).");

            // Expresión regular para encontrar CUALQUIER recurso con las extensiones deseadas dentro del JS.
            var pathRegex = new Regex(@"\.?(?<path>[\w\.\/-]*\.(?:jpg|png|gif|webm|svg|webp|ogg|json))", RegexOptions.IgnoreCase | RegexOptions.Multiline);

            // Encontrar todas las posibles rutas de assets dentro del contenido del JS.
            var potentialFiles = pathRegex.Matches(content)
                .Cast<Match>()
                .Select(m => m.Groups["path"].Value)
                .Select(p => p.Replace("/vendor", "/commons")) // Tu lógica de reemplazo
                .ToList();
                                 
            // Eliminar duplicados para evitar descargas redundantes.
            var uniqueAssetPaths = potentialFiles.Distinct().ToList();

            _logService.Log($"Found {uniqueAssetPaths.Count} unique potential asset paths in the JS file.");
            
            // Buscar cuantos .svgs se encuentran
            var foundSvgs = await Finder.FindSvgs(tmpDir, content, _logService);

            // Derivar la URL base para la descarga de estos assets.
            // Será la parte de la distURL sin el nombre del archivo (ej., app.XXXX.js)
            string downloadBaseUrl = (Path.GetDirectoryName(distURL) ?? string.Empty)
                                         .Replace("\\", "/") // Normalizar barras
                                         .Replace("https:/", "https://"); // Asegurarse de tener el doble slash
            
            // Ruta del archivo para registrar los assets descargados (ej. files.txt).
            string filesPath = Path.Combine(tmpDir, "files.txt");

            // Descargar cada asset encontrado.
            foreach (var assetRelativePath in uniqueAssetPaths)
            {
                // THIS is the correct place for the 'if' statement
                if (assetRelativePath.Contains("/fe/"))
                {
                    continue; // This 'continue' now has a loop to operate on
                }
                
                // Construir la URL completa para la descarga.
                string fullAssetUrl = $"{downloadBaseUrl}/{assetRelativePath.TrimStart('/')}";
                
                // Normalizar el nombre para el registro de assets descargados.
                var normalizedName = ObjectHelper.NormalizeAssetName(fullAssetUrl);
                
                // Verificar si el asset ya fue descargado en esta sesión.
                if (_downloadedAssets.Contains(normalizedName))
                {
                    _logService.LogWarning($"Skipping download of {Path.GetFileName(assetRelativePath)}, already downloaded (normalized: {normalizedName}).");
                    continue;
                }
                
                _downloadedAssets.Add(normalizedName); // Marcar como descargado

                // Determinar el directorio de exportación manteniendo la estructura de directorios relativa.
                string assetFileName = Path.GetFileName(assetRelativePath);
                string rawAssetFileDirectory = Path.GetDirectoryName(assetRelativePath);
                string assetFileDirectory = rawAssetFileDirectory == null ? "" : rawAssetFileDirectory.Replace("\\", "/");
                assetFileDirectory = assetFileDirectory.Replace("_/lib-embed/", "lib-embed/");
                
                // El directorio final de exportación ahora incluye la ruta relativa completa del asset.
                // No se reemplaza "_/lib-embed/" aquí, ya que queremos que la estructura local lo mantenga.
                string finalExportDir = Path.Combine(tmpDir, assetFileDirectory).Replace("\\", "/");

                // Crear las carpetas necesarias.
                Directory.CreateDirectory(finalExportDir);

                try
                {
                    await _assetDownloader.DownloadAsset(fullAssetUrl, finalExportDir);

                    // Registrar el archivo descargado en "files.txt".
                    await File.AppendAllTextAsync(filesPath, Path.Combine(assetFileDirectory, assetFileName) + Environment.NewLine);
                }
                catch (Exception ex) // Capturamos cualquier excepción, ya no hay lógica de reintento alternativa aquí.
                {
                    _logService.LogError($"Failed to download asset {assetFileName} from {fullAssetUrl}: {ex.Message}");
                }
            }
            
            // Saves the found SVGs.
            await SaveSvgs(foundSvgs, tmpDir);
        }
                
        private async Task DownloadCssAssets(string content, string distURL, string tmpDir)
        {
            var urlRegex = new Regex(@"url\((['""]?)(?<url>https?:\/\/[^'""\)]+\.(?:jpg|png|gif|webm|svg|webp|ogg|json))\1\)", RegexOptions.IgnoreCase);

            var assetUrls = urlRegex.Matches(content)
                .Cast<Match>()
                .Select(m => m.Groups["url"].Value)
                .Distinct()
                .ToList();

            _logService.Log($"Found {assetUrls.Count} asset URLs in CSS.");

            string filesPath = Path.Combine(tmpDir, "files.txt");

            foreach (var assetUrl in assetUrls)
            {
                // Ahora ObjectHelper está en RiftManager.Utils
                var normalizedName = ObjectHelper.NormalizeAssetName(assetUrl);
                if (_downloadedAssets.Contains(normalizedName))
                {
                    _logService.Log($"Skipping download of {normalizedName}, already downloaded.");
                    continue;
                }
                
                _downloadedAssets.Add(normalizedName);

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
                    .Replace("_/lib-embed/", "lib-embed/") // Mantengo estas líneas según tu código original
                    .Replace("_next/static/", ""); // Mantengo estas líneas según tu código original

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

                string fileName = Path.GetFileName(assetUrl) ?? string.Empty;

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
        /// </summary>
        /// <param name="embedUrl">The embed URL (e.g., https://embed.rgpub.io/wwpub-hall-of-legends-embed-2025/en-us/).</param>
        /// <param name="tmpDir">The temporary directory to save assets.</param>
        public async Task HandleEmbedEventAsync(string embedUrl, string tmpDir)
        {                   
            // Primero, descargamos el HTML de la URL principal
            string htmlContent;
            try
            {
                htmlContent = await _webScraper.GetContentFromUrl(embedUrl);
            }
            catch (Exception ex)
            {
                _logService.LogError($"EmbedAssetScraperService: Failed to download HTML content from {embedUrl}: {ex.Message}");
                return;
            }

            // Expresión regular para encontrar CUALQUIERA de los archivos JavaScript o CSS principales (distURL)
            var distUrlRegex = new Regex(@"https://assetcdn\.rgpub\.io/public/live/bundle-offload/[^/]+/[^/]+/app\.[a-f0-9]+\.(?:js|css)", RegexOptions.IgnoreCase);

            // Usamos Matches para obtener todas las coincidencias
            var distMatches = distUrlRegex.Matches(htmlContent);

            if (distMatches.Count == 0)
            {
                _logService.LogError($"Could not find any main JS/CSS dist files in {embedUrl}.");
                return; // Si no hay matches, salimos del método.
            }

            // --- CAMBIO PRINCIPAL: Desduplicar las URLs de los assets principales antes de procesarlos ---
            // Creamos un HashSet para almacenar solo URLs únicas de los archivos dist (app.xxxx.js/css).
            HashSet<string> uniqueDistUrls = new HashSet<string>(distMatches.Cast<Match>().Select(m => m.Value));

            _logService.Log($"Found {uniqueDistUrls.Count} unique main dist files in {embedUrl}.");

            // Ahora iteramos sobre las URLs únicas de los archivos dist
            foreach (string distURL in uniqueDistUrls)
            {
                _logService.Log($"Processing main dist file: {distURL}");
                string fileName = (Path.GetFileName(distURL) ?? string.Empty).Split('?')[0];

                _logService.Log($"Validating main file: {fileName}");
                CheckForMainFile(fileName); // Llama a la función que verifica el tipo de archivo principal
                Directory.CreateDirectory(tmpDir); // Asegurarse de que el directorio temporal exista

                // Descargar el archivo principal (JS o CSS)
                await _assetDownloader.DownloadDistFile(distURL, tmpDir); // Usando tu AssetDownloader

                // Contenido del archivo principal descargado (este 'content' es local al bucle).
                string content = await File.ReadAllTextAsync(Path.Combine(tmpDir, fileName), Encoding.UTF8);

                // Determinar si es un archivo CSS o JS y llamar a la lógica correspondiente
                if (fileName.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
                {
                    await DownloadCssAssets(content, distURL, tmpDir);
                }
                else if (fileName.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
                {
                    await DownloadJsAssets(content, distURL, tmpDir);
                }
            }
            _downloadedAssets.Clear(); // Limpiar el hashset para la próxima ejecución si la instancia se reutiliza.
        }
    }
}