namespace QuickLook.ControlPanel.Views
{
    public partial class SettingsPage : Page
    {
        public SettingsPage()
        {
            this.InitializeComponent();
            InitializeThemeSelection();
        }

        private void InitializeThemeSelection()
        {
            if (App.Window?.Content is FrameworkElement rootElement)
            {
                var currentTheme = rootElement.RequestedTheme;
                foreach (ComboBoxItem item in ThemeComboBox.Items)
                {
                    if (item.Tag?.ToString() == currentTheme.ToString())
                    {
                        ThemeComboBox.SelectedItem = item;
                        break;
                    }
                }
            }
        }

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ThemeComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                var themeStr = selectedItem.Tag.ToString();
                if (Enum.TryParse<ElementTheme>(themeStr, out var theme))
                {
                    if (App.Window.Content is FrameworkElement rootElement)
                    {
                        rootElement.RequestedTheme = theme;
                    }
                }
            }
        }
    }
}
