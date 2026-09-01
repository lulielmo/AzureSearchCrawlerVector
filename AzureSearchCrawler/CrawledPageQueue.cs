using System.Collections.Concurrent;
using AzureSearchCrawler.Models;

namespace AzureSearchCrawler
{
    /// <summary>
    /// A thread-safe queue for storing crawled web pages.
    /// </summary>
    public class CrawledPageQueue
    {
        private readonly ConcurrentQueue<CrawledWebPage> _queue = new();
        private bool _isComplete;

        /// <summary>
        /// Gets a value indicating whether the queue is empty and marked as complete.
        /// </summary>
        public bool IsEmptyAndComplete => _queue.IsEmpty && _isComplete;

        /// <summary>
        /// Gets the number of items in the queue.
        /// </summary>
        public int Count => _queue.Count;

        /// <summary>
        /// Adds a crawled page to the queue.
        /// </summary>
        /// <param name="page">The page to add.</param>
        /// <exception cref="InvalidOperationException">Thrown if the queue is marked as complete.</exception>
        public void Enqueue(CrawledWebPage page)
        {
            if (_isComplete)
            {
                throw new InvalidOperationException("Cannot enqueue items to a completed queue.");
            }

            _queue.Enqueue(page);
        }

        /// <summary>
        /// Attempts to remove and return the page at the beginning of the queue.
        /// </summary>
        /// <param name="page">When this method returns, if the operation was successful, contains the object removed.</param>
        /// <returns>true if an element was removed and returned from the beginning of the queue successfully; otherwise, false.</returns>
        public bool TryDequeue(out CrawledWebPage? page)
        {
            return _queue.TryDequeue(out page);
        }

        /// <summary>
        /// Marks the queue as complete, indicating that no more items will be added.
        /// </summary>
        public void MarkAsComplete()
        {
            _isComplete = true;
        }
    }
} 