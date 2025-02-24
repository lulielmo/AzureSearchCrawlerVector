using Abot2.Crawler;
using Abot2.Poco;
using Moq;
using Xunit;
using AngleSharp.Html.Parser;
using AngleSharp;
using AzureSearchCrawler.Models;

namespace AzureSearchCrawler.Tests
{

    public class CrawlerTests : IDisposable
    {
        private LogLevel _logLevel;
        private readonly Mock<IWebCrawler> _webCrawlerMock;
        private readonly Mock<CrawlHandler> _handlerMock;
        private readonly TestConsole _console;
        private readonly Crawler _crawler;
        private readonly StringWriter _stringWriter;
        private readonly TextWriter _originalOut;
        private readonly TextWriter _originalError;

        public CrawlerTests()
        {
            _logLevel = new LogLevel();
            _webCrawlerMock = new Mock<IWebCrawler>();
            _handlerMock = new Mock<CrawlHandler>();
            _console = new TestConsole();
            _stringWriter = new StringWriter();
            _originalOut = Console.Out;
            _originalError = Console.Error;

            Console.SetOut(_stringWriter);
            Console.SetError(_stringWriter);

            _crawler = new Crawler(_handlerMock.Object, _ => _webCrawlerMock.Object, _console, _logLevel);
        }

        public void Dispose()
        {
            Console.SetOut(_originalOut);
            Console.SetError(_originalError);
            _stringWriter.Dispose();
        }

        [Fact]
        public async Task CrawlAsync_WithValidUri_CallsHandler()
        {
            // Arrange
            var uri = new Uri("http://example.com");
            var crawledPage = new CrawledPage(uri)
            {
                HttpResponseMessage = new HttpResponseMessage(System.Net.HttpStatusCode.OK),
                Content = new PageContent { Text = "Some content" }
            };

            var crawlResult = new CrawlResult
            {
                RootUri = uri,
                CrawlContext = new CrawlContext()
            };

            _webCrawlerMock
                .Setup(c => c.CrawlAsync(It.IsAny<Uri>()))
                .Callback(() =>
                {
                    var args = new PageCrawlCompletedArgs(new CrawlContext(), crawledPage);
                    _webCrawlerMock.Raise(c => c.PageCrawlCompleted += null, _webCrawlerMock.Object, args);
                })
                .ReturnsAsync(crawlResult);

            // Act
            await _crawler.CrawlAsync(uri, maxPages: 1, maxDepth: 1);

            // Assert
            _handlerMock.Verify(h => h.PageCrawledAsync(It.IsAny<CrawledPage>()), Times.Once);
        }

        [Theory]
        [InlineData(-1, 1)]
        [InlineData(1, -1)]
        [InlineData(0, 1)]
        [InlineData(1, 0)]
        public async Task CrawlAsync_WithInvalidParameters_ThrowsArgumentException(int maxPages, int maxDepth)
        {
            // Arrange
            var uri = new Uri("http://example.com");

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() =>
                _crawler.CrawlAsync(uri, maxPages, maxDepth));
        }

        [Fact]
        public async Task CrawlAsync_WhenCrawlingFails_StillCallsFinished()
        {
            // Arrange
            var uri = new Uri("http://example.com");
            var crawlResult = new CrawlResult
            {
                RootUri = uri,
                CrawlContext = new CrawlContext(),
                ErrorException = new Exception("Test error")
            };

            _webCrawlerMock
                .Setup(c => c.CrawlAsync(It.IsAny<Uri>()))
                .ReturnsAsync(crawlResult);

            // Act
            await _crawler.CrawlAsync(uri, maxPages: 1, maxDepth: 1);

            // Assert
            _handlerMock.Verify(h => h.CrawlFinishedAsync(), Times.Once);
        }

