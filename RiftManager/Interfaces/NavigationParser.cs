using System;
using System.Text.Json;
using RiftManager.Models;

namespace RiftManager.Interfaces
{
    public class NavigationParser
    {
        public const string EmbedUrlIdentifier = "https://embed.rgpub.io/"; // Identificador de la URL principal

        /// <summary>
        /// Intenta extraer la URL principal del evento desde el objeto JSON de navegación inicial.
        /// </summary>
        /// <param name="eventElement">El elemento JSON del evento de la navegación inicial.</param>
        /// <returns>La URL principal si se encuentra y contiene el identificador de embed.rgpub.io, de lo contrario null.</returns>
        public string GetMainEventUrlFromNavigationItem(JsonElement eventElement)
        {
            if (eventElement.TryGetProperty("action", out JsonElement actionElement) && actionElement.ValueKind == JsonValueKind.Object
                && actionElement.TryGetProperty("payload", out JsonElement payloadElement) && payloadElement.ValueKind == JsonValueKind.Object
                && payloadElement.TryGetProperty("url", out JsonElement urlElement) && urlElement.ValueKind == JsonValueKind.String)
            {
                string url = urlElement.GetString();
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