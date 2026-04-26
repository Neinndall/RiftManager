using System.Reflection;

namespace RiftManager.Utils
{
    /// <summary>
    /// Motor de identidad técnica de RiftManager.
    /// Gestiona la versión y el tipo de build del software.
    /// </summary>
    public static class ApplicationInfos
    {
        private static readonly Assembly _assembly = Assembly.GetExecutingAssembly();
        private static readonly string _version = _assembly.GetName().Version?.ToString() ?? "1.0.0.0";

        /// <summary>
        /// Obtiene la versión semántica completa definida en el .csproj.
        /// </summary>
        public static string Version => _version;

        /// <summary>
        /// Obtiene la versión formateada con el prefijo técnico (v1.0.0.0).
        /// </summary>
        public static string FormattedVersion => $"v{_version}";

        /// <summary>
        /// Determina si la build actual es de tipo QA/Experimental basado en el sufijo.
        /// </summary>
        public static bool IsQA => Version.Contains("-");
    }
}
