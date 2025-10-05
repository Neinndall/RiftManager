using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using RiftManager.Services; // Para LogService

namespace RiftManager.Utils
{
    public static class Finder
    {
        /// <summary>
        /// Finds SVGs from data provided.
        /// </summary>
        /// <param name="exportDir">Directory to export file names to (will be saved under "files.txt").</param>
        /// <param name="fileData">File data.</param>
        /// <param name="logService">The LogService instance for logging.</param> // Añadido este parámetro
        /// <returns>Found files.</returns>
        public static async Task<List<string>> FindSvgs(string exportDir, string fileData, LogService logService) // logService como parámetro
        {
            // Puedes añadir una comprobación defensiva
            if (logService == null)
            {
                Console.WriteLine("[ERROR]: LogService is null in Finder.FindSvgs. Logging will be degraded.");
            }

            // Define la expresión regular para encontrar SVGs completos
            var svgRegex = new Regex(@"<svg\b[^>]*?(?:viewBox=""(\b[^""]*)"")?>([\s\S]*?)<\/svg>",
                                RegexOptions.IgnoreCase | RegexOptions.Multiline);

            // Encuentra todas las coincidencias en el contenido del archivo
            var matches = svgRegex.Matches(fileData).Cast<Match>().ToList();

            // Extrae y retorna solo los contenidos de los SVGs encontrados
            var svgContents = matches.Select(m => m.Value).ToList(); // Simplificado, ya que 'matches' es List<Match>

            // Declarar fileNames
            var fileNames = new List<string>();

            // Log de la cantidad de SVGs encontrados usando tu LogService
            logService?.Log($"Found {matches.Count} SVGs.");

            // Añadir los nombres a fileNames
            foreach (var svgContent in svgContents)
            {
                // Asegúrate de que Crypto.Md5HashEncode esté disponible y en RiftManager.Utils
                var fileName = $"{Crypto.Md5HashEncode(svgContent)}.svg";
                fileNames.Add(fileName);
            }

            // Cambia a fileNames.Count para evitar un chequeo innecesario
            if (fileNames.Count > 0)
            {
                await File.AppendAllTextAsync(Path.Combine(exportDir, "files.txt"), string.Join(Environment.NewLine, fileNames));
            }

            return svgContents;
        }
    }
}