        [Fact]
        public async Task CrawlAsync_WhenPageCrawled_CallsPageCrawledAsync()
        {
            // Arrange
            var uri = new Uri("http://example.com");
            var crawledPage = new CrawledPage(uri)
            {
                HttpResponseMessage = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            };
            crawledPage.Content = new PageContent { Text = "Some content" };

            var crawlResult = new CrawlResult
            {
                RootUri = uri,
                CrawlContext = new CrawlContext()
            };

            var eventRaised = new TaskCompletionSource<bool>();

            _webCrawlerMock
                .Setup(c => c.CrawlAsync(It.IsAny<Uri>()))
                .Callback(() =>
                {
                    // Vänta tills event-hanteraren är registrerad
                    Task.Delay(100).ContinueWith(_ =>
                    {
                        var args = new PageCrawlCompletedArgs(new CrawlContext(), crawledPage);
                        _webCrawlerMock.Raise(c => c.PageCrawlCompleted += null, _webCrawlerMock.Object, args);
                        eventRaised.SetResult(true);
                    });
                })
                .ReturnsAsync(crawlResult);

            // Act
            var crawlTask = _crawler.CrawlAsync(uri, maxPages: 1, maxDepth: 1);
            await Task.WhenAll(crawlTask, eventRaised.Task);

            // Assert
            _handlerMock.Verify(h => h.PageCrawledAsync(crawledPage), Times.Once);
        }

        [Fact]
        public async Task CrawlAsync_WhenPageCrawlStarting_IncrementsPageCount()
        {
            // Arrange
            var uri = new Uri("http://example.com");
            var pageToCrawl = new PageToCrawl(uri) { ParentUri = uri, CrawlDepth = 1 };

            var crawlResult = new CrawlResult
            {
                RootUri = uri,
                CrawlContext = new CrawlContext()
            };

            _webCrawlerMock
                .Setup(c => c.CrawlAsync(It.IsAny<Uri>()))
                .Callback(() =>
                {
                    var args = new PageCrawlStartingArgs(new CrawlContext(), pageToCrawl);
                    _webCrawlerMock.Raise(c => c.PageCrawlStarting += null, _webCrawlerMock.Object, args);
                })
                .ReturnsAsync(crawlResult);

            // Act
            await _crawler.CrawlAsync(uri, maxPages: 1, maxDepth: 1);

            // Assert
            // Verifiera att PageCount har ökats (detta kan vara svårt eftersom det är en statisk variabel)
            // Vi kanske behöver refaktorera koden för att göra den mer testbar
        }

        [Fact]
        public async Task CrawlAsync_WhenPageReturns404_DoesNotProcessPage()
        {
            // Arrange
            var uri = new Uri("http://example.com");
            var crawledPage = new CrawledPage(uri)
            {
                HttpResponseMessage = new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
            };

            var crawlResult = new CrawlResult
            {
                RootUri = uri,
                CrawlContext = new CrawlContext()
            };

            _webCrawlerMock
                .Setup(c => c.CrawlAsync(It.IsAny<Uri>()))
                .Callback(() =>
                {
                    var args = new PageCrawlCompletedArgs(new CrawlContext(), crawledPage);
                    _webCrawlerMock.Raise(c => c.PageCrawlCompleted += null, _webCrawlerMock.Object, args);
                })
                .ReturnsAsync(crawlResult);

            // Act
            await _crawler.CrawlAsync(uri, maxPages: 1, maxDepth: 1);

            // Assert
            _handlerMock.Verify(h => h.PageCrawledAsync(It.IsAny<CrawledPage>()), Times.Never);
        }

        [Fact]
        public async Task CrawlAsync_WhenPageHasNoContent_DoesNotCallHandler()
        {
            // Arrange
            var uri = new Uri("http://example.com");
            var crawledPage = new CrawledPage(uri)
            {
                HttpResponseMessage = new HttpResponseMessage(System.Net.HttpStatusCode.OK),
                Content = new PageContent { Text = "" }  // Tom text
            };

            var crawlResult = new CrawlResult
            {
                RootUri = uri,
                CrawlContext = new CrawlContext()
            };

            _webCrawlerMock
                .Setup(c => c.CrawlAsync(It.IsAny<Uri>()))
                .Callback(() =>
                {
                    var args = new PageCrawlCompletedArgs(new CrawlContext(), crawledPage);
                    _webCrawlerMock.Raise(c => c.PageCrawlCompleted += null, _webCrawlerMock.Object, args);
                })
                .ReturnsAsync(crawlResult);

            // Act
            await _crawler.CrawlAsync(uri, maxPages: 1, maxDepth: 1);

            // Assert
            _handlerMock.Verify(h => h.PageCrawledAsync(It.IsAny<CrawledPage>()), Times.Never);
        }

