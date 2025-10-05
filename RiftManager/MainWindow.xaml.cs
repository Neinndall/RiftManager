using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RiftManager.Services;
using RiftManager.Models;
using RiftManager.Utils;

namespace RiftManager
{
    public partial class MainWindow : Window
    {
        private readonly EventCoordinatorService _eventService;
        private readonly EventProcessor _eventProcessor;
        private readonly RiotClientManifestService _riotClientManifestService;
        private readonly LogService _logService;
        private Dictionary<string, EventDetails> _allEventData = new Dictionary<string, EventDetails>();
        private EventDetails _selectedEvent;

        public MainWindow(LogService logService, EventCoordinatorService eventService, EventProcessor eventProcessor, RiotClientManifestService riotClientManifestService)
        {
            InitializeComponent();

            // Initialize fields from injected services
            _logService = logService;
            _eventService = eventService;
            _eventProcessor = eventProcessor;
            _riotClientManifestService = riotClientManifestService;

            // Subscribe to log messages for the UI
            _logService.OnLogMessage += LogMessageReceived;

            Loaded += MainWindow_Loaded;
        }

        private void LogMessageReceived(string level, string message)
        {
            // Filter out DEBUG messages from being displayed in the UI
            if (level.ToUpper() == "DEBUG")
            {
                return;
            }

            Dispatcher.Invoke(() =>
            {
                var paragraph = new Paragraph();
                var run = new Run($"[{level}] {message}");

                switch (level.ToUpper())
                {
                    case "SUCCESS":
                        run.Foreground = Brushes.LightGreen;
                        break;
                    case "ERROR":
                        run.Foreground = Brushes.IndianRed;
                        break;
                    case "WARNING":
                        run.Foreground = Brushes.Yellow;
                        break;
                    default:
                        run.Foreground = Brushes.WhiteSmoke;
                        break;
                }

                paragraph.Inlines.Add(run);
                LogRichTextBox.Document.Blocks.Add(paragraph);
                LogRichTextBox.ScrollToEnd();
            });
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadEvents();
        }

        private async Task LoadEvents()
        {
            _logService.Log("Loading events...");
            _logService.LogDebug("LoadEvents: Iniciando carga de eventos desde la URL de navegación.");
            string navigationUrl = "https://content.publishing.riotgames.com/publishing-content/v1.0/public/client-navigation/league_client_navigation";
            _allEventData = await _eventService.TrackEvents(navigationUrl);

            if (_allEventData.Count == 0)
            {
                _logService.LogWarning("No events found.");
                _logService.LogDebug("LoadEvents: No se encontraron eventos. EventsListView.ItemsSource no se actualizará.");
                return;
            }

            // Filtrar eventos que contengan "patch" o "info-hub" en su NavigationItemId
            var filteredEvents = _allEventData.Values.Where(e => 
                !e.NavigationItemId.Contains("patch", StringComparison.OrdinalIgnoreCase) && 
                !e.NavigationItemId.Contains("info-hub", StringComparison.OrdinalIgnoreCase)
            ).ToList();

            EventsListView.ItemsSource = filteredEvents;
            _logService.LogSuccess($"Found {filteredEvents.Count} events.");
            _logService.LogDebug($"LoadEvents: Eventos cargados y asignados a EventsListView. Se encontraron {filteredEvents.Count} eventos después del filtrado.");
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            // Use the currently selected event from the ListView
            var eventDetails = _selectedEvent;

            if (eventDetails != null)
            {
                try
                {
                    _logService.LogDebug($"DownloadButton_Click: Iniciando descarga para el evento: {eventDetails.Title}");
                    string assetsFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");
                    
                    MainEventLink selectedLink = null;

                    if (eventDetails.MainEventLinks != null && eventDetails.MainEventLinks.Count > 1)
                    {
                        _logService.LogDebug($"DownloadButton_Click: Múltiples enlaces principales encontrados ({eventDetails.MainEventLinks.Count}). Abriendo diálogo de selección.");
                        var dialog = new LinkSelectionDialog(eventDetails.MainEventLinks, eventDetails.Title);
                        if (dialog.ShowDialog() == true)
                        {
                            selectedLink = dialog.SelectedLink;
                            _logService.LogDebug($"DownloadButton_Click: Enlace seleccionado por el usuario: {selectedLink?.Url ?? "N/A"}");
                        }
                        else
                        {
                            _logService.LogWarning($"Download for event {eventDetails.Title} cancelled by user (no link selected).");
                            return; // User cancelled selection
                        }
                    }
                    else if (eventDetails.MainEventLinks != null && eventDetails.MainEventLinks.Count == 1)
                    {
                        selectedLink = eventDetails.MainEventLinks.First();
                        _logService.LogDebug($"DownloadButton_Click: Un único enlace principal encontrado. Seleccionado automáticamente: {selectedLink?.Url ?? "N/A"}");
                    }
                    else
                    {
                        _logService.LogDebug($"DownloadButton_Click: No se encontraron enlaces principales para selección. selectedLink permanece nulo.");
                    }

                    if (selectedLink != null || (eventDetails.MainEventLinks == null || !eventDetails.MainEventLinks.Any()))
                    {
                        _logService.LogDebug($"DownloadButton_Click: Procesando evento con selectedLink: {selectedLink?.Url ?? "N/A"}");
                        await _eventProcessor.ProcessEventAsync(eventDetails, assetsFolderPath, selectedLink);

                        string baseDirForRelativePath = AppDomain.CurrentDomain.BaseDirectory;
                        _logService.LogDebug($"DownloadButton_Click: Eliminando directorios vacíos en {assetsFolderPath}");
                        await FileSystemHelper.RemoveEmptyDirectories(assetsFolderPath, _logService, baseDirForRelativePath);

                        _logService.LogSuccess($"Download finished for event: {eventDetails.Title}");
                        _logService.LogDebug($"DownloadButton_Click: Descarga completada para el evento: {eventDetails.Title}");
                    }
                    else
                    {
                        _logService.LogWarning($"No valid main event link found for {eventDetails.Title}. Skipping download.");
                    }
                }
                catch (Exception ex)
                { 
                    _logService.LogError($"DownloadButton_Click: Error inesperado durante la descarga del evento {eventDetails.Title}: {ex.Message}");
                    _logService.LogError($"DownloadButton_Click: StackTrace: {ex.StackTrace}");
                }
            }
            else
            {
                _logService.LogWarning("DownloadButton_Click: No hay evento seleccionado para descargar.");
            }
        }

