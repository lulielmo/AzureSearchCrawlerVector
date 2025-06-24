using Abot2.Poco;
using AzureSearchCrawler.Interfaces;
using AzureSearchCrawler.Models;
using Microsoft.Playwright;

namespace AzureSearchCrawler
{
    public class HeadlessBrowserCrawler : IWebCrawlingStrategy, IDisposable
    {
        //private readonly CrawledPageQueue _queue;
        private readonly ICrawledPageProcessor _processor;
        private readonly IConsole _console;
        private readonly IPlaywright _playwright;
        private readonly IBrowser _browser;
        private readonly HashSet<string> _visitedUrls = [];
        private readonly bool _ownsPlaywright;
        private bool _disposed;

        public HeadlessBrowserCrawler(ICrawledPageProcessor processor, IConsole console, IPlaywright? playwright = null)
        {
            //_queue = queue ?? throw new ArgumentNullException(nameof(queue));
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

        //public HeadlessBrowserCrawler(CrawledPageQueue queue, IConsole console, IPlaywright? playwright = null)
        //{
        //    _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        //    _console = console ?? throw new ArgumentNullException(nameof(console));

        //    if (playwright == null)
        //    {
        //        _ownsPlaywright = true;
        //        _playwright = Playwright.CreateAsync().GetAwaiter().GetResult();
        //    }
        //    else
        //    {
        //        _ownsPlaywright = false;
        //        _playwright = playwright;
        //    }

        //    _browser = _playwright.Chromium.LaunchAsync().GetAwaiter().GetResult();
        //}

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
                var browserPage = await context.NewPageAsync();

                await browserPage.SetExtraHTTPHeadersAsync(new Dictionary<string, string>
                {
                    ["User-Agent"] = "AzureSearchCrawler/1.0"
                });

                await CrawlPageAsync(browserPage, rootUri.ToString(), maxPages, maxDepth, 0, domSelector);

                _console.WriteLine($"Crawl completed successfully. Processed {_visitedUrls.Count} pages.", LogLevel.Information);
            }
            catch (Exception ex)
            {
                _console.WriteLine($"Critical error during crawl: {ex.Message}", LogLevel.Error);
                _console.WriteLine($"Technical details: {ex}", LogLevel.Debug);
                throw;
            }
        }

        private async Task CrawlPageAsync(IPage browserPage, string url, int maxPages, int maxDepth, int currentDepth, string? domSelector)
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

                var response = await browserPage.GotoAsync(url);
                if (response == null)
                {
                    _console.WriteLine($"Failed to load page: {url}", LogLevel.Warning);
                    return;
                }

                if (!response.Ok)
                {
                    _console.WriteLine($"Received non-200 status code {response.Status} for {url}", LogLevel.Warning);
                    return;
                }

                string content;
                try
                {
                    _console.WriteLine($"About to call ContentAsync", LogLevel.Debug);
                    content = await browserPage.ContentAsync();
                    _console.WriteLine($"ContentAsync returned, content.Length = {content.Length}", LogLevel.Debug);
                }
                catch (Exception ex)
                {
                    _console.WriteLine($"Exception in ContentAsync: {ex.Message}", LogLevel.Error);
                    _console.WriteLine($"Technical details: {ex}", LogLevel.Debug);
                    throw;
                }
                
                if (string.IsNullOrWhiteSpace(content))
                {
                    _console.WriteLine($"Skipping empty page content: {url}", LogLevel.Warning);
                    return;
                }

                _console.WriteLine($"Content details - Size: {content.Length} bytes", LogLevel.Debug);

                _visitedUrls.Add(url);

                var crawledPage = new CrawledWebPage(
                    new Uri(url),
                    await browserPage.TitleAsync(),
                    content,
                    response.Status,
                    null);

                //_queue.Enqueue(crawledPage);
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
                var links = await browserPage.QuerySelectorAllAsync(selector);
                _console.WriteLine($"Found {links.Count} links on page", LogLevel.Debug);
                var validLinks = new List<string>();

                if (links.Count == 0)
                {
                    _console.WriteLine("No links found on page", LogLevel.Information);
                    return;
                }

                foreach (var link in links)
                {
                    var href = await link.GetAttributeAsync("href");
                    if (string.IsNullOrEmpty(href))
                    {
                        _console.WriteLine("Skipping link with empty href", LogLevel.Debug);
                        continue;
                    }

                    try
                    {
                        if (!IsValidUrl(href))
                        {
                            _console.WriteLine($"Skipping invalid URL: {href}", LogLevel.Warning);
                            continue;
                        }

                        var linkUri = new Uri(new Uri(url), href);
                        if (linkUri.Host == new Uri(url).Host)
                        {
                            var absoluteUrl = linkUri.ToString();
                            _console.WriteLine($"Processing valid URL: {absoluteUrl}", LogLevel.Debug);
                            validLinks.Add(absoluteUrl);
                        }
                        else
                        {
                            _console.WriteLine($"Skipping external link: {linkUri}", LogLevel.Debug);
                        }
                    }
                    catch (UriFormatException)
                    {
                        _console.WriteLine($"Skipping malformed URL: {href}", LogLevel.Debug);
                    }
                }

                _console.WriteLine($"Found {validLinks.Count} valid links to crawl", LogLevel.Debug);

                foreach (var link in validLinks)
                {
                    if (_visitedUrls.Count >= maxPages)
                    {
                        _console.WriteLine($"Crawl complete: Reached maximum pages limit ({maxPages})", LogLevel.Information);
                        return;
                    }

                    var newPage = await browserPage.Context.NewPageAsync();
                    await newPage.SetExtraHTTPHeadersAsync(new Dictionary<string, string>
                    {
                        ["User-Agent"] = "AzureSearchCrawler/1.0"
                    });

                    await CrawlPageAsync(newPage, link, maxPages, maxDepth, currentDepth + 1, domSelector);
                    await newPage.CloseAsync();
                }
            }
            catch (Exception ex)
            {
                _console.WriteLine($"Error crawling {url}: {ex.Message}", LogLevel.Error);
                _console.WriteLine($"Technical details: {ex}", LogLevel.Debug);
            }
        }

        // Method for testing purposes
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
                    // Continue with next link
                }
            }

            _console.WriteLine($"Found {validLinks.Count} valid links", LogLevel.Information);
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
    }
} 