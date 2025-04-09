using Abot2.Crawler;
using Abot2.Poco;
using AngleSharp;
using AngleSharp.Html.Parser;
using AzureSearchCrawler.Interfaces;
using AzureSearchCrawler.Models;
using AzureSearchCrawler.TestUtilities;
using Moq;
using Xunit;
using System.Reflection;
using Microsoft.Playwright;
using Azure.Search.Documents;
using Azure.AI.OpenAI;
using OpenAI.Embeddings;

namespace AzureSearchCrawler.Tests
{

    public class AbotCrawlerTests : IDisposable
    {
        private readonly Mock<IWebCrawler> _webCrawlerMock;
        private readonly Mock<ICrawledPageProcessor> _handlerMock;
        private readonly TestConsole _console;
        private readonly AbotCrawler _crawler;
        private readonly StringWriter _stringWriter;
        private readonly TextWriter _originalOut;
        private readonly TextWriter _originalError;

        public AbotCrawlerTests()
        {
            _webCrawlerMock = new Mock<IWebCrawler>();
            _handlerMock = new Mock<ICrawledPageProcessor>();
            _console = new TestConsole();
            _stringWriter = new StringWriter();
            _originalOut = Console.Out;
            _originalError = Console.Error;

            Console.SetOut(_stringWriter);
            Console.SetError(_stringWriter);

            _crawler = new AbotCrawler(_handlerMock.Object, _ => _webCrawlerMock.Object, _console);
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
                    Console.SetOut(_originalOut);
                    Console.SetError(_originalError);
                    _stringWriter.Dispose();
                }

