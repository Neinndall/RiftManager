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
            _logService.Log($"[CatalogParser] Starting parse with keywords: {metagameId ?? "N/A"}");
            List<string> bundleUrls = new List<string>();

            if (rootToken == null)
            {
                _logService.LogWarning("[CatalogParser] The catalog JSON token is null when attempting to parse.");
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
                        pathForChecks = internalPath.Replace("0#", "WebGL/");
                        fullBundleUrl = assetBaseUrl + pathForChecks;
                    }
                    else if (internalPath.StartsWith("1#"))
                    {
                        pathForChecks = internalPath.Replace("1#", "WebGL/ui_assets_assets/prefabs/ui/");
                        fullBundleUrl = assetBaseUrl + pathForChecks;
                    }
                    else
                    {
                        fullBundleUrl = internalPath.Replace("{UnityEngine.AddressableAssets.Addressables.RuntimePath}", assetBaseUrl);
                    }

                    if (pathForChecks.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase) &&
                        pathForChecks.Contains("WebGL", StringComparison.OrdinalIgnoreCase))
                    {
                        string fileName = Path.GetFileName(pathForChecks).ToLower();

                        // Lógica de filtrado mejorada para bundles de cómics
                        if (fileName.StartsWith("comics_assets_mc_", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(metagameId))
                        {
                            var contextKeywords = metagameId.ToLower().Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries).Where(k => k.Length > 2).ToList();

                            if (contextKeywords.Any())
                            {
                                bool matchFound = contextKeywords.Any(contextKey => fileName.Contains(contextKey));

                                if (!matchFound)
                                {
                                    // Cambiado a Log para visibilidad total
                                    _logService.Log($"[CatalogParser] SKIPPING: '{fileName}' (No match with keywords: {string.Join(", ", contextKeywords)})");
                                    continue;
                                }
                                else
                                {
                                    _logService.Log($"[CatalogParser] ACCEPTED: '{fileName}' (Match found!)");
                                }
                            }
                        }

                        bundleUrls.Add(fullBundleUrl);
                    }
                }
            }
            else
            {
                _logService.LogError("[CatalogParser] The catalog JSON document does not contain the 'm_InternalIds' property as an array. Bundles could not be extracted.");
            }

            return bundleUrls;
        }
    }
}