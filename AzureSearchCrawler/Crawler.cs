using Abot2.Crawler;
using Abot2.Poco;
using AzureSearchCrawler.Interfaces;
using System;
using System.Net;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
using AngleSharp.Dom;

namespace AzureSearchCrawler
{
    /// <summary>
    ///  A convenience wrapper for an Abot crawler with a reasonable default configuration and console logging.
    ///  The actual action to be performed on the crawled pages is passed in as a CrawlHandler.
    /// </summary>
    public class Crawler : ICrawler
    {
        private static int PageCount = 0;

        private readonly CrawlHandler _handler;
        private readonly Func<CrawlConfiguration, IWebCrawler> _crawlerFactory;
        private readonly IConsole _console;

        public Crawler(CrawlHandler handler, IConsole console)
            : this(handler, config => new PoliteWebCrawler(config), console)
        {
        }

        public Crawler(CrawlHandler handler, Func<CrawlConfiguration, IWebCrawler> crawlerFactory, IConsole console)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            _crawlerFactory = crawlerFactory ?? throw new ArgumentNullException(nameof(crawlerFactory));
            _console = console ?? throw new ArgumentNullException(nameof(console));
        }

        public async Task CrawlAsync(Uri rootUri, int maxPages, int maxDepth)
        {
            PageCount = 0;

            if (maxPages <= 0)
                throw new ArgumentException("maxPages must be greater than 0", nameof(maxPages));

            if (maxDepth <= 0)
                throw new ArgumentException("maxDepth must be greater than 0", nameof(maxDepth));

            var config = CreateCrawlConfiguration(maxPages, maxDepth);
            IWebCrawler crawler = _crawlerFactory(config);

            crawler.PageCrawlStarting += (sender, args) => crawler_ProcessPageCrawlStarting(sender!, args);
            crawler.PageCrawlCompleted += (sender, args) => crawler_ProcessPageCrawlCompleted(sender!, args);
            
            // Lägg till händelsehanterare för länkfiltrering
            var indexer = _handler as AzureSearchIndexer;
            if (indexer?.DomSelector != null)
            {
                crawler.ShouldScheduleLinkDecisionMaker = (uri, crawledPage, crawlContext) =>
                {
                    if (crawledPage.AngleSharpHtmlDocument == null)
                        return true;

                    _console.WriteLine($"Checking {uri.AbsoluteUri} against selector '{indexer.DomSelector}'");
                    var links = crawledPage.AngleSharpHtmlDocument
                        .QuerySelectorAll($"{indexer.DomSelector} a")
                        .Where(a => a.OuterHtml.Contains(uri.LocalPath)); // Kan tyvärr inte nyttja href (knasigt värde), eller PathName (oåtkomlig)

                    var shouldCrawl = links.Any();
                    if (!shouldCrawl)
                    {
                        _console.WriteLine($"Skipping {uri.AbsoluteUri} - not found within {indexer.DomSelector}");
                    }
                    
                    return shouldCrawl;
                };
            }

            try
            {
                _console.WriteLine($"Starting crawl of {rootUri.AbsoluteUri} (max {maxPages} pages, depth {maxDepth})");
                var result = await crawler.CrawlAsync(rootUri);

                var status = result.ErrorOccurred ? "with error" : "without error";
                _console.WriteLine($"Crawl of {rootUri.AbsoluteUri} ({PageCount} pages) completed {status}");

                if (result.ErrorOccurred && result.ErrorException != null)
                {
                    _console.WriteError($"Error: {result.ErrorException.Message}");
                }

                await _handler.CrawlFinishedAsync();
            }
            finally
            {
                crawler.PageCrawlStarting -= crawler_ProcessPageCrawlStarting;
                crawler.PageCrawlCompleted -= crawler_ProcessPageCrawlCompleted;
            }
        }

        private void crawler_ProcessPageCrawlStarting(object? sender, PageCrawlStartingArgs args)
        {
            ArgumentNullException.ThrowIfNull(args);
            Interlocked.Increment(ref PageCount);

            PageToCrawl pageToCrawl = args.PageToCrawl;
            var parentUri = pageToCrawl.ParentUri?.AbsoluteUri ?? "root";
            LogMessage($"{pageToCrawl.Uri.AbsoluteUri}  found on  {parentUri}, Depth: {pageToCrawl.CrawlDepth}");
        }

        private async void crawler_ProcessPageCrawlCompleted(object? sender, PageCrawlCompletedArgs args)
        {
            ArgumentNullException.ThrowIfNull(args);
            CrawledPage crawledPage = args.CrawledPage;
            string uri = crawledPage.Uri.AbsoluteUri;

            if (crawledPage.HttpRequestException != null || crawledPage.HttpResponseMessage?.StatusCode != HttpStatusCode.OK)
            {
                LogMessage($"Crawl of page failed {uri}: exception '{crawledPage.HttpRequestException?.Message}', response status {crawledPage.HttpResponseMessage?.StatusCode}");
                return;
            }

            if (string.IsNullOrEmpty(crawledPage.Content.Text))
            {
                LogMessage($"Page had no content {uri}");
                return;
            }

            await _handler.PageCrawledAsync(crawledPage);
        }

        private CrawlConfiguration CreateCrawlConfiguration(int maxPages, int maxDepth)
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

        private void LogMessage(string message)
        {
            _console.WriteLine(message);
        }
    }
}
