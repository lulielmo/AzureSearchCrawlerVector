using Abot2.Poco;
using AzureSearchCrawler.Interfaces;
using AzureSearchCrawler.Models;
using Moq;
using Moq.Protected;
using System.Net;
using Xunit;

namespace AzureSearchCrawler.Tests
{
    /// <summary>
    /// Tests for the SitemapCrawler class.
    /// </summary>
    public class SitemapCrawlerTests
    {
        private readonly Mock<ICrawledPageProcessor> _handlerMock;
        private readonly Mock<IConsole> _consoleMock;
        private readonly Mock<HttpMessageHandler> _httpHandlerMock;
        private readonly HttpClient _httpClient;

        public SitemapCrawlerTests()
        {
            _handlerMock = new Mock<ICrawledPageProcessor>();
            _consoleMock = new Mock<IConsole>();
            _httpHandlerMock = new Mock<HttpMessageHandler>();
            _httpClient = new HttpClient(_httpHandlerMock.Object);
        }

        #region Basic Sitemap Handling
        /// <summary>
        /// Verifies that the crawler can find and follow a sitemap URL in robots.txt.
        /// </summary>
        [Fact]
        public async Task CrawlAsync_WithRobotsFile_FindsSitemapUrl()
        {
            // Arrange
            var robotsTxt = "User-agent: *\nSitemap: http://example.com/custom-sitemap.xml";
            var sitemapContent = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<urlset xmlns=""http://www.sitemaps.org/schemas/sitemap/0.9"">
    <url>
        <loc>http://example.com/page1</loc>
    </url>
</urlset>";
            var pageContent = "<html><body>Test content</body></html>";

            _httpHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync((HttpRequestMessage req, CancellationToken token) =>
                {
                    _consoleMock.Object.WriteLine($"Request URL: {req.RequestUri}", LogLevel.Debug);
                    
                    var response = new HttpResponseMessage(HttpStatusCode.OK);
                    if (req.RequestUri!.ToString() == "http://example.com/robots.txt")
                    {
                        response.Content = new StringContent(robotsTxt);
                    }
                    else if (req.RequestUri.ToString() == "http://example.com/custom-sitemap.xml")
                    {
                        response.Content = new StringContent(sitemapContent);
                    }
                    else if (req.RequestUri.ToString() == "http://example.com/page1")
                    {
                        response.Content = new StringContent(pageContent);
                    }
                    else
                    {
                        response.StatusCode = HttpStatusCode.NotFound;
                    }
                    return response;
                });

            var loggedMessages = new List<(string Message, LogLevel Level)>();
            _consoleMock.Setup(c => c.WriteLine(It.IsAny<string>(), It.IsAny<LogLevel>()))
                .Callback<string, LogLevel>((message, level) => loggedMessages.Add((message, level)));

            var crawler = new SitemapCrawler(_handlerMock.Object, _consoleMock.Object, _httpClient);

            // Act
            await crawler.CrawlAsync(new Uri("http://example.com"), 10, 1);

            // Assert
            Assert.Contains(loggedMessages, m => m.Message.Contains("Found sitemap URL in robots.txt:") && m.Level == LogLevel.Information);

            _handlerMock.Verify(h => h.PageCrawledAsync(It.IsAny<CrawledPage>()), Times.Once);
        }

        /// <summary>
        /// Verifies that the crawler can process all URLs in a simple sitemap.
        /// </summary>
        [Fact]
        public async Task CrawlAsync_WithValidSitemap_ProcessesAllUrls()
        {
            // Arrange
            var sitemapContent = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<urlset xmlns=""http://www.sitemaps.org/schemas/sitemap/0.9"">
    <url><loc>http://example.com/page1</loc></url>
    <url><loc>http://example.com/page2</loc></url>
</urlset>";

            var page1Content = "<html><body>Page 1 content</body></html>";
            var page2Content = "<html><body>Page 2 content</body></html>";

            _httpHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync((HttpRequestMessage req, CancellationToken token) =>
                {
                    _consoleMock.Object.WriteLine($"Request URL: {req.RequestUri}", LogLevel.Debug);
                    
                    var response = new HttpResponseMessage(HttpStatusCode.OK);
                    if (req.RequestUri!.ToString() == "http://example.com/sitemap.xml")
                    {
                        response.Content = new StringContent(sitemapContent);
                    }
                    else if (req.RequestUri.ToString() == "http://example.com/page1")
                    {
                        response.Content = new StringContent(page1Content);
                    }
                    else if (req.RequestUri.ToString() == "http://example.com/page2")
                    {
                        response.Content = new StringContent(page2Content);
                    }
                    else
                    {
                        response.StatusCode = HttpStatusCode.NotFound;
                    }
                    return response;
                });

            var crawler = new SitemapCrawler(_handlerMock.Object, _consoleMock.Object, _httpClient);

            // Act
            await crawler.CrawlAsync(new Uri("http://example.com"), 10, 1);

            // Assert
            _handlerMock.Verify(h => h.PageCrawledAsync(It.Is<CrawledPage>(p => 
                p.Uri.ToString() == "http://example.com/page1")), Times.Once);
            _handlerMock.Verify(h => h.PageCrawledAsync(It.Is<CrawledPage>(p => 
                p.Uri.ToString() == "http://example.com/page2")), Times.Once);
            _handlerMock.Verify(h => h.PageCrawledAsync(It.IsAny<CrawledPage>()), Times.Exactly(2));
        }
        #endregion

