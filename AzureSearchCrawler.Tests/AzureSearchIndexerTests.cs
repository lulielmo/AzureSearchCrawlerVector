using Abot2.Poco;
using Azure;
using Azure.AI.OpenAI;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using AzureSearchCrawler.Tests.Mocks;
using Moq;
using OpenAI.Embeddings;
using System.Reflection;
using Xunit;
using static AzureSearchCrawler.AzureSearchIndexer;

namespace AzureSearchCrawler.Tests
{
    public class AzureSearchIndexerTests : IDisposable
    {
        private readonly Mock<SearchClient> _searchClientMock;
        private readonly Mock<AzureOpenAIClient> _aiClientMock;
        private readonly Mock<EmbeddingClient> _embeddingClientMock;
        private readonly Mock<TextExtractor> _textExtractor;
        private readonly TestConsole _console;
        private readonly AzureSearchIndexer _indexer;
        private const int IndexingBatchSize = 1000;

        public AzureSearchIndexerTests()
        {
            _searchClientMock = new Mock<SearchClient>();
            _aiClientMock = new Mock<AzureOpenAIClient>();
            _embeddingClientMock = new Mock<EmbeddingClient>();
            _textExtractor = new Mock<TextExtractor>();
            _console = new TestConsole();

            // Setup default TextExtractor behavior
            _textExtractor
                .Setup(x => x.ExtractText(It.IsAny<bool>(), It.IsAny<string>()))
                .Returns(new Dictionary<string, string>
                {
                    ["title"] = "Test Title",
                    ["content"] = "Test Content"
                });

            var indexer = new AzureSearchIndexer(
                "https://test.search.windows.net",
                "test-index",
                "test-key",
                "https://test.ai.windows.net",
                "test-key2",
                "ai-deployment",
                1,
                extractText: true,
                textExtractor: _textExtractor.Object,
                dryRun: false,
                console: _console);

            var searchClientField = typeof(AzureSearchIndexer)
                .GetField("_searchClient", BindingFlags.NonPublic | BindingFlags.Instance);
            searchClientField!.SetValue(indexer, _searchClientMock.Object);

            var aiClientField = typeof(AzureSearchIndexer)
                .GetField("_azureOpenAIClient", BindingFlags.NonPublic | BindingFlags.Instance);
            aiClientField!.SetValue(indexer, _aiClientMock.Object);

            var embeddingClientField = typeof(AzureSearchIndexer)
                .GetField("_embeddingClient", BindingFlags.NonPublic | BindingFlags.Instance);
            embeddingClientField!.SetValue(indexer, _embeddingClientMock.Object);

            _indexer = indexer;
        }

        public void Dispose()
        {
            _console.Dispose();
        }

        [Fact]
        public async Task CrawlFinishedAsync_WithNonEmptyQueue_LogsWarning()
        {
            // Arrange
            _searchClientMock
                .Setup(c => c.MergeOrUploadDocumentsAsync(
                    It.IsAny<IEnumerable<WebPage>>(),
                    It.IsAny<IndexDocumentsOptions>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("SearchClient cannot be initialized"));

            //_embeddingClientMock
            //    .Setup(c => c.GenerateEmbeddingAsync(
            //        It.IsAny<string>(),
            //        It.IsAny<EmbeddingGenerationOptions>(),
            //        It.IsAny<CancellationToken>())).ReturnsAsync(null);

            // Lägg till 5 sidor
            for (int i = 0; i < 5; i++)
            {
                var crawledPage = new CrawledPage(new Uri($"http://example.com/{i}"))
                {
                    Content = new PageContent
                    {
                        Text = "<html><body>Test content</body></html>"
                    }
                };
                await _indexer.PageCrawledAsync(crawledPage);
            }

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await _indexer.CrawlFinishedAsync());
            Assert.Contains("SearchClient cannot be initialized", exception.Message);
        }

        [Fact]
        public async Task PageCrawledAsync_WithEmptyContent_LogsWarning()
        {
            // Arrange
            _textExtractor
                .Setup(x => x.ExtractText(It.IsAny<bool>(), It.IsAny<string>()))
                .Returns(new Dictionary<string, string>
                {
                    ["title"] = "Test Title",
                    ["content"] = ""
                });
            var crawledPage = new CrawledPage(new Uri("http://example.com"))
            {
                Content = new PageContent
                {
                    Text = ""
                }
            };

            // Act
            await _indexer.PageCrawledAsync(crawledPage);

            // Assert
            var output = string.Join(Environment.NewLine, _console.Output);
            Assert.Contains("No content for page", output);
        }

