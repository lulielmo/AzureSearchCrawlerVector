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
        //private readonly LogLevel _logLevel;
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

        public SitemapCrawler(ICrawledPageProcessor processor, IConsole console, LogLevel logLevel = LogLevel.Information, HttpClient? httpClient = null)
        {
            _processor = processor ?? throw new ArgumentNullException(nameof(processor));
            _console = console ?? throw new ArgumentNullException(nameof(console));
            //_logLevel = logLevel; //TODO: Make use of this setting
            _httpClient = httpClient ?? new HttpClient();
            _processedSitemaps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            if (httpClient == null)
            {
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
            if (depth > 10) // Förhindra för djup rekursion
            {
                _console.WriteLine("Maximum sitemap depth reached", LogLevel.Warning);
                return;
            }

            var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;
            foreach (var sitemapElement in doc.Descendants(ns + "sitemap"))
            {
                if (_processedPages >= maxPages)
                {
                    _console.WriteLine($"Reached maximum pages limit ({maxPages})", LogLevel.Information);
                    break;
                }

                var locElement = sitemapElement.Element(ns + "loc");
                if (locElement == null || string.IsNullOrWhiteSpace(locElement.Value))
                {
                    _console.WriteLine("Invalid sitemap location: empty", LogLevel.Warning);
                    continue;
                }

                try
                {
                    var sitemapUrl = ResolveUrl(rootUri, locElement.Value);
                    
                    // Kontrollera cirkulära referenser
                    if (!_processedSitemaps.Add(sitemapUrl.ToString()))
                    {
                        _console.WriteLine($"Circular reference detected: {sitemapUrl}", LogLevel.Warning);
                        continue;
                    }

                    var sitemapContent = await _httpClient.GetStringAsync(sitemapUrl);
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
                }
            }
        }

        private async Task ProcessUrlset(XDocument doc, Uri rootUri, int maxPages)
        {
            var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;
            foreach (var urlElement in doc.Descendants(ns + "url"))
            {
                if (_processedPages >= maxPages)
                {
                    _console.WriteLine($"Reached maximum pages limit ({maxPages})", LogLevel.Information);
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
                    _console.WriteLine($"Skipping invalid URL: {locElement.Value}", LogLevel.Warning);
                    continue;
                }

                if (pageUri.Host != rootUri.Host)
                {
                    _console.WriteLine($"Skipping URL from different domain: {pageUri}", LogLevel.Warning);
                    continue;
                }

                try
                {
                    var pageContent = await _httpClient.GetStringAsync(pageUri);
                    var crawledPage = new CrawledPage(pageUri)
                    {
                        Content = new PageContent { Text = pageContent }
                    };
                    await _processor.PageCrawledAsync(crawledPage);
                    _processedPages++;
                }
                catch (Exception ex)
                {
                    _console.WriteLine($"Failed to fetch page {pageUri}: {ex.Message}", LogLevel.Error);
                }
            }
        }

        public async Task CrawlAsync(Uri rootUri, int maxPages, int maxDepth, string? domSelector = null)
        {
            _processedPages = 0;
            _processedSitemaps.Clear();

            foreach (var path in SITEMAP_PATHS)
            {
                try
                {
                    var sitemapUrl = new Uri(rootUri, path);
                    _console.WriteLine($"Trying sitemap at {sitemapUrl}", LogLevel.Debug);

                    if (path == "/robots.txt")
                    {
                        var robotsTxt = await _httpClient.GetStringAsync(sitemapUrl);
                        var sitemapLine = robotsTxt.Split('\n')
                            .FirstOrDefault(line => line.StartsWith("Sitemap:", StringComparison.OrdinalIgnoreCase));
                        
                        if (sitemapLine != null)
                        {
                            var actualSitemapUrl = sitemapLine.Split(':', 2)[1].Trim();
                            _console.WriteLine($"Found sitemap URL in robots.txt: {actualSitemapUrl}", LogLevel.Information);
                            sitemapUrl = new Uri(actualSitemapUrl);
                        }
                    }

                    var sitemapContent = await _httpClient.GetStringAsync(sitemapUrl);
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
                    return;
                }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _console.WriteLine($"No sitemap found at {path}", LogLevel.Debug);
                    continue;
                }
                catch (XmlException)
                {
                    _console.WriteLine($"Invalid XML found at {path}", LogLevel.Warning);
                    continue;
                }
                catch (Exception ex)
                {
                    _console.WriteLine($"Error processing sitemap at {path}: {ex.Message}", LogLevel.Error);
                    continue;
                }
            }

            throw new Exception("Could not find sitemap at any common locations");
        }
    }
}