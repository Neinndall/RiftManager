// RiftManager.Utils/Crypto.cs
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions; // Añadido
using System.Threading.Tasks; // No usado directamente en el código, pero si se planea usar Async en el futuro, se mantendría.
using RiftManager.Services; // Para LogService
using System.Linq; // Necesario para .Skip si no lo estaba ya.
using System.Collections.Generic; // Necesario para List<string>

namespace RiftManager.Utils
{
    public static class Crypto
    {
        /// <summary>
        /// Encodes the text given into an MD5 string using the built-in .NET implementation.
        /// </summary>
        /// <param name="data">The text to encode.</param>
        /// <returns>The MD5 hashed string.</returns>
        public static string Md5HashEncode(string data)
        {
            // Este método es una función pura y no debe tener logs internos, solo retornar el hash o lanzar excepción.
            using (var md5 = MD5.Create())
            {
                byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(data.Trim());
                byte[] hashBytes = md5.ComputeHash(inputBytes);
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            }
        }

        /// <summary>
        /// Adds a truncated (to 7 characters) MD5 hash (using the contents of the file) to the filename.
        /// </summary>
        /// <param name="filePath">The path of the file.</param>
        /// <param name="logService">The LogService instance for logging.</param>
        public static void AddMd5HashToFile(string filePath, LogService logService)
        {
            // No es necesario el if (logService == null) aquí.
            // El operador ?. se encargará de no invocar el método si logService es null.
            // En un entorno DI bien configurado, logService NUNCA debería ser null.

            try
            {
                // File.ReadAllText es sincrónico, por lo que no necesitamos 'await' aquí.
                // Podríamos considerar File.ReadAllBytes para consistencia si el archivo no es texto puro.
                // Sin embargo, para JSON/HTML/etc, ReadAllText es adecuado.
                string contents = File.ReadAllText(filePath, Encoding.UTF8);

                using (MD5 md5 = MD5.Create())
                {
                    byte[] inputBytes = Encoding.UTF8.GetBytes(contents.Trim());
                    byte[] hashBytes = md5.ComputeHash(inputBytes);

                    StringBuilder sb = new StringBuilder();
                    foreach (byte b in hashBytes)
                    {
                        sb.Append(b.ToString("x2")); // Convertir a hexadecimal
                    }
                    string hash = sb.ToString();
                    string truncatedHash = hash.Substring(0, 7);

                    string existingFileName = Path.GetFileName(filePath);
                    string[] fileNameArr = existingFileName.Split('.');

                    if (fileNameArr.Length > 1)
                    {
                        // Asegura que no se duplique el hash si ya existe
                        // Por ejemplo, "file.hash.ext" no se convierte en "file.hash.hash.ext"
                        if (fileNameArr.Length >= 2 && Regex.IsMatch(fileNameArr[fileNameArr.Length - 2], "^[a-f0-9]{7}$"))
                        {
                            // Si el penúltimo componente es un hash de 7 caracteres, lo reemplazamos
                            fileNameArr[fileNameArr.Length - 2] = truncatedHash;
                        }
                        else
                        {
                            // Inserta el hash antes de la última extensión
                            var newParts = new List<string>(fileNameArr);
                            newParts.Insert(fileNameArr.Length - 1, truncatedHash);
                            fileNameArr = newParts.ToArray();
                        }
                    }
                    else
                    {
                        // Si no hay extensión, simplemente añade el hash al final
                        fileNameArr = new[] { fileNameArr[0], truncatedHash };
                    }

                    string newFileName = string.Join(".", fileNameArr);
                    string? existingDirectoryPath = Path.GetDirectoryName(filePath); // Usar string? para nullability

                    if (!string.IsNullOrEmpty(existingDirectoryPath)) // Comprobación más robusta
                    {
                        string newPath = Path.Combine(existingDirectoryPath, newFileName);
                        File.Move(filePath, newPath); // Mover el archivo a la nueva ubicación
                        logService?.LogSuccess($"Crypto: Renamed '{existingFileName}' to '{newFileName}'"); // Usar LogService
                    }
                    else
                    {
                        // Si filePath es solo un nombre de archivo sin ruta o la ruta es el directorio actual
                        File.Move(filePath, newFileName);
                        logService?.LogSuccess($"Crypto: Renamed '{existingFileName}' to '{newFileName}' in current directory."); // Usar LogService
                    }
                }
            }
            catch (Exception ex)
            {
                logService?.LogError($"Crypto: Error while renaming file '{filePath}': {ex.Message}"); // Usar LogService
            }
        }
    }
}