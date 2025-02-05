namespace AzureSearchCrawler.Models
{
    public class SiteConfig
    {
        public required string Uri { get; set; }
        public int MaxDepth { get; set; } = 10; // Default value
    }
}
