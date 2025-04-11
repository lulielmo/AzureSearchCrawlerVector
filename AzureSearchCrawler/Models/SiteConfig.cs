namespace AzureSearchCrawler.Models
{
    public class SiteConfig
    {
        public required string Uri { get; set; }
        public int MaxDepth { get; set; } = 10;  // Maximum depth of link traversal from root URL
        public string? DomSelector { get; set; }  // CSS selector to filter which links to follow, e.g. "div.blog-content"
    }
}
