// RiftManager.Views.Models/EventDetails.cs

namespace RiftManager.Views.Models
{
    /// <summary>
    /// Event kinds handled by the app. Why: v1.1.x guessed the pipeline from
    /// link titles ("comic"/"play"); explicit types let EventProcessor route
    /// directly: Normal -&gt; cmsassets only, EmbedWeb -&gt; Nuxt JS/CSS scraper,
    /// UnityCatalog -&gt; catalog.bin -&gt; bundles -&gt; extract -&gt; audio.
    /// </summary>
    public enum EventType
    {
        Unknown,
        /// <summary>lc_home_tab without embed: only Background/Icon/AdditionalAssets.</summary>
        Normal,
        /// <summary>Nuxt SPA on embed.rgpub.io + assetcdn (HoL 2026): no catalog.bin.</summary>
        EmbedWeb,
        /// <summary>Unity WebGL (comics/play/minigame pipelines): StreamingAssets/aa/catalog.bin.</summary>
        UnityCatalog
    }

    public class EventDetails
    {
        public string Title { get; set; }
        public string NavigationItemId { get; set; }

        public string DisplayType
        {
            get
            {
                // v1.2.0: explicit type wins (shown in the technical panel);
                // legacy flags kept as fallback for Unknown.
                if (Type != EventType.Unknown) return Type.ToString();
                string type = "";
                if (CatalogInformation != null) type += "Catalog ";
                if (HasMainEmbedUrl) type += "Embed ";
                return string.IsNullOrWhiteSpace(type) ? "N/A" : type.Trim();
            }
        }

        /// <summary>
        /// Explicit pipeline routing. Coordinator sets Normal/EmbedWeb from links;
        /// EventProcessor upgrades to UnityCatalog once catalog.bin is confirmed.
        /// </summary>
        public EventType Type { get; set; } = EventType.Unknown;

        /// <summary>
        /// Recomputes <see cref="Type"/> from current links/catalog state.
        /// </summary>
        public void RefreshType()
        {
            if (CatalogInformation != null)
                Type = EventType.UnityCatalog;
            else if (HasMainEmbedUrl || (MainEventLinks?.Any() == true))
                Type = EventType.EmbedWeb;
            else
                Type = EventType.Normal;
        }

        public EventDetails(string title, string navigationItemId)
        {
            Title = title;
            NavigationItemId = navigationItemId;
        }

        public override string ToString()
        {
            return Title; 
        }
        
        public string BackgroundUrl { get; set; }
        public string IconUrl { get; set; }

        // Estas propiedades ahora pueden ser redundantes si siempre usas MainEventLinks
        // Pero las mantenemos para compatibilidad con el resto del código que las use.
        public string MainEventUrl { get; set; }
        public string MetagameId { get; set; } // Propiedad para almacenar el metagameId

        // ¡NUEVA PROPIEDAD! Para almacenar todos los enlaces principales del evento
        public List<MainEventLink> MainEventLinks { get; set; } = new List<MainEventLink>();

        public List<string> AdditionalAssetUrls { get; set; } = new List<string>();
        public bool HasMainEmbedUrl { get; set; }

        public CatalogData CatalogInformation { get; set; }
    }
    
    // Tu clase MainEventLink ya está aquí, lo cual es perfecto.
    public class MainEventLink
    {
        public string Url { get; set; }
        public string MetagameId { get; set; }
        public string Title { get; set; }

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrEmpty(Title) && !string.IsNullOrEmpty(MetagameId))
                    return $"{Title} | {MetagameId}";
                
                return Title ?? MetagameId ?? "Unknown Link";
            }
        }

        public MainEventLink(string url)
        {
            Url = url;
        }
    }
}