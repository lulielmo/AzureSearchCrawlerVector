using Abot2.Poco;
using AzureSearchCrawler.Interfaces;
using AzureSearchCrawler.Models;
using System.Xml;
using System.Xml.Linq;

namespace AzureSearchCrawler
{
    public class SitemapCrawler : IWebCrawlingStrategy
    {
        private readonly ICrawledPageProcessor _processor;
        private readonly IConsole _console;
        private readonly HttpClient _httpClient;
        private int _processedPages;
        private readonly HashSet<string> _processedSitemaps;

        private static readonly string[] SITEMAP_PATHS =
        [
            "/sitemap.xml",
            "/sitemap_index.xml",
            "/sitemaps/sitemap.xml",
            "/sitemap/sitemap.xml",
            "/robots.txt"  // Vi kollar robots.txt först för att hitta sitemap-URL
        ];

        public SitemapCrawler(ICrawledPageProcessor processor, IConsole console, HttpClient? httpClient = null)
        {
            _processor = processor ?? throw new ArgumentNullException(nameof(processor));
            _console = console ?? throw new ArgumentNullException(nameof(console));
            _httpClient = httpClient ?? new HttpClient();
            _processedSitemaps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            if (httpClient == null)
            {
                _console.WriteLine("Setting up HTTP client with custom User-Agent", LogLevel.Verbose);
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; AzureSearchCrawler/1.0)");
            }
        }