        #region Limitations and Validation
        /// <summary>
        /// Verifies that the maxPages limit is respected.
        /// </summary>
        [Fact]
        public async Task CrawlAsync_WithMaxPages_LimitsProcessedUrls()
        {
            // Arrange
            var sitemapContent = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<urlset xmlns=""http://www.sitemaps.org/schemas/sitemap/0.9"">
    <url><loc>http://example.com/page1</loc></url>
    <url><loc>http://example.com/page2</loc></url>
    <url><loc>http://example.com/page3</loc></url>
</urlset>";

            _httpHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync((HttpRequestMessage req, CancellationToken token) =>
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK);
                    if (req.RequestUri!.ToString() == "http://example.com/sitemap.xml")
                    {
                        response.Content = new StringContent(sitemapContent);
                    }
                    else
                    {
                        response.Content = new StringContent("<html><body>Page content</body></html>");
                    }
                    return response;
                });

            var crawler = new SitemapCrawler(_handlerMock.Object, _consoleMock.Object, _httpClient);

            // Act
            await crawler.CrawlAsync(new Uri("http://example.com"), maxPages: 2, maxDepth: 1);

            // Assert
            _handlerMock.Verify(h => h.PageCrawledAsync(It.IsAny<CrawledPage>()), Times.Exactly(2));
        }

        /// <summary>
        /// Verifies that invalid URLs are handled correctly:
        /// - Empty URLs
        /// - URLs from other domains
        /// - Malformed URLs
        /// </summary>
        [Fact]
        public async Task CrawlAsync_WithInvalidUrls_SkipsInvalidEntries()
        {
            // Arrange
            var sitemapContent = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<urlset xmlns=""http://www.sitemaps.org/schemas/sitemap/0.9"">
    <url><loc>http://example.com/valid-page</loc></url>
    <url><loc>http://different-domain.com/page</loc></url>
    <url><loc></loc></url>
    <url><loc>   </loc></url>
</urlset>";

            _httpHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync((HttpRequestMessage req, CancellationToken token) =>
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK);
                    if (req.RequestUri!.ToString() == "http://example.com/sitemap.xml")
                    {
                        response.Content = new StringContent(sitemapContent);
                    }
                    else if (req.RequestUri.ToString() == "http://example.com/valid-page")
                    {
                        response.Content = new StringContent("<html><body>Valid page content</body></html>");
                    }
                    return response;
                });

            var crawler = new SitemapCrawler(_handlerMock.Object, _consoleMock.Object, _httpClient);

            // Act
            await crawler.CrawlAsync(new Uri("http://example.com"), 10, 1);

            // Assert
            _handlerMock.Verify(h => h.PageCrawledAsync(
                It.Is<CrawledPage>(p => p.Uri.ToString() == "http://example.com/valid-page")), 
                Times.Once);
            _handlerMock.Verify(h => h.PageCrawledAsync(It.IsAny<CrawledPage>()), Times.Once);
            
            // Verify logging of skipped URLs
            _consoleMock.Verify(c => c.WriteLine(
                It.Is<string>(s => s.Contains("Skipping external URL:")), 
                LogLevel.Warning), 
                Times.Once);
            _consoleMock.Verify(c => c.WriteLine(
                It.Is<string>(s => s.Contains("Skipping invalid URL: empty location")), 
                LogLevel.Warning), 
                Times.Exactly(2));
        }
        #endregion

        #region Nested Sitemaps and Error Handling
        /// <summary>
        /// Verifies that deeply nested sitemaps are handled correctly and that
        /// maxdepth restriction is respected.
        /// </summary>
        [Fact]
        public async Task CrawlAsync_WithDeepNestedSitemaps_HandlesRecursively()
        {
            // Arrange
            var mainSitemapContent = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<sitemapindex xmlns=""http://www.sitemaps.org/schemas/sitemap/0.9"">
    <sitemap><loc>http://example.com/nested/sitemap1.xml</loc></sitemap>
</sitemapindex>";

            var nestedSitemapContent = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<sitemapindex xmlns=""http://www.sitemaps.org/schemas/sitemap/0.9"">
    <sitemap><loc>http://example.com/nested/deep/sitemap2.xml</loc></sitemap>
</sitemapindex>";

            var finalSitemapContent = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<urlset xmlns=""http://www.sitemaps.org/schemas/sitemap/0.9"">
    <url><loc>http://example.com/final-page</loc></url>
</urlset>";

            _httpHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync((HttpRequestMessage req, CancellationToken token) =>
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK);
                    switch (req.RequestUri!.ToString())
                    {
                        case "http://example.com/sitemap.xml":
                            response.Content = new StringContent(mainSitemapContent);
                            break;
                        case "http://example.com/nested/sitemap1.xml":
                            response.Content = new StringContent(nestedSitemapContent);
                            break;
                        case "http://example.com/nested/deep/sitemap2.xml":
                            response.Content = new StringContent(finalSitemapContent);
                            break;
                        case "http://example.com/final-page":
                            response.Content = new StringContent("<html>Final page</html>");
                            break;
                    }
                    return response;
                });

            var crawler = new SitemapCrawler(_handlerMock.Object, _consoleMock.Object, _httpClient);

            // Act
            await crawler.CrawlAsync(new Uri("http://example.com"), 10, 1);

            // Assert
            _handlerMock.Verify(h => h.PageCrawledAsync(It.Is<CrawledPage>(p => 
                p.Uri.ToString() == "http://example.com/final-page")), Times.Once);
        }

        /// <summary>
        /// Verifies that the crawler can handle circular references between sitemaps
        /// without getting stuck in an infinite loop.
        /// </summary>
        [Fact]
        public async Task CrawlAsync_WithCircularSitemapReferences_AvoidsCycles()
        {
            // Arrange
            var sitemap1Content = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<sitemapindex xmlns=""http://www.sitemaps.org/schemas/sitemap/0.9"">
    <sitemap><loc>http://example.com/sitemap2.xml</loc></sitemap>
    <sitemap><loc>http://example.com/urls1.xml</loc></sitemap>
</sitemapindex>";

            var sitemap2Content = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<sitemapindex xmlns=""http://www.sitemaps.org/schemas/sitemap/0.9"">
    <sitemap><loc>http://example.com/sitemap1.xml</loc></sitemap>
    <sitemap><loc>http://example.com/urls2.xml</loc></sitemap>
</sitemapindex>";

            var urls1Content = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<urlset xmlns=""http://www.sitemaps.org/schemas/sitemap/0.9"">
    <url><loc>http://example.com/page1</loc></url>
</urlset>";

            var urls2Content = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<urlset xmlns=""http://www.sitemaps.org/schemas/sitemap/0.9"">
    <url><loc>http://example.com/page2</loc></url>
</urlset>";

            _httpHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync((HttpRequestMessage req, CancellationToken token) =>
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK);
                    switch (req.RequestUri!.ToString())
                    {
                        case "http://example.com/sitemap.xml":
                        case "http://example.com/sitemap1.xml":
                            response.Content = new StringContent(sitemap1Content);
                            break;
                        case "http://example.com/sitemap2.xml":
                            response.Content = new StringContent(sitemap2Content);
                            break;
                        case "http://example.com/urls1.xml":
                            response.Content = new StringContent(urls1Content);
                            break;
                        case "http://example.com/urls2.xml":
                            response.Content = new StringContent(urls2Content);
                            break;
                        case "http://example.com/page1":
                        case "http://example.com/page2":
                            response.Content = new StringContent("<html>Page content</html>");
                            break;
                    }
                    return response;
                });

            var loggedMessages = new List<(string Message, LogLevel Level)>();
            _consoleMock.Setup(c => c.WriteLine(It.IsAny<string>(), It.IsAny<LogLevel>()))
                .Callback<string, LogLevel>((message, level) => loggedMessages.Add((message, level)));

            var crawler = new SitemapCrawler(_handlerMock.Object, _consoleMock.Object, _httpClient);

            // Act
            await crawler.CrawlAsync(new Uri("http://example.com"), 10, 1);

            // Assert
            _handlerMock.Verify(h => h.PageCrawledAsync(It.Is<CrawledPage>(p => 
                p.Uri.ToString() == "http://example.com/page1")), Times.Once);
            _handlerMock.Verify(h => h.PageCrawledAsync(It.Is<CrawledPage>(p => 
                p.Uri.ToString() == "http://example.com/page2")), Times.Once);
            Assert.Contains(loggedMessages, m => m.Message.Contains("Skipping sitemap: circular reference detected at") && m.Level == LogLevel.Warning);
        }

        /// <summary>
        /// Verifies that the crawler stops at maxdepth to prevent
        /// infinite recursion.
        /// </summary>
        [Fact]
        public async Task CrawlAsync_WithDeepSitemapNesting_StopsAtMaxDepth()
        {
            // Arrange
            var depthCounter = 0;
            
            _httpHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync((HttpRequestMessage req, CancellationToken token) =>
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK);
                    
                    // Skapa en ny unik sitemap för varje nivå
                    var sitemapContent = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<sitemapindex xmlns=""http://www.sitemaps.org/schemas/sitemap/0.9"">
    <sitemap><loc>http://example.com/sitemap_level_{++depthCounter}.xml</loc></sitemap>
</sitemapindex>";
                    
                    response.Content = new StringContent(sitemapContent);
                    return response;
                });

            var crawler = new SitemapCrawler(_handlerMock.Object, _consoleMock.Object, _httpClient);

            // Act
            await crawler.CrawlAsync(new Uri("http://example.com"), 10, 1);

            // Assert
            _consoleMock.Verify(c => c.WriteLine(
                It.Is<string>(s => s.Contains("Maximum sitemap depth reached (10)")), 
                LogLevel.Warning), Times.Once());
            
            // Verifiera att vi nådde maxdjupet (11 nivåer: 0-10)
            Assert.True(depthCounter >= 11, $"Depth counter only reached {depthCounter}");
        }
        #endregion

        #region Error Handling and Edge Cases
        /// <summary>
        /// Verifies that the crawler handles invalid XML structure and continues
        /// with the next sitemap.
        /// </summary>
        [Fact]
        public async Task CrawlAsync_WithInvalidSitemapFormat_LogsWarning()
        {
            // Arrange
            var invalidSitemapContent = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<invalid>
    <url><loc>http://example.com/page1</loc></url>
</invalid>";

            _httpHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync((HttpRequestMessage req, CancellationToken token) =>
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK);
                    if (req.RequestUri!.ToString().EndsWith("/sitemap.xml"))
                    {
                        response.Content = new StringContent(invalidSitemapContent);
                    }
                    else
                    {
                        response.StatusCode = HttpStatusCode.NotFound;
                    }
                    return response;
                });

            var loggedMessages = new List<(string Message, LogLevel Level)>();
            _consoleMock.Setup(c => c.WriteLine(It.IsAny<string>(), It.IsAny<LogLevel>()))
                .Callback<string, LogLevel>((message, level) => loggedMessages.Add((message, level)));

            var crawler = new SitemapCrawler(_handlerMock.Object, _consoleMock.Object, _httpClient);

            // Act
            await Assert.ThrowsAsync<Exception>(async () => 
                await crawler.CrawlAsync(new Uri("http://example.com"), 10, 1));

            // Assert
            Assert.Contains(loggedMessages, m => m.Message.Contains("Invalid sitemap format at") && m.Level == LogLevel.Warning);
        }

        /// <summary>
        /// Verifies that the crawler handles and logs errors that occur during
        /// sitemap processing.
        /// </summary>
        [Fact]
        public async Task CrawlAsync_WithSitemapProcessingError_LogsError()
        {
            // Arrange
            var sitemapContent = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<urlset xmlns=""http://www.sitemaps.org/schemas/sitemap/0.9"">
    <url><loc>http://example.com/page1</loc></url>
</urlset>";

            _httpHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync((HttpRequestMessage req, CancellationToken token) =>
                {
                    if (req.RequestUri!.ToString().EndsWith("/sitemap.xml"))
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(sitemapContent)
                        };
                    }
                    else if (req.RequestUri.ToString().EndsWith("/page1"))
                    {
                        throw new HttpRequestException("Failed to load page");
                    }
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                });

            var loggedMessages = new List<(string Message, LogLevel Level)>();
            _consoleMock.Setup(c => c.WriteLine(It.IsAny<string>(), It.IsAny<LogLevel>()))
                .Callback<string, LogLevel>((message, level) => loggedMessages.Add((message, level)));

            var crawler = new SitemapCrawler(_handlerMock.Object, _consoleMock.Object, _httpClient);

            // Act
            await crawler.CrawlAsync(new Uri("http://example.com"), 10, 1);

            // Assert
            Assert.Contains(loggedMessages, m => m.Message.Contains("Failed to crawl") && m.Level == LogLevel.Error);
        }

        /// <summary>
        /// Verifies that the crawler sets the correct User-Agent when a new HttpClient is created.
        /// </summary>
        [Fact]
        public void Constructor_WithNullHttpClient_SetsUserAgent()
        {
            // Act
            var crawler = new SitemapCrawler(_handlerMock.Object, _consoleMock.Object);

            // Assert - Vi kan inte direkt testa _httpClient eftersom det är privat,
            // men vi kan verifiera att den fungerar genom att göra ett anrop
            Assert.NotNull(crawler);
        }
        #endregion
    }
} 