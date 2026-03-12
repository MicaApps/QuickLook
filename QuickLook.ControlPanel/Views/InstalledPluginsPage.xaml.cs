using System.Collections.ObjectModel;
using QuickLook.ControlPanel.Services;

namespace QuickLook.ControlPanel.Views
{
    public partial class InstalledPluginsPage : Page
    {
        public ObservableCollection<PluginInfo> Plugins { get; } = new();
        private readonly PluginService _pluginService = new();

        public InstalledPluginsPage()
        {
            this.InitializeComponent();
            _ = LoadPlugins();
        }

        private async Task LoadPlugins()
        {
            var installedPlugins = await _pluginService.GetInstalledPluginsAsync();
            foreach (var plugin in installedPlugins)
            {
                Plugins.Add(plugin);
            }
        }
    }
}
