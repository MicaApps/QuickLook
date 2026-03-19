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
            InstallButton.Visibility = Visibility.Visible;
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

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (PluginListView.SelectedItem is PluginItem selectedPlugin)
        {
            // 这里可以添加真正的安装逻辑，比如从 GitHub Release 下载 .qlplugin 文件并运行
            // 目前先显示一个提示
            ContentDialog dialog = new ContentDialog
            {
                Title = "准备安装",
                Content = $"即将安装插件: {selectedPlugin.Name}\n存储库: {selectedPlugin.RepoUrl}\n\n注意：目前尚未实现自动下载，您可以点击“在浏览器中打开”并从 Release 页面下载 .qlplugin 文件。",
                PrimaryButtonText = "确定",
                DefaultButton = ContentDialogButton.Primary
            };

            await dialog.ShowAsync();
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
            /* 1. 只有在 readme 模式下才应用这些样式 */
            .ql-readme-mode body > *:not(style):not(script):not(.ql-readme-container) {
                display: none !important;
            }

            /* 2. 强制背景透明 */
            html.ql-readme-mode, body.ql-readme-mode {
                background-color: transparent !important;
                background: transparent !important;
                overflow-x: hidden !important;
            }

            /* 3. 这里的逻辑由 JS 动态处理，CSS 负责兜底隐藏一些顽固元素 */
            .ql-readme-mode .AppHeader, 
            .ql-readme-mode .Box-header, 
            .ql-readme-mode .file-navigation, 
            .ql-readme-mode .Layout-sidebar, 
            .ql-readme-mode #repository-container-header {
                display: none !important;
            }

            /* 4. README 样式美化 */
            .ql-readme-mode .markdown-body {
                background-color: transparent !important;
                color: #e6edf3 !important;
                padding: 40px !important;
                font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Helvetica, Arial, sans-serif !important;
            }

            /* 修复标题下划线，使用 GitHub 默认的深色模式边框色 */
            .ql-readme-mode .markdown-body h1, 
            .ql-readme-mode .markdown-body h2 {
                border-bottom: 1px solid #30363d !important;
                padding-bottom: .3em !important;
            }

            /* 辅助类 */
            .ql-hide { display: none !important; }
            .ql-transparent-container {
                background: transparent !important;
                background-color: transparent !important;
                border: none !important;
                box-shadow: none !important;
                padding: 0 !important;
                margin: 0 !important;
                width: 100% !important;
                max-width: 100% !important;
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
                document.body.classList.add('ql-readme-mode');
                document.documentElement.classList.add('ql-readme-mode');

                function cleanup() {{
                    if (!document.body.classList.contains('ql-readme-mode')) return;
                    
                    var readme = document.getElementById('readme') || document.querySelector('.markdown-body');
                    if (readme) {{
                        // 标记路径上的容器
                        var node = readme;
                        while (node && node.parentElement && node.parentElement !== document.body) {{
                            var parent = node.parentElement;
                            parent.classList.add('ql-readme-container');
                            parent.classList.add('ql-transparent-container');
                            
                            // 隐藏同级节点
                            var siblings = parent.children;
                            for (var i = 0; i < siblings.length; i++) {{
                                if (siblings[i] !== node && siblings[i].tagName !== 'STYLE' && !siblings[i].classList.contains('ql-readme-container')) {{
                                    siblings[i].classList.add('ql-hide');
                                }}
                            }}
                            node = parent;
                        }}
                    }}
                }}

                // 立即执行一次
                cleanup();
                
                // 定时执行几次，防止 GitHub 动态加载回流
                var count = 0;
                var timer = setInterval(function() {{
                    if (!document.body.classList.contains('ql-readme-mode')) {{
                        clearInterval(timer);
                        return;
                    }}
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
            (function() {
                // 1. 移除模式标记
                document.body.classList.remove('ql-readme-mode');
                document.documentElement.classList.remove('ql-readme-mode');
                
                // 2. 移除注入的样式
                var style = document.getElementById('readme-custom-style');
                if (style) style.remove();
                
                // 3. 恢复所有被隐藏的元素
                document.querySelectorAll('.ql-hide').forEach(el => el.classList.remove('ql-hide'));
                document.querySelectorAll('.ql-transparent-container').forEach(el => el.classList.remove('ql-transparent-container'));
                document.querySelectorAll('.ql-readme-container').forEach(el => el.classList.remove('ql-readme-container'));
                
                // 4. 彻底清理可能残留的 inline styles（针对旧版本的清理）
                var elements = document.querySelectorAll('[style*=""display: none""]');
                elements.forEach(function(el) {
                    el.style.removeProperty('display');
                });
                
                // 恢复背景
                document.body.style.removeProperty('background');
                document.body.style.removeProperty('background-color');
                
                // 强制触发一次重绘
                window.dispatchEvent(new Event('resize'));
            })();
        ";
        await detailWebView.CoreWebView2.ExecuteScriptAsync(js);
    }
}