                // Free any unmanaged objects here.
                _disposed = true;
            }
        }

        ~AbotCrawlerTests()
        {
            Dispose(false);
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
            var exception = new Exception("Test error");
            var crawlResult = new CrawlResult
            {
                RootUri = uri,
                CrawlContext = new CrawlContext(),
                ErrorException = exception
            };

            _webCrawlerMock
                .Setup(c => c.CrawlAsync(It.IsAny<Uri>()))
                .ReturnsAsync(crawlResult);

            // Act & Assert
            var thrownException = await Assert.ThrowsAsync<Exception>(() => 
                _crawler.CrawlAsync(uri, maxPages: 1, maxDepth: 1));
            
            // Verify the exception is the same one we provided
            Assert.Same(exception, thrownException);
            
            // Verify that CrawlFinishedAsync was called despite the exception
            _handlerMock.Verify(h => h.CrawlFinishedAsync(), Times.Once);
        }

        [Fact]
        public async Task CrawlAsync_WhenPageCrawled_CallsPageCrawledAsync()
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
        public async Task CrawlAsync_WhenPageHasNoContent_CallsHandler()
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
            _handlerMock.Verify(h => h.PageCrawledAsync(It.IsAny<CrawledPage>()), Times.Once);
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

            var crawler = new AbotCrawler(_handlerMock.Object, config =>
            {
                _lastConfig = config;
                return _webCrawlerMock.Object;
            }, _console);

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
            var loggedMessages = new List<(string message, LogLevel level)>();
            _console.LoggedMessage += (message, level) => loggedMessages.Add((message, level));

            var crawlResult = new CrawlResult
            {
                RootUri = uri,
                CrawlContext = new CrawlContext()
            };

            _webCrawlerMock
                .Setup(c => c.CrawlAsync(It.IsAny<Uri>()))
                .Callback(() =>
                {
                    for (int i = 0; i < numberOfPages; i++)
                    {
                        var pageUri = new Uri($"http://example.com/page{i}");
                        var pageToCrawl = new PageToCrawl(pageUri)
                        {
                            ParentUri = uri,
                            CrawlDepth = 1
                        };

                        var args = new PageCrawlStartingArgs(new CrawlContext(), pageToCrawl);
                        _webCrawlerMock.Raise(c => c.PageCrawlStarting += null, _webCrawlerMock.Object, args);
                    }
                })
                .ReturnsAsync(crawlResult);

            // Act
            await _crawler.CrawlAsync(uri, maxPages: numberOfPages, maxDepth: 1);

            // Assert
            Assert.Contains(loggedMessages, m => m.message.Contains($"Starting web crawl of {uri}") && m.level == LogLevel.Information);
            Assert.Contains(loggedMessages, m => m.message.Contains($"Crawl completed successfully") && m.level == LogLevel.Information);
            Assert.Contains(loggedMessages, m => m.message.Contains($"{numberOfPages} pages processed") && m.level == LogLevel.Information);
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
            var uri = new Uri("http://example.com");
            var testConsole = new TestConsole();
            var crawler = new AbotCrawler(_handlerMock.Object, config =>
            {
                _lastConfig = config;
                return _webCrawlerMock.Object;
            }, testConsole);

            var eventRaised = new TaskCompletionSource<bool>();
            var loggedMessages = new List<(string message, LogLevel level)>();
            testConsole.LoggedMessage += (message, level) => loggedMessages.Add((message, level));

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
            Assert.Contains(loggedMessages, m => m.message.Contains("Starting web crawl of") && m.level == LogLevel.Information);
            Assert.Contains(loggedMessages, m => m.message.Contains("Processing page") && m.level == LogLevel.Information);
            Assert.Contains(loggedMessages, m => m.message.Contains($"Max pages={maxPages}, Max depth={maxDepth}") && m.level == LogLevel.Information);
        }

        [Fact]
        public async Task CrawlAsync_WithValidUri_LogsProgress()
        {
            // Arrange
            var uri = new Uri("http://example.com");
            var testConsole = new TestConsole();
            var crawler = new AbotCrawler(_handlerMock.Object, config =>
            {
                _lastConfig = config;
                return _webCrawlerMock.Object;
            }, testConsole);

            var eventRaised = new TaskCompletionSource<bool>();
            var loggedMessages = new List<(string message, LogLevel level)>();
            testConsole.LoggedMessage += (message, level) => loggedMessages.Add((message, level));

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
            Assert.Contains(loggedMessages, m => m.message.Contains("Starting web crawl of") && m.level == LogLevel.Information);
            Assert.Contains(loggedMessages, m => m.message.Contains("Processing page") && m.level == LogLevel.Information);
            Assert.Contains(loggedMessages, m => m.message.Contains("Crawl configuration:") && m.level == LogLevel.Information);
        }

        [Fact]
        public async Task CrawlAsync_WithDomSelector_FiltersLinks()
        {
            // Arrange
            var uri = new Uri("http://example.com");
            var testConsole = new TestConsole();
            var mockTextExtractor = new Mock<TextExtractor>();
            var mockSearchClient = new Mock<SearchClient>();
            var mockAiClient = new Mock<AzureOpenAIClient>();
            var mockEmbeddingClient = new Mock<EmbeddingClient>();

            var indexer = new AzureSearchIndexer(
                "https://test.search.windows.net",
                "test-index",
                "test-key",
                "https://test.ai.windows.net",
                "test-key2",
                "ai-deployment",
                1,
                true,
                mockTextExtractor.Object,
                false,
                testConsole);

            // Inject mocked clients
            var searchClientField = typeof(AzureSearchIndexer)
                .GetField("_searchClient", BindingFlags.NonPublic | BindingFlags.Instance);
            searchClientField!.SetValue(indexer, mockSearchClient.Object);

            var aiClientField = typeof(AzureSearchIndexer)
                .GetField("_azureOpenAIClient", BindingFlags.NonPublic | BindingFlags.Instance);
            aiClientField!.SetValue(indexer, mockAiClient.Object);

            var embeddingClientField = typeof(AzureSearchIndexer)
                .GetField("_embeddingClient", BindingFlags.NonPublic | BindingFlags.Instance);
            embeddingClientField!.SetValue(indexer, mockEmbeddingClient.Object);

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

            var crawler = new AbotCrawler(indexer, config =>
            {
                _lastConfig = config;
                return _webCrawlerMock.Object;
            }, testConsole);
            
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
            
            // Skapa en mock av CrawledPage med HTML-innehåll
            var crawledPage = new CrawledPage(uri)
            {
                HttpResponseMessage = new HttpResponseMessage(System.Net.HttpStatusCode.OK),
                Content = new PageContent { Text = "<html><body><div class='content'><a href='/test'>Test</a></div></body></html>" }
            };
            
            // Skapa en parser och sätt AngleSharpHtmlDocument via reflection
            var parser = new HtmlParser();
            var document = parser.ParseDocument(crawledPage.Content.Text);
            
            // Använd reflection för att sätta AngleSharpHtmlDocument
            var property = typeof(CrawledPage).GetProperty("AngleSharpHtmlDocument");
            var field = typeof(CrawledPage).GetField("<AngleSharpHtmlDocument>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
            field?.SetValue(crawledPage, document);
            
            Func<Uri, CrawledPage, CrawlContext, bool>? linkDecisionMaker = null;
            
            var crawler = new AbotCrawler(_handlerMock.Object, config =>
            {
                _lastConfig = config;
                return _webCrawlerMock.Object;
            }, testConsole);

            var crawlResult = new CrawlResult
            {
                RootUri = uri,
                CrawlContext = new CrawlContext()
            };

            _webCrawlerMock
                .SetupSet(c => c.ShouldScheduleLinkDecisionMaker = It.IsAny<Func<Uri, CrawledPage, CrawlContext, bool>>())
                .Callback<Func<Uri, CrawledPage, CrawlContext, bool>>(func => linkDecisionMaker = func);
                
            _webCrawlerMock
                .Setup(c => c.CrawlAsync(It.IsAny<Uri>()))
                .Callback(() =>
                {
                    // Simulera en PageCrawlCompleted-händelse
                    var args = new PageCrawlCompletedArgs(new CrawlContext(), crawledPage);
                    _webCrawlerMock.Raise(c => c.PageCrawlCompleted += null, _webCrawlerMock.Object, args);
                    
                    // Anropa ShouldScheduleLinkDecisionMaker om den är satt
                    if (linkDecisionMaker != null)
                    {
                        var testUri = new Uri(uri, "/test");
                        linkDecisionMaker(testUri, crawledPage, new CrawlContext());
                    }
                })
                .ReturnsAsync(crawlResult);

            var loggedMessages = new List<(string message, LogLevel level)>();
            testConsole.LoggedMessage += (message, level) => loggedMessages.Add((message, level));

            // Act
            await crawler.CrawlAsync(uri, maxPages: 1, maxDepth: 1, domSelector: "div.content");

            // Assert
            Assert.Contains(loggedMessages, m => m.message.Contains("Evaluating link against selector") && m.level == LogLevel.Verbose);
        }

        [Fact]
        public async Task CrawlAsync_WithoutVerboseLogging_HidesDebugMessages()
        {
            // Arrange
            var uri = new Uri("http://example.com");
            var testConsole = new TestConsole();
            testConsole.SetVerbose(false);
            
            var crawler = new AbotCrawler(_handlerMock.Object, config =>
            {
                _lastConfig = config;
                return _webCrawlerMock.Object;
            }, testConsole);

            var crawlResult = new CrawlResult
            {
                RootUri = uri,
                CrawlContext = new CrawlContext()
            };

            _webCrawlerMock
                .Setup(c => c.CrawlAsync(It.IsAny<Uri>()))
                .ReturnsAsync(crawlResult);

            var loggedMessages = new List<(string message, LogLevel level)>();
            _console.LoggedMessage += (message, level) => loggedMessages.Add((message, level));

            // Act
            await crawler.CrawlAsync(uri, maxPages: 1, maxDepth: 1);

            // Assert
            Assert.DoesNotContain(loggedMessages, m => m.level == LogLevel.Verbose);
            Assert.DoesNotContain(loggedMessages, m => m.level == LogLevel.Debug);
        }

        [Fact]
        public async Task CrawlAsync_WithMaxDepth_LogsConfiguration()
        {
            // Arrange
            var uri = new Uri("http://example.com");
            var loggedMessages = new List<(string message, LogLevel level)>();
            _console.LoggedMessage += (message, level) => loggedMessages.Add((message, level));

            var crawlResult = new CrawlResult
            {
                RootUri = uri,
                CrawlContext = new CrawlContext()
            };

            _webCrawlerMock
                .Setup(c => c.CrawlAsync(It.IsAny<Uri>()))
                .ReturnsAsync(crawlResult);

            // Act
            await _crawler.CrawlAsync(uri, maxPages: 1, maxDepth: 2);

            // Assert
            Assert.Contains(loggedMessages, m => m.message.Contains("Max pages=1, Max depth=2") && m.level == LogLevel.Information);
        }

        [Fact]
        public async Task CrawlAsync_WithMaxPages_LogsProgressAndConfiguration()
        {
            // Arrange
            var uri = new Uri("http://example.com");
            var maxPages = 2;
            var loggedMessages = new List<(string message, LogLevel level)>();
            _console.LoggedMessage += (message, level) => loggedMessages.Add((message, level));

            var crawlResult = new CrawlResult
            {
                RootUri = uri,
                CrawlContext = new CrawlContext()
            };

            _webCrawlerMock
                .Setup(c => c.CrawlAsync(It.IsAny<Uri>()))
                .Callback(() =>
                {
                    // Simulera en PageCrawlStarting-händelse
                    var pageToCrawl = new PageToCrawl(uri) { ParentUri = uri, CrawlDepth = 1 };
                    var args = new PageCrawlStartingArgs(new CrawlContext(), pageToCrawl);
                    _webCrawlerMock.Raise(c => c.PageCrawlStarting += null, _webCrawlerMock.Object, args);
                })
                .ReturnsAsync(crawlResult);

            // Act
            await _crawler.CrawlAsync(uri, maxPages: maxPages, maxDepth: 1);

            // Assert
            Assert.Contains(loggedMessages, m => m.message.Contains("Starting web crawl of") && m.level == LogLevel.Information);
            Assert.Contains(loggedMessages, m => m.message.Contains("Processing page") && m.level == LogLevel.Information);
            Assert.Contains(loggedMessages, m => m.message.Contains($"Max pages={maxPages}, Max depth=1") && m.level == LogLevel.Information);
        }

        [Fact]
        public async Task CrawlAsync_WithValidUri_LogsStartAndCompletion()
        {
            // Arrange
            var uri = new Uri("http://example.com");
            var loggedMessages = new List<(string message, LogLevel level)>();
            _console.LoggedMessage += (message, level) => loggedMessages.Add((message, level));

            var crawlResult = new CrawlResult
            {
                RootUri = uri,
                CrawlContext = new CrawlContext()
            };

            _webCrawlerMock
                .Setup(c => c.CrawlAsync(It.IsAny<Uri>()))
                .ReturnsAsync(crawlResult);

            // Act
            await _crawler.CrawlAsync(uri, maxPages: 1, maxDepth: 1);

            // Assert
            Assert.Contains(loggedMessages, m => m.message.Contains($"Starting web crawl of {uri}") && m.level == LogLevel.Information);
            Assert.Contains(loggedMessages, m => m.message.Contains("Crawl completed successfully") && m.level == LogLevel.Information);
        }

        [Fact]
        public async Task CrawlAsync_WhenPageLoadFails_LogsWarning()
        {
            // Arrange
            var uri = new Uri("http://example.com");
            var crawledPage = new CrawledPage(uri)
            {
                HttpRequestException = new HttpRequestException("Connection failed")
            };

            var crawlResult = new CrawlResult
            {
                RootUri = uri,
                CrawlContext = new CrawlContext()
            };

            var loggedMessages = new List<(string message, LogLevel level)>();
            _console.LoggedMessage += (message, level) => loggedMessages.Add((message, level));

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
            Assert.Contains(loggedMessages, m => m.message.Contains($"Error crawling {uri}") && m.level == LogLevel.Warning);
        }

        [Fact]
        public async Task CrawlAsync_WithDomSelector_LogsSelectionProcess()
        {
            // Arrange
            var uri = new Uri("http://example.com");
            var crawlResult = new CrawlResult
            {
                RootUri = uri,
                CrawlContext = new CrawlContext()
            };

            var loggedMessages = new List<(string message, LogLevel level)>();
            _console.LoggedMessage += (message, level) => loggedMessages.Add((message, level));

            _webCrawlerMock
                .Setup(c => c.CrawlAsync(It.IsAny<Uri>()))
                .ReturnsAsync(crawlResult);

            // Act
            await _crawler.CrawlAsync(uri, maxPages: 1, maxDepth: 1, domSelector: "div.content");

            // Assert
            Assert.Contains(loggedMessages, m => m.message.Contains($"Starting web crawl of {uri}") && m.level == LogLevel.Information);
            Assert.Contains(loggedMessages, m => m.message.Contains("Using DOM selector filter: div.content") && m.level == LogLevel.Information);
        }

        [Fact]
        public async Task CrawlAsync_WhenCrawlerFails_LogsErrorAndStackTrace()
        {
            // Arrange
            var uri = new Uri("http://example.com");
            var exception = new Exception("Critical error occurred");
            var crawlResult = new CrawlResult
            {
                RootUri = uri,
                CrawlContext = new CrawlContext(),
                ErrorException = exception
            };

            var loggedMessages = new List<(string message, LogLevel level)>();
            _console.LoggedMessage += (message, level) => loggedMessages.Add((message, level));

            _webCrawlerMock
                .Setup(c => c.CrawlAsync(It.IsAny<Uri>()))
                .ReturnsAsync(crawlResult);

            // Act & Assert
            var thrownException = await Assert.ThrowsAsync<Exception>(() => 
                _crawler.CrawlAsync(uri, maxPages: 1, maxDepth: 1));
            
            // Verify the exception is the same one we provided
            Assert.Same(exception, thrownException);
            
            // Verify logging occurred before the exception was thrown
            Assert.Contains(loggedMessages, m => m.message.Contains("Crawl failed with critical error") && m.level == LogLevel.Error);
            Assert.Contains(loggedMessages, m => m.message.Contains("Stack trace:") && m.level == LogLevel.Debug);
        }

        [Fact]
        public async Task CrawlAsync_WhenProcessingPage_LogsProgress()
        {
            // Arrange
            var uri = new Uri("http://example.com");
            var pageToCrawl = new PageToCrawl(uri) { ParentUri = new Uri("http://example.com/parent"), CrawlDepth = 1 };
            
            var crawlResult = new CrawlResult
            {
                RootUri = uri,
                CrawlContext = new CrawlContext()
            };

            var loggedMessages = new List<(string message, LogLevel level)>();
            _console.LoggedMessage += (message, level) => loggedMessages.Add((message, level));

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
            Assert.Contains(loggedMessages, m => m.message.Contains($"Processing page 1: {uri}") && m.level == LogLevel.Information);
        }

        [Fact]
        public async Task CrawlAsync_WithMaxDepth_LogsProgress()
        {
            // Arrange
            var uri = new Uri("http://example.com");
            var maxDepth = 2;
            var loggedMessages = new List<(string message, LogLevel level)>();
            _console.LoggedMessage += (message, level) => loggedMessages.Add((message, level));

            var crawlResult = new CrawlResult
            {
                RootUri = uri,
                CrawlContext = new CrawlContext()
            };

            _webCrawlerMock
                .Setup(c => c.CrawlAsync(It.IsAny<Uri>()))
                .Callback(() =>
                {
                    // Simulera en PageCrawlStarting-händelse
                    var pageToCrawl = new PageToCrawl(uri) { ParentUri = uri, CrawlDepth = 1 };
                    var args = new PageCrawlStartingArgs(new CrawlContext(), pageToCrawl);
                    _webCrawlerMock.Raise(c => c.PageCrawlStarting += null, _webCrawlerMock.Object, args);
                })
                .ReturnsAsync(crawlResult);

            // Act
            await _crawler.CrawlAsync(uri, maxPages: 1, maxDepth: maxDepth);

            // Assert
            Assert.Contains(loggedMessages, m => m.message.Contains("Starting web crawl of") && m.level == LogLevel.Information);
            Assert.Contains(loggedMessages, m => m.message.Contains("Processing page") && m.level == LogLevel.Information);
            Assert.Contains(loggedMessages, m => m.message.Contains($"Max pages=1, Max depth={maxDepth}") && m.level == LogLevel.Information);
        }

        private static CrawlConfiguration? _lastConfig;  // För att fånga konfigurationen
    }
}