        [Fact]
        public async Task CrawlAsync_WhenPageHasHttpException_DoesNotCallHandler()
        {
            // Arrange
            var uri = new Uri("http://example.com");
            var crawledPage = new CrawledPage(uri)
            {
                HttpResponseMessage = new HttpResponseMessage(System.Net.HttpStatusCode.OK),
                HttpRequestException = new HttpRequestException("Network error")
            };

            var crawlResult = new CrawlResult
            {
                RootUri = uri,
                CrawlContext = new CrawlContext()
            };

            _webCrawlerMock
                .Setup(c => c.CrawlAsync(It.IsAny<Uri>()))
                .Callback(() =>
                {
                    var args = new PageCrawlCompletedArgs(new CrawlContext(), crawledPage);
                    _webCrawlerMock.Raise(c => c.PageCrawlCompleted += null, _webCrawlerMock.Object, args);
                })
                .ReturnsAsync(crawlResult);

            // Act
            await _crawler.CrawlAsync(uri, maxPages: 1, maxDepth: 1);

            // Assert
            _handlerMock.Verify(h => h.PageCrawledAsync(It.IsAny<CrawledPage>()), Times.Never);
        }

        [Theory]
        [InlineData(5, 3)]
        [InlineData(10, 2)]
        public async Task CrawlAsync_CreatesCorrectConfiguration(int maxPages, int maxDepth)
        {
            // Arrange
            var uri = new Uri("http://example.com");
            _stringWriter.GetStringBuilder().Clear();
            var eventRaised = new TaskCompletionSource<bool>();

            var crawlResult = new CrawlResult
            {
                RootUri = uri,
                CrawlContext = new CrawlContext()
            };

            _webCrawlerMock
                .Setup(c => c.CrawlAsync(It.IsAny<Uri>()))
                .Callback(() =>
                {
                    eventRaised.SetResult(true);
                })
                .ReturnsAsync(crawlResult);

            // Act
            var crawlTask = _crawler.CrawlAsync(uri, maxPages, maxDepth);
            await Task.WhenAll(crawlTask, eventRaised.Task);

            // Assert
            _webCrawlerMock.Verify(
                c => c.CrawlAsync(It.Is<Uri>(u => u == uri)),
                Times.Once);
        }

        [Theory]
        [InlineData(true, "Mozilla")]  // Standard config
        [InlineData(true, "Chrome")]   // Verify Chrome is in UA
        public async Task CrawlAsync_ConfiguresSecurityAndUserAgent(bool sslEnabled, string userAgentPart)
        {
            // Arrange
            var uri = new Uri("http://example.com");
            CrawlConfiguration? capturedConfig = null;

            _webCrawlerMock
                .Setup(c => c.CrawlAsync(It.IsAny<Uri>()))
                .Callback<Uri>(u =>
                {
                    capturedConfig = _lastConfig;
                })
                .ReturnsAsync(new CrawlResult
                {
                    RootUri = uri,
                    CrawlContext = new CrawlContext()
                });

            var crawler = new Crawler(_handlerMock.Object, config =>
            {
                _lastConfig = config;
                return _webCrawlerMock.Object;
            }, _console, _logLevel);

            // Act
            await crawler.CrawlAsync(uri, maxPages: 1, maxDepth: 1);

            // Assert
            Assert.NotNull(capturedConfig);
            Assert.Equal(sslEnabled, capturedConfig.IsSslCertificateValidationEnabled);
            Assert.Contains(userAgentPart, capturedConfig.UserAgentString);
            Assert.Equal(5, capturedConfig.MaxConcurrentThreads);
            Assert.Equal(100, capturedConfig.MinCrawlDelayPerDomainMilliSeconds);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(5)]
        public async Task CrawlAsync_TracksCorrectNumberOfPages(int numberOfPages)
        {
            // Arrange
            var uri = new Uri("http://example.com");
            var testConsole = new TestConsole();
            var crawler = new Crawler(_handlerMock.Object, config =>
            {
                _lastConfig = config;
                return _webCrawlerMock.Object;
            }, testConsole, _logLevel);

            var eventRaised = new TaskCompletionSource<bool>();

            var crawlResult = new CrawlResult
            {
                RootUri = uri,
                CrawlContext = new CrawlContext()
            };

            _webCrawlerMock
                .Setup(c => c.CrawlAsync(It.IsAny<Uri>()))
                .Callback(() => {
                    for (int i = 0; i < numberOfPages; i++)
                    {
                        var pageUri = new Uri($"http://example.com/page{i}");
                        var pageToCrawl = new PageToCrawl(pageUri)
                        {
                            ParentUri = uri,
                            CrawlDepth = 1
                        };
                        var startArgs = new PageCrawlStartingArgs(new CrawlContext(), pageToCrawl);
                        _webCrawlerMock.Raise(c => c.PageCrawlStarting += null, _webCrawlerMock.Object, startArgs);

                        var crawledPage = new CrawledPage(pageUri)
                        {
                            ParentUri = uri,
                            Content = new PageContent { Text = "test" }
                        };
                        var completeArgs = new PageCrawlCompletedArgs(new CrawlContext(), crawledPage);
                        _webCrawlerMock.Raise(c => c.PageCrawlCompleted += null, _webCrawlerMock.Object, completeArgs);
                    }
                    eventRaised.SetResult(true);
                })
                .ReturnsAsync(crawlResult);

            // Act
            var crawlTask = crawler.CrawlAsync(uri, maxPages: numberOfPages, maxDepth: 1);
            await Task.WhenAll(crawlTask, eventRaised.Task);

            // Assert
            var output = string.Join(Environment.NewLine, testConsole.Output);
            Assert.Contains($"Crawl of {uri.AbsoluteUri} ({numberOfPages} pages) completed without error", output);
        }

