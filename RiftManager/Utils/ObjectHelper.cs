using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;
using System;

namespace RiftManager.Utils
{
    public static class ObjectHelper
    {
        /// <summary>
        /// Flattens a nested dictionary.
        /// </summary>
        /// <param name="ob">The nested dictionary to flatten.</param>
        /// <returns>A flattened dictionary.</returns>
        public static Dictionary<string, object> FlattenObject(JObject ob)
        {
            var result = new Dictionary<string, object>();

            foreach (var property in ob.Properties())
            {
                var value = property.Value;

                if (value is JObject nestedObject)
                {
                    var flatObject = FlattenObject(nestedObject);
                    foreach (var nestedKvp in flatObject)
                    {
                        result[$"{property.Name}_{nestedKvp.Key}"] = nestedKvp.Value;
                    }
                }
                else
                {
                    result[property.Name] = value is JValue jValue ? jValue.Value : value;
                }
            }

            return result;
        }
        
        public static string NormalizeAssetName(string url)
        {
            // Obtener el nombre del archivo (ej. "preview-immortalized.webm")
            string fileName = Path.GetFileName(url);

            // Regex para eliminar SOLO el hash (ej. .d4eac4) si está presente, antes de la extensión.
            // Asegurarse de no eliminar sufijos descriptivos como -immortalized o -pass.
            // La expresión busca un punto seguido de 6 a 16 caracteres hexadecimales (hash común)
            // justo antes de la extensión.
            var normalizedName = Regex.Replace(fileName ?? string.Empty, @"\.[0-9a-fA-F]{6,16}(\.[a-zA-Z]+)$", "$1");

            return normalizedName;
        }
    }
}
