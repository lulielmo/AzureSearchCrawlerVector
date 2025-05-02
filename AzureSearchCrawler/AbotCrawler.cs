using Abot2.Crawler;
using Abot2.Poco;
using AzureSearchCrawler.Interfaces;
using AzureSearchCrawler.Models;
using System.Net;
using System.Threading;
using System.Collections.Concurrent;

namespace AzureSearchCrawler
{
    /// <summary>
    ///  A convenience wrapper for an Abot crawler with a reasonable default configuration and console logging.
    ///  The actual action to be performed on the crawled pages is passed in as a ICrawledPageProcessor.
    /// </summary>
    public class AbotCrawler : IWebCrawlingStrategy
    {
        private readonly TaskCompletionSource<Exception> _crawlError = new();
        private readonly ConcurrentDictionary<Uri, TaskCompletionSource> _activePages = new();
        private int _pageCount;
        private bool _isDisposed;
        private readonly object _lock = new();

        private readonly CrawledPageQueue _queue;
        private readonly Func<CrawlConfiguration, IWebCrawler> _webCrawlerFactory;
        private readonly IConsole _console;
        private string? _domSelector;
        private IWebCrawler? _crawler;

        public AbotCrawler(CrawledPageQueue queue, IConsole console, string? domSelector = null)
            : this(queue, config => new PoliteWebCrawler(config), console, domSelector)
        {
        }

        public AbotCrawler(CrawledPageQueue queue, Func<CrawlConfiguration, IWebCrawler> crawlerFactory, IConsole console, string? domSelector = null)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _webCrawlerFactory = crawlerFactory ?? throw new ArgumentNullException(nameof(crawlerFactory));
            _console = console ?? throw new ArgumentNullException(nameof(console));
            _pageCount = 0;
            if (domSelector != null)
            {
                _domSelector = domSelector;
            }
        }

