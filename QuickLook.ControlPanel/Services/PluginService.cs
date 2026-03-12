using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace QuickLook.ControlPanel.Services
{
    public class PluginInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string AssemblyPath { get; set; } = string.Empty;
    }

    public class StorePluginInfo
    {
        public string Name { get; set; } = string.Empty;
        public string LastUpdate { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string RepositoryUrl { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public class PluginService
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        public PluginService()
        {
            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "QuickLook-ControlPanel");
            }
        }

        public async Task<List<StorePluginInfo>> GetStorePluginsAsync()
        {
            try
            {
                var wikiUrl = "https://github.com/QL-Win/QuickLook/wiki/Available-Plugins";
                using (var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(15)))
                {
                    var html = await _httpClient.GetStringAsync(wikiUrl, cts.Token);
                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);

                    var plugins = ParseWikiHtml(doc);
                    if (plugins.Any()) return plugins;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to fetch store plugins via HTML: {ex.Message}");
            }

            return GetStaticFallbackPlugins();
        }

        private List<StorePluginInfo> ParseWikiHtml(HtmlDocument doc)
        {
            var plugins = new List<StorePluginInfo>();
            var wikiBody = doc.DocumentNode.SelectSingleNode("//div[@id='wiki-body']");
            if (wikiBody == null) return plugins;

            // 仅使用方法 1: 解析三行一组的插件信息
            // 这种模式通常出现在 *1: Native installation only 之前
            var wikiText = wikiBody.InnerText;
            var stopText = "*1: Native installation only";
            int stopIndex = wikiText.IndexOf(stopText, StringComparison.OrdinalIgnoreCase);
            var scanText = stopIndex > 0 ? wikiText.Substring(0, stopIndex) : wikiText;

            // 使用正则直接匹配三行一组的模式，提高健壮性
            // 模式：[名称/文本]: [GitHub链接] \n [Release/文本]: [下载链接]
            // 注意：有些行可能包含额外的空格或符号
            var pattern = @"([^\n\r:]+):?\s*\[?(https?://github\.com/[^\s\]\)\>]+)\]?\s*[\r\n]+\s*([^\n\r:]+release[^\n\r:]*):?\s*\[?(https?://github\.com/[^\s\]\)\>]+)\]?";
            var matches = Regex.Matches(scanText, pattern, RegexOptions.IgnoreCase);

            foreach (Match match in matches)
            {
                string name = match.Groups[1].Value.Trim();
                string repoUrl = match.Groups[2].Value.Trim();
                string downloadUrl = match.Groups[4].Value.Trim();

                if (IsValidGitHubRepo(repoUrl))
                {
                    plugins.Add(new StorePluginInfo
                    {
                        Name = name,
                        RepositoryUrl = CleanUrl(repoUrl),
                        DownloadUrl = CleanUrl(downloadUrl),
                        Description = "社区插件",
                        LastUpdate = "未知"
                    });
                }
            }

            // 如果正则没匹配到，尝试之前的行拆分逻辑作为备选（保持兼容性）
            if (!plugins.Any())
            {
                var lines = scanText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                                   .Select(l => l.Trim())
                                   .Where(l => !string.IsNullOrWhiteSpace(l))
                                   .ToList();

                for (int i = 0; i < lines.Count - 1; i++)
                {
                    var line1 = lines[i];
                    var line2 = lines[i + 1];

                    if (line1.Contains("github.com", StringComparison.OrdinalIgnoreCase) && 
                        !line1.Contains("release", StringComparison.OrdinalIgnoreCase))
                    {
                        if (line2.Contains("release", StringComparison.OrdinalIgnoreCase) && 
                            line2.Contains("github.com", StringComparison.OrdinalIgnoreCase))
                        {
                            string repoUrl = ExtractUrl(line1);
                            string downloadUrl = ExtractUrl(line2);

                            if (IsValidGitHubRepo(repoUrl))
                            {
                                string name = line1.Split(new[] { ':', '[', '(' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                                plugins.Add(new StorePluginInfo
                                {
                                    Name = name,
                                    RepositoryUrl = CleanUrl(repoUrl),
                                    DownloadUrl = CleanUrl(downloadUrl),
                                    Description = "社区插件",
                                    LastUpdate = "未知"
                                });
                                i++;
                            }
                        }
                    }
                }
            }

            return plugins;
        }

        private string ExtractUrl(string text)
        {
            var match = Regex.Match(text, @"(https?://github\.com/[^\s\]\)\>]+)");
            return match.Success ? match.Groups[1].Value : "";
        }

        private bool IsValidGitHubRepo(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            
            // 规则 1: 必须包含 github.com
            if (!url.Contains("github.com", StringComparison.OrdinalIgnoreCase)) return false;

            // 规则 2: 排除掉 Wiki 内部页面和主仓库自身非插件链接
            if (url.EndsWith("/Available-Plugins", StringComparison.OrdinalIgnoreCase) || 
                url.Contains("/wiki/", StringComparison.OrdinalIgnoreCase)) return false;

            // 规则 3: 正则匹配标准格式 https://github.com/user/repo
            // 使用用户建议的正则，忽略锚点
            var repoRegex = new Regex(@"https?://github\.com/[\w-]+/[\w.-]+/?$");
            return repoRegex.IsMatch(url.Split('#')[0]);
        }

        private string NormalizeUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                if (url.StartsWith("/")) return "https://github.com" + url;
                return ""; // 可能是相对路径或锚点，忽略
            }
            return url;
        }

        private string CleanUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            // 移除锚点和尾随斜杠
            var clean = url.Split('#')[0].TrimEnd('/');
            return clean;
        }

        private List<StorePluginInfo> GetStaticFallbackPlugins()
        {
            return new List<StorePluginInfo>
            {
                new StorePluginInfo { Name = "OfficeViewer v6", LastUpdate = "2025-12-18", RepositoryUrl = "https://github.com/emako/QuickLook.Plugin.OfficeViewer", DownloadUrl = "https://github.com/emako/QuickLook.Plugin.OfficeViewer/releases", Description = "View Office formats without installing MS Office" },
                new StorePluginInfo { Name = "HelixViewer v1.0.1-beta", LastUpdate = "2018-10-24", RepositoryUrl = "https://github.com/jeremyhart/QuickLook.Plugin.HelixViewer", DownloadUrl = "https://github.com/jeremyhart/QuickLook.Plugin.HelixViewer/releases", Description = "A plugin for viewing 3D models" },
                new StorePluginInfo { Name = "FolderViewer v1.2", LastUpdate = "2021-01-04", RepositoryUrl = "https://github.com/adyanth/QuickLook.Plugin.FolderViewer", DownloadUrl = "https://github.com/adyanth/QuickLook.Plugin.FolderViewer/releases", Description = "Preview content inside folders" },
            };
        }

        public async Task<List<PluginInfo>> GetInstalledPluginsAsync()
        {
            return await Task.Run(() =>
            {
                var plugins = new List<PluginInfo>();
                var paths = GetPluginSearchPaths();
                foreach (var path in paths)
                {
                    if (Directory.Exists(path))
                    {
                        var dlls = Directory.GetFiles(path, "QuickLook.Plugin.*.dll", SearchOption.AllDirectories);
                        foreach (var dll in dlls)
                        {
                            try
                            {
                                var versionInfo = FileVersionInfo.GetVersionInfo(dll);
                                plugins.Add(new PluginInfo
                                {
                                    Name = Path.GetFileNameWithoutExtension(dll).Replace("QuickLook.Plugin.", ""),
                                    Version = versionInfo.FileVersion ?? "1.0.0",
                                    Description = versionInfo.FileDescription ?? "",
                                    Author = versionInfo.CompanyName ?? "",
                                    Path = Path.GetDirectoryName(dll) ?? "",
                                    AssemblyPath = dll
                                });
                            }
                            catch { }
                        }
                    }
                }
                return plugins.GroupBy(p => p.Name).Select(g => g.First()).ToList();
            });
        }

        private List<string> GetPluginSearchPaths()
        {
            var paths = new List<string>();
            paths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"pooi.moe\QuickLook\QuickLook.Plugin\"));
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            paths.Add(Path.Combine(localAppData, @"Packages\21090PaddyXu.QuickLook_egxr34yet59cg\LocalCache\Roaming\pooi.moe\QuickLook\QuickLook.Plugin\"));
            return paths;
        }
    }
}
