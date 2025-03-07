using Abot2.Poco;
using AzureSearchCrawler.Models;
using AzureSearchCrawler.Interfaces;
using Microsoft.Playwright;
using Moq;
using Xunit;

namespace AzureSearchCrawler.Tests
{
    public class HeadlessBrowserCrawlerTests : IDisposable
    {
        private readonly Mock<ICrawledPageProcessor> _handlerMock;
        private readonly Mock<IPlaywright> _playwrightMock;
        private readonly Mock<IBrowser> _browserMock;
        private readonly Mock<IBrowserContext> _contextMock;
        private readonly Mock<IPage> _pageMock;
        private readonly Mock<IResponse> _responseMock;
        private readonly TestConsole _console;
        private readonly HeadlessBrowserCrawler _crawler;

        public HeadlessBrowserCrawlerTests()
        {
            _handlerMock = new Mock<ICrawledPageProcessor>();
            _playwrightMock = new Mock<IPlaywright>();
            _browserMock = new Mock<IBrowser>();
            _contextMock = new Mock<IBrowserContext>();
            _pageMock = new Mock<IPage>();
            _responseMock = new Mock<IResponse>();
            _console = new TestConsole();

            // Sätt upp grundläggande mock-beteende
            var browserTypeMock = new Mock<IBrowserType>();
            browserTypeMock.Setup(b => b.LaunchAsync(It.IsAny<BrowserTypeLaunchOptions>()))
                .ReturnsAsync(_browserMock.Object);
            _playwrightMock.Setup(p => p.Chromium).Returns(browserTypeMock.Object);

            _browserMock.Setup(b => b.NewContextAsync(It.IsAny<BrowserNewContextOptions>()))
                .ReturnsAsync(_contextMock.Object);
            _contextMock.Setup(c => c.NewPageAsync()).ReturnsAsync(_pageMock.Object);
            _pageMock.Setup(p => p.Context).Returns(_contextMock.Object);
            _pageMock.Setup(p => p.GotoAsync(It.IsAny<string>(), It.IsAny<PageGotoOptions>()))
                .Callback<string, PageGotoOptions>((url, _) => Console.WriteLine($"GotoAsync called with URL: {url}"))
                .ReturnsAsync(_responseMock.Object);
            _responseMock.Setup(r => r.Ok).Returns(true);
            _responseMock.Setup(r => r.Status).Returns(200);

            _crawler = new HeadlessBrowserCrawler(_handlerMock.Object, _console, _playwrightMock.Object);
        }

        private bool _disposed = false;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Free any other managed objects here.
                    _console.Dispose();
                    _crawler.Dispose();
                }

