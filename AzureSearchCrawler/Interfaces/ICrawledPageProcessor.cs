using Abot2.Poco;

namespace AzureSearchCrawler.Interfaces
{
    /// <summary>
    /// Defines a processor for crawled web pages.
    /// Implementations determine what happens with pages after they are crawled
    /// (e.g., indexing in search engine, saving to database, etc.).
    /// </summary>
    public interface ICrawledPageProcessor
    {
        /// <summary>
        /// Processes a single crawled page.
        /// </summary>
        /// <param name="page">The crawled page to process</param>
        Task PageCrawledAsync(CrawledPage page);

        /// <summary>
        /// Called when the crawling process is complete.
        /// Useful for implementing cleanup or final processing steps.
        /// </summary>
        Task CrawlFinishedAsync();
    }
} 