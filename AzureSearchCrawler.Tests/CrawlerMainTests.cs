using Xunit;
using Moq;
using AzureSearchCrawler.Interfaces;

namespace AzureSearchCrawler.Tests
{
    public class CrawlerMainTests : IDisposable
    {
        private readonly Mock<ICrawler> _crawlerMock;
        private readonly CrawlerMain _crawlerMain;
        private readonly StringWriter _consoleOutput;
        private readonly StringWriter _consoleError;
        private readonly TextWriter _originalOut;
        private readonly TextWriter _originalError;

        public CrawlerMainTests()
        {
            _crawlerMock = new Mock<ICrawler>();
            _originalOut = Console.Out;
            _originalError = Console.Error;
            _consoleOutput = new StringWriter();
            _consoleError = new StringWriter();

            Console.SetOut(_consoleOutput);
            Console.SetError(_consoleError);

            // Uppdatera konstruktorn utan domSelector
            _crawlerMain = new CrawlerMain(
                (endpoint, index, key, embeddingEndpoint, embeddingKey, embeddingDeployment, embeddingDimensions, extract, extractor, dryRun, console) =>
                    new AzureSearchIndexer(endpoint, index, key, embeddingEndpoint, embeddingKey, embeddingDeployment, embeddingDimensions, extract, extractor, dryRun, console),
                (indexer) => _crawlerMock.Object);

            // Uppdatera mock setup med domSelector parameter
            _crawlerMock
                .Setup(c => c.CrawlAsync(It.IsAny<Uri>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);
        }

        public void Dispose()
        {
            Console.SetOut(_originalOut);
            Console.SetError(_originalError);
            _consoleOutput.Dispose();
            _consoleError.Dispose();
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

            // Act
            var result = await _crawlerMain.RunAsync(args, new TestConsole());

            // Assert
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
            Assert.Contains("Option '--serviceEndPoint' is required", testConsole.Error.ToString());
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
            Assert.Contains("Invalid service endpoint URL", string.Join(Environment.NewLine, testConsole.Errors));
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
            Assert.Contains("Invalid service endpoint URL", string.Join(Environment.NewLine, testConsole.Errors));
        }

        [Fact]
        public async Task RunAsync_WithInvalidUrl_ReturnsErrorCode()
        {
            // Arrange
            var testConsole = new TestConsole();
            var args = new[]
            {
                "--rootUri", "ht tp://invalid.com", // Mellanslag gör URIn ogiltig men kraschar inte
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
            Assert.Contains("Invalid root URI format: ht tp://invalid.", string.Join(Environment.NewLine, testConsole.Errors));
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

            // Act
            var result = await _crawlerMain.RunAsync(args, new TestConsole());

            // Assert
            Assert.Equal(0, result);
            _crawlerMock.Verify(c => c.CrawlAsync(
                It.IsAny<Uri>(),
                It.Is<int>(p => p == 50),
                It.Is<int>(d => d == 3),
                It.IsAny<string?>()),
                Times.Once);
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

            try
            {
                // Act
                var result = await _crawlerMain.RunAsync(args, testConsole);

                // Assert
                Assert.Equal(0, result);
                _crawlerMock.Verify(c => c.CrawlAsync(
                    It.Is<Uri>(u => u.Host == "example.com"),
                    It.IsAny<int>(),
                    It.Is<int>(d => d == 3),
                    It.Is<string>(s => s == "div.blog-content")),
                    Times.Once);
                _crawlerMock.Verify(c => c.CrawlAsync(
                    It.Is<Uri>(u => u.Host == "another-site.com"),
                    It.IsAny<int>(),
                    It.Is<int>(d => d == 5),
                    It.Is<string>(s => s == "div.articles")),
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

            try
            {
                // Act
                var result = await _crawlerMain.RunAsync(args, testConsole);

                // Assert
                Assert.Equal(0, result);
                Assert.Contains("Invalid URI in sites file: invalid-url", string.Join(Environment.NewLine, testConsole.Errors));
                _crawlerMock.Verify(c => c.CrawlAsync(
                    It.Is<Uri>(u => u.Host == "valid-site.com"),
                    It.IsAny<int>(),
                    It.Is<int>(d => d == 5),
                    It.IsAny<string?>()),
                    Times.Once);
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
                Assert.Contains("Error parsing sites file:", string.Join(Environment.NewLine, testConsole.Errors));
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
            Assert.Contains("Either --rootUri or --sitesFile must be specified", testConsole.Errors);
        }

        [Fact]
        public async Task RunAsync_WithEmptySitesFile_ReturnsError()
        {
            // Arrange
            var testConsole = new TestConsole();
            var tempFile = Path.GetTempFileName();
            await File.WriteAllTextAsync(tempFile, "[]"); // Tom array

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
                Assert.Contains($"Could not read sites from file: {tempFile}", testConsole.Errors);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task RunAsync_WhenUnexpectedErrorOccurs_ReturnsError()
        {
            // Arrange
            var testConsole = new TestConsole();
            _crawlerMock
                .Setup(c => c.CrawlAsync(It.IsAny<Uri>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
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

            // Act
            var result = await _crawlerMain.RunAsync(args, testConsole);

            // Assert
            Assert.Equal(1, result);
            Assert.Contains("Error: Unexpected error", testConsole.Errors);
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

            // Act
            var result = await _crawlerMain.RunAsync(args, new TestConsole());

            // Assert
            Assert.Equal(0, result);
            _crawlerMock.Verify(c => c.CrawlAsync(
                It.IsAny<Uri>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.Is<string>(s => s == "div.blog-content")),
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

            // Act
            var result = await _crawlerMain.RunAsync(args, new TestConsole());

            // Assert
            Assert.Equal(0, result);
            _crawlerMock.Verify(c => c.CrawlAsync(
                It.IsAny<Uri>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
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

            try
            {
                // Act
                var result = await _crawlerMain.RunAsync(args, testConsole);

                // Assert
                Assert.Equal(0, result);
                _crawlerMock.Verify(c => c.CrawlAsync(
                    It.IsAny<Uri>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.Is<string?>(s => s == null)),
                    Times.Exactly(2));
            }
            finally
            {
                File.Delete(tempFile);
            }
        }
    }
}