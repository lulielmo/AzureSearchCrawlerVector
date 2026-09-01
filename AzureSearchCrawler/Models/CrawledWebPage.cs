using System;

namespace AzureSearchCrawler.Models
{
    /// <summary>
    /// Represents a crawled web page that needs to be processed and indexed.
    /// </summary>
    public class CrawledWebPage
    {
        /// <summary>
        /// The URL of the crawled page.
        /// </summary>
        public Uri Uri { get; }

        /// <summary>
        /// The title of the page.
        /// </summary>
        public string Title { get; }

        /// <summary>
        /// The content of the page.
        /// </summary>
        public string Content { get; }

        /// <summary>
        /// The HTTP status code received when crawling the page.
        /// </summary>
        public int StatusCode { get; }

        /// <summary>
        /// Any error message if the page could not be crawled.
        /// </summary>
        public string? ErrorMessage { get; }

        /// <summary>
        /// Optional CSS selector for the main content area used during text extraction.
        /// When null, the entire body is used.
        /// </summary>
        public string? ContentSelector { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="CrawledWebPage"/> class.
        /// </summary>
        /// <param name="uri">The URL of the crawled page.</param>
        /// <param name="title">The title of the page.</param>
        /// <param name="content">The content of the page.</param>
        /// <param name="statusCode">The HTTP status code.</param>
        /// <param name="errorMessage">Any error message.</param>
        /// <param name="contentSelector">Optional CSS selector for the main content area.</param>
        public CrawledWebPage(
            Uri uri,
            string title,
            string content,
            int statusCode,
            string? errorMessage = null,
            string? contentSelector = null)
        {
            Uri = uri ?? throw new ArgumentNullException(nameof(uri));
            Title = title ?? throw new ArgumentNullException(nameof(title));
            Content = content ?? throw new ArgumentNullException(nameof(content));
            StatusCode = statusCode;
            ErrorMessage = errorMessage;
            ContentSelector = contentSelector;
        }
    }
} 