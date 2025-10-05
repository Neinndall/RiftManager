using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RiftManager.Services
{
    public class JsonFetcherService
    {
        private readonly HttpClient _httpClient;
        private readonly LogService _logService;

        public JsonFetcherService(HttpClient httpClient, LogService logService)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        }

        /// <summary>
        /// Realiza una petición HTTP y parsea la respuesta JSON.
        /// </summary>
        /// <param name="url">La URL a la que se realizará la petición.</param>
        /// <param name="suppressConsoleOutput">
        /// Si es true, suprime la salida de consola (LogDebug) para las fases de obtención y éxito.
        /// Los errores (LogError) siempre se mostrarán, independientemente de este parámetro.
        /// </param>
        /// <returns>Un JToken si la petición es exitosa y el JSON es válido, de lo contrario null.</returns>
        public async Task<JToken> GetJTokenAsync(string url, bool suppressConsoleOutput = false)
        {
            try
            {
                // Solo loguea el inicio de la obtención si suppressConsoleOutput es false
                if (!suppressConsoleOutput)
                {
                    _logService.LogDebug($"JsonFetcherService: Obteniendo datos desde: {url}");
                }

                HttpResponseMessage response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode(); // Lanza excepción si el código de estado HTTP no es de éxito
                string jsonResponse = await response.Content.ReadAsStringAsync();

                // Solo loguea el éxito de la obtención si suppressConsoleOutput es false
                if (!suppressConsoleOutput)
                {
                    _logService.LogDebug("JsonFetcherService: Datos obtenidos exitosamente.");
                }
                return JToken.Parse(jsonResponse);
            }
            catch (HttpRequestException e)
            {
                // Los errores son CRÍTICOS y siempre deben ser logueados,
                // independientemente del parámetro suppressConsoleOutput.
                _logService.LogError($"JsonFetcherService: Error HTTP al obtener datos desde {url}: {e.Message}");
                return null;
            }
            catch (JsonException e)
            {
                // Los errores son CRÍTICOS y siempre deben ser logueados.
                _logService.LogError($"JsonFetcherService: Error al parsear JSON desde {url}: {e.Message}");
                return null;
            }
            catch (Exception e)
            {
                // Los errores son CRÍTICOS y siempre deben ser logueados.
                _logService.LogError($"JsonFetcherService: Un error inesperado ocurrió al obtener datos desde {url}: {e.Message}");
                return null;
            }
        }
    }
}