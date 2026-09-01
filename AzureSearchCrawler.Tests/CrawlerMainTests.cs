using Xunit;
using Moq;
using AzureSearchCrawler.Interfaces;
using AzureSearchCrawler.Models;
using AzureSearchCrawler.TestUtilities;

namespace AzureSearchCrawler.Tests
{
    public class CrawlerMainTests : IDisposable
    {
        private readonly Mock<IWebCrawlingStrategy> _crawlerMock;
        private readonly Mock<ICrawledPageProcessor> _processorMock;
        private readonly CrawlerMain _crawlerMain;
        private readonly StringWriter _consoleOutput;
        private readonly StringWriter _consoleError;
        private readonly TextWriter _originalOut;
        private readonly TextWriter _originalError;
        
        public CrawlerMainTests()
        {
            _crawlerMock = new Mock<IWebCrawlingStrategy>();
            _processorMock = new Mock<ICrawledPageProcessor>();

            // Fabrik som returnerar mockad processor (och en slutförd Task)
            Func<string, string, string, string, string, string, int, Interfaces.IConsole, CrawledPageQueue, bool, (ICrawledPageProcessor, Task)> processorFactory =
                (s1, s2, s3, s4, s5, s6, i, console, queue, dryRun) => (_processorMock.Object, Task.CompletedTask);

            // Fabrik som returnerar mock-crawler
            Func<ICrawledPageProcessor, CrawlMode, Interfaces.IConsole, IWebCrawlingStrategy> crawlerFactory = 
                (processor, mode, console) => _crawlerMock.Object;

            _crawlerMain = new CrawlerMain(processorFactory, crawlerFactory);

            _originalOut = Console.Out;
            _originalError = Console.Error;
            _consoleOutput = new StringWriter();
            _consoleError = new StringWriter();
            
            Console.SetOut(_consoleOutput);
            Console.SetError(_consoleError);
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
                    Console.SetOut(_originalOut);
                    Console.SetError(_originalError);
                    _consoleOutput.Dispose();
                    _consoleError.Dispose();
                }