        private static Uri ResolveUrl(Uri baseUri, string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var absoluteUri))
            {
                return absoluteUri;
            }

            // Hantera relativa URLs som börjar med / eller ./ eller ../
            url = url.TrimStart('.');
            if (!url.StartsWith('/'))
            {
                url = "/" + url;
            }

            return new Uri(baseUri, url);
        }

        private async Task ProcessSitemapIndex(XDocument doc, Uri rootUri, int maxPages, int depth = 0)
        {
            if (depth >= 10)
            {
                _console.WriteLine("Maximum sitemap depth reached (10). Possible circular reference detected.", LogLevel.Warning);
                return;
            }

            var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;
            var sitemaps = doc.Descendants(ns + "loc").ToList();
            _console.WriteLine($"Found {sitemaps.Count} sitemaps in index", LogLevel.Information);
            _console.WriteLine($"Current depth: {depth}, Namespace: {ns}", LogLevel.Debug);

            foreach (var locElement in sitemaps)
            {
                if (_processedPages >= maxPages)
                {
                    _console.WriteLine($"Crawl complete: Reached maximum pages limit ({maxPages})", LogLevel.Information);
                    return;
                }

                if (string.IsNullOrWhiteSpace(locElement.Value))
                {
                    _console.WriteLine("Skipping invalid sitemap: empty location", LogLevel.Warning);
                    continue;
                }

                try
                {
                    var sitemapUrl = ResolveUrl(rootUri, locElement.Value);
                    _console.WriteLine($"Processing sitemap: {sitemapUrl}", LogLevel.Information);
                    
                    if (!_processedSitemaps.Add(sitemapUrl.ToString()))
                    {
                        _console.WriteLine($"Skipping sitemap: circular reference detected at {sitemapUrl}", LogLevel.Warning);
                        continue;
                    }

                    var startTime = DateTime.Now;
                    var sitemapContent = await _httpClient.GetStringAsync(sitemapUrl);
                    var loadTime = DateTime.Now - startTime;
                    
                    _console.WriteLine($"Sitemap details - Size: {sitemapContent.Length} bytes", LogLevel.Debug);
                    _console.WriteLine($"Download timing: {loadTime.TotalSeconds:F2} seconds", LogLevel.Verbose);
                    _console.WriteLine($"Request details - URL: {sitemapUrl}, Depth: {depth}", LogLevel.Verbose);

                    var subDoc = XDocument.Parse(sitemapContent);
                    var rootName = subDoc.Root?.Name.LocalName.ToLowerInvariant();

                    if (rootName == "sitemapindex")
                    {
                        await ProcessSitemapIndex(subDoc, rootUri, maxPages, depth + 1);
                    }
                    else if (rootName == "urlset")
                    {
                        await ProcessUrlset(subDoc, rootUri, maxPages);
                    }
                    else
                    {
                        _console.WriteLine($"Invalid sitemap format at {sitemapUrl}", LogLevel.Warning);
                    }
                }
                catch (Exception ex)
                {
                    _console.WriteLine($"Failed to process sitemap: {ex.Message}", LogLevel.Error);
                    _console.WriteLine($"Technical details: {ex}", LogLevel.Debug);
                }
            }
        }

        private async Task ProcessUrlset(XDocument doc, Uri rootUri, int maxPages)
        {
            var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;
            var urls = doc.Descendants(ns + "url").ToList();
            _console.WriteLine($"Found {urls.Count} URLs in sitemap", LogLevel.Information);

            foreach (var urlElement in urls)
            {
                if (_processedPages >= maxPages)
                {
                    _console.WriteLine($"Crawl complete: Reached maximum pages limit ({maxPages})", LogLevel.Information);
                    break;
                }

                var locElement = urlElement.Element(ns + "loc");
                if (locElement == null || string.IsNullOrWhiteSpace(locElement.Value))
                {
                    _console.WriteLine("Skipping invalid URL: empty location", LogLevel.Warning);
                    continue;
                }

                Uri pageUri;
                try
                {
                    pageUri = ResolveUrl(rootUri, locElement.Value);
                }
                catch (UriFormatException)
                {
                    _console.WriteLine($"Skipping malformed URL: {locElement.Value}", LogLevel.Warning);
                    continue;
                }

                if (pageUri.Host != rootUri.Host)
                {
                    _console.WriteLine($"Skipping external URL: {pageUri}", LogLevel.Warning);
                    continue;
                }

                try
                {
                    _console.WriteLine($"Processing page {_processedPages + 1}/{maxPages}: {pageUri}", LogLevel.Information);
                    var startTime = DateTime.Now;
                    var pageContent = await _httpClient.GetStringAsync(pageUri);
                    var loadTime = DateTime.Now - startTime;
                    
                    _console.WriteLine($"Page details - Size: {pageContent.Length} bytes", LogLevel.Debug);
                    _console.WriteLine($"Download timing: {loadTime.TotalSeconds:F2} seconds", LogLevel.Verbose);
                    _console.WriteLine($"Request details - URL: {pageUri}, Content type: {pageContent.Length}", LogLevel.Verbose);

                    var crawledPage = new CrawledPage(pageUri)
                    {
                        Content = new PageContent { Text = pageContent }
                    };
                    await _processor.PageCrawledAsync(crawledPage);
                    _processedPages++;
                }
                catch (Exception ex)
                {
                    _console.WriteLine($"Failed to crawl {pageUri}: {ex.Message}", LogLevel.Error);
                    _console.WriteLine($"Technical details: {ex}", LogLevel.Debug);
                }
            }
        }

        public async Task CrawlAsync(Uri rootUri, int maxPages, int maxDepth, string? domSelector = null)
        {
            ArgumentNullException.ThrowIfNull(rootUri);
            if (maxPages <= 0) throw new ArgumentException("Must be greater than 0", nameof(maxPages));
            if (maxDepth <= 0) throw new ArgumentException("Must be greater than 0", nameof(maxDepth));

            try
            {
                _console.WriteLine($"Starting sitemap crawl of {rootUri}", LogLevel.Information);
                _console.WriteLine($"Configuration - Max pages: {maxPages}, Max depth: {maxDepth}", LogLevel.Debug);
                _processedPages = 0;
                _processedSitemaps.Clear();

                foreach (var path in SITEMAP_PATHS)
                {
                    try
                    {
                        var sitemapUrl = new Uri(rootUri, path);
                        _console.WriteLine($"Checking sitemap at {sitemapUrl}", LogLevel.Information);

                        if (path == "/robots.txt")
                        {
                            var robotsStartTime = DateTime.Now;
                            var robotsTxt = await _httpClient.GetStringAsync(sitemapUrl);
                            var robotsLoadTime = DateTime.Now - robotsStartTime;
                            
                            _console.WriteLine($"Robots.txt details - Size: {robotsTxt.Length} bytes", LogLevel.Debug);
                            _console.WriteLine($"Download timing: {robotsLoadTime.TotalSeconds:F2} seconds", LogLevel.Verbose);
                            _console.WriteLine($"Content: {robotsTxt}", LogLevel.Verbose);

                            var sitemapLine = robotsTxt.Split('\n')
                                .FirstOrDefault(line => line.StartsWith("Sitemap:", StringComparison.OrdinalIgnoreCase));
                            
                            if (sitemapLine != null)
                            {
                                var actualSitemapUrl = sitemapLine.Split(':', 2)[1].Trim();
                                _console.WriteLine($"Found sitemap URL in robots.txt: {actualSitemapUrl}", LogLevel.Information);
                                sitemapUrl = new Uri(actualSitemapUrl);
                            }
                        }

                        var startTime = DateTime.Now;
                        var sitemapContent = await _httpClient.GetStringAsync(sitemapUrl);
                        var loadTime = DateTime.Now - startTime;
                        
                        _console.WriteLine($"Sitemap details - Size: {sitemapContent.Length} bytes", LogLevel.Debug);
                        _console.WriteLine($"Download timing: {loadTime.TotalSeconds:F2} seconds", LogLevel.Verbose);
                        _console.WriteLine($"Request details - URL: {sitemapUrl}", LogLevel.Verbose);

                        var doc = XDocument.Parse(sitemapContent);
                        var rootName = doc.Root?.Name.LocalName.ToLowerInvariant();

                        _processedSitemaps.Add(sitemapUrl.ToString());

                        if (rootName == "sitemapindex")
                        {
                            await ProcessSitemapIndex(doc, rootUri, maxPages);
                        }
                        else if (rootName == "urlset")
                        {
                            await ProcessUrlset(doc, rootUri, maxPages);
                        }
                        else
                        {
                            _console.WriteLine($"Invalid sitemap format at {sitemapUrl}", LogLevel.Warning);
                            continue;
                        }

                        await _processor.CrawlFinishedAsync();
                        _console.WriteLine($"Crawl completed successfully. Processed {_processedPages} pages.", LogLevel.Information);
                        return;
                    }
                    catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        _console.WriteLine($"No sitemap found at {path}", LogLevel.Debug);
                        continue;
                    }
                    catch (Exception ex)
                    {
                        _console.WriteLine($"Failed to process sitemap at {path}: {ex.Message}", LogLevel.Error);
                        _console.WriteLine($"Technical details: {ex}", LogLevel.Debug);
                        continue;
                    }
                }

                throw new Exception("Could not find sitemap at any common locations");
            }
            catch (Exception ex)
            {
                _console.WriteLine($"Critical error during crawl: {ex.Message}", LogLevel.Error);
                _console.WriteLine($"Technical details: {ex}", LogLevel.Debug);
                throw;
            }
        }
    }
}