        public async Task CrawlAsync(Uri rootUri, int maxPages, int maxDepth, string? domSelector = null)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(AbotCrawler));
            }

            var config = CreateCrawlConfiguration(maxPages, maxDepth);

            lock (_lock)
            {
                if (_crawler != null)
                {
                    throw new InvalidOperationException("Crawler is already running");
                }

                _pageCount = 0;
                _activePages.Clear();
                _crawlError.TrySetResult(null!);

                if (maxPages <= 0)
                    throw new ArgumentException("maxPages must be greater than 0", nameof(maxPages));

                if (maxDepth <= 0)
                    throw new ArgumentException("maxDepth must be greater than 0", nameof(maxDepth));

                if (domSelector != null)
                {
                    _domSelector = domSelector;
                }

                _crawler = _webCrawlerFactory(config);

                _crawler.PageCrawlStarting += Crawler_ProcessPageCrawlStarting;
                _crawler.PageCrawlCompleted += Crawler_ProcessPageCrawlCompleted;
            }
            
            try
            {
                _console.WriteLine($"Starting web crawl of {rootUri.AbsoluteUri}", LogLevel.Information);
                _console.WriteLine($"Crawl configuration: Max pages={maxPages}, Max depth={maxDepth}, Concurrent threads={config.MaxConcurrentThreads}", LogLevel.Information);
                _console.WriteLine($"Performance settings: Timeout={config.CrawlTimeoutSeconds}s, Delay between requests={config.MinCrawlDelayPerDomainMilliSeconds}ms", LogLevel.Debug);
                _console.WriteLine($"Request configuration: User-Agent='{config.UserAgentString}'", LogLevel.Debug);
                
                if (_domSelector != null)
                {
                    _console.WriteLine($"Using DOM selector filter: {_domSelector}", LogLevel.Information);
                    _crawler.ShouldScheduleLinkDecisionMaker = (uri, crawledPage, crawlContext) =>
                    {
                        if (crawledPage.AngleSharpHtmlDocument == null)
                        {
                            _console.WriteLine($"Skipping link evaluation - No HTML document available for {uri.AbsoluteUri}", LogLevel.Debug);
                            return true;
                        }

                        _console.WriteLine($"Evaluating link against selector '{_domSelector}': {uri.AbsoluteUri}", LogLevel.Verbose);
                        var links = crawledPage.AngleSharpHtmlDocument
                            .QuerySelectorAll($"{_domSelector} a")
                            .Where(a => a.OuterHtml.Contains(uri.LocalPath));

                        var shouldCrawl = links.Any();
                        if (!shouldCrawl)
                        {
                            _console.WriteLine($"Filtered out link that does not match selector: {uri.AbsoluteUri}", LogLevel.Debug);
                        }
                        
                        return shouldCrawl;
                    };
                }

                var startTime = DateTime.Now;
                var result = await _crawler.CrawlAsync(rootUri);
                var duration = DateTime.Now - startTime;

                // Wait for all pages to finish processing with a timeout
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try 
                {
                    while (_activePages.Count > 0 && !cts.Token.IsCancellationRequested)
                    {
                        _console.WriteLine($"Waiting for {_activePages.Count} pages to finish processing...", LogLevel.Debug);
                        
                        // Create a list of tasks with timeout
                        var tasks = _activePages.Values.Select(tcs => tcs.Task).ToList();
                        if (!tasks.Any()) break;

                        try
                        {
                            await Task.WhenAll(tasks.ToArray());
                            break;
                        }
                        catch (Exception ex)
                        {
                            _console.WriteLine($"Error while waiting for pages: {ex.Message}", LogLevel.Warning);
                            // Continue waiting for other pages
                        }

                        await Task.Delay(1000, cts.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    _console.WriteLine("Timeout while waiting for pages to complete", LogLevel.Warning);
                }
                finally
                {
                    // Clean up any remaining pages
                    foreach (var page in _activePages.Keys.ToList())
                    {
                        if (_activePages.TryRemove(page, out var pendingTcs))
                        {
                            try
                            {
                                pendingTcs.TrySetCanceled();
                            }
                            catch (InvalidOperationException)
                            {
                                // Task was already completed
                            }
                        }
                    }
                }

                // Check if we had any errors during crawling
                var error = await _crawlError.Task;
                if (error != null)
                {
                    throw error;
                }

                if (result.ErrorOccurred)
                {
                    if (result.ErrorException != null)
                    {
                        _console.WriteLine($"Crawl failed with critical error: {result.ErrorException.Message}", LogLevel.Error);
                        _console.WriteLine($"Stack trace: {result.ErrorException.StackTrace}", LogLevel.Debug);
                        throw result.ErrorException;
                    }
                    else
                    {
                        _console.WriteLine("Crawl failed with an unknown error", LogLevel.Error);
                        throw new Exception("Crawl failed with an unknown error");
                    }
                }
                else
                {
                    _console.WriteLine($"Crawl completed successfully: {_pageCount} pages processed in {duration.TotalSeconds:F2} seconds", LogLevel.Information);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _console.WriteLine($"Crawl failed with critical error: {ex.Message}", LogLevel.Error);
                _console.WriteLine($"Stack trace: {ex.StackTrace}", LogLevel.Debug);
                throw;
            }
            finally
            {
                _queue.MarkAsComplete();
                
                lock (_lock)
                {
                    if (_crawler != null)
                    {
                        _crawler.PageCrawlStarting -= Crawler_ProcessPageCrawlStarting;
                        _crawler.PageCrawlCompleted -= Crawler_ProcessPageCrawlCompleted;
                        _crawler.Dispose();
                        _crawler = null;
                    }
                }
            }
        }

        private void Crawler_ProcessPageCrawlStarting(object? sender, PageCrawlStartingArgs e)
        {
            _pageCount++;
            _console.WriteLine($"Processing page {_pageCount}: {e.PageToCrawl.Uri.AbsoluteUri}", LogLevel.Information);
        }

        private async void Crawler_ProcessPageCrawlCompleted(object? sender, PageCrawlCompletedArgs e)
        {
            var tcs = new TaskCompletionSource();
            if (!_activePages.TryAdd(e.CrawledPage.Uri, tcs))
            {
                // Page is already being processed
                return;
            }

            try
            {
                if (e.CrawledPage.HttpRequestException != null)
                {
                    _console.WriteLine($"Error crawling {e.CrawledPage.Uri.AbsoluteUri}: {e.CrawledPage.HttpRequestException.Message}", LogLevel.Warning);
                    tcs.TrySetResult();
                    return;
                }

                if (e.CrawledPage.HttpResponseMessage.StatusCode != HttpStatusCode.OK)
                {
                    _console.WriteLine($"Received non-200 status code {e.CrawledPage.HttpResponseMessage.StatusCode} for {e.CrawledPage.Uri.AbsoluteUri}", LogLevel.Warning);
                    tcs.TrySetResult();
                    return;
                }

                try
                {
                    var page = new CrawledWebPage(
                        e.CrawledPage.Uri,
                        e.CrawledPage.AngleSharpHtmlDocument?.QuerySelector("title")?.TextContent ?? string.Empty,
                        e.CrawledPage.AngleSharpHtmlDocument?.Body?.TextContent ?? string.Empty,
                        (int)e.CrawledPage.HttpResponseMessage.StatusCode,
                        null);

                    _queue.Enqueue(page);
                    tcs.TrySetResult();
                }
                catch (Exception ex)
                {
                    _console.WriteLine($"Error processing {e.CrawledPage.Uri.AbsoluteUri}: {ex.Message}", LogLevel.Error);
                    _console.WriteLine($"Stack trace: {ex.StackTrace}", LogLevel.Debug);
                    _crawlError.TrySetResult(ex);
                    tcs.TrySetException(ex);
                }
            }
            finally
            {
                _activePages.TryRemove(e.CrawledPage.Uri, out _);
            }
        }

        private static CrawlConfiguration CreateCrawlConfiguration(int maxPages, int maxDepth)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            CrawlConfiguration crawlConfig = new()
            {
                CrawlTimeoutSeconds = maxPages * 10,
                MaxConcurrentThreads = 5,
                MinCrawlDelayPerDomainMilliSeconds = 100,
                IsSslCertificateValidationEnabled = true,
                MaxPagesToCrawl = maxPages,
                MaxCrawlDepth = maxDepth,
                UserAgentString = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
            };

            return crawlConfig;
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                lock (_lock)
                {
                    if (_crawler != null)
                    {
                        _crawler.PageCrawlStarting -= Crawler_ProcessPageCrawlStarting;
                        _crawler.PageCrawlCompleted -= Crawler_ProcessPageCrawlCompleted;
                        _crawler.Dispose();
                        _crawler = null;
                    }
                }
            }
        }
    }
}
