using System;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Gemini
{
    public sealed partial class WelcomePage : Page
    {
        public WelcomePage()
        {
            this.InitializeComponent();
        }

        private void WelcomeFlipView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (WelcomeFlipView == null || BackNavButton == null || NextNavButton == null) return;

            int index = WelcomeFlipView.SelectedIndex;
            int count = WelcomeFlipView.Items.Count;

            BackNavButton.Visibility = index > 0 ? Visibility.Visible : Visibility.Collapsed;
            NextNavButton.Visibility = index < count - 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BackNavButton_Click(object sender, RoutedEventArgs e)
        {
            if (WelcomeFlipView.SelectedIndex > 0)
            {
                WelcomeFlipView.SelectedIndex--;
            }
        }

        private void NextNavButton_Click(object sender, RoutedEventArgs e)
        {
            if (WelcomeFlipView.SelectedIndex < WelcomeFlipView.Items.Count - 1)
            {
                WelcomeFlipView.SelectedIndex++;
            }
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            string apiKey = WelcomeApiKeyInput.Password.Trim();
            if (string.IsNullOrEmpty(apiKey))
            {
                // In a real app, show a message. Here we'll just return.
                return;
            }

            var localSettings = ApplicationData.Current.LocalSettings;
            localSettings.Values["ApiKey"] = apiKey;

            // Navigate to MainPage
            Frame.Navigate(typeof(MainPage));
        }
    }
}