                // Free any unmanaged objects here.
                _disposed = true;
            }
        }

        ~HeadlessBrowserCrawlerTests()
        {
            Dispose(false);
        }

        [Fact]
        public async Task CrawlAsync_WithInvalidParameters_ThrowsArgumentException()
        {
            // Arrange
            var uri = new Uri("http://example.com");

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => _crawler.CrawlAsync(uri, 0, 1));
            await Assert.ThrowsAsync<ArgumentException>(() => _crawler.CrawlAsync(uri, 1, 0));
            await Assert.ThrowsAsync<ArgumentNullException>(() => _crawler.CrawlAsync(null!, 1, 1));
        }

        [Fact]
        public async Task CrawlAsync_WithDomSelector_OnlyFollowsLinksWithinSelector()
        {
            // Arrange
            var rootUrl = "http://example.com";
            var insideLink = new Mock<IElementHandle>();
            var outsideLink = new Mock<IElementHandle>();

            _pageMock.Setup(p => p.ContentAsync())
                .ReturnsAsync("<html><body><div class='content'><a href='/inside'>Inside</a></div><a href='/outside'>Outside</a></body></html>");

            _pageMock.Setup(p => p.QuerySelectorAllAsync("div.content a[href]"))
                .ReturnsAsync([insideLink.Object]);

            insideLink.Setup(e => e.GetAttributeAsync("href"))
                .ReturnsAsync("/inside");

            // Act
            await _crawler.CrawlAsync(new Uri(rootUrl), maxPages: 10, maxDepth: 2, domSelector: "div.content");

            // Assert
            _pageMock.Verify(p => p.GotoAsync(It.Is<string>(url => url.TrimEnd('/') == rootUrl.TrimEnd('/')), It.IsAny<PageGotoOptions>()), Times.Once);
            _pageMock.Verify(p => p.GotoAsync("http://example.com/inside", It.IsAny<PageGotoOptions>()), Times.Once);
            _pageMock.Verify(p => p.GotoAsync("http://example.com/outside", It.IsAny<PageGotoOptions>()), Times.Never);
        }

        [Fact]
        public async Task CrawlAsync_WithoutDomSelector_FollowsAllLinks()
        {
            // Arrange
            var rootUrl = "http://example.com";
            var insideLink = new Mock<IElementHandle>();
            var outsideLink = new Mock<IElementHandle>();

            _pageMock.Setup(p => p.ContentAsync())
                .ReturnsAsync("<html><body><div class='content'><a href='/inside'>Inside</a></div><a href='/outside'>Outside</a></body></html>");

            _pageMock.Setup(p => p.QuerySelectorAllAsync("a[href]"))
                .ReturnsAsync([insideLink.Object, outsideLink.Object]);

            insideLink.Setup(e => e.GetAttributeAsync("href"))
                .ReturnsAsync("/inside");
            outsideLink.Setup(e => e.GetAttributeAsync("href"))
                .ReturnsAsync("/outside");

            // Act
            await _crawler.CrawlAsync(new Uri(rootUrl), maxPages: 10, maxDepth: 2);

            // Assert
            _pageMock.Verify(p => p.GotoAsync(It.Is<string>(url => url.TrimEnd('/') == rootUrl.TrimEnd('/')), It.IsAny<PageGotoOptions>()), Times.Once);
            _pageMock.Verify(p => p.GotoAsync("http://example.com/inside", It.IsAny<PageGotoOptions>()), Times.Once);
            _pageMock.Verify(p => p.GotoAsync("http://example.com/outside", It.IsAny<PageGotoOptions>()), Times.Once);
        }

        [Fact]
        public async Task CrawlAsync_WithMaxPages_RespectsLimit()
        {
            // Arrange
            var rootUrl = "http://example.com";
            var page1Link = new Mock<IElementHandle>();
            var page2Link = new Mock<IElementHandle>();
            var page3Link = new Mock<IElementHandle>();

            // Root page setup
            _pageMock.Setup(p => p.QuerySelectorAllAsync("a[href]"))
                .ReturnsAsync([page1Link.Object, page2Link.Object, page3Link.Object]);
            _pageMock.Setup(p => p.ContentAsync())
                .ReturnsAsync(@"<html><body>
                    <a href='/page1'>Page 1</a>
                    <a href='/page2'>Page 2</a>
                    <a href='/page3'>Page 3</a>
                    </body></html>");

            page1Link.Setup(e => e.GetAttributeAsync("href"))
                .ReturnsAsync("/page1");
            page2Link.Setup(e => e.GetAttributeAsync("href"))
                .ReturnsAsync("/page2");
            page3Link.Setup(e => e.GetAttributeAsync("href"))
                .ReturnsAsync("/page3");

            // Page 1 setup
            var page1 = new Mock<IPage>();
            page1.Setup(p => p.Context).Returns(_contextMock.Object);
            page1.Setup(p => p.GotoAsync(It.IsAny<string>(), It.IsAny<PageGotoOptions>()))
                .ReturnsAsync(_responseMock.Object);
            page1.Setup(p => p.QuerySelectorAllAsync("a[href]"))
                .ReturnsAsync([]);
            page1.Setup(p => p.ContentAsync())
                .ReturnsAsync("<html><body>Page 1</body></html>");

            // Page 2 setup
            var page2 = new Mock<IPage>();
            page2.Setup(p => p.Context).Returns(_contextMock.Object);
            page2.Setup(p => p.GotoAsync(It.IsAny<string>(), It.IsAny<PageGotoOptions>()))
                .ReturnsAsync(_responseMock.Object);
            page2.Setup(p => p.QuerySelectorAllAsync("a[href]"))
                .ReturnsAsync([]);
            page2.Setup(p => p.ContentAsync())
                .ReturnsAsync("<html><body>Page 2</body></html>");

            // Setup page creation sequence
            var pageQueue = new Queue<IPage>([_pageMock.Object, page1.Object, page2.Object]);
            _contextMock.Setup(c => c.NewPageAsync())
                .ReturnsAsync(() => pageQueue.Dequeue());

            var visitedUrls = new List<string>();
            _handlerMock
                .Setup(h => h.PageCrawledAsync(It.IsAny<CrawledPage>()))
                .Callback<CrawledPage>(page => visitedUrls.Add(page.Uri.ToString()));

            // Act
            await _crawler.CrawlAsync(new Uri(rootUrl), maxPages: 2, maxDepth: 2);

            // Assert
            Assert.Equal(2, visitedUrls.Count);
            _pageMock.Verify(p => p.GotoAsync(It.Is<string>(url => url.TrimEnd('/') == rootUrl.TrimEnd('/')), It.IsAny<PageGotoOptions>()), Times.Once);
            page1.Verify(p => p.GotoAsync("http://example.com/page1", It.IsAny<PageGotoOptions>()), Times.Once);
            page2.Verify(p => p.GotoAsync("http://example.com/page2", It.IsAny<PageGotoOptions>()), Times.Never);
            page2.Verify(p => p.GotoAsync("http://example.com/page3", It.IsAny<PageGotoOptions>()), Times.Never);
        }

        [Fact]
        public async Task CrawlAsync_WithMaxDepth_RespectsLimit()
        {
            // Arrange
            var rootUrl = "http://example.com";
            var depth1Link = new Mock<IElementHandle>();
            var depth2Link = new Mock<IElementHandle>();
            var depth3Link = new Mock<IElementHandle>();

            // Root page setup
            _pageMock.Setup(p => p.QuerySelectorAllAsync("a[href]"))
                .ReturnsAsync([depth1Link.Object]);
            _pageMock.Setup(p => p.ContentAsync())
                .ReturnsAsync("<html><body><a href='/depth1'>Depth 1</a></body></html>");
            depth1Link.Setup(e => e.GetAttributeAsync("href"))
                .ReturnsAsync("/depth1");

            // Depth 1 page setup
            var depth1Page = new Mock<IPage>();
            depth1Page.Setup(p => p.QuerySelectorAllAsync("a[href]"))
                .ReturnsAsync([depth2Link.Object]);
            depth1Page.Setup(p => p.ContentAsync())
                .ReturnsAsync("<html><body><a href='/depth2'>Depth 2</a></body></html>");
            depth1Page.Setup(p => p.Context).Returns(_contextMock.Object);
            depth1Page.Setup(p => p.GotoAsync(It.IsAny<string>(), It.IsAny<PageGotoOptions>()))
                .ReturnsAsync(_responseMock.Object);
            depth2Link.Setup(e => e.GetAttributeAsync("href"))
                .ReturnsAsync("/depth2");

            // Depth 2 page setup
            var depth2Page = new Mock<IPage>();
            depth2Page.Setup(p => p.QuerySelectorAllAsync("a[href]"))
                .ReturnsAsync([depth3Link.Object]);
            depth2Page.Setup(p => p.ContentAsync())
                .ReturnsAsync("<html><body><a href='/depth3'>Depth 3</a></body></html>");
            depth2Page.Setup(p => p.Context).Returns(_contextMock.Object);
            depth2Page.Setup(p => p.GotoAsync(It.IsAny<string>(), It.IsAny<PageGotoOptions>()))
                .ReturnsAsync(_responseMock.Object);
            depth3Link.Setup(e => e.GetAttributeAsync("href"))
                .ReturnsAsync("/depth3");

            // Setup page creation sequence
            var pageQueue = new Queue<IPage>([_pageMock.Object, depth1Page.Object, depth2Page.Object]);
            _contextMock.Setup(c => c.NewPageAsync())
                .ReturnsAsync(() => pageQueue.Dequeue());

            // Act
            await _crawler.CrawlAsync(new Uri(rootUrl), maxPages: 10, maxDepth: 2);

            // Assert
            _pageMock.Verify(p => p.GotoAsync(It.Is<string>(url => url.TrimEnd('/') == rootUrl.TrimEnd('/')), It.IsAny<PageGotoOptions>()), Times.Once);
            depth1Page.Verify(p => p.GotoAsync("http://example.com/depth1", It.IsAny<PageGotoOptions>()), Times.Once);
            depth2Page.Verify(p => p.GotoAsync("http://example.com/depth2", It.IsAny<PageGotoOptions>()), Times.Once);
            _contextMock.Verify(c => c.NewPageAsync(), Times.AtMost(3), 
                "Should not create more than 3 pages");
        }

        [Fact]
        public async Task CrawlAsync_WithDomSelector_NoLinksFound_LogsMessage()
        {
            // Arrange
            var rootUrl = "http://example.com";
            var domSelector = "div.content";
            
            _pageMock.Setup(p => p.ContentAsync())
                .ReturnsAsync("<html><body><div class='content'></div></body></html>");

            _pageMock.Setup(p => p.QuerySelectorAllAsync($"{domSelector} a[href]"))
                .ReturnsAsync([]);

            var loggedMessages = new List<(string Message, LogLevel Level)>();
            _console.LoggedMessage += (message, level) => loggedMessages.Add((message, level));

            // Act
            await _crawler.CrawlAsync(new Uri(rootUrl), maxPages: 10, maxDepth: 2, domSelector: domSelector);

            // Assert
            _pageMock.Verify(p => p.GotoAsync(It.Is<string>(url => url.TrimEnd('/') == rootUrl.TrimEnd('/')), It.IsAny<PageGotoOptions>()), Times.Once);
            
            // Debug: Skriv ut alla loggade meddelanden
            foreach (var msg in loggedMessages)
            {
                _console.WriteLine($"DEBUG - Logged message: '{msg.Message}' at level {msg.Level}");
            }

            // Verify all expected log messages
            Assert.Contains(loggedMessages, m => m.Message == $"Crawling {rootUrl}/" && m.Level == LogLevel.Debug);
            Assert.Contains(loggedMessages, m => m.Message == $"Checking links against selector '{domSelector}'" && m.Level == LogLevel.Verbose);
            Assert.Contains(loggedMessages, m => m.Message == $"No links found within {domSelector} on {rootUrl}/" && m.Level == LogLevel.Debug);
        }

        [Fact]
        public async Task CrawlAsync_WithInvalidLinks_SkipsInvalidUrls()
        {
            // Arrange
            var rootUrl = "http://example.com";
            var emptyLink = new Mock<IElementHandle>();
            var invalidLink = new Mock<IElementHandle>();
            var validLink = new Mock<IElementHandle>();

            _pageMock.Setup(p => p.ContentAsync())
                .ReturnsAsync("<html><body><a href=''>Empty</a><a href='javascript:void(0)'>Invalid</a><a href='/valid'>Valid</a></body></html>");

            _pageMock.Setup(p => p.QuerySelectorAllAsync("a[href]"))
                .ReturnsAsync([emptyLink.Object, invalidLink.Object, validLink.Object]);

            emptyLink.Setup(e => e.GetAttributeAsync("href"))
                .ReturnsAsync("");
            invalidLink.Setup(e => e.GetAttributeAsync("href"))
                .ReturnsAsync("javascript:void(0)");
            validLink.Setup(e => e.GetAttributeAsync("href"))
                .ReturnsAsync("/valid");

            var loggedMessages = new List<(string Message, LogLevel Level)>();
            _console.LoggedMessage += (message, level) => loggedMessages.Add((message, level));

            // Act
            await _crawler.CrawlAsync(new Uri(rootUrl), maxPages: 10, maxDepth: 2);

            // Assert
            _pageMock.Verify(p => p.GotoAsync(It.Is<string>(url => url.TrimEnd('/') == rootUrl.TrimEnd('/')), It.IsAny<PageGotoOptions>()), Times.Once);
            _pageMock.Verify(p => p.GotoAsync("http://example.com/valid", It.IsAny<PageGotoOptions>()), Times.Once);
            // Verify that empty and invalid URLs are not visited
            _pageMock.Verify(p => p.GotoAsync("", It.IsAny<PageGotoOptions>()), Times.Never);
            _pageMock.Verify(p => p.GotoAsync("javascript:void(0)", It.IsAny<PageGotoOptions>()), Times.Never);
        }

        [Fact]
        public async Task CrawlAsync_WithFailedResponse_LogsWarningAndSkipsPage()
        {
            // Arrange
            var rootUrl = "http://example.com";
            var failedLink = new Mock<IElementHandle>();
            var failedResponse = new Mock<IResponse>();
            
            _pageMock.Setup(p => p.ContentAsync())
                .ReturnsAsync("<html><body><a href='/failed'>Failed Link</a></body></html>");

            _pageMock.Setup(p => p.QuerySelectorAllAsync("a[href]"))
                .ReturnsAsync([failedLink.Object]);

            failedLink.Setup(e => e.GetAttributeAsync("href"))
                .ReturnsAsync("/failed");

            // Sätt upp att den första sidan lyckas men den andra misslyckas
            _pageMock.Setup(p => p.GotoAsync(It.Is<string>(url => url.EndsWith("/failed")), It.IsAny<PageGotoOptions>()))
                .ReturnsAsync(failedResponse.Object);
            failedResponse.Setup(r => r.Ok).Returns(false);
            failedResponse.Setup(r => r.Status).Returns(404);

            var loggedMessages = new List<(string Message, LogLevel Level)>();
            _console.LoggedMessage += (message, level) => loggedMessages.Add((message, level));

            // Act
            await _crawler.CrawlAsync(new Uri(rootUrl), maxPages: 10, maxDepth: 2);

            // Assert
            _pageMock.Verify(p => p.GotoAsync(It.Is<string>(url => url.TrimEnd('/') == rootUrl.TrimEnd('/')), It.IsAny<PageGotoOptions>()), Times.Once);
            _pageMock.Verify(p => p.GotoAsync("http://example.com/failed", It.IsAny<PageGotoOptions>()), Times.Once);
            
            // Verify warning message
            Assert.Contains(loggedMessages, m => 
                m.Message == "Failed to load http://example.com/failed: 404" && 
                m.Level == LogLevel.Warning);

            // Verify that no content was extracted from the failed page
            _pageMock.Verify(p => p.ContentAsync(), Times.Once); // Bara för första sidan
        }

        [Fact]
        public async Task CrawlAsync_WhenBrowserContextFails_LogsErrorAndRethrows()
        {
            // Arrange
            var rootUrl = "http://example.com";
            var expectedError = new PlaywrightException("Failed to create browser context");
            
            _browserMock.Setup(b => b.NewContextAsync(null))
                .ThrowsAsync(expectedError);

            var loggedMessages = new List<(string Message, LogLevel Level)>();
            _console.LoggedMessage += (message, level) => loggedMessages.Add((message, level));

            // Act & Assert
            var exception = await Assert.ThrowsAsync<PlaywrightException>(async () => 
                await _crawler.CrawlAsync(new Uri(rootUrl), maxPages: 10, maxDepth: 2));

            Assert.Same(expectedError, exception);
            Assert.Contains(loggedMessages, m => 
                m.Message == $"Error during crawl: {expectedError.Message}" && 
                m.Level == LogLevel.Error);
        }

        [Fact]
        public async Task CrawlPageAsync_WhenProcessingLinkFails_LogsWarningAndContinues()
        {
            // Arrange
            var rootUrl = "http://example.com";
            var link1 = new Mock<IElementHandle>();
            var link2 = new Mock<IElementHandle>();
            
            _pageMock.Setup(p => p.ContentAsync())
                .ReturnsAsync("<html><body><a href='/link1'>Link 1</a><a href='/link2'>Link 2</a></body></html>");

            _pageMock.Setup(p => p.QuerySelectorAllAsync("a[href]"))
                .ReturnsAsync([link1.Object, link2.Object]);

            // Första länken kastar exception vid GetAttributeAsync
            link1.Setup(e => e.GetAttributeAsync("href"))
                .ThrowsAsync(new Exception("Failed to get href attribute"));
            
            // Andra länken fungerar normalt
            link2.Setup(e => e.GetAttributeAsync("href"))
                .ReturnsAsync("/link2");

            var loggedMessages = new List<(string Message, LogLevel Level)>();
            _console.LoggedMessage += (message, level) => loggedMessages.Add((message, level));

            // Act
            await _crawler.CrawlAsync(new Uri(rootUrl), maxPages: 10, maxDepth: 2);

            // Assert
            // Verifierar att vi fortsätter crawla efter felet
            _pageMock.Verify(p => p.GotoAsync(It.Is<string>(url => url.TrimEnd('/') == rootUrl.TrimEnd('/')), It.IsAny<PageGotoOptions>()), Times.Once);
            _pageMock.Verify(p => p.GotoAsync("http://example.com/link2", It.IsAny<PageGotoOptions>()), Times.Once);
            
            // Verifierar att varningen loggades
            Assert.Contains(loggedMessages, m => 
                m.Message == "Error processing link: Failed to get href attribute" && 
                m.Level == LogLevel.Warning);
        }

        [Fact]
        public async Task CrawlPageAsync_WhenPageLoadFails_LogsErrorAndContinues()
        {
            // Arrange
            var rootUrl = "http://example.com";
            var link = new Mock<IElementHandle>();
            var expectedError = new PlaywrightException("Navigation failed");
            
            _pageMock.Setup(p => p.ContentAsync())
                .ReturnsAsync("<html><body><a href='/error-page'>Error Link</a></body></html>");

            _pageMock.Setup(p => p.QuerySelectorAllAsync("a[href]"))
                .ReturnsAsync([link.Object]);

            link.Setup(e => e.GetAttributeAsync("href"))
                .ReturnsAsync("/error-page");

            // Sätt upp att den första sidan lyckas men att innehållshämtning för den andra sidan kastar exception
            var errorPage = new Mock<IPage>();
            errorPage.Setup(p => p.Context).Returns(_contextMock.Object);
            errorPage.Setup(p => p.GotoAsync(It.IsAny<string>(), It.IsAny<PageGotoOptions>()))
                .ThrowsAsync(expectedError);

            var pageQueue = new Queue<IPage>([_pageMock.Object, errorPage.Object]);
            _contextMock.Setup(c => c.NewPageAsync())
                .ReturnsAsync(() => pageQueue.Dequeue());

            var loggedMessages = new List<(string Message, LogLevel Level)>();
            _console.LoggedMessage += (message, level) => loggedMessages.Add((message, level));

            // Act
            await _crawler.CrawlAsync(new Uri(rootUrl), maxPages: 10, maxDepth: 2);

            // Assert
            _pageMock.Verify(p => p.GotoAsync(It.Is<string>(url => url.TrimEnd('/') == rootUrl.TrimEnd('/')), It.IsAny<PageGotoOptions>()), Times.Once);
            errorPage.Verify(p => p.GotoAsync("http://example.com/error-page", It.IsAny<PageGotoOptions>()), Times.Once);
            
            // Verifierar att felet loggades
            Assert.Contains(loggedMessages, m => 
                m.Message == "Error crawling http://example.com/error-page: Navigation failed" && 
                m.Level == LogLevel.Error);

            // Verifierar att inga fler anrop gjordes på error-sidan
            errorPage.Verify(p => p.ContentAsync(), Times.Never);
        }
    }
} 