                // Free any unmanaged objects here.
                _disposed = true;
            }
        }

        ~CrawlerMainTests()
        {
            Dispose(false);
        }

        [Fact]
        public async Task RunAsync_WhenAllArgumentsAreValid_CompletesSuccessfully()
        {
            // Arrange
            var args = new[]
            {
                "--rootUri", "http://example.com",
                "--serviceEndPoint", "https://test.search.windows.net",
                "--indexName", "test-index",
                "--adminApiKey", "test-key",
                "--embeddingEndPoint", "https://test.ai.windows.net",
                "--embeddingAdminKey", "test-key2",
                "--embeddingDeploymentName", "ai-deployment",
                "--azureOpenAIEmbeddingDimensions", "3072"
            };
            var testConsole = new TestConsole();
            _crawlerMock.Setup(c => c.CrawlAsync(It.IsAny<Uri>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>())).Returns(Task.CompletedTask);

            // Act
            var result = await _crawlerMain.RunAsync(args, testConsole);

            // Assert
            Assert.Equal(0, result);
            _crawlerMock.Verify(c => c.CrawlAsync(new Uri("http://example.com"), 100, 10, null, null), Times.Once);
        }

        [Fact]
        public async Task RunAsync_WithEnableOcr_CompletesSuccessfully()
        {
            var args = new[]
            {
                "--rootUri", "http://example.com",
                "--serviceEndPoint", "https://test.search.windows.net",
                "--indexName", "test-index",
                "--adminApiKey", "test-key",
                "--embeddingEndPoint", "https://test.ai.windows.net",
                "--embeddingAdminKey", "test-key2",
                "--embeddingDeploymentName", "ai-deployment",
                "--azureOpenAIEmbeddingDimensions", "3072",
                "--enableOcr",
                "--ocrLanguage", "swe+eng",
                "--ocrTextThreshold", "150"
            };
            var testConsole = new TestConsole();
            _crawlerMock.Setup(c => c.CrawlAsync(It.IsAny<Uri>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>()))
                .Returns(Task.CompletedTask);

            var result = await _crawlerMain.RunAsync(args, testConsole);

            Assert.Equal(0, result);
        }

        [Fact]
        public async Task RunAsync_WithOcrPreview_CompletesSuccessfully()
        {
            var args = new[]
            {
                "--rootUri", "http://example.com",
                "--serviceEndPoint", "https://test.search.windows.net",
                "--indexName", "test-index",
                "--adminApiKey", "test-key",
                "--embeddingEndPoint", "https://test.ai.windows.net",
                "--embeddingAdminKey", "test-key2",
                "--embeddingDeploymentName", "ai-deployment",
                "--azureOpenAIEmbeddingDimensions", "3072",
                "--ocrPreview",
                "--ocrTextThreshold", "150"
            };
            var testConsole = new TestConsole();
            _crawlerMock.Setup(c => c.CrawlAsync(It.IsAny<Uri>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>()))
                .Returns(Task.CompletedTask);

            var result = await _crawlerMain.RunAsync(args, testConsole);

            Assert.Equal(0, result);
        }

        [Fact]
        public async Task RunAsync_WithMissingRequiredArgument_ReturnsErrorCode()
        {
            // Arrange
            var testConsole = new TestConsole();
            var args = new[] { "--rootUri", "http://example.com" };  // Saknar required arguments

            // Act
            var result = await _crawlerMain.RunAsync(args, testConsole);

            // Assert
            Assert.Equal(1, result);
            Assert.Contains(testConsole.Errors, e => e.Contains("Option '--serviceEndPoint' is required"));
        }

        [Fact]
        public async Task RunAsync_WithInvalidServiceEndpoint_ReturnsErrorCode()
        {
            // Arrange
            var testConsole = new TestConsole();
            var args = new[]
            {
                "--rootUri", "http://example.com",
                "--serviceEndPoint", "not-a-valid-url",
                "--indexName", "test-index",
                "--adminApiKey", "test-key",
                "--embeddingEndPoint", "https://test.ai.windows.net",
                "--embeddingAdminKey", "test-key2",
                "--embeddingDeploymentName", "ai-deployment",
                "--azureOpenAIEmbeddingDimensions", "3072"
            };

            // Act
            var result = await _crawlerMain.RunAsync(args, testConsole);

            // Assert
            Assert.Equal(1, result);
            Assert.Contains(testConsole.Errors, e => e.Contains("Invalid service endpoint URL format"));
        }

        [Fact]
        public async Task RunAsync_WithInvalidAiServiceEndpoint_ReturnsErrorCode()
        {
            // Arrange
            var testConsole = new TestConsole();
            var args = new[]
            {
                "--rootUri", "http://example.com",
                "--serviceEndPoint", "https://test.search.windows.net",
                "--indexName", "test-index",
                "--adminApiKey", "test-key",
                "--embeddingEndPoint", "not-a-valid-url",
                "--embeddingAdminKey", "test-key2",
                "--embeddingDeploymentName", "ai-deployment",
                "--azureOpenAIEmbeddingDimensions", "3072"
            };

            // Act
            var result = await _crawlerMain.RunAsync(args, testConsole);

            // Assert
            Assert.Equal(1, result);
            Assert.Contains(testConsole.Errors, e => e.Contains("Invalid embedding endpoint URL format"));
        }

        [Fact]
        public async Task RunAsync_WithInvalidUrl_ReturnsErrorCode()
        {
            // Arrange
            var testConsole = new TestConsole();
            var args = new[]
            {
                "--rootUri", "ht tp://invalid.com", // Space makes the URI invalid but doesn't crash
                "--serviceEndPoint", "https://test.search.windows.net",
                "--indexName", "test-index",
                "--adminApiKey", "test-key",
                "--embeddingEndPoint", "https://test.ai.windows.net",
                "--embeddingAdminKey", "test-key2",
                "--embeddingDeploymentName", "ai-deployment",
                "--azureOpenAIEmbeddingDimensions", "3072"
            };
            _crawlerMock.Setup(c => c.CrawlAsync(It.IsAny<Uri>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>())).Returns(Task.CompletedTask);

            // Act
            var result = await _crawlerMain.RunAsync(args, testConsole);

            // Assert
            Assert.Equal(1, result);
            Assert.Contains("Invalid root URI format: ht tp://invalid.", string.Join(Environment.NewLine, testConsole.Errors));

            //Assert.Contains(testConsole.Errors, e => e.Contains("Invalid URI in sites file: ht tp://invalid.com"));
        }

        [Fact]
        public async Task RunAsync_WithDryRun_ExecutesSuccessfully()
        {
            // Arrange
            var args = new[]
            {
                "--rootUri", "http://example.com",
                "--serviceEndPoint", "https://test.search.windows.net",
                "--indexName", "test-index",
                "--adminApiKey", "test-key",
                "--dryRun", "true",
                "--embeddingEndPoint", "https://test.ai.windows.net",
                "--embeddingAdminKey", "test-key2",
                "--embeddingDeploymentName", "ai-deployment",
                "--azureOpenAIEmbeddingDimensions", "3072"
            };
            var testConsole = new TestConsole();

            // Act
            var result = await _crawlerMain.RunAsync(args, testConsole);

            // Assert
            Assert.Equal(0, result);
        }

        [Fact]
        public async Task RunAsync_WithCustomLimits_PassesLimitsToIndexer()
        {
            // Arrange
            var args = new[]
            {
                "--rootUri", "http://example.com",
                "--serviceEndPoint", "https://test.search.windows.net",
                "--indexName", "test-index",
                "--adminApiKey", "test-key",
                "--maxPages", "50",
                "--maxDepth", "3",
                "--embeddingEndPoint", "https://test.ai.windows.net",
                "--embeddingAdminKey", "test-key2",
                "--embeddingDeploymentName", "ai-deployment",
                "--azureOpenAIEmbeddingDimensions", "3072"
            };
            _crawlerMock.Setup(c => c.CrawlAsync(It.IsAny<Uri>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>())).Returns(Task.CompletedTask);

            // Act
            var result = await _crawlerMain.RunAsync(args, new TestConsole());

            // Assert
            Assert.Equal(0, result);
            // TODO: Verifiera att rätt parametrar används. Kräver refaktorering av CrawlerMain för att kunna injicera mockar.
        }

        [Fact]
        public async Task RunAsync_WithValidSitesFile_CrawlsAllSites()
        {
            // Arrange
            var testConsole = new TestConsole();
            var tempFile = Path.GetTempFileName();
            await File.WriteAllTextAsync(tempFile, @"[
                {""uri"": ""http://example.com"", ""maxDepth"": 3, ""domSelector"": ""div.blog-content""},
                {""uri"": ""http://another-site.com"", ""maxDepth"": 5, ""domSelector"": ""div.articles""}
            ]");

            var args = new[]
            {
                "--sitesFile", tempFile,
                "--serviceEndPoint", "https://test.search.windows.net",
                "--indexName", "test-index",
                "--adminApiKey", "test-key",
                "--embeddingEndPoint", "https://test.ai.windows.net",
                "--embeddingAdminKey", "test-key2",
                "--embeddingDeploymentName", "ai-deployment",
                "--azureOpenAIEmbeddingDimensions", "3072"
            };
            _crawlerMock.Setup(c => c.CrawlAsync(It.IsAny<Uri>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>())).Returns(Task.CompletedTask);

            try
            {
                // Act
                var result = await _crawlerMain.RunAsync(args, testConsole);

                // Assert
                Assert.Equal(0, result);
                _crawlerMock.Verify(c => c.CrawlAsync(
                    new Uri("http://example.com"),
                    100, // Default
                    3, 
                    "div.blog-content",
                    null),
                    Times.Once);
                _crawlerMock.Verify(c => c.CrawlAsync(
                    new Uri("http://another-site.com"),
                    100, // Default
                    5, 
                    "div.articles",
                    null),
                    Times.Once);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task RunAsync_WithInvalidUrlInSitesFile_SkipsInvalidUrlAndContinues()
        {
            // Arrange
            var testConsole = new TestConsole();
            var tempFile = Path.GetTempFileName();
            await File.WriteAllTextAsync(tempFile, @"[
                {""uri"": ""invalid-url""},
                {""uri"": ""http://valid-site.com"", ""maxDepth"": 5}
            ]");

            var args = new[]
            {
                "--sitesFile", tempFile,
                "--serviceEndPoint", "https://test.search.windows.net",
                "--indexName", "test-index",
                "--adminApiKey", "test-key",
                "--embeddingEndPoint", "https://test.ai.windows.net",
                "--embeddingAdminKey", "test-key2",
                "--embeddingDeploymentName", "ai-deployment",
                "--azureOpenAIEmbeddingDimensions", "3072"
            };
            _crawlerMock.Setup(c => c.CrawlAsync(It.IsAny<Uri>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>())).Returns(Task.CompletedTask);

            try
            {
                // Act
                var result = await _crawlerMain.RunAsync(args, testConsole);

                // Assert
                Assert.Equal(0, result);
                Assert.Contains(testConsole.Errors, e => e.Contains("Invalid URI in sites file: invalid-url"));

                _crawlerMock.Verify(c => c.CrawlAsync(
                    It.Is<Uri>(u => u.AbsoluteUri == "http://valid-site.com/"),
                    100, // Default
                    5, 
                    null,
                    null),
                    Times.Once);
                
                // Verifiera att den ogiltiga URLen aldrig anropades
                _crawlerMock.Verify(c => c.CrawlAsync(
                    It.Is<Uri>(u => u.OriginalString == "invalid-url"),
                    It.IsAny<int>(), 
                    It.IsAny<int>(), 
                    It.IsAny<string>(),
                    It.IsAny<string>()), 
                    Times.Never);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task RunAsync_WithNonexistentSitesFile_ReturnsError()
        {
            // Arrange
            var testConsole = new TestConsole();
            var args = new[]
            {
                "--sitesFile", "nonexistent.json",
                "--serviceEndPoint", "https://test.search.windows.net",
                "--indexName", "test-index",
                "--adminApiKey", "test-key",
                "--embeddingEndPoint", "https://test.ai.windows.net",
                "--embeddingAdminKey", "test-key2",
                "--embeddingDeploymentName", "ai-deployment",
                "--azureOpenAIEmbeddingDimensions", "3072"
            };

            // Act
            var result = await _crawlerMain.RunAsync(args, testConsole);

            // Assert
            Assert.Equal(1, result);
            Assert.Contains("Sites file not found:", string.Join(Environment.NewLine, testConsole.Errors));
            //Assert.Contains(testConsole.Errors, e => e.Contains("Error parsing sites file"));
        }

        [Fact]
        public async Task RunAsync_WithInvalidJsonInSitesFile_ReturnsError()
        {
            // Arrange
            var testConsole = new TestConsole();
            var tempFile = Path.GetTempFileName();
            await File.WriteAllTextAsync(tempFile, "invalid json content");

            var args = new[]
            {
                "--sitesFile", tempFile,
                "--serviceEndPoint", "https://test.search.windows.net",
                "--indexName", "test-index",
                "--adminApiKey", "test-key",
                "--embeddingEndPoint", "https://test.ai.windows.net",
                "--embeddingAdminKey", "test-key2",
                "--embeddingDeploymentName", "ai-deployment",
                "--azureOpenAIEmbeddingDimensions", "3072"
            };

            try
            {
                // Act
                var result = await _crawlerMain.RunAsync(args, testConsole);

                // Assert
                Assert.Equal(1, result);
                Assert.Contains(testConsole.Errors, e => e.Contains("Error parsing sites file"));
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task RunAsync_WithoutRootUriAndSitesFile_ReturnsError()
        {
            // Arrange
            var testConsole = new TestConsole();
            var args = new[]
            {
                "--serviceEndPoint", "https://test.search.windows.net",
                "--indexName", "test-index",
                "--adminApiKey", "test-key",
                "--embeddingEndPoint", "https://test.ai.windows.net",
                "--embeddingAdminKey", "test-key2",
                "--embeddingDeploymentName", "ai-deployment",
                "--azureOpenAIEmbeddingDimensions", "3072"
            };

            // Act
            var result = await _crawlerMain.RunAsync(args, testConsole);

            // Assert
            Assert.Equal(1, result);
            Assert.Contains(testConsole.Errors, e => e.Contains("Either --rootUri or --sitesFile must be specified"));
        }

        [Fact]
        public async Task RunAsync_WithEmptySitesFile_ReturnsError()
        {
            // Arrange
            var testConsole = new TestConsole();
            var sitesFilePath = Path.GetTempFileName();
            await File.WriteAllTextAsync(sitesFilePath, "[]");
            var args = new[]
            {
                "--sitesFile", sitesFilePath,
                "--serviceEndPoint", "https://test.search.windows.net",
                "--indexName", "test-index",
                "--adminApiKey", "test-key",
                "--embeddingEndPoint", "https://test.ai.windows.net",
                "--embeddingAdminKey", "test-key2",
                "--embeddingDeploymentName", "ai-deployment",
                "--azureOpenAIEmbeddingDimensions", "3072"
            };

            // Act
            var result = await _crawlerMain.RunAsync(args, testConsole);

            // Assert
            Assert.Equal(1, result);
            Assert.Contains($"Could not read any sites from file, or the file is empty: {sitesFilePath}", testConsole.Errors);
            //Assert.Contains(testConsole.Output, e => e.Contains("No sites to crawl"));
            File.Delete(sitesFilePath);
        }

        [Fact]
        public async Task RunAsync_WhenUnexpectedErrorOccurs_ReturnsError()
        {
            // Arrange
            _crawlerMock.Setup(c => c.CrawlAsync(It.IsAny<Uri>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>()))
                .ThrowsAsync(new InvalidOperationException("Unexpected error"));
                
            var args = new[]
            {
                "--rootUri", "http://example.com", 
                "--serviceEndPoint", "https://test.search.windows.net",
                "--indexName", "test-index",
                "--adminApiKey", "test-key",
                "--embeddingEndPoint", "https://test.ai.windows.net",
                "--embeddingAdminKey", "test-key2",
                "--embeddingDeploymentName", "ai-deployment",
                "--azureOpenAIEmbeddingDimensions", "3072"
            };
            var testConsole = new TestConsole();

            // Act
            var result = await _crawlerMain.RunAsync(args, testConsole);

            // Assert
            Assert.Equal(1, result);
            Assert.Contains(testConsole.Errors, e => e.Contains("An error occurred while crawling http://example.com") && e.Contains("Unexpected error"));
        }

        [Fact]
        public async Task RunAsync_WithDomSelector_PassesSelectorToCrawler()
        {
            // Arrange
            var args = new[]
            {
                "--rootUri", "http://example.com",
                "--serviceEndPoint", "https://test.search.windows.net",
                "--indexName", "test-index",
                "--adminApiKey", "test-key",
                "--domSelector", "div.blog-content",
                "--embeddingEndPoint", "https://test.ai.windows.net",
                "--embeddingAdminKey", "test-key2",
                "--embeddingDeploymentName", "ai-deployment",
                "--azureOpenAIEmbeddingDimensions", "3072"
            };
            _crawlerMock.Setup(c => c.CrawlAsync(It.IsAny<Uri>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>())).Returns(Task.CompletedTask);

            // Act
            var result = await _crawlerMain.RunAsync(args, new TestConsole());

            // Assert
            Assert.Equal(0, result);
            _crawlerMock.Verify(c => c.CrawlAsync(
                It.IsAny<Uri>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.Is<string>(s => s == "div.blog-content"),
                It.IsAny<string?>()),
                Times.Once);
        }

        [Fact]
        public async Task RunAsync_WithoutDomSelector_PassesNullSelector()
        {
            // Arrange
            var args = new[]
            {
                "--rootUri", "http://example.com",
                "--serviceEndPoint", "https://test.search.windows.net",
                "--indexName", "test-index",
                "--adminApiKey", "test-key",
                "--embeddingEndPoint", "https://test.ai.windows.net",
                "--embeddingAdminKey", "test-key2",
                "--embeddingDeploymentName", "ai-deployment",
                "--azureOpenAIEmbeddingDimensions", "3072"
            };
            _crawlerMock.Setup(c => c.CrawlAsync(It.IsAny<Uri>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>())).Returns(Task.CompletedTask);

            // Act
            var result = await _crawlerMain.RunAsync(args, new TestConsole());

            // Assert
            Assert.Equal(0, result);
            _crawlerMock.Verify(c => c.CrawlAsync(
                It.IsAny<Uri>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.Is<string?>(s => s == null),
                It.Is<string?>(s => s == null)),
                Times.Once);
        }

        [Fact]
        public async Task RunAsync_WithSitesFileWithoutDomSelector_PassesNullSelector()
        {
            // Arrange
            var testConsole = new TestConsole();
            var tempFile = Path.GetTempFileName();
            await File.WriteAllTextAsync(tempFile, @"[
                {""uri"": ""http://example.com"", ""maxDepth"": 3},
                {""uri"": ""http://another-site.com"", ""maxDepth"": 5}
            ]");

            var args = new[]
            {
                "--sitesFile", tempFile,
                "--serviceEndPoint", "https://test.search.windows.net",
                "--indexName", "test-index",
                "--adminApiKey", "test-key",
                "--embeddingEndPoint", "https://test.ai.windows.net",
                "--embeddingAdminKey", "test-key2",
                "--embeddingDeploymentName", "ai-deployment",
                "--azureOpenAIEmbeddingDimensions", "3072"
            };
            _crawlerMock.Setup(c => c.CrawlAsync(It.IsAny<Uri>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>())).Returns(Task.CompletedTask);

            try
            {
                // Act
                var result = await _crawlerMain.RunAsync(args, testConsole);

                // Assert
                Assert.Equal(0, result);
                _crawlerMock.Verify(c => c.CrawlAsync(
                    new Uri("http://example.com"),
                    100,
                    3,
                    null,
                    null),
                    Times.Once);
                _crawlerMock.Verify(c => c.CrawlAsync(
                    new Uri("http://another-site.com"),
                    100,
                    5,
                    null,
                    null),
                    Times.Once);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task RunAsync_WithContentSelector_PassesSelectorToCrawler()
        {
            var args = new[]
            {
                "--rootUri", "http://example.com",
                "--serviceEndPoint", "https://test.search.windows.net",
                "--indexName", "test-index",
                "--adminApiKey", "test-key",
                "--contentSelector", "article.guide_article",
                "--embeddingEndPoint", "https://test.ai.windows.net",
                "--embeddingAdminKey", "test-key2",
                "--embeddingDeploymentName", "ai-deployment",
                "--azureOpenAIEmbeddingDimensions", "3072"
            };
            _crawlerMock.Setup(c => c.CrawlAsync(
                    It.IsAny<Uri>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>()))
                .Returns(Task.CompletedTask);

            var result = await _crawlerMain.RunAsync(args, new TestConsole());

            Assert.Equal(0, result);
            _crawlerMock.Verify(c => c.CrawlAsync(
                It.IsAny<Uri>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.Is<string>(s => s == "article.guide_article")),
                Times.Once);
        }

        [Fact]
        public async Task RunAsync_WithSitesFileContentSelector_PassesPerSiteSelector()
        {
            var testConsole = new TestConsole();
            var tempFile = Path.GetTempFileName();
            await File.WriteAllTextAsync(tempFile, @"[
                {""uri"": ""http://example.com"", ""maxDepth"": 3, ""contentSelector"": ""article.guide_article""},
                {""uri"": ""http://another-site.com"", ""maxDepth"": 5}
            ]");

            var args = new[]
            {
                "--sitesFile", tempFile,
                "--contentSelector", "main",
                "--serviceEndPoint", "https://test.search.windows.net",
                "--indexName", "test-index",
                "--adminApiKey", "test-key",
                "--embeddingEndPoint", "https://test.ai.windows.net",
                "--embeddingAdminKey", "test-key2",
                "--embeddingDeploymentName", "ai-deployment",
                "--azureOpenAIEmbeddingDimensions", "3072"
            };
            _crawlerMock.Setup(c => c.CrawlAsync(
                    It.IsAny<Uri>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>()))
                .Returns(Task.CompletedTask);

            try
            {
                var result = await _crawlerMain.RunAsync(args, testConsole);

                Assert.Equal(0, result);
                _crawlerMock.Verify(c => c.CrawlAsync(
                    new Uri("http://example.com"),
                    100,
                    3,
                    null,
                    "article.guide_article"),
                    Times.Once);
                _crawlerMock.Verify(c => c.CrawlAsync(
                    new Uri("http://another-site.com"),
                    100,
                    5,
                    null,
                    "main"),
                    Times.Once);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }
    }
}