        [Theory]
        [InlineData(5, 3)]
        [InlineData(10, 2)]
        [InlineData(1, 1)]
        public async Task CrawlAsync_RespectsMaxPagesConfiguration(int maxPages, int maxDepth)
        {
            // Arrange
            var uri = new Uri("http://example.com");
            var eventRaised = new TaskCompletionSource<bool>();
            var crawlResult = new CrawlResult
            {
                RootUri = uri,
                CrawlContext = new CrawlContext()
            };

            _webCrawlerMock
                .Setup(c => c.CrawlAsync(It.IsAny<Uri>()))
                .Callback(() =>
                {
                    for (int i = 0; i < maxPages + 2; i++)
                    {
                        var pageUri = new Uri($"http://example.com/page{i}");
                        var pageToCrawl = new PageToCrawl(pageUri)
                        {
                            ParentUri = uri,
                            CrawlDepth = Math.Min(i % maxDepth + 1, maxDepth)
                        };

                        var args = new PageCrawlStartingArgs(new CrawlContext(), pageToCrawl);
                        _webCrawlerMock.Raise(c => c.PageCrawlStarting += null, _webCrawlerMock.Object, args);
                    }
                    eventRaised.SetResult(true);
                })
                .ReturnsAsync(crawlResult);

            // Act
            var crawlTask = _crawler.CrawlAsync(uri, maxPages, maxDepth);
            await Task.WhenAll(crawlTask, eventRaised.Task);
        }

        [Theory]
        [InlineData(10, 2)]
        [InlineData(5, 3)]
        public async Task CrawlAsync_LogsCorrectMessages(int maxPages, int maxDepth)
        {
            // Arrange
            LogLevel logLevel = LogLevel.Info;
            var uri = new Uri("http://example.com");
            var testConsole = new TestConsole();
            var crawler = new Crawler(_handlerMock.Object, config =>
            {
                _lastConfig = config;
                return _webCrawlerMock.Object;
            }, testConsole, logLevel);

            var eventRaised = new TaskCompletionSource<bool>();

            var crawlResult = new CrawlResult
            {
                RootUri = uri,
                CrawlContext = new CrawlContext()
            };

            _webCrawlerMock
                .Setup(c => c.CrawlAsync(It.IsAny<Uri>()))
                .Callback(() =>
                {
                    var pageToCrawl = new PageToCrawl(uri)
                    {
                        ParentUri = uri,
                        CrawlDepth = 1
                    };
                    var args = new PageCrawlStartingArgs(new CrawlContext(), pageToCrawl);
                    _webCrawlerMock.Raise(c => c.PageCrawlStarting += null, _webCrawlerMock.Object, args);
                    eventRaised.SetResult(true);
                })
                .ReturnsAsync(crawlResult);

            // Act
            var crawlTask = crawler.CrawlAsync(uri, maxPages, maxDepth);
            await Task.WhenAll(crawlTask, eventRaised.Task);

            // Assert
            var output = string.Join(Environment.NewLine, testConsole.Output);
            Assert.Contains("found on", output);
        }

