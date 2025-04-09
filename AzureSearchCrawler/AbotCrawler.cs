using Abot2.Crawler;
using Abot2.Poco;
using AzureSearchCrawler.Interfaces;
using AzureSearchCrawler.Models;
using System.Net;

namespace AzureSearchCrawler
{
    /// <summary>
    ///  A convenience wrapper for an Abot crawler with a reasonable default configuration and console logging.
    ///  The actual action to be performed on the crawled pages is passed in as a ICrawledPageProcessor.
    /// </summary>
    public class AbotCrawler : IWebCrawlingStrategy
    {
        private int _pageCount;

        private readonly ICrawledPageProcessor _processor;
        private readonly Func<CrawlConfiguration, IWebCrawler> _webCrawlerFactory;
        private readonly IConsole _console;
        private string? _domSelector;

        public AbotCrawler(ICrawledPageProcessor processor, IConsole console, string? domSelector = null)
            : this(processor, config => new PoliteWebCrawler(config), console, domSelector)
        {
        }

        public AbotCrawler(ICrawledPageProcessor processor, Func<CrawlConfiguration, IWebCrawler> crawlerFactory, IConsole console, string? domSelector = null)
        {
            _processor = processor ?? throw new ArgumentNullException(nameof(processor));
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
            _pageCount = 0;

            if (maxPages <= 0)
                throw new ArgumentException("maxPages must be greater than 0", nameof(maxPages));

            if (maxDepth <= 0)
                throw new ArgumentException("maxDepth must be greater than 0", nameof(maxDepth));

            if (domSelector != null)
            {
                _domSelector = domSelector;
            }

            var config = CreateCrawlConfiguration(maxPages, maxDepth);
            var crawler = _webCrawlerFactory(config);

            crawler.PageCrawlStarting += Crawler_ProcessPageCrawlStarting;
            crawler.PageCrawlCompleted += Crawler_ProcessPageCrawlCompleted;
            
            _console.WriteLine($"Starting web crawl of {rootUri.AbsoluteUri}", LogLevel.Information);
            _console.WriteLine($"Crawl configuration: Max pages={maxPages}, Max depth={maxDepth}, Concurrent threads={config.MaxConcurrentThreads}", LogLevel.Information);
            _console.WriteLine($"Performance settings: Timeout={config.CrawlTimeoutSeconds}s, Delay between requests={config.MinCrawlDelayPerDomainMilliSeconds}ms", LogLevel.Debug);
            _console.WriteLine($"Request configuration: User-Agent='{config.UserAgentString}'", LogLevel.Debug);
            
            if (_domSelector != null)
            {
                _console.WriteLine($"Using DOM selector filter: {_domSelector}", LogLevel.Information);
                crawler.ShouldScheduleLinkDecisionMaker = (uri, crawledPage, crawlContext) =>
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

            try
            {
                var startTime = DateTime.Now;
                var result = await crawler.CrawlAsync(rootUri);
                var duration = DateTime.Now - startTime;

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
                await _processor.CrawlFinishedAsync();
            }
        }

        private void Crawler_ProcessPageCrawlStarting(object? sender, PageCrawlStartingArgs e)
        {
            _pageCount++;
            _console.WriteLine($"Processing page {_pageCount}: {e.PageToCrawl.Uri.AbsoluteUri}", LogLevel.Information);
        }

        private async void Crawler_ProcessPageCrawlCompleted(object? sender, PageCrawlCompletedArgs e)
        {
            if (e.CrawledPage.HttpRequestException != null)
            {
                _console.WriteLine($"Error crawling {e.CrawledPage.Uri.AbsoluteUri}: {e.CrawledPage.HttpRequestException.Message}", LogLevel.Warning);
                return;
            }

            if (e.CrawledPage.HttpResponseMessage.StatusCode != HttpStatusCode.OK)
            {
                _console.WriteLine($"Received non-200 status code {e.CrawledPage.HttpResponseMessage.StatusCode} for {e.CrawledPage.Uri.AbsoluteUri}", LogLevel.Warning);
                return;
            }

            try
            {
                await _processor.PageCrawledAsync(e.CrawledPage);
            }
            catch (Exception ex)
            {
                _console.WriteLine($"Error processing {e.CrawledPage.Uri.AbsoluteUri}: {ex.Message}", LogLevel.Error);
                _console.WriteLine($"Stack trace: {ex.StackTrace}", LogLevel.Debug);
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
    }
}
