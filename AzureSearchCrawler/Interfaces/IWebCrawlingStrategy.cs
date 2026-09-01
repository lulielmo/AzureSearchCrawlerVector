namespace AzureSearchCrawler.Interfaces
{
    /// <summary>
    /// Defines a strategy for crawling web pages.
    /// Different implementations can use different mechanisms (Abot, Playwright, Sitemap, etc.)
    /// to discover and crawl web pages.
    /// </summary>
    public interface IWebCrawlingStrategy
    {
        /// <summary>
        /// Crawls web pages starting from the specified root URI.
        /// </summary>
        /// <param name="rootUri">The starting point for the crawl</param>
        /// <param name="maxPages">Maximum number of pages to crawl</param>
        /// <param name="maxDepth">Maximum depth of links to follow</param>
        /// <param name="domSelector">Optional CSS selector to filter which links to follow</param>
        /// <param name="contentSelector">Optional CSS selector for the main content area to extract</param>
        Task CrawlAsync(
            Uri rootUri,
            int maxPages,
            int maxDepth,
            string? domSelector = null,
            string? contentSelector = null);
    }
} 