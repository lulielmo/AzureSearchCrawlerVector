using AzureSearchCrawler.Interfaces;
using AzureSearchCrawler.Models;
using AzureSearchCrawler.TestUtilities;
using Moq;
using Xunit;
using System.Reflection;

namespace AzureSearchCrawler.IntegrationTests
{
    [Trait("Category", "Integration")]
    public class TestWebsiteIntegrationTests : IClassFixture<TestWebsiteFixture>, IClassFixture<TestWebsite2Fixture>, IClassFixture<TestSpaWebsiteFixture>, IClassFixture<TestConsole>
    {
        private readonly TestWebServer _webServer;
        private readonly TestWebServer _webServer2;
        private readonly TestSpaWebsiteFixture _spaWebServer;
        private readonly TestConsole _console;

        // Suppressing IDE0290 as the traditional constructor provides better readability
        // in this case with multiple fields and fixtures. Primary constructor is more suitable for simpler classes.
#pragma warning disable IDE0290 // Use primary constructor
        public TestWebsiteIntegrationTests(TestWebsiteFixture fixture, TestWebsite2Fixture fixture2, TestSpaWebsiteFixture spaFixture, TestConsole console)
        {
            _webServer = fixture;
            _webServer2 = fixture2;
            _spaWebServer = spaFixture;
            _console = console;
        }
#pragma warning restore IDE0290 // Use primary constructor

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
                    Console.WriteLine($"Creating crawler with mode: {mode}");
                    return mode switch
                    {
                        CrawlMode.Sitemap => new SitemapCrawler(indexer, console),
                        CrawlMode.Standard => new AbotCrawler(indexer, console),
                        CrawlMode.Headless => new HeadlessBrowserCrawler(indexer, console),
                        _ => throw new ArgumentException($"Unsupported crawl mode: {mode}", nameof(mode))
                    };
                });

            Console.WriteLine("Running CrawlerMain...");
            await crawlerMain.RunAsync(args, _console);

            // Assert
            Console.WriteLine($"Test completed. Logged messages count: {loggedMessages.Count}");
            var messagesCopy = loggedMessages.ToList();
            
            // Print all log messages for debugging
            Console.WriteLine("All logged messages:");
            foreach (var (Message, Level) in messagesCopy)
            {
                Console.WriteLine($"[{Level}] {Message}");
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
                (m.Message.Contains("/about") || m.Message.Contains("/contact") || m.Message.Contains("/products")) && 
                m.Level == LogLevel.Information);
        }

        [Fact]
        public async Task CrawlTestWebsite2_WithDomSelector_OnlyCrawlsCases()
        {
            // Arrange
            var rootUri = new Uri($"{_webServer2.BaseUrl}/cases.html");
            var maxPages = 20;
            var maxDepth = 3;
            var domSelector = "div.case-header";

            var loggedMessages = new List<(string Message, LogLevel Level)>();
            _console.LoggedMessage += (message, level) => 
            {
                loggedMessages.Add((message, level));
                Console.WriteLine($"[TestConsole] [{level}] {message}");
            };
            _console.SetVerbose(true);

            Console.WriteLine($"Starting test with URL: {rootUri}");

            // Act
            var args = new[]
            {
                "--rootUri", rootUri.ToString(),
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
                    Console.WriteLine($"Creating crawler with mode: {mode}");
                    return mode switch
                    {
                        CrawlMode.Sitemap => new SitemapCrawler(indexer, console),
                        CrawlMode.Standard => new AbotCrawler(indexer, console),
                        CrawlMode.Headless => new HeadlessBrowserCrawler(indexer, console),
                        _ => throw new ArgumentException($"Unsupported crawl mode: {mode}", nameof(mode))
                    };
                });

            Console.WriteLine("Running CrawlerMain...");
            await crawlerMain.RunAsync(args, _console);

            // Assert
            Console.WriteLine($"Test completed. Logged messages count: {loggedMessages.Count}");
            var messagesCopy = loggedMessages.ToList();
            
            // Print all log messages for debugging
            Console.WriteLine("All logged messages:");
            foreach (var (Message, Level) in messagesCopy)
            {
                Console.WriteLine($"[{Level}] {Message}");
            }
            
            // Verify that we're using the correct DOM selector
            Assert.Contains(messagesCopy, m => 
                m.Message.Contains($"Using DOM selector filter: {domSelector}") && 
                m.Level == LogLevel.Information);

            // Verify that we process case pages
            Assert.Contains(messagesCopy, m => 
                m.Message.Contains("Processing page") && 
                m.Message.Contains("/cases/ecommerce-giant.html") && 
                m.Level == LogLevel.Information);
            Assert.Contains(messagesCopy, m => 
                m.Message.Contains("Processing page") && 
                m.Message.Contains("/cases/news-agency.html") && 
                m.Level == LogLevel.Information);

            // Verify that we don't process non-case pages
            Assert.DoesNotContain(messagesCopy, m => 
                m.Message.Contains("Processing page") && 
                (m.Message.Contains("/services.html") || 
                 m.Message.Contains("/about.html") || 
                 m.Message.Contains("/contact.html")) && 
                m.Level == LogLevel.Information);
        }

        [Fact]
        public async Task CrawlSpaWebsite_WithSitemap_CrawlsAllPages()
        {
            // Arrange
            var rootUri = new Uri(_spaWebServer.BaseUrl);
            var maxPages = 100;
            var maxDepth = 2;

            var loggedMessages = new List<(string Message, LogLevel Level)>();
            _console.LoggedMessage += (message, level) => 
            {
                loggedMessages.Add((message, level));
                Console.WriteLine($"[TestConsole] [{level}] {message}");
            };
            _console.SetVerbose(true);

            Console.WriteLine($"Starting test with URL: {rootUri}");

            // Act
            var args = new[]
            {
                "--rootUri", rootUri.ToString(),
                "--maxPages", maxPages.ToString(),
                "--maxDepth", maxDepth.ToString(),
                "--serviceEndPoint", "https://dummy-search-endpoint",
                "--indexName", "test-index",
                "--adminApiKey", "dummy-key",
                "--embeddingEndPoint", "https://dummy-embedding-endpoint",
                "--embeddingAdminKey", "dummy-key",
                "--embeddingDeploymentName", "dummy-deployment",
                "--azureOpenAIEmbeddingDimensions", "1536",
                "--crawlMode", "Sitemap",
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
                    Console.WriteLine($"Creating crawler with mode: {mode}");
                    return mode switch
                    {
                        CrawlMode.Sitemap => new SitemapCrawler(indexer, console),
                        CrawlMode.Standard => new AbotCrawler(indexer, console),
                        CrawlMode.Headless => new HeadlessBrowserCrawler(indexer, console),
                        _ => throw new ArgumentException($"Unsupported crawl mode: {mode}", nameof(mode))
                    };
                });

            Console.WriteLine("Running CrawlerMain...");
            await crawlerMain.RunAsync(args, _console);

            // Assert
            Console.WriteLine($"Test completed. Logged messages count: {loggedMessages.Count}");
            var messagesCopy = loggedMessages.ToList();
            
            // Print all log messages for debugging
            Console.WriteLine("All logged messages:");
            foreach (var (Message, Level) in messagesCopy)
            {
                Console.WriteLine($"[{Level}] {Message}");
            }
            
            // Verify that we're using sitemap mode
            Assert.Contains(messagesCopy, m => 
                m.Message.Contains("Starting sitemap crawl of") && 
                m.Level == LogLevel.Information);

            // Verify that we find and process blog posts
            Assert.Contains(messagesCopy, m => 
                m.Message.Contains("Processing page") && 
                m.Message.Contains("/blog/") && 
                m.Level == LogLevel.Information);

            // Verify that we don't process non-blog pages
            Assert.DoesNotContain(messagesCopy, m => 
                m.Message.Contains("Processing page") && 
                m.Message.Contains("/api/") && 
                m.Level == LogLevel.Information);
        }

        [Fact]
        public async Task CrawlSpaWebsite_WithHeadlessBrowser_OnlyCrawlsBlogPosts()
        {
            // Arrange
            var rootUri = new Uri(_spaWebServer.BaseUrl);
            var maxPages = 100;
            var maxDepth = 2;
            var domSelector = "div[class*=\"blog-teaser\"]";

            var loggedMessages = new List<(string Message, LogLevel Level)>();
            _console.LoggedMessage += (message, level) => 
            {
                loggedMessages.Add((message, level));
                Console.WriteLine($"[TestConsole] [{level}] {message}");
            };
            _console.SetVerbose(true);

            Console.WriteLine($"Starting test with URL: {rootUri}");

            // Act
            var args = new[]
            {
                "--rootUri", rootUri.ToString(),
                "--maxPages", maxPages.ToString(),
                "--maxDepth", maxDepth.ToString(),
                "--serviceEndPoint", "https://dummy-search-endpoint",
                "--indexName", "test-index",
                "--adminApiKey", "dummy-key",
                "--embeddingEndPoint", "https://dummy-embedding-endpoint",
                "--embeddingAdminKey", "dummy-key",
                "--embeddingDeploymentName", "dummy-deployment",
                "--azureOpenAIEmbeddingDimensions", "1536",
                "--crawlMode", "Headless",
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
                    Console.WriteLine($"Creating crawler with mode: {mode}");
                    return mode switch
                    {
                        CrawlMode.Sitemap => new SitemapCrawler(indexer, console),
                        CrawlMode.Standard => new AbotCrawler(indexer, console),
                        CrawlMode.Headless => new HeadlessBrowserCrawler(indexer, console),
                        _ => throw new ArgumentException($"Unsupported crawl mode: {mode}", nameof(mode))
                    };
                });

            Console.WriteLine("Running CrawlerMain...");
            await crawlerMain.RunAsync(args, _console);

            // Assert
            Console.WriteLine($"Test completed. Logged messages count: {loggedMessages.Count}");
            var messagesCopy = loggedMessages.ToList();
            
            // Print all log messages for debugging
            Console.WriteLine("All logged messages:");
            foreach (var (Message, Level) in messagesCopy)
            {
                Console.WriteLine($"[{Level}] {Message}");
            }
            
            // Verify that we're using headless mode with correct selector
            Assert.Contains(messagesCopy, m => 
                m.Message.Contains("Starting headless browser crawl of") && 
                m.Level == LogLevel.Information);
            Assert.Contains(messagesCopy, m => 
                m.Message.Contains($"Using DOM selector filter: {domSelector}") && 
                m.Level == LogLevel.Information);

            // Verify that we find and process blog posts
            Assert.Contains(messagesCopy, m => 
                m.Message.Contains("Processing page") && 
                m.Message.Contains("/blog/testing-dynamic-content") && 
                m.Level == LogLevel.Information);
            Assert.Contains(messagesCopy, m => 
                m.Message.Contains("Processing page") && 
                m.Message.Contains("/blog/crawling-spas") && 
                m.Level == LogLevel.Information);

            // Verify that we don't process non-blog pages
            Assert.DoesNotContain(messagesCopy, m => 
                m.Message.Contains("Processing page") && 
                m.Message.Contains("/api/") && 
                m.Level == LogLevel.Information);
        }

        [Fact]
        public async Task CrawlMultipleSites_FromSitesFile_CrawlsAllSites()
        {
            // Arrange
            var sitesFile = Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
                "..", "..", "..", "..", "IntegrationTests", "sites.json"
            );

            // Update sites.json with correct ports for our test web servers
            var sitesContent = File.ReadAllText(sitesFile);
            sitesContent = sitesContent.Replace("http://localhost:5141", _webServer.BaseUrl);
            sitesContent = sitesContent.Replace("http://localhost:5142", _webServer2.BaseUrl);
            sitesContent = sitesContent.Replace("http://localhost:3000", _spaWebServer.BaseUrl);
            
            var tempSitesFile = Path.GetTempFileName();
            File.WriteAllText(tempSitesFile, sitesContent);

            var loggedMessages = new List<(string Message, LogLevel Level)>();
            _console.LoggedMessage += (message, level) => 
            {
                loggedMessages.Add((message, level));
                Console.WriteLine($"[TestConsole] [{level}] {message}");
            };
            _console.SetVerbose(true);

            try
            {
                // Act
                var args = new[]
                {
                    "--sitesFile", tempSitesFile,
                    "--serviceEndPoint", "https://dummy-search-endpoint",
                    "--indexName", "test-index",
                    "--adminApiKey", "dummy-key",
                    "--embeddingEndPoint", "https://dummy-embedding-endpoint",
                    "--embeddingAdminKey", "dummy-key",
                    "--embeddingDeploymentName", "dummy-deployment",
                    "--azureOpenAIEmbeddingDimensions", "1536",
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
                        Console.WriteLine($"Creating crawler with mode: {mode}");
                        return mode switch
                        {
                            CrawlMode.Sitemap => new SitemapCrawler(indexer, console),
                            CrawlMode.Standard => new AbotCrawler(indexer, console),
                            CrawlMode.Headless => new HeadlessBrowserCrawler(indexer, console),
                            _ => throw new ArgumentException($"Unsupported crawl mode: {mode}", nameof(mode))
                        };
                    });

                Console.WriteLine("Running CrawlerMain...");
                await crawlerMain.RunAsync(args, _console);

                // Assert
                Console.WriteLine($"Test completed. Logged messages count: {loggedMessages.Count}");
                var messagesCopy = loggedMessages.ToList();
                
                // Print all log messages for debugging
                Console.WriteLine("All logged messages:");
                foreach (var (Message, Level) in messagesCopy)
                {
                    Console.WriteLine($"[{Level}] {Message}");
                }

                // Verify that we process pages from all three sites
                // Test website 1 (blog)
                Assert.Contains(messagesCopy, m => 
                    m.Message.Contains("Processing page") && 
                    m.Message.Contains(_webServer.BaseUrl) &&
                    m.Message.Contains("/blog/") && 
                    m.Level == LogLevel.Information);

                // Test website 2
                Assert.Contains(messagesCopy, m => 
                    m.Message.Contains("Processing page") && 
                    m.Message.Contains(_webServer2.BaseUrl) && 
                    m.Level == LogLevel.Information);

                // SPA website
                Assert.Contains(messagesCopy, m => 
                    m.Message.Contains("Processing page") && 
                    m.Message.Contains(_spaWebServer.BaseUrl) && 
                    m.Level == LogLevel.Information);

                // Verify that DOM selector is respected for the first site
                Assert.Contains(messagesCopy, m => 
                    m.Message.Contains("Using DOM selector filter: div.blog-content") && 
                    m.Level == LogLevel.Information);
            }
            finally
            {
                // Cleanup
                try
                {
                    if (File.Exists(tempSitesFile))
                    {
                        File.Delete(tempSitesFile);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Failed to delete temporary file: {ex.Message}");
                }
            }
        }
    }
} 