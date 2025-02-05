using System.Threading.Tasks;
using System;

namespace AzureSearchCrawler.Interfaces
{
    public interface ICrawler
    {
        Task CrawlAsync(Uri uri, int maxPages, int maxDepth);
    }
}