using System.Threading.Tasks;
using System;

namespace AzureSearchCrawler.Interfaces
{
    public interface ICrawler
    {
        Task CrawlAsync(Uri rootUri, int maxPages, int maxDepth, string? domSelector = null);
    }
}