        private async void DownloadRiotManifests_Click(object sender, RoutedEventArgs e)
        {
            _logService.Log("Downloading Riot Manifests...");
            _logService.LogDebug("DownloadRiotManifests_Click: Iniciando descarga de manifiestos de Riot.");
            string assetsFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");
            string riotClientAssetsDestinationFolder = Path.Combine(assetsFolderPath, "RiotClientAssets");
            try
            {
                await _riotClientManifestService.ProcessRiotClientManifests(riotClientAssetsDestinationFolder);
                _logService.LogSuccess("Riot Manifests download finished.");
                _logService.LogDebug("DownloadRiotManifests_Click: Descarga de manifiestos de Riot completada.");
            }
            catch (Exception ex)
            {
                _logService.LogError($"DownloadRiotManifests_Click: Error al descargar manifiestos de Riot: {ex.Message}");
                _logService.LogError($"DownloadRiotManifests_Click: StackTrace: {ex.StackTrace}");
            }
        }

        private void EventsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _logService.LogDebug("EventsListView_SelectionChanged: Evento de selección de ListView disparado.");
            try
            {
                var selectedEvent = EventsListView.SelectedItem as EventDetails;
                if (selectedEvent != null)
                {
                    _logService.LogDebug($"EventsListView_SelectionChanged: Evento seleccionado: {selectedEvent.Title} (ID: {selectedEvent.NavigationItemId})");
                    _selectedEvent = selectedEvent;
                    EventDetailsPanel.Visibility = Visibility.Visible;
                    EventTitleTextBlock.Text = selectedEvent.Title;
                    EventIdTextBlock.Text = selectedEvent.NavigationItemId;

                    // Update Event Type
                    string eventType = "";
                    _logService.LogDebug($"EventsListView_SelectionChanged: Comprobando CatalogInformation (es nulo? {selectedEvent.CatalogInformation == null})");
                    if (selectedEvent.CatalogInformation != null) eventType += "Catalog (Unity Bundles) ";
                    _logService.LogDebug($"EventsListView_SelectionChanged: Comprobando HasMainEmbedUrl (es {selectedEvent.HasMainEmbedUrl})");
                    if (selectedEvent.HasMainEmbedUrl) eventType += "Embed (Web Content) ";
                    EventTypeTextBlock.Text = string.IsNullOrWhiteSpace(eventType) ? "N/A" : eventType.Trim();
                    _logService.LogDebug($"EventsListView_SelectionChanged: Tipo de evento determinado: {EventTypeTextBlock.Text}");

                    // Update Main Event Links
                    _logService.LogDebug($"EventsListView_SelectionChanged: Comprobando MainEventLinks (es nulo? {selectedEvent.MainEventLinks == null}, Count: {selectedEvent.MainEventLinks?.Count ?? 0})");
                    if (selectedEvent.MainEventLinks != null && selectedEvent.MainEventLinks.Any() && selectedEvent.MainEventLinks.Count > 1)
                    {
                        MainLinksHeader.Visibility = Visibility.Visible;
                        MainEventLinksList.Visibility = Visibility.Visible;
                        MainEventLinksList.ItemsSource = selectedEvent.MainEventLinks;
                        _logService.LogDebug($"EventsListView_SelectionChanged: Mostrando {selectedEvent.MainEventLinks.Count} enlaces principales.");
                    }
                    else
                    {
                        MainLinksHeader.Visibility = Visibility.Collapsed;
                        MainEventLinksList.Visibility = Visibility.Collapsed;
                        MainEventLinksList.ItemsSource = null;
                        _logService.LogDebug("EventsListView_SelectionChanged: Ocultando enlaces principales (menos de 2 o nulos).");
                    }

                    _logService.LogDebug($"EventsListView_SelectionChanged: Comprobando BackgroundUrl (es nulo o vacío? {string.IsNullOrEmpty(selectedEvent.BackgroundUrl)})");
                    if (!string.IsNullOrEmpty(selectedEvent.BackgroundUrl))
                    {
                        try
                        {
                            string highResUrl = GetHighResolutionUrl(selectedEvent.BackgroundUrl);
                            _logService.LogDebug($"EventsListView_SelectionChanged: Intentando cargar imagen de fondo desde: {highResUrl}");
                            EventBackgroundBrush.ImageSource = new BitmapImage(new Uri(highResUrl));
                            _logService.LogDebug("EventsListView_SelectionChanged: Imagen de fondo cargada exitosamente.");
                        }
                        catch (Exception uriEx)
                        {
                            _logService.LogError($"EventsListView_SelectionChanged: Error al cargar la imagen de fondo para {selectedEvent.Title}: {uriEx.Message}");
                            _logService.LogError($"EventsListView_SelectionChanged: StackTrace de la imagen de fondo: {uriEx.StackTrace}");
                            EventBackgroundBrush.ImageSource = null; // Asegurarse de que no haya una imagen rota
                        }
                    }
                    else
                    {
                        EventBackgroundBrush.ImageSource = null; // Or a default image
                        _logService.LogDebug("EventsListView_SelectionChanged: BackgroundUrl es nulo o vacío. No se cargará imagen de fondo.");
                    }
                }
                else
                {
                    EventDetailsPanel.Visibility = Visibility.Collapsed;
                    _logService.LogDebug("EventsListView_SelectionChanged: No hay evento seleccionado. Panel de detalles oculto.");
                }
            }
            catch (Exception ex)
            {
                _logService.LogError($"EventsListView_SelectionChanged: Error inesperado en la selección de evento: {ex.Message}");
                _logService.LogError($"EventsListView_SelectionChanged: StackTrace: {ex.StackTrace}");
            }
        }