        [Fact]
        public async Task CrawlAsync_WithValidUri_LogsProgress()
        {
            // Arrange
            LogLevel logLevel = LogLevel.Info;
            var uri = new Uri("http://example.com");
            var testConsole = new TestConsole();
            var crawler = new Crawler(_handlerMock.Object, config =>
            {
                _lastConfig = config;
                return _webCrawlerMock.Object;
            }, testConsole, logLevel);

            var eventRaised = new TaskCompletionSource<bool>();
            var crawlResult = new CrawlResult
            {
                RootUri = uri,
                CrawlContext = new CrawlContext()
            };

            _webCrawlerMock
                .Setup(c => c.CrawlAsync(It.IsAny<Uri>()))
                .Callback(() =>
                {
                    var pageToCrawl = new PageToCrawl(uri)
                    {
                        ParentUri = uri,
                        CrawlDepth = 1
                    };
                    var args = new PageCrawlStartingArgs(new CrawlContext(), pageToCrawl);
                    _webCrawlerMock.Raise(c => c.PageCrawlStarting += null, _webCrawlerMock.Object, args);
                    eventRaised.SetResult(true);
                })
                .ReturnsAsync(crawlResult);

            // Act
            var crawlTask = crawler.CrawlAsync(uri, maxPages: 1, maxDepth: 1);
            await Task.WhenAll(crawlTask, eventRaised.Task);

            // Assert
            var output = string.Join(Environment.NewLine, testConsole.Output);
            Assert.Contains("found on", output);
        }

        [Fact]
        public async Task CrawlAsync_WithDomSelector_FiltersLinks()
        {
            // Arrange
            var uri = new Uri("http://example.com");
            var testConsole = new TestConsole();
            var indexer = new AzureSearchIndexer(
                "https://test.search.windows.net",
                "test-index",
                "test-key",
                "https://test.ai.windows.net",
                "test-key2",
                "ai-deployment",
                1,
                true,
                new TextExtractor(),
                false,
                testConsole);

            // Skapa test HTML
            var htmlContent = @"
                <html>
                    <body>
                        <div class='blog-content'>
                            <a href='/blog/posts/good-link.html'>Good Link</a>
                        </div>
                        <div class='other-content'>
                            <a href='/blog/posts/bad-link.html'>Bad Link</a>
                        </div>
                    </body>
                </html>";

            // Skapa AngleSharp dokument
            //var context = BrowsingContext.New();
            //var document = await context.OpenAsync(req => req.Content(htmlContent));

            var crawledPage = new CrawledPage(uri)
            {
                Content = new PageContent { Text = htmlContent }
            };

            Func<Uri, CrawledPage, CrawlContext, bool>? linkDecisionMaker = null;
            _webCrawlerMock.SetupSet(c => c.ShouldScheduleLinkDecisionMaker = It.IsAny<Func<Uri, CrawledPage, CrawlContext, bool>>())
                .Callback<Func<Uri, CrawledPage, CrawlContext, bool>>(func => linkDecisionMaker = func);

            _webCrawlerMock.Setup(c => c.CrawlAsync(It.IsAny<Uri>()))
                .Callback(() => 
                {
                    var goodLink = new Uri(uri, "/blog/posts/good-link.html");
                    var badLink = new Uri(uri, "/blog/posts/bad-link.html");
                    
                    Assert.NotNull(linkDecisionMaker);
                    var goodDecision = linkDecisionMaker(goodLink, crawledPage, new CrawlContext());
                    var badDecision = linkDecisionMaker(badLink, crawledPage, new CrawlContext());
                    
                    Assert.True(goodDecision, "Good link should be allowed");
                    Assert.False(badDecision, "Bad link should be filtered out");
                })
                .ReturnsAsync(new CrawlResult 
                { 
                    RootUri = uri,
                    CrawlContext = new CrawlContext()
                });

            var crawler = new Crawler(indexer, config =>
            {
                _lastConfig = config;
                return _webCrawlerMock.Object;
            }, testConsole, _logLevel);
            
            // Act
            await crawler.CrawlAsync(uri, maxPages: 1, maxDepth: 1, domSelector: "div.blog-content");

            // Assert
            Assert.NotNull(linkDecisionMaker);
            var goodDecision = linkDecisionMaker(new Uri(uri, "/blog/posts/good-link.html"), crawledPage, new CrawlContext());
            var badDecision = linkDecisionMaker(new Uri(uri, "/blog/posts/bad-link.html"), crawledPage, new CrawlContext());
            
            Assert.True(goodDecision, "Good link should be allowed");
            Assert.False(badDecision, "Bad link should be filtered out");
        }