        [Fact]
        public async Task PageCrawledAsync_WithNullContent_LogsWarning()
        {
            // Arrange
            var crawledPage = new CrawledPage(new Uri("http://example.com"))
            {
                Content = null
            };

            // Act
            await _indexer.PageCrawledAsync(crawledPage);

            // Assert
            var output = string.Join(Environment.NewLine, _console.Output);
            Assert.Contains("No content for page", output);
        }

        [Fact]
        public async Task IndexPageAsync_WithDryRun_LogsAndDoesNotIndex()
        {
            // Arrange
            var indexer = new AzureSearchIndexer(
                "https://test.search.windows.net",
                "test-index",
                "test-key",
                "https://test.ai.windows.net",
                "test-key2",
                "ai-deployment",
                1,
                true,
                _textExtractor.Object,
                dryRun: true,
                console: _console);

            // Sätt searchClient via reflection
            var searchClientField = typeof(AzureSearchIndexer)
                .GetField("_searchClient", BindingFlags.NonPublic | BindingFlags.Instance);
            searchClientField!.SetValue(indexer, _searchClientMock.Object);

            var content = new Dictionary<string, string>
            {
                ["title"] = "Test Title",
                ["content"] = "Test Content"
            };

            // Act
            await indexer.IndexPageAsync("http://example.com", content);

            // Assert
            var output = string.Join(Environment.NewLine, _console.Output);
            Assert.Contains("[DRY RUN] Would index page:", output);
            _searchClientMock.Verify(
                c => c.MergeOrUploadDocumentsAsync(
                    It.IsAny<IEnumerable<WebPage>>(),
                    It.IsAny<IndexDocumentsOptions>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task IndexPageAsync_WithInvalidState_ThrowsException()
        {
            // Arrange
            var indexer = new AzureSearchIndexer(
                "https://test.search.windows.net",
                "test-index",
                "test-key",
                "https://test.ai.windows.net",
                "test-key2",
                "ai-deployment",
                1,
                true,
                _textExtractor.Object,
                dryRun: false,
                console: _console);

            // Sätt searchClient via reflection
            var searchClientField = typeof(AzureSearchIndexer)
                .GetField("_searchClient", BindingFlags.NonPublic | BindingFlags.Instance);
            searchClientField!.SetValue(indexer, _searchClientMock.Object);

            _searchClientMock
                .Setup(c => c.IndexDocumentsAsync(
                    It.IsAny<IndexDocumentsBatch<SearchDocument>>(),
                    It.IsAny<IndexDocumentsOptions>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RequestFailedException(403, "Forbidden"));

            // Act & Assert
            var exception = await Assert.ThrowsAsync<RequestFailedException>(async () =>
                await indexer.IndexPageAsync("http://example.com", new Dictionary<string, string>
                {
                    ["title"] = "Test Title",
                    ["content"] = "Test Content"
                }));

            Assert.Equal(403, exception.Status);
            Assert.Contains("Forbidden", exception.Message);
        }

        [Fact]
        public void Constructor_WithNullConsole_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new AzureSearchIndexer(
                "https://test.search.windows.net",
                "test-index",
                "test-key",
                "https://test.ai.windows.net",
                "test-key2",
                "ai-deployment",
                1,
                true,
                _textExtractor.Object,
                false,
                console: null!));
        }

        [Fact]
        public async Task PageCrawledAsync_WithNullCrawledPage_ThrowsArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                _indexer.PageCrawledAsync(null!));
        }

        [Fact]
        public async Task PageCrawledAsync_WithNullContent_LogsWarningAndDoesNotIndex()
        {
            // Arrange
            var crawledPage = new CrawledPage(new Uri("http://example.com"))
            {
                Content = null
            };

            // Act
            await _indexer.PageCrawledAsync(crawledPage);

            // Assert
            var output = string.Join(Environment.NewLine, _console.Output);
            Assert.Contains("No content for page", output);
        }

