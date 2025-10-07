using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using RiftManager.Models;

namespace RiftManager.Dialogs
{
    public partial class LinkSelectionDialog : Window
    {
        public MainEventLink SelectedLink { get; private set; }

        public LinkSelectionDialog(List<MainEventLink> links, string eventTitle)
        {
            InitializeComponent();
            Title = $"Select a link for {eventTitle}";
            LinksItemsControl.ItemsSource = links;
        }

        private void LinkButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button clickedButton && clickedButton.DataContext is MainEventLink selectedLink)
            {
                SelectedLink = selectedLink;
                DialogResult = true;
            }
        }
    }
}