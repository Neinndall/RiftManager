using System;
using System.Collections.Generic;
using System.IO; // Necesario para Path.GetFileName
using System.Linq; // Necesario para .Select
using Newtonsoft.Json.Linq; // Cambiado desde System.Text.Json
using System.Text.RegularExpressions;
using RiftManager.Services; // Para LogService

namespace RiftManager.Interfaces
{
    public class CatalogParser
    {
        private readonly LogService _logService;

        public CatalogParser(LogService logService)
        {
           _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        }

        /// <summary>
        /// Parsea un token JToken de catálogo y extrae las URLs de bundles relevantes.
        /// Este método es llamado durante la fase de *rastreo* por BundleService,
        /// por lo tanto, debe ser lo más silencioso posible, solo reportando errores críticos
        /// o depuración muy específica. El conteo de bundles debe ser logueado por el llamador (EventProcessor).
        /// </summary>
        /// <param name="rootToken">El JToken que contiene el catálogo.</param>
        /// <param name="assetBaseUrl">La URL base para construir las URLs completas de los bundles.</param>
        /// <param name="metagameId">ID de metajuego opcional para filtrar bundles.</param>
        /// <returns>Una lista de URLs de bundles.</returns>
        public List<string> ParseBundleUrlsFromCatalogJson(JToken rootToken, string assetBaseUrl, string metagameId = null)
        {
            _logService.LogDebug($"CatalogParser: ParseBundleUrlsFromCatalogJson llamado con Metagame ID: {metagameId ?? "N/A"}");
            List<string> bundleUrls = new List<string>();

            if (rootToken == null)
            {
                _logService.LogWarning("CatalogParser: El token JSON del catálogo es nulo al intentar parsear.");
                return bundleUrls;
            }

            JToken internalIdsToken = rootToken["m_InternalIds"];
            if (internalIdsToken != null && internalIdsToken.Type == JTokenType.Array)
            {
                foreach (JToken idElement in internalIdsToken)
                {
                    string internalPath = idElement.ToString();
                    if (string.IsNullOrEmpty(internalPath)) continue;

                    string fullBundleUrl;
                    string pathForChecks = internalPath;

                    if (internalPath.StartsWith("0#"))
                    {
                        // Nuevo formato para 0#: Reemplaza "0#" y antepone la URL base.
                        pathForChecks = internalPath.Replace("0#", "WebGL/");
                        fullBundleUrl = assetBaseUrl + pathForChecks;
                    }
                    else if (internalPath.StartsWith("1#"))
                    {
                        // Nuevo formato para 1#: Reemplaza "1#" y antepone una ruta específica.
                        pathForChecks = internalPath.Replace("1#", "WebGL/ui_assets_assets/prefabs/ui/");
                        fullBundleUrl = assetBaseUrl + pathForChecks;
                    }
                    else
                    {
                        // Formato antiguo: Reemplaza el placeholder.
                        fullBundleUrl = internalPath.Replace("{UnityEngine.AddressableAssets.Addressables.RuntimePath}", assetBaseUrl);
                    }

                    if (pathForChecks.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase) &&
                        pathForChecks.Contains("WebGL", StringComparison.OrdinalIgnoreCase))
                    {
                        string fileName = Path.GetFileName(pathForChecks).ToLower();

                        // Lógica de filtrado mejorada para bundles de cómics
                        if (fileName.StartsWith("comics_assets_mc_", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(metagameId))
                        {
                            // Dividimos el metagameId (que ahora puede contener partes de la URL) y el nombre del archivo en palabras clave.
                            var contextKeywords = metagameId.ToLower().Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries).Where(k => k.Length > 2).ToList();
                            var fileNameKeywords = fileName.ToLower().Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries).ToList();

                            // Si no hay palabras clave de contexto, no filtramos.
                            if (contextKeywords.Any())
                            {
                                // Comprobamos si alguna palabra clave del contexto coincide (parcial o totalmente) con alguna palabra clave del nombre del archivo.
                                bool matchFound = contextKeywords.Any(contextKey =>
                                    fileNameKeywords.Any(fileKey => fileKey.Contains(contextKey) || contextKey.Contains(fileKey))
                                );

                                if (!matchFound)
                                {
                                    _logService.LogDebug($"CatalogParser: Saltando bundle de cómic '{fileName}' porque no coincide con las palabras clave de contexto: '{metagameId}'.");
                                    continue;
                                }
                            }
                        }

                        bundleUrls.Add(fullBundleUrl);
                    }
                }
            }
            else
            {
                _logService.LogError("CatalogParser: El documento JSON del catálogo no contiene la propiedad 'm_InternalIds' como un array. No se pudieron extraer bundles.");
            }

            return bundleUrls;
        }
    }
}