        [Fact]
        public async Task CrawlFinishedAsync_WithEmptyQueue_CompletesSuccessfully()
        {
            // Arrange
            _searchClientMock
                .Setup(c => c.MergeOrUploadDocumentsAsync(
                    It.IsAny<IEnumerable<WebPage>>(),
                    It.IsAny<IndexDocumentsOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(
                    new MockIndexDocumentsResult(new MockHttpResponse()),
                    new MockHttpResponse()));

            // Act
            await _indexer.CrawlFinishedAsync();

            // Assert
            var output = string.Join(Environment.NewLine, _console.Output);
            Assert.DoesNotContain("Error:", output);
            Assert.True(true, "Method completed without throwing exception");
        }

        [Fact]
        public async Task IndexBatchIfNecessary_WithNullSearchClient_ReturnsImmediately()
        {
            // Arrange
            _textExtractor
                .Setup(x => x.ExtractText(It.IsAny<bool>(), It.IsAny<string>()))
                .Returns(new Dictionary<string, string>
                {
                    ["title"] = "Test Title",
                    ["content"] = "Test Content"
                });
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
                _textExtractor.Object,
                dryRun: true,  // Detta gör att _searchClient inte initialiseras
                console: testConsole);

            var crawledPage = new CrawledPage(new Uri("http://example.com"))
            {
                Content = new PageContent
                {
                    Text = "<html><body><h1>Test Title</h1><p>Test content</p></body></html>"
                }
            };

            // Act
            await indexer.PageCrawledAsync(crawledPage);

            // Assert
            var output = string.Join(Environment.NewLine, testConsole.Output);
            Assert.Contains("[DRY RUN] Would index page: http://example.com", output);
        }

