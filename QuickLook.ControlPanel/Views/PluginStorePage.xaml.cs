using System.Collections.ObjectModel;
using System.Linq;
using QuickLook.ControlPanel.Services;

namespace QuickLook.ControlPanel.Views
{
    public partial class PluginStorePage : Page
    {
        public ObservableCollection<StorePluginInfo> StorePlugins { get; } = new();
        private readonly PluginService _pluginService = new();

        public PluginStorePage()
        {
            this.InitializeComponent();
            LoadPlugins();
        }

        private async void LoadPlugins()
        {
            LoadingRing.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            ErrorArea.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            PluginsGridView.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;

            var plugins = await _pluginService.GetStorePluginsAsync();
            
            if (plugins != null && plugins.Any())
            {
                PluginsGridView.ItemsSource = plugins;
                PluginsGridView.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            }
            else
            {
                ErrorArea.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            }

            LoadingRing.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        }

        private void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            LoadPlugins();
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string url)
            {
                // In a real app, this would download the .qlplugin and trigger installation.
                // For now, we just open the browser.
                await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
            }
        }

        private void DetailButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is StorePluginInfo plugin)
            {
                this.Frame.Navigate(typeof(PluginDetailPage), plugin);
            }
        }
    }
}
