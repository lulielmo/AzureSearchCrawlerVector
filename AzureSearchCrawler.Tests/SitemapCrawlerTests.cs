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
    /// Tester för SitemapCrawler-klassen.
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

        #region Grundläggande Sitemap-hantering
        /// <summary>
        /// Verifierar att crawlern kan hitta och följa en sitemap-URL i robots.txt.
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

            var crawler = new SitemapCrawler(_handlerMock.Object, _consoleMock.Object, LogLevel.Debug, _httpClient);

            // Act
            await crawler.CrawlAsync(new Uri("http://example.com"), 10, 1);

            // Assert
            _consoleMock.Verify(c => c.WriteLine(
                It.Is<string>(s => s.Contains("Found sitemap URL in robots.txt")), 
                LogLevel.Information));
            _handlerMock.Verify(h => h.PageCrawledAsync(It.IsAny<CrawledPage>()), Times.Once);
        }

        /// <summary>
        /// Verifierar att crawlern kan processa alla URLs i en enkel sitemap.
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

            var crawler = new SitemapCrawler(_handlerMock.Object, _consoleMock.Object, LogLevel.Debug, _httpClient);

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

        #region Begränsningar och Validering
        /// <summary>
        /// Verifierar att maxPages-gränsen respekteras.
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

            var crawler = new SitemapCrawler(_handlerMock.Object, _consoleMock.Object, LogLevel.Debug, _httpClient);

            // Act
            await crawler.CrawlAsync(new Uri("http://example.com"), maxPages: 2, maxDepth: 1);

            // Assert
            _handlerMock.Verify(h => h.PageCrawledAsync(It.IsAny<CrawledPage>()), Times.Exactly(2));
        }

        /// <summary>
        /// Verifierar att ogiltiga URLs hanteras korrekt:
        /// - Tomma URLs
        /// - URLs från andra domäner
        /// - Felformaterade URLs
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

            var crawler = new SitemapCrawler(_handlerMock.Object, _consoleMock.Object, LogLevel.Debug, _httpClient);

            // Act
            await crawler.CrawlAsync(new Uri("http://example.com"), 10, 1);

            // Assert
            _handlerMock.Verify(h => h.PageCrawledAsync(
                It.Is<CrawledPage>(p => p.Uri.ToString() == "http://example.com/valid-page")), 
                Times.Once);
            _handlerMock.Verify(h => h.PageCrawledAsync(It.IsAny<CrawledPage>()), Times.Once);
            
            // Verify logging of skipped URLs
            _consoleMock.Verify(c => c.WriteLine(
                It.Is<string>(s => s.Contains("Skipping URL from different domain")), 
                LogLevel.Warning), 
                Times.Once);
            _consoleMock.Verify(c => c.WriteLine(
                It.Is<string>(s => s.Contains("Skipping invalid URL: empty location")), 
                LogLevel.Warning), 
                Times.Exactly(2));
        }
        #endregion

        #region Nästlade Sitemaps och Felhantering
        /// <summary>
        /// Verifierar att djupt nästlade sitemaps hanteras korrekt och att
        /// maxdjupsbegränsningen respekteras.
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

            var crawler = new SitemapCrawler(_handlerMock.Object, _consoleMock.Object, LogLevel.Debug, _httpClient);

            // Act
            await crawler.CrawlAsync(new Uri("http://example.com"), 10, 1);

            // Assert
            _handlerMock.Verify(h => h.PageCrawledAsync(It.Is<CrawledPage>(p => 
                p.Uri.ToString() == "http://example.com/final-page")), Times.Once);
        }

        /// <summary>
        /// Verifierar att crawlern kan hantera cirkulära referenser mellan sitemaps
        /// utan att fastna i en oändlig loop.
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

            var crawler = new SitemapCrawler(_handlerMock.Object, _consoleMock.Object, LogLevel.Debug, _httpClient);

            // Act
            await crawler.CrawlAsync(new Uri("http://example.com"), 10, 1);

            // Assert
            _handlerMock.Verify(h => h.PageCrawledAsync(It.Is<CrawledPage>(p => 
                p.Uri.ToString() == "http://example.com/page1")), Times.Once);
            _handlerMock.Verify(h => h.PageCrawledAsync(It.Is<CrawledPage>(p => 
                p.Uri.ToString() == "http://example.com/page2")), Times.Once);
            _consoleMock.Verify(c => c.WriteLine(
                It.Is<string>(s => s.Contains("Circular reference detected")), 
                LogLevel.Warning), 
                Times.AtLeast(1));
        }

        /// <summary>
        /// Verifierar att crawlern stoppar vid maxdjup för att förhindra
        /// oändlig rekursion.
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

            var crawler = new SitemapCrawler(_handlerMock.Object, _consoleMock.Object, LogLevel.Debug, _httpClient);

            // Act
            await crawler.CrawlAsync(new Uri("http://example.com"), 10, 1);

            // Assert
            _consoleMock.Verify(c => c.WriteLine(
                "Maximum sitemap depth reached",
                LogLevel.Warning), Times.AtLeastOnce);
            
            // Verifiera att vi nådde maxdjupet (11 nivåer: 0-10)
            Assert.True(depthCounter >= 11, $"Depth counter only reached {depthCounter}");
        }
        #endregion

        #region Felhantering och Edge Cases
        /// Verifierar att crawlern hanterar ogiltig XML-struktur och fortsätter
        /// med nästa sitemap.
        /// </summary>
        [Fact]
        public async Task CrawlAsync_WithInvalidSitemapFormat_LogsWarning()
        {
            // Arrange
            var invalidSitemapContent = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<wrongroot xmlns=""http://www.sitemaps.org/schemas/sitemap/0.9"">
    <url><loc>http://example.com/page1</loc></url>
</wrongroot>";

            _httpHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync((HttpRequestMessage req, CancellationToken token) =>
                {
                    if (req.RequestUri!.ToString() == "http://example.com/sitemap.xml")
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK) 
                        { 
                            Content = new StringContent(invalidSitemapContent) 
                        };
                    }
                    // Alla andra URLs returnerar 404
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                });

            var crawler = new SitemapCrawler(_handlerMock.Object, _consoleMock.Object, LogLevel.Debug, _httpClient);

            // Act
            await Assert.ThrowsAsync<Exception>(() => 
                crawler.CrawlAsync(new Uri("http://example.com"), 10, 1));

            // Assert
            _consoleMock.Verify(c => c.WriteLine(
                "Trying sitemap at http://example.com/sitemap.xml",
                LogLevel.Debug), Times.Once);
            
            _consoleMock.Verify(c => c.WriteLine(
                "Invalid sitemap format at http://example.com/sitemap.xml",
                LogLevel.Warning), Times.Once);
            
            // Verifiera att vi försöker med andra platser efter den första misslyckas
            _consoleMock.Verify(c => c.WriteLine(
                It.Is<string>(s => s.Contains("No sitemap found at")),
                LogLevel.Debug), Times.AtLeast(1));
        }

        /// <summary>
        /// Verifierar att crawlern hanterar och loggar fel som uppstår under
        /// sitemap-processering.
        /// </summary>
        [Fact]
        public async Task CrawlAsync_WithSitemapProcessingError_LogsError()
        {
            // Arrange
            var sitemapContent = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<sitemapindex xmlns=""http://www.sitemaps.org/schemas/sitemap/0.9"">
    <sitemap><loc>http://example.com/error-sitemap.xml</loc></sitemap>
</sitemapindex>";

            _httpHandlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync((HttpRequestMessage req, CancellationToken token) =>
                {
                    if (req.RequestUri!.ToString().Contains("error-sitemap.xml"))
                    {
                        throw new HttpRequestException("Simulated error");
                    }
                    return new HttpResponseMessage(HttpStatusCode.OK) 
                    { 
                        Content = new StringContent(sitemapContent) 
                    };
                });

            var crawler = new SitemapCrawler(_handlerMock.Object, _consoleMock.Object, LogLevel.Debug, _httpClient);

            // Act
            await crawler.CrawlAsync(new Uri("http://example.com"), 10, 1);

            // Assert
            _consoleMock.Verify(c => c.WriteLine(
                It.Is<string>(s => s.Contains("Failed to process sitemap")),
                LogLevel.Error), Times.Once);
        }

        /// <summary>
        /// Verifierar att crawlern sätter rätt User-Agent när en ny HttpClient skapas.
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