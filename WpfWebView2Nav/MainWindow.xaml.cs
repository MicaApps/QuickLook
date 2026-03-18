using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using ModernWpf.Controls;
using HtmlAgilityPack;
using ModernWpf.Media.Animation;
using System.Windows.Media.Animation;

namespace WpfWebView2Nav;

public class ScrollViewerBehavior
{
    public static readonly DependencyProperty VerticalOffsetProperty =
        DependencyProperty.RegisterAttached("VerticalOffset", typeof(double), typeof(ScrollViewerBehavior),
            new PropertyMetadata(0.0, OnVerticalOffsetChanged));

    public static void SetVerticalOffset(FrameworkElement target, double value) => target.SetValue(VerticalOffsetProperty, value);
    public static double GetVerticalOffset(FrameworkElement target) => (double)target.GetValue(VerticalOffsetProperty);

    private static void OnVerticalOffsetChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is ScrollViewer scrollViewer)
        {
            scrollViewer.ScrollToVerticalOffset((double)e.NewValue);
        }
    }
}

public class PluginItem
{
    public string Name { get; set; } = string.Empty;
    public string LastUpdate { get; set; } = string.Empty;
    public string RepoUrl { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public ObservableCollection<PluginItem> Plugins { get; set; } = new();
    private readonly HttpClient _httpClient = new HttpClient();

    public MainWindow()
    {
        InitializeComponent();

        // 强制开启深色模式
        ModernWpf.ThemeManager.SetRequestedTheme(this, ModernWpf.ElementTheme.Dark);
        
        // 尝试开启 Windows 11 Mica 效果
        this.SourceInitialized += (s, e) => EnableMica();

        PluginListView.ItemsSource = Plugins;
        PluginListView.PreviewMouseWheel += PluginListView_PreviewMouseWheel;
        InitializeAsync();
        LoadPluginsAsync();
    }

    #region Mica 效果实现
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private void EnableMica()
    {
        IntPtr hWnd = new WindowInteropHelper(this).Handle;
        
        // 开启 Mica
        int micaValue = 2; // 2 = Mica, 3 = Acrylic, 4 = MicaAlt
        DwmSetWindowAttribute(hWnd, 38, ref micaValue, sizeof(int)); // DWMWA_SYSTEMBACKDROP_TYPE
        
        // 开启深色模式标题栏
        int darkMode = 1;
        DwmSetWindowAttribute(hWnd, 20, ref darkMode, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
        
        // 确保背景透明以让 Mica 透过来
        this.Background = Brushes.Transparent;
    }
    #endregion

    #region 平滑滚动实现
    private double _targetVerticalOffset = 0;
    private ScrollViewer? _listScrollViewer;

    private void PluginListView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_listScrollViewer == null)
        {
            _listScrollViewer = FindVisualChild<ScrollViewer>(PluginListView);
            if (_listScrollViewer != null)
            {
                _targetVerticalOffset = _listScrollViewer.VerticalOffset;
                // 同步当前位置到附加属性，以便动画从当前位置开始
                ScrollViewerBehavior.SetVerticalOffset(_listScrollViewer, _listScrollViewer.VerticalOffset);
            }
        }

        if (_listScrollViewer != null)
        {
            // 如果动画正在运行，我们需要从当前目标继续，或者同步当前偏移
            // 简单起见，如果偏离太远（比如用户手动拉了滚动条），则同步一下
            if (Math.Abs(_targetVerticalOffset - _listScrollViewer.VerticalOffset) > 100)
            {
                _targetVerticalOffset = _listScrollViewer.VerticalOffset;
            }

            _targetVerticalOffset -= e.Delta;
            
            _targetVerticalOffset = Math.Max(0, Math.Min(_targetVerticalOffset, _listScrollViewer.ScrollableHeight));

            DoubleAnimation animation = new DoubleAnimation
            {
                From = _listScrollViewer.VerticalOffset,
                To = _targetVerticalOffset,
                Duration = TimeSpan.FromMilliseconds(500),
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };

            _listScrollViewer.BeginAnimation(ScrollViewerBehavior.VerticalOffsetProperty, animation);
            e.Handled = true;
        }
    }

