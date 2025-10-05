namespace RiftManager.Models
{
    public class CatalogData
    {
        public string? BaseUrl { get; set; } // Esta es la URL base hasta /aa/
        public string? CatalogJsonUrl { get; set; } // Esta es la URL completa del archivo catalog.json
        public List<string> BundleUrls { get; set; } = new List<string>(); // Para almacenar las URLs de los bundles
    }
}