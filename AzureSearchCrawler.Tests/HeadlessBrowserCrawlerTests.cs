using Abot2.Poco;
using AzureSearchCrawler.Models;
using AzureSearchCrawler.Interfaces;
using AzureSearchCrawler.TestUtilities;
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
            _contextMock.Setup(c => c.NewPageAsync())
                .ReturnsAsync(_pageMock.Object);
            _pageMock.Setup(p => p.Context).Returns(_contextMock.Object);
            _pageMock.Setup(p => p.GotoAsync(It.IsAny<string>(), It.IsAny<PageGotoOptions>()))
                .Callback<string, PageGotoOptions>((url, _) => _console.WriteLine($"GotoAsync called with URL: {url}", LogLevel.Debug))
                .ReturnsAsync(_responseMock.Object);
            _responseMock.Setup(r => r.Ok).Returns(true);
            _responseMock.Setup(r => r.Status).Returns(200);
            _responseMock.Setup(r => r.Headers).Returns([]);
            _responseMock.Setup(r => r.StatusText).Returns("OK");

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
            _pageMock.Verify(p => p.GotoAsync("http://example.com/", It.IsAny<PageGotoOptions>()), Times.Once);
            _pageMock.Verify(p => p.GotoAsync("http://example.com/inside", It.IsAny<PageGotoOptions>()), Times.Once);
            _pageMock.Verify(p => p.GotoAsync("http://example.com/outside", It.IsAny<PageGotoOptions>()), Times.Never);
        }

        [Fact]
        public async Task CrawlAsync_WithoutDomSelector_FollowsAllLinks()
        {
            // Arrange
            var rootUrl = "http://example.com";
            var insideLink = new Mock<IElementHandle>();

            // Setup för första sidan
            var rootPage = new Mock<IPage>();
            rootPage.Setup(p => p.Context).Returns(_contextMock.Object);
            rootPage.Setup(p => p.GotoAsync(It.IsAny<string>(), It.IsAny<PageGotoOptions>()))
                .ReturnsAsync(_responseMock.Object);
            rootPage.Setup(p => p.QuerySelectorAllAsync("a[href]"))
                .ReturnsAsync([insideLink.Object]);
            rootPage.Setup(p => p.ContentAsync())
                .ReturnsAsync("<html><body><a href='/inside'>Inside</a></body></html>");
            rootPage.Setup(p => p.SetExtraHTTPHeadersAsync(It.IsAny<Dictionary<string, string>>()))
                .Returns(Task.CompletedTask);

            insideLink.Setup(e => e.GetAttributeAsync("href"))
                .ReturnsAsync("/inside");

            // Setup för den andra sidan
            var insidePage = new Mock<IPage>();
            insidePage.Setup(p => p.Context).Returns(_contextMock.Object);
            insidePage.Setup(p => p.GotoAsync(It.IsAny<string>(), It.IsAny<PageGotoOptions>()))
                .ReturnsAsync(_responseMock.Object);
            insidePage.Setup(p => p.QuerySelectorAllAsync("a[href]"))
                .ReturnsAsync([]);
            insidePage.Setup(p => p.ContentAsync())
                .ReturnsAsync("<html><body>Inside page</body></html>");
            insidePage.Setup(p => p.CloseAsync(It.IsAny<PageCloseOptions>()))
                .Returns(Task.CompletedTask);
            insidePage.Setup(p => p.SetExtraHTTPHeadersAsync(It.IsAny<Dictionary<string, string>>()))
                .Returns(Task.CompletedTask);

            // Setup page creation sequence
            var pageQueue = new Queue<IPage>([rootPage.Object, insidePage.Object]);
            var pageIndex = 0;
            _contextMock.Setup(c => c.NewPageAsync())
                .ReturnsAsync(() => pageQueue.Dequeue())
                .Callback(() => _console.WriteLine($"Creating page {++pageIndex}", LogLevel.Debug));

            // Setup för context disposal
            _contextMock.Setup(c => c.DisposeAsync())
                .Returns(ValueTask.CompletedTask);

            // Act
            await _crawler.CrawlAsync(new Uri(rootUrl), maxPages: 10, maxDepth: 2);

            // Assert
            rootPage.Verify(p => p.GotoAsync(rootUrl + "/", It.IsAny<PageGotoOptions>()), Times.Once);
            insidePage.Verify(p => p.GotoAsync("http://example.com/inside", It.IsAny<PageGotoOptions>()), Times.Once);
            rootPage.Verify(p => p.QuerySelectorAllAsync("a[href]"), Times.Once);
            insidePage.Verify(p => p.QuerySelectorAllAsync("a[href]"), Times.Once);
            insideLink.Verify(e => e.GetAttributeAsync("href"), Times.Once);
            _contextMock.Verify(c => c.NewPageAsync(), Times.AtLeast(2));
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
            _pageMock.Verify(p => p.GotoAsync("http://example.com/", It.IsAny<PageGotoOptions>()), Times.Once);
            page1.Verify(p => p.GotoAsync("http://example.com/page1", It.IsAny<PageGotoOptions>()), Times.Once);
            page2.Verify(p => p.GotoAsync("http://example.com/page2", It.IsAny<PageGotoOptions>()), Times.Never);
            page2.Verify(p => p.GotoAsync("http://example.com/page3", It.IsAny<PageGotoOptions>()), Times.Never);
        }

        [Fact]
        public async Task CrawlAsync_WithMaxDepth_RespectsLimit()
        {
            // Arrange
            var rootUrl = "http://example.com";
            var maxDepth = 2;
            var pageCount = 0;

            var loggedMessages = new List<(string Message, LogLevel Level)>();
            _console.LoggedMessage += (message, level) => loggedMessages.Add((message, level));

            var mockPage1 = new Mock<IPage>();
            var mockPage2 = new Mock<IPage>();
            var mockPage3 = new Mock<IPage>();

            var mockResponse1 = new Mock<IResponse>();
            var mockResponse2 = new Mock<IResponse>();
            var mockResponse3 = new Mock<IResponse>();

            mockResponse1.Setup(r => r.Ok).Returns(true);
            mockResponse2.Setup(r => r.Ok).Returns(true);
            mockResponse3.Setup(r => r.Ok).Returns(true);

            mockResponse1.Setup(r => r.Headers).Returns([]);
            mockResponse2.Setup(r => r.Headers).Returns([]);
            mockResponse3.Setup(r => r.Headers).Returns([]);

            mockPage1.Setup(p => p.GotoAsync(It.IsAny<string>(), It.IsAny<PageGotoOptions>()))
                .ReturnsAsync(mockResponse1.Object);
            mockPage2.Setup(p => p.GotoAsync(It.IsAny<string>(), It.IsAny<PageGotoOptions>()))
                .ReturnsAsync(mockResponse2.Object);
            mockPage3.Setup(p => p.GotoAsync(It.IsAny<string>(), It.IsAny<PageGotoOptions>()))
                .ReturnsAsync(mockResponse3.Object);

            mockPage1.Setup(p => p.ContentAsync())
                .ReturnsAsync("<html><body><a href='http://example.com/depth1'>Link 1</a></body></html>");
            mockPage2.Setup(p => p.ContentAsync())
                .ReturnsAsync("<html><body><a href='http://example.com/depth2'>Link 2</a></body></html>");
            mockPage3.Setup(p => p.ContentAsync())
                .ReturnsAsync("<html><body><a href='http://example.com/depth3'>Link 3</a></body></html>");

            mockPage1.Setup(p => p.QuerySelectorAllAsync("a[href]"))
                .ReturnsAsync([CreateMockElement("http://example.com/depth1").Object]);
            mockPage2.Setup(p => p.QuerySelectorAllAsync("a[href]"))
                .ReturnsAsync([CreateMockElement("http://example.com/depth2").Object]);
            mockPage3.Setup(p => p.QuerySelectorAllAsync("a[href]"))
                .ReturnsAsync([CreateMockElement("http://example.com/depth3").Object]);

            mockPage1.Setup(p => p.Context).Returns(_contextMock.Object);
            mockPage2.Setup(p => p.Context).Returns(_contextMock.Object);
            mockPage3.Setup(p => p.Context).Returns(_contextMock.Object);

            mockPage1.Setup(p => p.SetExtraHTTPHeadersAsync(It.IsAny<Dictionary<string, string>>()))
                .Returns(Task.CompletedTask);
            mockPage2.Setup(p => p.SetExtraHTTPHeadersAsync(It.IsAny<Dictionary<string, string>>()))
                .Returns(Task.CompletedTask);
            mockPage3.Setup(p => p.SetExtraHTTPHeadersAsync(It.IsAny<Dictionary<string, string>>()))
                .Returns(Task.CompletedTask);

            var pages = new Queue<IPage>([mockPage1.Object, mockPage2.Object, mockPage3.Object]);

            _browserMock.Setup(b => b.NewContextAsync(It.IsAny<BrowserNewContextOptions>()))
                .ReturnsAsync(_contextMock.Object);

            _contextMock.Setup(c => c.NewPageAsync())
                .ReturnsAsync(() =>
                {
                    if (pageCount >= 3)
                    {
                        return null;
                    }

                    pageCount++;
                    return pages.Dequeue();
                });

            var processorMock = new Mock<ICrawledPageProcessor>();
            processorMock.Setup(p => p.PageCrawledAsync(It.IsAny<CrawledPage>()))
                .Returns(Task.CompletedTask);
            processorMock.Setup(p => p.CrawlFinishedAsync())
                .Returns(Task.CompletedTask);

            var crawler = new HeadlessBrowserCrawler(processorMock.Object, _console, _playwrightMock.Object);

            // Act
            await crawler.CrawlAsync(new Uri(rootUrl), maxPages: 10, maxDepth: maxDepth);

            // Assert
            _contextMock.Verify(c => c.NewPageAsync(), Times.Exactly(3));
            Assert.Equal(3, pageCount);

            var messagesCopy = loggedMessages.ToList();
            AssertContainsMessage(messagesCopy, $"Starting headless browser crawl of {rootUrl}", LogLevel.Information);
            AssertContainsMessage(messagesCopy, "Configuration - Max pages: 10, Max depth: 2", LogLevel.Debug);
            AssertContainsMessage(messagesCopy, "Initializing browser configuration", LogLevel.Information);
            AssertContainsMessage(messagesCopy, "Browser details - Engine: Chromium, Mode: Headless", LogLevel.Debug);
            AssertContainsMessage(messagesCopy, "Processing page 1/10: http://example.com/", LogLevel.Information);
            AssertContainsMessage(messagesCopy, "Page details - Depth: 0/2", LogLevel.Debug);
            AssertContainsMessage(messagesCopy, "Processing page 2/10: http://example.com/depth1", LogLevel.Information);
            AssertContainsMessage(messagesCopy, "Page details - Depth: 1/2", LogLevel.Debug);
            AssertContainsMessage(messagesCopy, "Processing page 3/10: http://example.com/depth2", LogLevel.Information);
            AssertContainsMessage(messagesCopy, "Page details - Depth: 2/2", LogLevel.Debug);
        }

        [Fact]
        public async Task CrawlAsync_WithDomSelector_NoLinksFound_LogsMessage()
        {
            // Arrange
            var rootUrl = "http://example.com";
            var loggedMessages = new List<(string Message, LogLevel Level)>();
            _console.LoggedMessage += (message, level) => loggedMessages.Add((message, level));

            _pageMock.Setup(p => p.QuerySelectorAllAsync("div.content a[href]"))
                .ReturnsAsync([]);

            _pageMock.Setup(p => p.ContentAsync())
                .ReturnsAsync("<html><body><div class='content'></div></body></html>");

            // Act
            await _crawler.CrawlAsync(new Uri(rootUrl), maxPages: 10, maxDepth: 2, domSelector: "div.content");

            // Assert
            var messagesCopy = loggedMessages.ToList();
            AssertContainsMessage(messagesCopy, $"Starting headless browser crawl of {rootUrl}", LogLevel.Information);
            AssertContainsMessage(messagesCopy, $"Using DOM selector filter: div.content", LogLevel.Information);
            AssertContainsMessage(messagesCopy, "No links found on page", LogLevel.Information);
        }

        [Fact]
        public async Task CrawlAsync_WithInvalidLinks_SkipsInvalidUrls()
        {
            // Arrange
            var rootUrl = "http://example.com";
            var loggedMessages = new List<(string Message, LogLevel Level)>();
            _console.LoggedMessage += (message, level) => loggedMessages.Add((message, level));

            var link1 = new Mock<IElementHandle>();
            var link2 = new Mock<IElementHandle>();

            link1.Setup(e => e.GetAttributeAsync("href"))
                .ReturnsAsync("invalid-url");
            link2.Setup(e => e.GetAttributeAsync("href"))
                .ReturnsAsync("http://example.com/valid");

            _pageMock.Setup(p => p.QuerySelectorAllAsync("a[href]"))
                .ReturnsAsync([link1.Object, link2.Object]);

            _pageMock.Setup(p => p.ContentAsync())
                .ReturnsAsync("<html><body><a href='invalid-url'>Invalid</a><a href='http://example.com/valid'>Valid</a></body></html>");

            _pageMock.Setup(p => p.Context).Returns(_contextMock.Object);
            _pageMock.Setup(p => p.SetExtraHTTPHeadersAsync(It.IsAny<Dictionary<string, string>>()))
                .Returns(Task.CompletedTask);

            // Act
            await _crawler.CrawlAsync(new Uri(rootUrl), maxPages: 10, maxDepth: 2);

            // Assert
            var messagesCopy = loggedMessages.ToList();
            AssertContainsMessage(messagesCopy, "Found 2 links on page", LogLevel.Debug);
            AssertContainsMessage(messagesCopy, "Skipping invalid URL: invalid-url", LogLevel.Warning);
            AssertContainsMessage(messagesCopy, "Processing valid URL: http://example.com/valid", LogLevel.Debug);
        }

        [Fact]
        public async Task CrawlAsync_WithFailedResponse_LogsWarningAndSkipsPage()
        {
            // Arrange
            var rootUrl = "http://example.com";
            var loggedMessages = new List<(string Message, LogLevel Level)>();
            _console.LoggedMessage += (message, level) => loggedMessages.Add((message, level));

            _responseMock.Setup(r => r.Ok).Returns(false);
            _responseMock.Setup(r => r.Status).Returns(404);
            _responseMock.Setup(r => r.StatusText).Returns("Not Found");

            // Act
            await _crawler.CrawlAsync(new Uri(rootUrl), maxPages: 10, maxDepth: 2);

            // Assert
            var messagesCopy = loggedMessages.ToList();
            AssertContainsMessage(messagesCopy, $"Starting headless browser crawl of {rootUrl}", LogLevel.Information);
            AssertContainsMessage(messagesCopy, "About to call GotoAsync", LogLevel.Debug);
            AssertContainsMessage(messagesCopy, "Failed to load http://example.com/ (404 Not Found)", LogLevel.Warning);
            AssertContainsMessage(messagesCopy, "Crawl completed successfully", LogLevel.Information);
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

            var messagesCopy = loggedMessages.ToList();
            AssertContainsMessage(messagesCopy, $"Starting headless browser crawl of {rootUrl}", LogLevel.Information);
            AssertContainsMessage(messagesCopy, "Configuration - Max pages: 10, Max depth: 2", LogLevel.Debug);
            AssertContainsMessage(messagesCopy, "Initializing browser configuration", LogLevel.Information);
            AssertContainsMessage(messagesCopy, "Browser details - Engine: Chromium, Mode: Headless", LogLevel.Debug);
            AssertContainsMessage(messagesCopy, "Critical error during crawl: Failed to create brow", LogLevel.Error);
            AssertContainsMessage(messagesCopy, "Technical details: Microsoft.Playwright.Playwright", LogLevel.Debug);
        }

        [Fact]
        public async Task CrawlPageAsync_WhenProcessingLinkFails_LogsWarningAndContinues()
        {
            // Arrange
            var rootUrl = "http://example.com";
            var link1 = new Mock<IElementHandle>();
            var link2 = new Mock<IElementHandle>();

            // Skapa en enkel mock för att testa att varningar loggas när en länk misslyckas
            var pageMock = new Mock<IPage>();
            
            pageMock.Setup(p => p.QuerySelectorAllAsync(It.IsAny<string>()))
                .ReturnsAsync([link1.Object, link2.Object]);
            
            link1.Setup(e => e.GetAttributeAsync("href"))
                .ThrowsAsync(new Exception("Failed to get href attribute"));
            
            link2.Setup(e => e.GetAttributeAsync("href"))
                .ReturnsAsync("/link2");
            
            // Skapa en mock för ICrawledPageProcessor
            var processorMock = new Mock<ICrawledPageProcessor>();
            
            // Skapa en TestConsole för att fånga loggar
            var console = new TestConsole();
            var loggedMessages = new List<(string message, LogLevel level)>();
            console.LoggedMessage += (message, level) => loggedMessages.Add((message, level));
            
            // Skapa mocks för Playwright
            var playwrightMock = new Mock<IPlaywright>();
            var browserTypeMock = new Mock<IBrowserType>();
            var browserMock = new Mock<IBrowser>();
            var contextMock = new Mock<IBrowserContext>();

            browserTypeMock.Setup(b => b.LaunchAsync(It.IsAny<BrowserTypeLaunchOptions>()))
                .ReturnsAsync(browserMock.Object);
            playwrightMock.Setup(p => p.Chromium).Returns(browserTypeMock.Object);
            browserMock.Setup(b => b.NewContextAsync(It.IsAny<BrowserNewContextOptions>()))
                .ReturnsAsync(contextMock.Object);
            
            // Skapa en HeadlessBrowserCrawler med våra mocks
            var crawler = new HeadlessBrowserCrawler(processorMock.Object, console, playwrightMock.Object);
            
            // Act - anropa ProcessLinksAsync direkt
            await crawler.ProcessLinksAsync(pageMock.Object, rootUrl);
            
            // Assert
            // Verifiera att varningar loggades för den misslyckade länken
            Assert.Contains(loggedMessages, m => m.message.Contains("Failed to process link") && m.level == LogLevel.Warning);
        }

        [Fact]
        public async Task CrawlPageAsync_WhenPageLoadFails_LogsErrorAndContinues()
        {
            // Arrange
            var rootUrl = "http://example.com";
            var expectedError = new PlaywrightException("Navigation failed");
            
            // Skapa en mock för IPage
            var pageMock = new Mock<IPage>();
            
            // Skapa en giltig respons för rootUrl
            var responseMock = new Mock<IResponse>();
            responseMock.Setup(r => r.Ok).Returns(true);
            responseMock.Setup(r => r.Status).Returns(200);
            responseMock.Setup(r => r.StatusText).Returns("OK");
            responseMock.Setup(r => r.Headers).Returns([]);
            
            // Konfigurera rootUrl att returnera en giltig respons
            pageMock.Setup(p => p.GotoAsync(rootUrl, It.IsAny<PageGotoOptions>()))
                .ReturnsAsync(responseMock.Object);
            
            // Konfigurera innehåll med en länk till error-page
            pageMock.Setup(p => p.ContentAsync())
                .ReturnsAsync("<html><body><a href=\"/error-page\">Error Link</a></body></html>");
            
            // Konfigurera länken
            var link = new Mock<IElementHandle>();
            link.Setup(e => e.GetAttributeAsync("href"))
                .ReturnsAsync("/error-page");
            
            pageMock.Setup(p => p.QuerySelectorAllAsync("a[href]"))
                .ReturnsAsync([link.Object]);
            
            // Skapa en mock för errorPage
            var errorPageMock = new Mock<IPage>();
            
            // Skapa en mock för IBrowserContext
            var contextMock = new Mock<IBrowserContext>();
            
            // Konfigurera NewPageAsync att returnera errorPageMock för att simulera navigering till error-page
            contextMock.Setup(c => c.NewPageAsync())
                .Returns(Task.FromResult(errorPageMock.Object));
            
            // Konfigurera DisposeAsync för contextMock
            contextMock.Setup(c => c.DisposeAsync())
                .Returns(ValueTask.CompletedTask);
            
            // Konfigurera CloseAsync för errorPageMock
            errorPageMock.Setup(p => p.CloseAsync(null))
                .Returns(Task.CompletedTask);
            
            pageMock.Setup(p => p.Context).Returns(contextMock.Object);
            errorPageMock.Setup(p => p.Context).Returns(contextMock.Object);
            
            // Konfigurera errorPageMock att kasta undantag vid GotoAsync
            errorPageMock.Setup(p => p.GotoAsync(It.IsAny<string>(), It.IsAny<PageGotoOptions>()))
                .ThrowsAsync(expectedError);
            
            // Konfigurera SetExtraHTTPHeadersAsync
            pageMock.Setup(p => p.SetExtraHTTPHeadersAsync(It.IsAny<Dictionary<string, string>>()))
                .Returns(Task.CompletedTask);
            errorPageMock.Setup(p => p.SetExtraHTTPHeadersAsync(It.IsAny<Dictionary<string, string>>()))
                .Returns(Task.CompletedTask);
            
            // Skapa en mock för ICrawledPageProcessor
            var processorMock = new Mock<ICrawledPageProcessor>();
            processorMock.Setup(p => p.PageCrawledAsync(It.IsAny<CrawledPage>()))
                .Returns(Task.CompletedTask);
            
            // Skapa en TestConsole för att fånga loggar
            var console = new TestConsole();
            var loggedMessages = new List<(string message, LogLevel level)>();
            console.LoggedMessage += (message, level) => loggedMessages.Add((message, level));
            
            // Skapa en HeadlessBrowserCrawler med våra mocks
            var playwrightMock = new Mock<IPlaywright>();
            var browserTypeMock = new Mock<IBrowserType>();
            var browserMock = new Mock<IBrowser>();
            
            browserTypeMock.Setup(b => b.LaunchAsync(It.IsAny<BrowserTypeLaunchOptions>()))
                .ReturnsAsync(browserMock.Object);
            playwrightMock.Setup(p => p.Chromium).Returns(browserTypeMock.Object);
            browserMock.Setup(b => b.NewContextAsync(It.IsAny<BrowserNewContextOptions>()))
                .ReturnsAsync(contextMock.Object);
            
            var crawler = new HeadlessBrowserCrawler(processorMock.Object, console, playwrightMock.Object);
            
            // Act
            await crawler.CrawlAsync(new Uri(rootUrl), maxPages: 10, maxDepth: 2);
            
            // Debug: Skriv ut alla loggade meddelanden för felsökning
            foreach (var (message, level) in loggedMessages)
            {
                Console.WriteLine($"DEBUG - Logged message: '{message}' at level {level}", LogLevel.Debug);
            }
            
            // Assert
            // Verifiera att errorPageMock.GotoAsync anropades
            errorPageMock.Verify(p => p.GotoAsync(It.IsAny<string>(), It.IsAny<PageGotoOptions>()), Times.Once);
            
            // Verifiera att ett felmeddelande loggades
            Assert.Contains(loggedMessages, m => 
                m.message.Contains("Failed to crawl") && 
                m.level == LogLevel.Error);
        }

        private static Mock<IElementHandle> CreateMockElement(string href)
        {
            var mockElement = new Mock<IElementHandle>();
            mockElement.Setup(e => e.GetAttributeAsync("href"))
                .ReturnsAsync(href);
            return mockElement;
        }

        private void AssertContainsMessage(List<(string Message, LogLevel Level)> messages, string expectedContent, LogLevel expectedLevel)
        {
            var messagesCopy = messages.ToList();
            var found = messagesCopy.Any(m => 
                m.Message.Contains(expectedContent, StringComparison.OrdinalIgnoreCase) && 
                m.Level == expectedLevel);
            
            if (!found)
            {
                // Skriv ut alla meddelanden för felsökning
                _console.WriteLine($"Failed to find message containing '{expectedContent}' at level {expectedLevel}", LogLevel.Debug);
                _console.WriteLine("Available messages:", LogLevel.Debug);
                foreach (var (Message, Level) in messagesCopy)
                {
                    _console.WriteLine($"- [{Level}] {Message}", LogLevel.Debug);
                }
            }
            
            Assert.True(found, $"Expected to find message containing '{expectedContent}' at level {expectedLevel}");
        }
    }
} 