using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Web.WebView2.Core;
using QuickLook.ControlPanel.Services;

using System.Runtime.InteropServices;
using WinRT;

namespace QuickLook.ControlPanel.Views
{
    public sealed partial class PluginDetailPage : Page
    {
        // Win32 常量和方法
        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")]
        private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x80000;
        private const uint LWA_ALPHA = 0x2;
        private const uint GW_CHILD = 5;

        private StorePluginInfo _plugin = new();

        public PluginDetailPage()
        {
            this.InitializeComponent();
            this.Loaded += PluginDetailPage_Loaded;
        }

        private void PluginDetailPage_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeWebView();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is StorePluginInfo plugin)
            {
                _plugin = plugin;
                UrlDisplay.Text = string.IsNullOrEmpty(_plugin.RepositoryUrl) ? "解析失败" : _plugin.RepositoryUrl;
            }
        }

        private CoreWebView2Controller? _controller;
        private CoreWebView2? _coreWebView2;

        private async void InitializeWebView()
        {
            if (string.IsNullOrEmpty(_plugin.RepositoryUrl))
            {
                LoadingStatus.Text = "该插件未提供详情地址。";
                LoadingRing.IsActive = false;
                return;
            }

            try
            {
                LoadingStatus.Text = "正在以 HWND 模式初始化内核...";

                // 1. 创建环境 (强制设置透明环境变量)
                Environment.SetEnvironmentVariable("WEBVIEW2_DEFAULT_BACKGROUND_COLOR", "0");
                var env = await CoreWebView2Environment.CreateAsync();

                // 2. 获取主窗口句柄
                var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.Window);
                var windowRef = CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)windowHandle);

                // 3. 创建标准的 HWND 控制器 (而不是 Composition)
                // 这种方式最稳定，且支持内核透明
                _controller = await env.CreateCoreWebView2ControllerAsync(windowRef);
                
                // 4. 强制设置内核背景为完全透明，并显式设置可见
                _controller.DefaultBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
                _controller.IsVisible = true;
                
                _coreWebView2 = _controller.CoreWebView2;

                // 5. 实时同步位置逻辑
                void UpdateBounds()
                {
                    if (_controller != null && WebViewHost != null)
                    {
                        try 
                        {
                            // 获取当前的 DPI 缩放比例
                            var scale = WebViewHost.XamlRoot?.RasterizationScale ?? 1.0;

                            // 在 WinUI 3 中，相对于 Window.Content 转换通常比 null (屏幕) 更稳健
                            var ttv = WebViewHost.TransformToVisual(App.Window.Content);
                            var point = ttv.TransformPoint(new Windows.Foundation.Point(0, 0));
                            
                            // 物理像素 = 逻辑像素 * 缩放比例
                            var left = point.X * scale;
                            var top = point.Y * scale;
                            var width = Math.Max(1, WebViewHost.ActualWidth * scale);
                            var height = Math.Max(1, WebViewHost.ActualHeight * scale);

                            _controller.Bounds = new Windows.Foundation.Rect(left, top, width, height);
                        }
                        catch { }
                    }
                }
                
                // 首次强制更新
                UpdateBounds();

                // 监听尺寸变化和布局更新，双重保障
                WebViewHost.SizeChanged += (s, e) => UpdateBounds();
                WebViewHost.LayoutUpdated += (s, e) => UpdateBounds();

                // 6. 加载插件的 GitHub 仓库页面
                _coreWebView2.Navigate(_plugin.RepositoryUrl);

                _coreWebView2.NavigationCompleted += async (s, e) =>
                {
                    LoadingArea.Visibility = Visibility.Collapsed;
                    LoadingRing.IsActive = false;
                    
                    // 再次强制刷新透明度
                    try { _controller.DefaultBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0); } catch { }

                    // 注入 GitHub 透明 CSS
                    string css = @"
                        :root, body, .application-main, .bg-canvas, .Box, .Header, .footer {
                            background-color: transparent !important;
                            background: transparent !important;
                        }
                        [data-color-mode] {
                            --color-canvas-default: transparent !important;
                            --color-canvas-subtle: transparent !important;
                            --color-canvas-inset: transparent !important;
                        }
                        /* 隐藏不必要的背景层 */
                        .Layout-main, .Layout-sidebar {
                            background-color: transparent !important;
                        }
                    ";
                    string script = $"(function() {{ " +
                                    $"var style = document.createElement('style'); " +
                                    $"style.innerHTML = `{css}`; " +
                                    $"document.head.appendChild(style); " +
                                    $"}})();";
                    await _coreWebView2.ExecuteScriptAsync(script);
                };
            }
            catch (Exception ex)
            {
                LoadingStatus.Text = $"HWND 初始化失败: {ex.Message}";
                LoadingRing.IsActive = false;
                Debug.WriteLine($"WebView2 Error: {ex}");
            }
        }

        private void OpenInBrowser_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_plugin.RepositoryUrl))
            {
                Process.Start(new ProcessStartInfo(_plugin.RepositoryUrl) { UseShellExecute = true });
            }
        }

        private void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            // 后续实现下载逻辑
        }
    }
}
