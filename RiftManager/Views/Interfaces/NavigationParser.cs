using System;
using Newtonsoft.Json.Linq;
using RiftManager.Views.Models;
using RiftManager.Utils;

namespace RiftManager.Views.Interfaces
{
    public class NavigationParser
    {
        public const string EmbedUrlIdentifier = "https://embed.rgpub.io/"; // Identificador de la URL principal

        /// <summary>
        /// Intenta extraer la URL principal del evento desde el token JSON de navegación inicial.
        /// </summary>
        /// <param name="eventToken">El token JSON del evento de la navegación inicial.</param>
        /// <returns>La URL principal si se encuentra y contiene el identificador de embed.rgpub.io, de lo contrario null.</returns>
        public string GetMainEventUrlFromNavigationItem(JToken eventToken)
        {
            JToken actionToken = eventToken["action"];
            if (actionToken == null) return null;

            string actionType = actionToken.Value<string>("type");
            if (actionType != "iframed")
            {
                return null;
            }

            JToken urlToken = actionToken.SelectToken("payload.url");

            if (urlToken != null && urlToken.Type == JTokenType.String)
            {
                string url = urlToken.ToString();

                // v1.2.0: normalize via UrlNormalizer ({locale} -> en-us, collapse "//").
                // Why: old code replaced "{locale}" with "" leaving
                // "...embed-2026//" (see application.log 10:37:12.537).
                if (url != null && url.Contains(EmbedUrlIdentifier, StringComparison.OrdinalIgnoreCase))
                {
                    return UrlNormalizer.NormalizeEmbedUrl(url);
                }
            }
            
            return null;
        }
    }
}