        [Fact]
        public async Task CrawlFinishedAsync_WithPartialBatch_IndexesRemainingPages()
        {
            // Arrange
            _textExtractor
                .Setup(x => x.ExtractText(It.IsAny<bool>(), It.IsAny<string>()))
                .Returns(new Dictionary<string, string>
                {
                    ["title"] = "Test Title",
                    ["content"] = "Test Content"
                });

            _searchClientMock
                .Setup(c => c.MergeOrUploadDocumentsAsync(
                    It.IsAny<IEnumerable<WebPage>>(),
                    It.IsAny<IndexDocumentsOptions>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(new MockIndexDocumentsResult(new MockHttpResponse()) as Response<IndexDocumentsResult>));

            for (int i = 0; i < 3; i++)
            {
                var crawledPage = new CrawledPage(new Uri($"http://example.com/{i}"))
                {
                    Content = new PageContent
                    {
                        Text = $"<html><body>Test content {i}</body></html>"
                    }
                };
                await _indexer.PageCrawledAsync(crawledPage);
            }

            // Act
            await _indexer.CrawlFinishedAsync();

            // Assert
            var output = string.Join(Environment.NewLine, _console.Output);
            Assert.Contains("Indexing batch of 3", output);
            _searchClientMock.Verify(
                c => c.MergeOrUploadDocumentsAsync(
                    It.IsAny<IEnumerable<WebPage>>(),
                    It.IsAny<IndexDocumentsOptions>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task PageCrawledAsync_WithMultipleBatches_IndexesCorrectly()
        {
            // Arrange
            _textExtractor
                .Setup(x => x.ExtractText(It.IsAny<bool>(), It.IsAny<string>()))
                .Returns(new Dictionary<string, string>
                {
                    ["title"] = "Test Title",
                    ["content"] = "Test Content"
                });
            var indexedBatches = 0;
            _searchClientMock
                .Setup(c => c.MergeOrUploadDocumentsAsync(
                    It.IsAny<IEnumerable<WebPage>>(),
                    It.IsAny<IndexDocumentsOptions>(),
                    It.IsAny<CancellationToken>()))
                .Callback(() => indexedBatches++)
                .Returns(Task.FromResult(new MockIndexDocumentsResult(new MockHttpResponse()) as Response<IndexDocumentsResult>));

            // Act
            for (int i = 0; i < 25; i++) // Ökat från 15 till 25 för att säkerställa flera batches
            {
                var crawledPage = new CrawledPage(new Uri($"http://example.com/{i}"))
                {
                    Content = new PageContent
                    {
                        Text = $"<html><body>Test content {i}</body></html>"
                    }
                };
                await _indexer.PageCrawledAsync(crawledPage);

                // Tvinga fram indexering efter var 10:e sida
                if (i > 0 && i % 10 == 0)
                {
                    await _indexer.CrawlFinishedAsync();
                }
            }

            await _indexer.CrawlFinishedAsync();

            // Assert
            Assert.True(indexedBatches > 1, "Should have indexed multiple batches");
            _searchClientMock.Verify(
                c => c.MergeOrUploadDocumentsAsync(
                    It.IsAny<IEnumerable<WebPage>>(),
                    It.IsAny<IndexDocumentsOptions>(),
                    It.IsAny<CancellationToken>()),
                Times.AtLeast(2));
        }

        [Fact]
        public async Task IndexBatchIfNecessary_WhenExceptionOccurs_LogsWarningAndRequeuesPages()
        {
            // Arrange
            _textExtractor
                .Setup(x => x.ExtractText(It.IsAny<bool>(), It.IsAny<string>()))
                .Returns(new Dictionary<string, string>
                {
                    ["title"] = "Test Title",
                    ["content"] = "Test Content"
                });
            _searchClientMock
                .Setup(c => c.MergeOrUploadDocumentsAsync(
                    It.IsAny<IEnumerable<WebPage>>(),
                    It.IsAny<IndexDocumentsOptions>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Test exception"));

            var crawledPage = new CrawledPage(new Uri("http://example.com"))
            {
                Content = new PageContent
                {
                    Text = "<html><body>Test content</body></html>"
                }
            };

            // Act
            await _indexer.PageCrawledAsync(crawledPage);
            await Assert.ThrowsAsync<Exception>(async () => await _indexer.CrawlFinishedAsync());

            // Assert
            var output = string.Join(Environment.NewLine, _console.Output);
            Assert.Contains("Indexing batch of 1", output);
            Assert.Contains("Error indexing batch:", output);
            Assert.Contains("Error: indexing queue is still not empty at the end", output);
        }

        [Fact]
        public async Task IndexPageAsync_WithValidContent_IndexesCorrectly()
        {
            // Arrange
            var url = "http://example.com";
            var content = new Dictionary<string, string>
            {
                ["title"] = "Test Title",
                ["content"] = "Test Content"
            };

            var capturedDocument = new SearchDocument();
            _searchClientMock
                .Setup(c => c.IndexDocumentsAsync(
                    It.IsAny<IndexDocumentsBatch<SearchDocument>>(),
                    It.IsAny<IndexDocumentsOptions>(),
                    It.IsAny<CancellationToken>()))
                .Callback<IndexDocumentsBatch<SearchDocument>, IndexDocumentsOptions, CancellationToken>(
                    (batch, options, token) =>
                    {
                        var doc = (SearchDocument)batch.Actions[0].Document;
                        capturedDocument["id"] = doc["id"];
                        capturedDocument["title"] = doc["title"];
                        capturedDocument["content"] = doc["content"];
                    })
                .ReturnsAsync(Response.FromValue(
                    new MockIndexDocumentsResult(new MockHttpResponse()),
                    new MockHttpResponse()));

            // Act
            await _indexer.IndexPageAsync(url, content);

            // Assert
            Assert.Equal(url, capturedDocument["id"]);
            Assert.Equal("Test Title", capturedDocument["title"]);
            Assert.Equal("Test Content", capturedDocument["content"]);
        }

        [Fact]
        public async Task IndexPageAsync_WithMissingContent_UsesEmptyStrings()
        {
            // Arrange
            var indexer = new AzureSearchIndexer(
                "https://test.search.windows.net",
                "test-index",
                "test-key",
                "https://test.ai.windows.net",
                "test-key2",
                "ai-deployment",
                1,
                true,
                _textExtractor.Object,
                dryRun: true,
                console: _console);

            var content = new Dictionary<string, string>
            {
                ["title"] = "Test Title"
                // "content" saknas medvetet
            };

            // Act
            await indexer.IndexPageAsync("http://example.com", content);

            // Assert
            var output = string.Join(Environment.NewLine, _console.Output);
            Assert.Contains("[DRY RUN] Would index page: http://example.com", output);
        }

        [Fact]
        public async Task PageCrawledAsync_WhenQueueExceedsBatchSize_TriggersImmediateIndexing()
        {
            // Arrange
            _searchClientMock
                .Setup(c => c.MergeOrUploadDocumentsAsync(
                    It.IsAny<IEnumerable<WebPage>>(),
                    It.IsAny<IndexDocumentsOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(
                    new MockIndexDocumentsResult(new MockHttpResponse()),
                    new MockHttpResponse()));

            // Lägg till sidor tills vi överstiger batchstorleken
            for (int i = 0; i < 15; i++)
            {
                var crawledPage = new CrawledPage(new Uri($"http://example.com/{i}"))
                {
                    Content = new PageContent { Text = "<html><body>Test content</body></html>" }
                };
                await _indexer.PageCrawledAsync(crawledPage);
            }

            // Act
            await _indexer.CrawlFinishedAsync();

            // Assert
            _searchClientMock.Verify(
                c => c.MergeOrUploadDocumentsAsync(
                    It.Is<IEnumerable<WebPage>>(pages => pages.Count() == 10),
                    It.IsAny<IndexDocumentsOptions>(),
                    It.IsAny<CancellationToken>()),
                Times.AtLeastOnce());
        }

        [Fact]
        public async Task CrawlFinishedAsync_WithRemainingItemsInQueue_LogsWarning()
        {
            // Arrange
            _textExtractor
                .Setup(x => x.ExtractText(It.IsAny<bool>(), It.IsAny<string>()))
                .Returns(new Dictionary<string, string>
                {
                    ["title"] = "Test Title",
                    ["content"] = "Test Content"
                });

            _searchClientMock
                .Setup(c => c.MergeOrUploadDocumentsAsync(
                    It.IsAny<IEnumerable<WebPage>>(),
                    It.IsAny<IndexDocumentsOptions>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Simulated indexing error"));

            var crawledPage = new CrawledPage(new Uri("http://example.com"))
            {
                Content = new PageContent { Text = "<html><body>Test content</body></html>" }
            };
            await _indexer.PageCrawledAsync(crawledPage);

            // Act & Assert
            await Assert.ThrowsAsync<Exception>(async () => await _indexer.CrawlFinishedAsync());

            var output = string.Join(Environment.NewLine, _console.Output);
            Assert.Contains("Indexing batch of 1", output);
            Assert.Contains("Error indexing batch:", output);
            Assert.Contains("Error: indexing queue is still not empty at the end", output);
        }

        [Theory]
        [InlineData("", "test-index", "test-key", "searchServiceEndpoint")]
        [InlineData(" ", "test-index", "test-key", "searchServiceEndpoint")]
        [InlineData("https://test.search.windows.net", "", "test-key", "indexName")]
        [InlineData("https://test.search.windows.net", "  ", "test-key", "indexName")]
        [InlineData("https://test.search.windows.net", "test-index", "", "adminApiKey")]
        [InlineData("https://test.search.windows.net", "test-index", "  ", "adminApiKey")]
        public void Constructor_WithInvalidParameters_ThrowsArgumentException(
            string endpoint, string index, string key, string expectedParamName)
        {
            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => new AzureSearchIndexer(
                endpoint, index, key,
                "https://test.ai.windows.net",
                "test-key2",
                "ai-deployment",
                1,
                extractText: true,
                textExtractor: _textExtractor.Object,
                dryRun: false,
                console: new TestConsole()));

            Assert.Equal(expectedParamName, exception.ParamName);
            Assert.Contains("Value cannot be null or empty.", exception.Message);
        }

        [Fact]
        public void Constructor_WithDryRun_DoesNotCreateSearchClient()
        {
            // Arrange & Act
            var indexer = new AzureSearchIndexer(
                "https://test.search.windows.net",
                "test-index",
                "test-key",
                "https://test.ai.windows.net",
                "test-key2",
                "ai-deployment",
                1,
                extractText: true,
                textExtractor: _textExtractor.Object,
                dryRun: true,  // Aktivera dry-run
                console: new TestConsole());

            // Assert
            // Använd reflection för att kontrollera _searchClient
            var searchClientField = typeof(AzureSearchIndexer)
                .GetField("_searchClient", BindingFlags.NonPublic | BindingFlags.Instance);
            var searchClient = searchClientField!.GetValue(indexer);

            Assert.Null(searchClient);
        }

        [Fact]
        public void ExtractPageContent_WithNullContent_ReturnsEmptyDictionary()
        {
            // Arrange
            var crawledPage = new CrawledPage(new Uri("http://example.com"))
            {
                Content = null
            };

            // Act
            var result = _indexer.ExtractPageContent(crawledPage);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(string.Empty, result["title"]);
            Assert.Equal(string.Empty, result["content"]);
        }

        [Fact]
        public void ExtractPageContent_WithValidContent_ReturnsExtractedContent()
        {
            // Arrange
            var expectedContent = "Test Content";
            var expectedTitle = "Test Title";
            _textExtractor
                .Setup(x => x.ExtractText(It.IsAny<bool>(), It.IsAny<string>()))
                .Returns(new Dictionary<string, string>
                {
                    ["title"] = expectedTitle,
                    ["content"] = expectedContent
                });

            var crawledPage = new CrawledPage(new Uri("http://example.com"))
            {
                Content = new PageContent { Text = "<html><body>Test</body></html>" }
            };

            // Act
            var result = _indexer.ExtractPageContent(crawledPage);

            // Assert
            Assert.Equal(expectedTitle, result["title"]);
            Assert.Equal(expectedContent, result["content"]);
        }

        [Fact]
        public void GetOrCreateSearchClient_WhenDryRun_ReturnsNull()
        {
            // Arrange
            var indexer = new AzureSearchIndexer(
                "https://test.search.windows.net",
                "test-index",
                "test-key",
                "https://test.ai.windows.net",
                "test-key2",
                "ai-deployment",
                1,
                extractText: true,
                textExtractor: _textExtractor.Object,
                dryRun: true,
                console: new TestConsole());

            // Act
            // Anropa GetOrCreateSearchClient via reflection eftersom den är private
            var method = typeof(AzureSearchIndexer)
                .GetMethod("GetOrCreateSearchClient", BindingFlags.NonPublic | BindingFlags.Instance);
            var result = method!.Invoke(indexer, null);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void GetOrCreateSearchClient_WhenMissingConfiguration_ThrowsArgumentException()
        {
            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => new AzureSearchIndexer(
                searchServiceEndpoint: "",  // Tom sträng istället för null
                indexName: "test-index",
                adminApiKey: "test-key",
                "https://test.ai.windows.net",
                "test-key2",
                "ai-deployment",
                1,
                extractText: true,
                textExtractor: _textExtractor.Object,
                dryRun: false,
                console: _console));

            Assert.Contains("searchServiceEndpoint", exception.Message);
        }

        [Fact]
        public async Task IndexPageAsync_WhenDryRun_LogsAndReturns()
        {
            // Arrange
            var indexer = new AzureSearchIndexer(
                "https://test.search.windows.net",
                "test-index",
                "test-key",
                "https://test.ai.windows.net",
                "test-key2",
                "ai-deployment",
                1,
                extractText: true,
                textExtractor: _textExtractor.Object,
                dryRun: true,
                console: _console);

            // Act
            await indexer.IndexPageAsync("http://example.com", new Dictionary<string, string>
            {
                ["title"] = "Test Title",
                ["content"] = "Test Content"
            });

            // Assert
            var output = string.Join(Environment.NewLine, _console.Output);
            Assert.Contains("[DRY RUN] Would index page: http://example.com", output);
        }

        [Fact]
        public async Task IndexPageAsync_WhenNotDryRunAndMissingConfiguration_ThrowsRequestFailedException()
        {
            // Arrange
            var indexer = new AzureSearchIndexer(
                "https://test.search.windows.net",
                "test-index",
                "invalid-key", // Använd ogiltig nyckel för att framkalla 403
                "https://test.ai.windows.net",
                "test-key2",
                "ai-deployment",
                1,
                extractText: true,
                textExtractor: _textExtractor.Object,
                dryRun: false,
                console: _console);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<RequestFailedException>(async () =>
                await indexer.IndexPageAsync("http://example.com", new Dictionary<string, string>
                {
                    ["title"] = "Test Title",
                    ["content"] = "Test Content"
                }));

            Assert.Equal(403, exception.Status);
        }

        [Fact]
        public async Task PageCrawledAsync_WithValidContent_AddsToQueueAndDoesNotIndex()
        {
            // Arrange
            var crawledPage = new CrawledPage(new Uri("http://example.com"))
            {
                Content = new PageContent { Text = "<html><body>Test content</body></html>" }
            };

            _textExtractor
                .Setup(x => x.ExtractText(It.IsAny<bool>(), It.IsAny<string>()))
                .Returns(new Dictionary<string, string>
                {
                    ["title"] = "Test Title",
                    ["content"] = "Test Content"
                });

            // Act
            await _indexer.PageCrawledAsync(crawledPage);

            // Assert
            _searchClientMock.Verify(
                c => c.MergeOrUploadDocumentsAsync(
                    It.IsAny<IEnumerable<WebPage>>(),
                    It.IsAny<IndexDocumentsOptions>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task PageCrawledAsync_WithNullPage_ThrowsArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await _indexer.PageCrawledAsync(null!));
            //await Assert.ThrowsAsync<ArgumentNullException>(
            //    async () => await _indexer.PageCrawledAsync(default(CrawledPage)));
        }

        [Fact]
        public async Task IndexBatchIfNecessary_WhenQueueIsFull_IndexesPages()
        {
            // Arrange
            _searchClientMock
                .Setup(c => c.MergeOrUploadDocumentsAsync(
                    It.IsAny<IEnumerable<WebPage>>(),
                    It.IsAny<IndexDocumentsOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Response.FromValue(
                    new MockIndexDocumentsResult(new MockHttpResponse()),
                    new MockHttpResponse()));

            // Fyll kön med 1000 sidor
            for (int i = 0; i < 1000; i++)
            {
                var crawledPage = new CrawledPage(new Uri($"http://example.com/{i}"))
                {
                    Content = new PageContent { Text = "<html><body>Test content</body></html>" }
                };
                await _indexer.PageCrawledAsync(crawledPage);
            }

            // Act
            await _indexer.CrawlFinishedAsync();

            // Assert
            _searchClientMock.Verify(
                c => c.MergeOrUploadDocumentsAsync(
                    It.IsAny<IEnumerable<WebPage>>(),
                    It.IsAny<IndexDocumentsOptions>(),
                    It.IsAny<CancellationToken>()),
                Times.AtLeastOnce());
        }

        [Fact]
        public async Task IndexBatchIfNecessary_WhenBatchFails_LogsError()
        {
            // Arrange
            _searchClientMock
                .Setup(c => c.MergeOrUploadDocumentsAsync(
                    It.IsAny<IEnumerable<WebPage>>(),
                    It.IsAny<IndexDocumentsOptions>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Simulated indexing error"));

            // Lägg till några sidor i kön
            for (int i = 0; i < 5; i++)
            {
                var crawledPage = new CrawledPage(new Uri($"http://example.com/{i}"))
                {
                    Content = new PageContent { Text = "<html><body>Test content</body></html>" }
                };
                await _indexer.PageCrawledAsync(crawledPage);
            }

            // Act & Assert
            var exception = await Assert.ThrowsAsync<Exception>(
                async () => await _indexer.CrawlFinishedAsync());
            Assert.Equal("Simulated indexing error", exception.Message);

            var output = string.Join(Environment.NewLine, _console.Output);
            Assert.Contains("Error indexing batch:", output);
        }

        [Fact]
        public async Task CrawlFinishedAsync_WithNonEmptyQueue_ProcessesRemainingItems()
        {
            // Arrange
            var indexer = new AzureSearchIndexer(
                "https://test.search.windows.net",
                "test-index",
                "test-key",
                "https://test.ai.windows.net",
                "test-key2",
                "ai-deployment",
                1,
                extractText: true,
                textExtractor: _textExtractor.Object,
                dryRun: true,
                console: _console);

            // Lägg till en sida i kön
            var crawledPage = new CrawledPage(new Uri("http://example.com"))
            {
                Content = new PageContent { Text = "<html><body>Test content</body></html>" }
            };
            await indexer.PageCrawledAsync(crawledPage);

            // Act
            await indexer.CrawlFinishedAsync();

            // Assert
            var output = string.Join(Environment.NewLine, _console.Output);
            Assert.Contains("[DRY RUN] Would index page: http://example.com", output);
        }

        [Fact]
        public async Task IndexBatchIfNecessary_WhenQueueIsFull_ProcessesBatch()
        {
            // Arrange
            var indexer = new AzureSearchIndexer(
                "https://test.search.windows.net",
                "test-index",
                "test-key",
                "https://test.ai.windows.net",
                "test-key2",
                "ai-deployment",
                1,
                extractText: true,
                textExtractor: _textExtractor.Object,
                dryRun: true,
                console: _console);

            // Fyll kön med 1000 sidor
            for (int i = 0; i < 1000; i++)
            {
                var crawledPage = new CrawledPage(new Uri($"http://example.com/{i}"))
                {
                    Content = new PageContent { Text = "<html><body>Test content</body></html>" }
                };
                await indexer.PageCrawledAsync(crawledPage);
            }

            // Act
            await indexer.CrawlFinishedAsync();

            // Assert
            var output = string.Join(Environment.NewLine, _console.Output);
            Assert.Contains("[DRY RUN] Would index page: http://example.com/0", output);
            Assert.Contains("[DRY RUN] Would index page: http://example.com/999", output);
        }
    }
}