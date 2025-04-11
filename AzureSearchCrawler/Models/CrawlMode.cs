namespace AzureSearchCrawler.Models
{
    public enum CrawlMode
    {
        Standard,   // Traditional web crawling with link following, best for static sites
        Sitemap,    // Follows sitemap.xml, ideal for complete site coverage
        Headless    // Browser-based crawling for JavaScript-rendered content and SPAs
    }
} 