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
using RiftManager.Dialogs;
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
            LogRichTextBox.Document = new FlowDocument();

            // Initialize fields from injected services
            _logService = logService;
            _eventService = eventService;
            _eventProcessor = eventProcessor;
            _riotClientManifestService = riotClientManifestService;

            // Subscribe to log messages for the UI
            _logService.OnLogMessage += LogMessageReceived;
            _logService.OnLogInteractiveSuccess += LogInteractiveSuccessReceived;
            _riotClientManifestService.StateChanged += OnManifestButtonStateChanged;

            Loaded += MainWindow_Loaded;
        }

        private void OnManifestButtonStateChanged(bool isEnabled, string tooltip)
        {
            Dispatcher.InvokeAsync(() =>
            {
                RiotManifestsButton.IsEnabled = isEnabled;
                RiotManifestsButton.ToolTip = tooltip;
            });
        }

        private void LogInteractiveSuccessReceived(string preLinkText, string linkText, string path)
        {
            Dispatcher.InvokeAsync(() =>
            {
                var paragraph = new Paragraph { Margin = new Thickness(0) };
                var run = new Run($"[SUCCESS] {preLinkText}");
                run.Foreground = Brushes.LightGreen;
                paragraph.Inlines.Add(run);

                var hyperlink = new Hyperlink(new Run(linkText));
                hyperlink.NavigateUri = new Uri(path);
                hyperlink.RequestNavigate += (s, e) =>
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                        e.Handled = true;
                    }
                    catch (Exception ex)
                    {
                        _logService.LogError($"Failed to open directory: {ex.Message}");
                    }
                };
                hyperlink.Foreground = Brushes.LightBlue;

                paragraph.Inlines.Add(hyperlink);

                LogRichTextBox.Document.Blocks.Add(paragraph);
                LogRichTextBox.ScrollToEnd();
            });
        }

        private void LogMessageReceived(string level, string message)
        {
            // Filter out DEBUG messages from being displayed in the UI
            if (level.ToUpper() == "DEBUG")
            {
                return;
            }

            Dispatcher.InvokeAsync(() =>
            {
                var paragraph = new Paragraph { Margin = new Thickness(0) };
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
            await _riotClientManifestService.UpdateManifestButtonStateAsync();
            await LoadEvents();
        }

        private async Task LoadEvents()
        {
            _logService.Log("Loading events...");
            _logService.LogDebug("LoadEvents: Starting event loading from navigation URL.");
            string navigationUrl = "https://content.publishing.riotgames.com/publishing-content/v1.0/public/client-navigation/league_client_navigation";
            _allEventData = await _eventService.TrackEvents(navigationUrl);

            if (_allEventData.Count == 0)
            {
                _logService.LogWarning("No events found.");
                _logService.LogDebug("LoadEvents: No events found. EventsListView.ItemsSource will not be updated.");
                return;
            }

            // Filtrar eventos que contengan "patch" o "info-hub" en su NavigationItemId
            var filteredEvents = _allEventData.Values.Where(e => 
                !e.NavigationItemId.Contains("patch", StringComparison.OrdinalIgnoreCase) && 
                !e.NavigationItemId.Contains("info-hub", StringComparison.OrdinalIgnoreCase)
            ).ToList();

            EventsListView.ItemsSource = filteredEvents;
            _logService.LogSuccess($"Found {filteredEvents.Count} events.");
            _logService.LogDebug($"LoadEvents: Events loaded and assigned to EventsListView. Found {filteredEvents.Count} events after filtering.");
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            // Use the currently selected event from the ListView
            var eventDetails = _selectedEvent;

            if (eventDetails != null)
            {
                try
                {
                    _logService.LogDebug($"DownloadButton_Click: Starting download for event: {eventDetails.Title}");
                    string assetsFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");
                    
                    MainEventLink selectedLink = null;

                    if (eventDetails.MainEventLinks != null && eventDetails.MainEventLinks.Count > 1)
                    {
                        _logService.LogDebug($"DownloadButton_Click: Multiple main links found ({eventDetails.MainEventLinks.Count}). Opening selection dialog.");
                        var dialog = new LinkSelectionDialog(eventDetails.MainEventLinks, eventDetails.Title);
                        if (dialog.ShowDialog() == true)
                        {
                            selectedLink = dialog.SelectedLink;
                            _logService.LogDebug($"DownloadButton_Click: Link selected by user: {selectedLink?.Url ?? "N/A"}");
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
                        _logService.LogDebug($"DownloadButton_Click: Single main link found. Automatically selected: {selectedLink?.Url ?? "N/A"}");
                    }
                    else
                    {
                        _logService.LogDebug($"DownloadButton_Click: No main links found for selection. selectedLink remains null.");
                    }

                    if (selectedLink != null || (eventDetails.MainEventLinks == null || !eventDetails.MainEventLinks.Any()))
                    {
                        _logService.LogDebug($"DownloadButton_Click: Processing event with selectedLink: {selectedLink?.Url ?? "N/A"}");
                        await _eventProcessor.ProcessEventAsync(eventDetails, assetsFolderPath, selectedLink);

                        string baseDirForRelativePath = AppDomain.CurrentDomain.BaseDirectory;
                        _logService.LogDebug($"DownloadButton_Click: Removing empty directories in {assetsFolderPath}");
                        await FileSystemHelper.RemoveEmptyDirectories(assetsFolderPath, _logService, baseDirForRelativePath);

                        string eventAssetsFolderPath = Path.Combine(assetsFolderPath, eventDetails.NavigationItemId);
                        _logService.LogInteractiveSuccess($"Download finished for event: ", eventDetails.Title, eventAssetsFolderPath);
                        _logService.LogDebug($"DownloadButton_Click: Download completed for event: {eventDetails.Title}");
                    }
                    else
                    {
                        _logService.LogWarning($"No valid main event link found for {eventDetails.Title}. Skipping download.");
                    }
                }
                catch (Exception ex)
                { 
                    _logService.LogError($"DownloadButton_Click: Unexpected error during event download {eventDetails.Title}: {ex.Message}");
                    _logService.LogError($"DownloadButton_Click: StackTrace: {ex.StackTrace}");
                }
            }
            else
            {
                _logService.LogWarning("DownloadButton_Click: No event selected for download.");
            }
        }

        private async void DownloadRiotManifests_Click(object sender, RoutedEventArgs e)
        {
            await _riotClientManifestService.ProcessRiotClientManifestsWithButtonLogicAsync();
        }

        private void EventsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _logService.LogDebug("EventsListView_SelectionChanged: ListView selection event fired.");
            try
            {
                var selectedEvent = EventsListView.SelectedItem as EventDetails;
                if (selectedEvent != null)
                {
                    _logService.LogDebug($"EventsListView_SelectionChanged: Selected event: {selectedEvent.Title} (ID: {selectedEvent.NavigationItemId})");
                    _selectedEvent = selectedEvent;
                    EventDetailsPanel.Visibility = Visibility.Visible;
                    EventTitleTextBlock.Text = selectedEvent.Title;
                    EventIdTextBlock.Text = selectedEvent.NavigationItemId;

                    // Update Event Type
                    string eventType = "";
                    _logService.LogDebug($"EventsListView_SelectionChanged: Checking CatalogInformation (is null? {selectedEvent.CatalogInformation == null})");
                    if (selectedEvent.CatalogInformation != null) eventType += "Catalog (Unity Bundles) ";
                    _logService.LogDebug($"EventsListView_SelectionChanged: Checking HasMainEmbedUrl (is {selectedEvent.HasMainEmbedUrl})");
                    if (selectedEvent.HasMainEmbedUrl) eventType += "Embed (Web Content) ";
                    EventTypeTextBlock.Text = string.IsNullOrWhiteSpace(eventType) ? "N/A" : eventType.Trim();
                    _logService.LogDebug($"EventsListView_SelectionChanged: Event type determined: {EventTypeTextBlock.Text}");

                    // Update Main Event Links
                    _logService.LogDebug($"EventsListView_SelectionChanged: Checking MainEventLinks (is null? {selectedEvent.MainEventLinks == null}, Count: {selectedEvent.MainEventLinks?.Count ?? 0})");
                    if (selectedEvent.MainEventLinks != null && selectedEvent.MainEventLinks.Any() && selectedEvent.MainEventLinks.Count > 1)
                    {
                        MainLinksHeader.Visibility = Visibility.Visible;
                        MainEventLinksList.Visibility = Visibility.Visible;
                        MainEventLinksList.ItemsSource = selectedEvent.MainEventLinks;
                        _logService.LogDebug($"EventsListView_SelectionChanged: Showing {selectedEvent.MainEventLinks.Count} main links.");
                    }
                    else
                    {
                        MainLinksHeader.Visibility = Visibility.Collapsed;
                        MainEventLinksList.Visibility = Visibility.Collapsed;
                        MainEventLinksList.ItemsSource = null;
                        _logService.LogDebug("EventsListView_SelectionChanged: Hiding main links (less than 2 or null).");
                    }

                    _logService.LogDebug($"EventsListView_SelectionChanged: Checking BackgroundUrl (is null or empty? {string.IsNullOrEmpty(selectedEvent.BackgroundUrl)})");
                    if (!string.IsNullOrEmpty(selectedEvent.BackgroundUrl))
                    {
                        try
                        {
                            string highResUrl = GetHighResolutionUrl(selectedEvent.BackgroundUrl);
                            _logService.LogDebug($"EventsListView_SelectionChanged: Attempting to load background image from: {highResUrl}");
                            EventBackgroundBrush.ImageSource = new BitmapImage(new Uri(highResUrl));
                            _logService.LogDebug("EventsListView_SelectionChanged: Background image loaded successfully.");
                        }
                        catch (Exception uriEx)
                        {
                            _logService.LogError($"EventsListView_SelectionChanged: Error loading background image for {selectedEvent.Title}: {uriEx.Message}");
                            _logService.LogError($"EventsListView_SelectionChanged: Background image StackTrace: {uriEx.StackTrace}");
                            EventBackgroundBrush.ImageSource = null; // Asegurarse de que no haya una imagen rota
                        }
                    }
                    else
                    {
                        EventBackgroundBrush.ImageSource = null; // Or a default image
                        _logService.LogDebug("EventsListView_SelectionChanged: BackgroundUrl is null or empty. No background image will be loaded.");
                    }
                }
                else
                {
                    EventDetailsPanel.Visibility = Visibility.Collapsed;
                    _logService.LogDebug("EventsListView_SelectionChanged: No event selected. Details panel hidden.");
                }
            }
            catch (Exception ex)
            {
                _logService.LogError($"EventsListView_SelectionChanged: Unexpected error in event selection: {ex.Message}");
                _logService.LogError($"EventsListView_SelectionChanged: StackTrace: {ex.StackTrace}");
            }
        }

        private string GetHighResolutionUrl(string originalUrl)
        {
            _logService.LogDebug($"GetHighResolutionUrl: Processing original URL: {originalUrl}");
            // Attempt to remove common thumbnail/low-res markers from Riot's CDN URLs
            // This is experimental and might not work for all URLs
            string highResUrl = Regex.Replace(originalUrl, "_tn.jpg", ".jpg", RegexOptions.IgnoreCase);
            // You could add more rules here, e.g., for query parameters like ?width=300
            // highResUrl = Regex.Replace(highResUrl, "\\?width=[^&]*", "", RegexOptions.IgnoreCase);
            _logService.LogDebug($"GetHighResolutionUrl: Resulting high resolution URL: {highResUrl}");
            return highResUrl;
        }

        private void ToolsButton_Click(object sender, RoutedEventArgs e)
        {
            _logService.LogDebug("ToolsButton_Click: Tools button clicked. Showing context menu.");
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

