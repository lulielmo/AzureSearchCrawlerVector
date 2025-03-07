using Microsoft.Playwright;
using AzureSearchCrawler.Interfaces;
using AzureSearchCrawler.Models;
using Abot2.Poco;
using System.Net;

namespace AzureSearchCrawler
{
    public class HeadlessBrowserCrawler : ICrawler, IDisposable
    {
        private readonly ICrawlHandler _handler;
        private readonly IConsole _console;
        private readonly IPlaywright _playwright;
        private readonly IBrowser _browser;
        private readonly HashSet<string> _visitedUrls;
        private readonly bool _ownsPlaywright;

        public HeadlessBrowserCrawler(ICrawlHandler handler, IConsole console)
            : this(handler, console, Playwright.CreateAsync().GetAwaiter().GetResult(), true)
        {
        }

        internal HeadlessBrowserCrawler(ICrawlHandler handler, IConsole console, IPlaywright playwright, bool ownsPlaywright = false)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            _console = console ?? throw new ArgumentNullException(nameof(console));
            _playwright = playwright ?? throw new ArgumentNullException(nameof(playwright));
            _ownsPlaywright = ownsPlaywright;
            _browser = _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true
            }).GetAwaiter().GetResult();
            _visitedUrls = [];
        }

        private bool _disposed = false;

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
                await using var context = await _browser.NewContextAsync();
                var page = await context.NewPageAsync();

                await page.SetExtraHTTPHeadersAsync(new Dictionary<string, string>
                {
                    ["User-Agent"] = "AzureSearchCrawler/1.0"
                });

                await CrawlPageAsync(page, rootUri.ToString(), maxPages, maxDepth, 0, domSelector);
            }
            catch (Exception ex)
            {
                _console.WriteLine($"Error during crawl: {ex.Message}", LogLevel.Error);
                throw;
            }
        }

        private async Task CrawlPageAsync(IPage page, string url, int maxPages, int maxDepth, int currentDepth, string? domSelector)
        {
            if (currentDepth > maxDepth || _visitedUrls.Count >= maxPages || _visitedUrls.Contains(url))
                return;

            try
            {
                _console.WriteLine($"Crawling {url}", LogLevel.Debug);
                var response = await page.GotoAsync(url, new PageGotoOptions 
                { 
                    WaitUntil = WaitUntilState.NetworkIdle,
                    Timeout = 30000
                });

                if (response?.Ok != true)
                {
                    _console.WriteLine($"Failed to load {url}: {response?.Status}", LogLevel.Warning);
                    return;
                }

                _visitedUrls.Add(url);

                // Hämta sidans innehåll
                var content = await page.ContentAsync();

                var crawledPage = new CrawledPage(new Uri(url))
                {
                    Content = new PageContent { Text = content },
                    HttpResponseMessage = new HttpResponseMessage((System.Net.HttpStatusCode)response.Status)
                };

                await _handler.PageCrawledAsync(crawledPage);

                if (currentDepth < maxDepth && _visitedUrls.Count < maxPages)
                {
                    // Samla alla länkar baserat på DOM-selektorn om en sådan finns
                    var links = new List<IElementHandle>();
                    if (!string.IsNullOrEmpty(domSelector))
                    {
                        _console.WriteLine($"Checking links against selector '{domSelector}'", LogLevel.Verbose);
                        var elements = await page.QuerySelectorAllAsync($"{domSelector} a[href]");
                        links.AddRange(elements);

                        if (links.Count == 0)
                        {
                            _console.WriteLine($"No links found within {domSelector} on {url}", LogLevel.Debug);
                        }
                    }
                    else
                    {
                        // Om ingen selektor finns, samla alla länkar på sidan
                        links.AddRange(await page.QuerySelectorAllAsync("a[href]"));
                    }
                    
                    // Crawla varje giltig länk med en ny page-instans
                    foreach (var link in links)
                    {
                        try
                        {
                            var href = await link.GetAttributeAsync("href");
                            if (string.IsNullOrEmpty(href) || !IsValidUrl(href))
                                continue;

                            var absoluteUrl = new Uri(new Uri(url), href).ToString();
                            var newPage = await page.Context.NewPageAsync();
                            await CrawlPageAsync(newPage, absoluteUrl, maxPages, maxDepth, currentDepth + 1, domSelector);
                        }
                        catch (Exception ex)
                        {
                            _console.WriteLine($"Error processing link: {ex.Message}", LogLevel.Warning);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _console.WriteLine($"Error crawling {url}: {ex.Message}", LogLevel.Error);
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
    }
} 