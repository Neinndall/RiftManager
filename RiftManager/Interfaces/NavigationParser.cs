using System;
using Newtonsoft.Json.Linq;
using RiftManager.Models;

namespace RiftManager.Interfaces
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
            JToken urlToken = eventToken.SelectToken("action.payload.url");

            if (urlToken != null && urlToken.Type == JTokenType.String)
            {
                string url = urlToken.ToString();
                if (url != null && url.Contains(EmbedUrlIdentifier, StringComparison.OrdinalIgnoreCase))
                {
                    return url;
                }
            }
            
            return null;
        }

        // Puedes añadir más métodos aquí si en el futuro necesitas extraer más datos específicos de la navegación.
    }
}