    private T? FindVisualChild<T>(DependencyObject obj) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(obj, i);
            if (child is T t) return t;
            T? childOfChild = FindVisualChild<T>(child);
            if (childOfChild != null) return childOfChild;
        }
        return null;
    }
    #endregion

    private async void LoadPluginsAsync()
    {
        try
        {
            const string wikiUrl = "https://github.com/QL-Win/QuickLook/wiki/Available-Plugins";
            var html = await _httpClient.GetStringAsync(wikiUrl);
            
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var table = doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'markdown-body')]//table");
            if (table != null)
            {
                var rows = table.SelectNodes(".//tr")?.Skip(1);
                if (rows != null)
                {
                    Plugins.Clear();
                    foreach (var row in rows)
                    {
                        var cells = row.SelectNodes(".//td");
                        if (cells != null && cells.Count >= 2)
                        {
                            var nameNode = cells[0].SelectSingleNode(".//a") ?? cells[0];
                            var name = nameNode.InnerText.Trim();
                            var link = nameNode.Attributes["href"]?.Value;

                            if (!string.IsNullOrEmpty(link) && !link.StartsWith("http"))
                            {
                                link = "https://github.com" + link;
                            }

                            var lastUpdate = cells.Count > 1 ? cells[1].InnerText.Trim() : "Unknown";

                            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(link))
                            {
                                Plugins.Add(new PluginItem 
                                {
                                    Name = name, 
                                    RepoUrl = link,
                                    LastUpdate = lastUpdate 
                                });
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"加载插件列表失败: {ex.Message}");
        }
    }

    async void InitializeAsync()
    {
        try
        {
            await webView.EnsureCoreWebView2Async(null);
            webView.CoreWebView2.Profile.PreferredColorScheme = Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Dark;
            webView.Source = new Uri("https://cn.bing.com");
            
            await detailWebView.EnsureCoreWebView2Async(null);
            detailWebView.CoreWebView2.Profile.PreferredColorScheme = Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Dark;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"WebView2 初始化失败: {ex.Message}");
        }
    }



    private void RootNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer != null)
        {
            var tag = args.SelectedItemContainer.Tag.ToString();
            
            if (tag == "bing")
            {
                webView.Visibility = Visibility.Visible;
                PluginStoreRoot.Visibility = Visibility.Collapsed;
            }
            else if (tag == "plugins")
            {
                webView.Visibility = Visibility.Collapsed;
                PluginStoreRoot.Visibility = Visibility.Visible;
            }
            else if (tag == "details")
            {
                webView.Visibility = Visibility.Collapsed;
                PluginStoreRoot.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void PluginListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PluginListView.SelectedItem is PluginItem selectedPlugin)
        {
            DetailPlaceholder.Visibility = Visibility.Collapsed;
            detailWebView.Visibility = Visibility.Visible;
            detailWebView.Source = new Uri(selectedPlugin.RepoUrl);
        }
    }

    private void ShowReadmeButton_Click(object sender, RoutedEventArgs e)
    {
        ShowReadmeButton.IsChecked = true;
        ShowFullPageButton.IsChecked = false;
        ApplyReadmeView();
    }

    private void ShowFullPageButton_Click(object sender, RoutedEventArgs e)
    {
        ShowReadmeButton.IsChecked = false;
        ShowFullPageButton.IsChecked = true;
        ApplyFullPageView();
    }

    private void OpenInBrowserButton_Click(object sender, RoutedEventArgs e)
    {
        if (detailWebView.Source != null)
        {
            Process.Start(new ProcessStartInfo(detailWebView.Source.ToString()) { UseShellExecute = true });
        }
    }

    private void detailWebView_NavigationCompleted(object sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
    {
        if (ShowReadmeButton.IsChecked == true)
        {
            ApplyReadmeView();
        }
    }

    private async void ApplyReadmeView()
    {
        if (detailWebView.CoreWebView2 == null) return;

        // 更加激进的 CSS：先隐藏所有内容，然后只显示 README 路径上的元素
        string css = @"
            /* 1. 默认隐藏所有直接子元素 */
            body > *:not(style):not(script) {
                display: none !important;
            }

            /* 2. 强制背景透明 */
            html, body {
                background-color: transparent !important;
                background: transparent !important;
                overflow-x: hidden !important;
            }

            /* 3. 这里的逻辑由 JS 动态处理，CSS 负责兜底隐藏一些顽固元素 */
            .AppHeader, .Box-header, .file-navigation, .Layout-sidebar, #repository-container-header {
                display: none !important;
            }

            /* 4. README 样式美化 */
            .markdown-body {
                background-color: transparent !important;
                color: #e6edf3 !important;
                padding: 40px !important;
                font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Helvetica, Arial, sans-serif !important;
            }

            /* 移除所有边距和边框 */
            * {
                border-color: transparent !important;
                box-shadow: none !important;
            }
        ";

        string js = $@"
            (function() {{
                var style = document.getElementById('readme-custom-style');
                if (!style) {{
                    style = document.createElement('style');
                    style.id = 'readme-custom-style';
                    document.head.appendChild(style);
                }}
                style.innerHTML = `{css}`;

                function cleanup() {{
                    var readme = document.getElementById('readme') || document.querySelector('.markdown-body');
                    if (readme) {{
                        // 隐藏 README 之前的所有兄弟节点（即文件列表等）
                        var prev = readme.previousElementSibling;
                        while (prev) {{
                            prev.style.setProperty('display', 'none', 'important');
                            prev = prev.previousElementSibling;
                        }}

                        // 隐藏 README 之后的所有兄弟节点
                        var next = readme.nextElementSibling;
                        while (next) {{
                            next.style.setProperty('display', 'none', 'important');
                            next = next.nextElementSibling;
                        }}

                        // 递归向上，隐藏父节点的所有其他兄弟
                        var node = readme;
                        while (node && node.parentElement && node.parentElement !== document.body) {{
                            var parent = node.parentElement;
                            var siblings = parent.children;
                            for (var i = 0; i < siblings.length; i++) {{
                                if (siblings[i] !== node && siblings[i].tagName !== 'STYLE') {{
                                    siblings[i].style.setProperty('display', 'none', 'important');
                                }}
                            }}
                            // 移除父容器的背景和边框
                            parent.style.setProperty('background', 'transparent', 'important');
                            parent.style.setProperty('background-color', 'transparent', 'important');
                            parent.style.setProperty('border', 'none', 'important');
                            parent.style.setProperty('box-shadow', 'none', 'important');
                            parent.style.setProperty('padding', '0', 'important');
                            parent.style.setProperty('margin', '0', 'important');
                            parent.style.setProperty('width', '100%', 'important');
                            parent.style.setProperty('max-width', '100%', 'important');
                            
                            node = parent;
                        }}
                        
                        // 确保最外层容器也是显示的
                        if (node) node.style.setProperty('display', 'block', 'important');
                    }}
                    document.body.style.setProperty('background', 'transparent', 'important');
                }}

                // 立即执行一次
                cleanup();
                
                // 定时执行几次，防止 GitHub 动态加载回流
                var count = 0;
                var timer = setInterval(function() {{
                    cleanup();
                    if (++count > 10) clearInterval(timer);
                }}, 500);
            }})();
        ";

        await detailWebView.CoreWebView2.ExecuteScriptAsync(js);
    }

    private async void ApplyFullPageView()
    {
        if (detailWebView.CoreWebView2 == null) return;

        string js = @"
            var style = document.getElementById('readme-custom-style');
            if (style) {
                style.remove();
            }
        ";
        await detailWebView.CoreWebView2.ExecuteScriptAsync(js);
    }
}
