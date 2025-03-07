namespace AzureSearchCrawler.Models
{
    public enum CrawlMode
    {
        Standard,   // Använder Abot för att crawla sidan
        Sitemap,    // Använder sitemap.xml
        Headless     // Lägg till det nya läget
    }
} 