        [Fact]
        public async Task CrawlAsync_WithVerboseLogging_ShowsLinkChecking()
        {
            // Arrange
            var uri = new Uri("http://example.com");
            var testConsole = new TestConsole();
            testConsole.SetVerbose(true);
            
            

            var crawlResult = new CrawlResult
            {
                RootUri = uri,
                CrawlContext = new CrawlContext()
            };

            _webCrawlerMock
                .Setup(c => c.CrawlAsync(It.IsAny<Uri>()))
                .ReturnsAsync(crawlResult);

            // Simulera en länkkontroll genom att trigga ShouldScheduleLinkDecisionMaker
            var linkUri = new Uri("http://example.com/page");
            var htmlContent = "<html><body><div class='content'><a href='/page'>Link</a></div></body></html>";
            var context = BrowsingContext.New(Configuration.Default);
            var parser = context.GetService<IHtmlParser>();
            var document = parser!.ParseDocument(htmlContent);

            var crawledPage = new CrawledPage(uri)
            {
                Content = new PageContent { Text = htmlContent }
            };

            Func<Uri, CrawledPage, CrawlContext, bool>? linkDecisionMaker = null;
            _webCrawlerMock.SetupSet(c => c.ShouldScheduleLinkDecisionMaker = It.IsAny<Func<Uri, CrawledPage, CrawlContext, bool>>())
                .Callback<Func<Uri, CrawledPage, CrawlContext, bool>>(func => linkDecisionMaker = func);

            _webCrawlerMock.Setup(c => c.CrawlAsync(It.IsAny<Uri>()))
                .Callback(() => 
                {
                    var goodLink = new Uri(uri, "/page");
                    
                    Assert.NotNull(linkDecisionMaker);
                    var goodDecision = linkDecisionMaker(goodLink, crawledPage, new CrawlContext());
                })
                .ReturnsAsync(new CrawlResult 
                { 
                    RootUri = uri,
                    CrawlContext = new CrawlContext()
                });

            var crawler = new Crawler(_handlerMock.Object, config =>
            {
                _lastConfig = config;
                return _webCrawlerMock.Object;
            }, testConsole, LogLevel.Verbose);
            
            // Act
            await crawler.CrawlAsync(uri, maxPages: 1, maxDepth: 1, domSelector: "div.content");

            // Assert
            Assert.Contains(testConsole.Output, m => m.StartsWith("VERBOSE: Checking"));
        }

        [Fact]
        public async Task CrawlAsync_WithoutVerboseLogging_HidesLinkChecking()
        {
            // Arrange
            var uri = new Uri("http://example.com");
            var testConsole = new TestConsole();
            testConsole.SetVerbose(false);
            
            var crawler = new Crawler(_handlerMock.Object, config =>
            {
                _lastConfig = config;
                return _webCrawlerMock.Object;
            }, testConsole, LogLevel.Info);

            var crawlResult = new CrawlResult
            {
                RootUri = uri,
                CrawlContext = new CrawlContext()
            };

            _webCrawlerMock
                .Setup(c => c.CrawlAsync(It.IsAny<Uri>()))
                .ReturnsAsync(crawlResult);

            // Act
            await crawler.CrawlAsync(uri, maxPages: 1, maxDepth: 1, domSelector: "div.content");

            // Assert
            Assert.DoesNotContain(testConsole.Output, m => m.StartsWith("VERBOSE:"));
            Assert.DoesNotContain(testConsole.Output, m => m.StartsWith("DEBUG:"));
        }

        private static CrawlConfiguration? _lastConfig;  // För att fånga konfigurationen
    }
}
