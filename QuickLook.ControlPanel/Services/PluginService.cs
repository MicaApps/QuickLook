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
            
            // 优先解析表格 (Wiki 中最新的插件通常在表格里)
            var rows = doc.DocumentNode.SelectNodes("//div[@id='wiki-body']//table/tbody/tr");
            if (rows != null)
            {
                foreach (var row in rows)
                {
                    var cells = row.SelectNodes("td");
                    if (cells != null && cells.Count >= 3)
                    {
                        var nameNode = cells[0].SelectSingleNode("a") ?? cells[0];
                        var name = nameNode.InnerText.Trim();
                        var repoUrl = nameNode.GetAttributeValue("href", "");
                        var date = cells[1].InnerText.Trim();
                        var downloadNode = cells[2].SelectSingleNode("a");
                        var downloadUrl = downloadNode?.GetAttributeValue("href", "");
                        var description = cells.Count > 3 ? cells[3].InnerText.Trim() : "";

                        if (IsValidGitHubRepo(repoUrl))
                        {
                            plugins.Add(new StorePluginInfo
                            {
                                Name = name,
                                LastUpdate = date,
                                RepositoryUrl = CleanUrl(repoUrl),
                                DownloadUrl = CleanUrl(downloadUrl ?? ""),
                                Description = description
                            });
                        }
                    }
                }
            }

            // 如果表格解析没结果，或者想补充之前的“三行一组”文本模式
            if (plugins.Count < 5)
            {
                var wikiBody = doc.DocumentNode.SelectSingleNode("//div[@id='wiki-body']");
                if (wikiBody != null)
                {
                    var wikiText = wikiBody.InnerText;
                    var stopText = "*1: Native installation only";
                    int stopIndex = wikiText.IndexOf(stopText, StringComparison.OrdinalIgnoreCase);
                    var scanText = stopIndex > 0 ? wikiText.Substring(0, stopIndex) : wikiText;

                    // 之前的正则模式
                    var pattern = @"([^\n\r:]+):?\s*\[?(https?://github\.com/[^\s\]\)\>]+)\]?\s*[\r\n]+\s*([^\n\r:]+release[^\n\r:]*):?\s*\[?(https?://github\.com/[^\s\]\)\>]+)\]?";
                    var matches = Regex.Matches(scanText, pattern, RegexOptions.IgnoreCase);

                    foreach (Match match in matches)
                    {
                        string name = match.Groups[1].Value.Trim();
                        string repoUrl = match.Groups[2].Value.Trim();
                        string downloadUrl = match.Groups[4].Value.Trim();

                        if (IsValidGitHubRepo(repoUrl) && !plugins.Any(p => p.RepositoryUrl == CleanUrl(repoUrl)))
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