        private string GetHighResolutionUrl(string originalUrl)
        {
            _logService.LogDebug($"GetHighResolutionUrl: Procesando URL original: {originalUrl}");
            // Attempt to remove common thumbnail/low-res markers from Riot's CDN URLs
            // This is experimental and might not work for all URLs
            string highResUrl = Regex.Replace(originalUrl, "_tn.jpg", ".jpg", RegexOptions.IgnoreCase);
            // You could add more rules here, e.g., for query parameters like ?width=300
            // highResUrl = Regex.Replace(highResUrl, "\\?width=[^&]*", "", RegexOptions.IgnoreCase);
            _logService.LogDebug($"GetHighResolutionUrl: URL de alta resolución resultante: {highResUrl}");
            return highResUrl;
        }

        private void ToolsButton_Click(object sender, RoutedEventArgs e)
        {
            _logService.LogDebug("ToolsButton_Click: Botón Herramientas clickeado. Mostrando menú contextual.");
            ContextMenu cm = new ContextMenu();

            MenuItem downloadManifestsMenuItem = new MenuItem();
            downloadManifestsMenuItem.Header = "Download Riot Manifests";
            downloadManifestsMenuItem.Click += DownloadRiotManifests_Click;
            cm.Items.Add(downloadManifestsMenuItem);

            // Attach the ContextMenu to the button that was clicked
            Button clickedButton = sender as Button;
            if (clickedButton != null)
            {
                clickedButton.ContextMenu = cm;
                cm.PlacementTarget = clickedButton;
                cm.IsOpen = true;
            }
        }
    }
}

