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

namespace QuickLook.ControlPanel.Views
{
    public sealed partial class PluginDetailPage : Page
    {
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
                LoadingStatus.Text = "正在初始化 WebView2 内核...";
                
                // 照抄官方示例的事件注册
                RepoWebView.CoreWebView2Initialized += (s, e) => 
                {
                    Debug.WriteLine("WebView2 Initialized");
                    if (e.Exception != null)
                    {
                        LoadingStatus.Text = $"内核初始化失败: {e.Exception.Message}";
                        return;
                    }
                    
                    RepoWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                    RepoWebView.CoreWebView2.Settings.IsZoomControlEnabled = true;
                    LoadingStatus.Text = "内核初始化成功，正在加载页面...";
                };

                RepoWebView.NavigationStarting += (s, e) =>
                {
                    LoadingStatus.Text = $"正在导航至: {e.Uri}";
                };

                RepoWebView.NavigationCompleted += (s, e) =>
                {
                    if (e.IsSuccess)
                    {
                        LoadingArea.Visibility = Visibility.Collapsed;
                        LoadingRing.IsActive = false;
                    }
                    else
                    {
                        LoadingStatus.Text = $"导航失败: {e.WebErrorStatus}";
                        LoadingRing.IsActive = false;
                    }
                };

                // 开始异步初始化
                await RepoWebView.EnsureCoreWebView2Async();

                // 导航至插件仓库地址
                RepoWebView.Source = new Uri(_plugin.RepositoryUrl);
            }
            catch (Exception ex)
            {
                LoadingStatus.Text = $"初始化崩溃: {ex.Message}";
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
