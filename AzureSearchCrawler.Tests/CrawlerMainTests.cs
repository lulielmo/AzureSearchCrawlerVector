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

            // Använder den riktiga AzureSearchIndexer men med dryRun=true
            _crawlerMain = new CrawlerMain(
                (endpoint, index, key, extract, extractor, dryRun, console, domSelector) =>
                    new AzureSearchIndexer(endpoint, index, key, extract, extractor, dryRun, console, domSelector),
                (indexer) => _crawlerMock.Object);

            // Setup mock behavior
            _crawlerMock
                .Setup(c => c.CrawlAsync(It.IsAny<Uri>(), It.IsAny<int>(), It.IsAny<int>()))
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
        public async Task RunAsync_WithValidArguments_ReturnsSuccessCode()
        {
            // Arrange
            var args = new[]
            {
                "--rootUri", "http://example.com",
                "--serviceEndPoint", "https://test.search.windows.net",
                "--indexName", "test-index",
                "--adminApiKey", "test-key"
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
                "--adminApiKey", "test-key"
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
                "--adminApiKey", "test-key"
            };

            // Act
            var result = await _crawlerMain.RunAsync(args, testConsole);

            // Assert
            Assert.Equal(1, result);
            Assert.Contains("Invalid root URI format: ht tp://invalid.com", string.Join(Environment.NewLine, testConsole.Errors));
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
                "--dryRun", "true"
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
                "--maxDepth", "3"
            };

            // Act
            var result = await _crawlerMain.RunAsync(args, new TestConsole());

            // Assert
            Assert.Equal(0, result);
            _crawlerMock.Verify(c => c.CrawlAsync(
                It.IsAny<Uri>(),
                It.Is<int>(p => p == 50),
                It.Is<int>(d => d == 3)),
                Times.Once);
        }

        [Fact]
        public async Task RunAsync_WithValidSitesFile_CrawlsAllSites()
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
                "--adminApiKey", "test-key"
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
                    It.Is<int>(d => d == 3)),
                    Times.Once);
                _crawlerMock.Verify(c => c.CrawlAsync(
                    It.Is<Uri>(u => u.Host == "another-site.com"),
                    It.IsAny<int>(),
                    It.Is<int>(d => d == 5)),
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
                "--adminApiKey", "test-key"
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
                    It.Is<int>(d => d == 5)),
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
                "--adminApiKey", "test-key"
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
                "--adminApiKey", "test-key"
            };

            try
            {
                // Act
                var result = await _crawlerMain.RunAsync(args, testConsole);

                // Assert
                Assert.Equal(1, result);
                //Error parsing sites file:
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
                "--adminApiKey", "test-key"
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
                "--adminApiKey", "test-key"
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
                .Setup(c => c.CrawlAsync(It.IsAny<Uri>(), It.IsAny<int>(), It.IsAny<int>()))
                .ThrowsAsync(new InvalidOperationException("Unexpected error"));

            var args = new[]
            {
                "--rootUri", "http://example.com",
                "--serviceEndPoint", "https://test.search.windows.net",
                "--indexName", "test-index",
                "--adminApiKey", "test-key"
            };

            // Act
            var result = await _crawlerMain.RunAsync(args, testConsole);

            // Assert
            Assert.Equal(1, result);
            Assert.Contains("Error: Unexpected error", testConsole.Errors);
        }
    }
}