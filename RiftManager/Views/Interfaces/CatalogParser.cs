using System;
using System.Collections.Generic;
using System.IO; // Necesario para Path.GetFileName
using System.Linq; // Necesario para .Select
using Newtonsoft.Json.Linq; // Cambiado desde System.Text.Json
using System.Text.RegularExpressions;
using RiftManager.Services; // Para LogService

namespace RiftManager.Views.Interfaces
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

                        // v1.2.0: generalized keyword filter. Why: old code only filtered
                        // files starting with "comics_assets_mc_", so play/minigame and
                        // future prefixes bypassed filtering entirely (all-or-nothing).
                        // Now any Unity bundle family is filtered by event keywords when
                        // keywords exist; empty keywords still accept everything.
                        if (!string.IsNullOrEmpty(metagameId) && IsFilterableBundle(fileName))
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

        /// <summary>
        /// Bundle families subject to keyword filtering (comics + play/minigame Unity).
        /// Shared thematic bundles that carry none of the keywords are still skipped
        /// by design; EventProcessor feeds ALL event links/titles so region bundles match.
        /// </summary>
        private static bool IsFilterableBundle(string fileNameLower)
        {
            return fileNameLower.StartsWith("comics_assets_", StringComparison.Ordinal)
                || fileNameLower.StartsWith("play_", StringComparison.Ordinal)
                || fileNameLower.StartsWith("minigame_", StringComparison.Ordinal)
                || fileNameLower.StartsWith("comics_", StringComparison.Ordinal);
        }
    }
}