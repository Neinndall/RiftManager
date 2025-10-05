// RiftManager.Models/EventDetails.cs

namespace RiftManager.Models
{
    public class EventDetails
    {
        public string Title { get; set; }
        public string NavigationItemId { get; set; }

        public EventDetails(string title, string navigationItemId)
        {
            Title = title;
            NavigationItemId = navigationItemId;
        }

        public override string ToString()
        {
            return Title; 
        }
        
        public string? BackgroundUrl { get; set; }
        public string? IconUrl { get; set; }

        // Estas propiedades ahora pueden ser redundantes si siempre usas MainEventLinks
        // Pero las mantenemos para compatibilidad con el resto del código que las use.
        public string? MainEventUrl { get; set; }
        public string? MetagameId { get; set; } // Propiedad para almacenar el metagameId

        // ¡NUEVA PROPIEDAD! Para almacenar todos los enlaces principales del evento
        public List<MainEventLink> MainEventLinks { get; set; } = new List<MainEventLink>();

        public List<string> AdditionalAssetUrls { get; set; } = new List<string>();
        public bool HasMainEmbedUrl { get; set; }

        public CatalogData? CatalogInformation { get; set; }
    }
    
    // Tu clase MainEventLink ya está aquí, lo cual es perfecto.
    public class MainEventLink
    {
        public string Url { get; set; }
        public string? MetagameId { get; set; }
        public string? Title { get; set; }

        public MainEventLink(string url)
        {
            Url = url;
        }
    }
}