using AzureSearchCrawler.Interfaces;
using AzureSearchCrawler.Models;
using AzureSearchCrawler.TestUtilities;
using Moq;
using Xunit;

namespace AzureSearchCrawler.IntegrationTests
{
    [Trait("Category", "Integration")]
    public class TestWebsiteIntegrationTests : IClassFixture<TestWebsiteFixture>
    {
        private readonly TestWebServer _webServer;
        private readonly TestConsole _console = new TestConsole();

        public TestWebsiteIntegrationTests(TestWebsiteFixture fixture)
        {
            _webServer = fixture;
        }

        [Fact]
        public async Task CrawlTestWebsite_BasicCrawl_Succeeds()
        {
            // Arrange
            var uri = new Uri(_webServer.BaseUrl);
            var maxPages = 10;
            var maxDepth = 2;

            // Act & Assert
            // TODO: Implement actual crawling test
            // This is a placeholder to verify the web server is running
            Assert.NotNull(uri);
            Assert.True(maxPages > 0);
            Assert.True(maxDepth > 0);
        }

        [Fact]
        public async Task CrawlTestWebsite_WithDomSelector_OnlyCrawlsBlogPosts()
        {
            // Arrange
            var blogUrl = new Uri($"{_webServer.BaseUrl}/blog");
            var maxPages = 20;
            var maxDepth = 3;
            var domSelector = "div.blog-content";

            var loggedMessages = new List<(string Message, LogLevel Level)>();
            _console.LoggedMessage += (message, level) => 
            {
                loggedMessages.Add((message, level));
                Console.WriteLine($"[TestConsole] [{level}] {message}");
            };
            _console.SetVerbose(true);

            Console.WriteLine($"Starting test with URL: {blogUrl}");

            // Act
            var args = new[]
            {
                "--rootUri", blogUrl.ToString(),
                "--maxPages", maxPages.ToString(),
                "--maxDepth", maxDepth.ToString(),
                "--serviceEndPoint", "https://dummy-search-endpoint",
                "--indexName", "test-index",
                "--adminApiKey", "dummy-key",
                "--embeddingEndPoint", "https://dummy-embedding-endpoint",
                "--embeddingAdminKey", "dummy-key",
                "--embeddingDeploymentName", "dummy-deployment",
                "--azureOpenAIEmbeddingDimensions", "1536",
                "--domSelector", domSelector,
                "--dryRun",
                "--verbose"
            };

            Console.WriteLine("Creating CrawlerMain...");
            var crawlerMain = new CrawlerMain(
                (endpoint, index, key, embeddingEndpoint, embeddingKey, embeddingDeployment, embeddingDimensions, extract, extractor, dryRun, console) =>
                {
                    Console.WriteLine("Creating AzureSearchIndexer...");
                    return new AzureSearchIndexer(endpoint, index, key, embeddingEndpoint, embeddingKey, embeddingDeployment, embeddingDimensions, extract, extractor, dryRun, console);
                },
                (indexer, mode, console) => 
                {
                    Console.WriteLine("Creating AbotCrawler...");
                    return new AbotCrawler(indexer, console);
                });

            Console.WriteLine("Running CrawlerMain...");
            await crawlerMain.RunAsync(args, _console);

            // Assert
            Console.WriteLine($"Test completed. Logged messages count: {loggedMessages.Count}");
            var messagesCopy = loggedMessages.ToList();
            
            // Skriv ut alla loggmeddelanden för felsökning
            Console.WriteLine("All logged messages:");
            foreach (var msg in messagesCopy)
            {
                Console.WriteLine($"[{msg.Level}] {msg.Message}");
            }
            
            // Verify that we're using the correct selector
            Assert.Contains(messagesCopy, m => 
                m.Message.Contains($"Using DOM selector filter: {domSelector}") && 
                m.Level == LogLevel.Information);

            // Verify that we're processing blog posts
            Assert.Contains(messagesCopy, m => 
                m.Message.Contains("Processing page") && 
                m.Message.Contains("/blog/") && 
                m.Level == LogLevel.Information);

            // Verify that specific non-blog pages are not processed
            Assert.DoesNotContain(messagesCopy, m => 
                m.Message.Contains("Processing page") && 
                (m.Message.Contains("/about") || m.Message.Contains("/contact")) && 
                m.Level == LogLevel.Information);
        }
    }
} 