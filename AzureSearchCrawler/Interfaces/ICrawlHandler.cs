using Abot2.Poco;

namespace AzureSearchCrawler.Interfaces
{
    /// <summary>
    /// A generic callback handler to be passed into a Crawler.
    /// </summary>
    public interface ICrawlHandler
    {
        Task PageCrawledAsync(CrawledPage page);

        Task CrawlFinishedAsync();
    }
} 