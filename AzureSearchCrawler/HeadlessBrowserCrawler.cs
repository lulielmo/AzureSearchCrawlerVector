using Abot2.Poco;
using AzureSearchCrawler.Interfaces;
using AzureSearchCrawler.Models;
using Microsoft.Playwright;

namespace AzureSearchCrawler
{
    public class HeadlessBrowserCrawler : IWebCrawlingStrategy, IDisposable
    {
        private readonly ICrawledPageProcessor _processor;
        private readonly IConsole _console;
        private readonly IPlaywright _playwright;
        private readonly IBrowser _browser;
        private readonly HashSet<string> _visitedUrls = [];
        private readonly bool _ownsPlaywright;
        private bool _disposed;

        public HeadlessBrowserCrawler(ICrawledPageProcessor processor, IConsole console, IPlaywright? playwright = null)
        {
            _processor = processor ?? throw new ArgumentNullException(nameof(processor));
            _console = console ?? throw new ArgumentNullException(nameof(console));
            
            if (playwright == null)
            {
                _ownsPlaywright = true;
                _playwright = Playwright.CreateAsync().GetAwaiter().GetResult();
            }
            else
            {
                _ownsPlaywright = false;
                _playwright = playwright;
            }

            _browser = _playwright.Chromium.LaunchAsync().GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _browser?.DisposeAsync().AsTask().Wait();
                    if (_ownsPlaywright)
                    {
                        _playwright?.Dispose();
                    }
                }
                _disposed = true;
            }
        }

        ~HeadlessBrowserCrawler()
        {
            Dispose(false);
        }

        public async Task CrawlAsync(Uri rootUri, int maxPages, int maxDepth, string? domSelector = null)
        {
            ArgumentNullException.ThrowIfNull(rootUri);
            if (maxPages <= 0) throw new ArgumentException("Must be greater than 0", nameof(maxPages));
            if (maxDepth <= 0) throw new ArgumentException("Must be greater than 0", nameof(maxDepth));

            try
            {
                _console.WriteLine($"Starting headless browser crawl of {rootUri}", LogLevel.Information);
                _console.WriteLine($"Configuration - Max pages: {maxPages}, Max depth: {maxDepth}, DOM selector: {domSelector ?? "none"}", LogLevel.Debug);
                if (domSelector != null)
                {
                    _console.WriteLine($"Using DOM selector filter: {domSelector}", LogLevel.Information);
                }

                _console.WriteLine("Initializing browser configuration", LogLevel.Information);
                _console.WriteLine("Browser details - Engine: Chromium, Mode: Headless", LogLevel.Debug);

                await using var context = await _browser.NewContextAsync();
                var page = await context.NewPageAsync();

                await page.SetExtraHTTPHeadersAsync(new Dictionary<string, string>
                {
                    ["User-Agent"] = "AzureSearchCrawler/1.0"
                });

                await CrawlPageAsync(page, rootUri.ToString(), maxPages, maxDepth, 0, domSelector);

                _console.WriteLine($"Crawl completed successfully. Processed {_visitedUrls.Count} pages.", LogLevel.Information);
            }
            catch (Exception ex)
            {
                _console.WriteLine($"Critical error during crawl: {ex.Message}", LogLevel.Error);
                _console.WriteLine($"Technical details: {ex}", LogLevel.Debug);
                throw;
            }
        }

        private async Task CrawlPageAsync(IPage page, string url, int maxPages, int maxDepth, int currentDepth, string? domSelector)
        {
            if (currentDepth > maxDepth)
            {
                _console.WriteLine($"Maximum depth reached for {url} (depth: {currentDepth})", LogLevel.Warning);
                return;
            }
            if (_visitedUrls.Count >= maxPages)
            {
                _console.WriteLine($"Crawl complete: Reached maximum pages limit ({maxPages})", LogLevel.Information);
                return;
            }
            if (_visitedUrls.Contains(url))
            {
                _console.WriteLine($"Skipping duplicate URL: {url}", LogLevel.Debug);
                return;
            }

            try
            {
                _console.WriteLine($"Processing page {_visitedUrls.Count + 1}/{maxPages}: {url}", LogLevel.Information);
                _console.WriteLine($"Page details - Depth: {currentDepth}/{maxDepth}", LogLevel.Debug);

                var startTime = DateTime.Now;
                _console.WriteLine($"About to call GotoAsync for {url}", LogLevel.Debug);
                var response = await page.GotoAsync(url, new PageGotoOptions 
                { 
                    WaitUntil = WaitUntilState.NetworkIdle,
                    Timeout = 30000
                });
                var loadTime = DateTime.Now - startTime;
                
                _console.WriteLine($"GotoAsync returned, response.Ok = {response?.Ok}, response.Status = {response?.Status}", LogLevel.Debug);
                _console.WriteLine($"Navigation details - Status: {response?.Status}, Load time: {loadTime.TotalSeconds:F2}s", LogLevel.Debug);
                _console.WriteLine($"Request configuration - Timeout: 30000ms, WaitUntil: NetworkIdle", LogLevel.Verbose);
                _console.WriteLine($"Response headers: {string.Join(", ", response?.Headers.Select(h => $"{h.Key}: {h.Value}") ?? [])}", LogLevel.Verbose);

                if (response?.Ok != true)
                {
                    _console.WriteLine($"Failed to load {url} ({response?.Status} {response?.StatusText})", LogLevel.Warning);
                    return;
                }

                string content;
                try
                {
                    _console.WriteLine($"About to call ContentAsync", LogLevel.Debug);
                    content = await page.ContentAsync();
                    _console.WriteLine($"ContentAsync returned, content.Length = {content.Length}", LogLevel.Debug);
                }
                catch (Exception ex)
                {
                    _console.WriteLine($"Exception in ContentAsync: {ex.Message}", LogLevel.Error);
                    _console.WriteLine($"Technical details: {ex}", LogLevel.Debug);
                    throw;
                }
                
                _console.WriteLine($"Content details - Size: {content.Length} bytes", LogLevel.Debug);

                _visitedUrls.Add(url);

                var crawledPage = new CrawledPage(new Uri(url))
                {
                    Content = new PageContent { Text = content }
                };

                await _processor.PageCrawledAsync(crawledPage);

                // Don't extract links if we're at max depth
                if (currentDepth >= maxDepth)
                {
                    _console.WriteLine($"At max depth ({maxDepth}), skipping link extraction", LogLevel.Debug);
                    return;
                }

                var selector = domSelector != null ? $"{domSelector} a[href]" : "a[href]";
                _console.WriteLine($"Link extraction - Using selector: {selector}", LogLevel.Debug);

                _console.WriteLine($"About to call QuerySelectorAllAsync with selector: {selector}", LogLevel.Debug);
                var links = await page.QuerySelectorAllAsync(selector);
                _console.WriteLine($"Found {links.Count} links on page", LogLevel.Debug);
                var validLinks = new List<string>();

                if (links.Count == 0)
                {
                    _console.WriteLine("No links found on page", LogLevel.Information);
                    return;
                }

                foreach (var link in links)
                {
                    try
                    {
                        var href = await link.GetAttributeAsync("href");
                        _console.WriteLine($"Link validation - URL: {href}", LogLevel.Verbose);

                        if (!string.IsNullOrEmpty(href) && !IsValidUrl(href))
                        {
                            _console.WriteLine($"Skipping invalid URL: {href}", LogLevel.Warning);
                            continue;
                        }

                        var absoluteUrl = new Uri(new Uri(url), href).ToString();
                        _console.WriteLine($"Processing valid URL: {absoluteUrl}", LogLevel.Debug);
                        validLinks.Add(absoluteUrl);
                    }
                    catch (Exception ex)
                    {
                        _console.WriteLine($"Failed to process link: {ex.Message}", LogLevel.Warning);
                        _console.WriteLine($"Technical details: {ex}", LogLevel.Debug);
                        // Fortsätt med nästa länk
                    }
                }

                _console.WriteLine($"Found {validLinks.Count} valid links", LogLevel.Information);

                foreach (var nextUrl in validLinks)
                {
                    _console.WriteLine($"About to create new page for {nextUrl}", LogLevel.Debug);
                    var nextPage = await page.Context.NewPageAsync();
                    _console.WriteLine($"About to call CrawlPageAsync for {nextUrl}", LogLevel.Debug);
                    await CrawlPageAsync(nextPage, nextUrl, maxPages, maxDepth, currentDepth + 1, domSelector);
                    _console.WriteLine($"CrawlPageAsync returned, about to close page", LogLevel.Debug);
                    await nextPage.CloseAsync();
                    _console.WriteLine($"Page closed", LogLevel.Debug);
                }
            }
            catch (Exception ex)
            {
                _console.WriteLine($"Failed to crawl {url}: {ex.Message}", LogLevel.Error);
                _console.WriteLine($"Technical details: {ex}", LogLevel.Debug);
            }
        }

        private static bool IsValidUrl(string href)
        {
            return !string.IsNullOrEmpty(href) &&
                   !href.StartsWith('#') &&
                   !href.StartsWith("javascript:") &&
                   !href.StartsWith("mailto:") &&
                   !href.StartsWith("tel:") &&
                   (href.StartsWith("http://") 
                    || href.StartsWith("https://") 
                    || href.StartsWith('/'));
        }

        // Metod för testning
        public async Task ProcessLinksAsync(IPage page, string url)
        {
            var selector = "a[href]";
            _console.WriteLine($"Link extraction - Using selector: {selector}", LogLevel.Debug);

            _console.WriteLine($"About to call QuerySelectorAllAsync with selector: {selector}", LogLevel.Debug);
            var links = await page.QuerySelectorAllAsync(selector);
            _console.WriteLine($"Found {links.Count} links on page", LogLevel.Debug);
            var validLinks = new List<string>();

            foreach (var link in links)
            {
                try
                {
                    var href = await link.GetAttributeAsync("href");
                    _console.WriteLine($"Link validation - URL: {href}", LogLevel.Verbose);

                    if (!string.IsNullOrEmpty(href) && !IsValidUrl(href))
                    {
                        _console.WriteLine($"Skipping invalid URL: {href}", LogLevel.Warning);
                        continue;
                    }

                    var absoluteUrl = new Uri(new Uri(url), href).ToString();
                    _console.WriteLine($"Processing valid URL: {absoluteUrl}", LogLevel.Debug);
                    validLinks.Add(absoluteUrl);
                }
                catch (Exception ex)
                {
                    _console.WriteLine($"Failed to process link: {ex.Message}", LogLevel.Warning);
                    _console.WriteLine($"Technical details: {ex}", LogLevel.Debug);
                    // Fortsätt med nästa länk
                }
            }

            _console.WriteLine($"Found {validLinks.Count} valid links", LogLevel.Information);
        }
    }
} 