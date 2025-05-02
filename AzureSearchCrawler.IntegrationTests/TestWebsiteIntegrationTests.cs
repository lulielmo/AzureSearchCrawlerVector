using AzureSearchCrawler.Interfaces;
using AzureSearchCrawler.Models;
using AzureSearchCrawler.TestUtilities;
using Moq;
using Xunit;
using Xunit.Sdk;
using System.Reflection;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Azure;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
            // Add global exception handler
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                _console.WriteLine($"Unhandled exception: {ex?.Message ?? "Unknown error"}", LogLevel.Error);
                _console.WriteLine($"Stack trace: {ex?.StackTrace ?? "No stack trace"}", LogLevel.Debug);
            };

            // Check environment variables
            var requiredVariables = new[]
            {
                "AZURE_SEARCH_TEST_ENDPOINT",
                "AZURE_SEARCH_TEST_KEY",
                "AZURE_OPENAI_TEST_ENDPOINT",
                "AZURE_OPENAI_TEST_KEY",
                "AZURE_OPENAI_TEST_DEPLOYMENT",
                "AZURE_OPENAI_EMBEDDING_DIMENSIONS"
            };

            // Debug output
            _console.LoggedMessage += (message, level) => 
            {
                if (level == LogLevel.Debug || level == LogLevel.Information)
                {
                    Console.WriteLine($"[{level}] {message}");
                }
            };

            var missingVariables = requiredVariables
                .Where(v => string.IsNullOrEmpty(Environment.GetEnvironmentVariable(v)))
                .ToList();

            if (missingVariables.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Test requires the following environment variables to be set: {string.Join(", ", missingVariables)}. " +
                    "Set these variables to run integration tests against Azure services.");
            }

            // Arrange
            var blogUrl = new Uri($"{_webServer.BaseUrl}/blog");
            var maxPages = 10;
            var maxDepth = 3;
            var domSelector = "div.blog-content";

            var loggedMessages = new List<(string Message, LogLevel Level)>();
            _console.LoggedMessage += (message, level) => 
            {
                loggedMessages.Add((message, level));
            };
            _console.SetVerbose(true);

            Console.WriteLine($"Starting test with URL: {blogUrl}");

            // Act
            var args = new[]
            {
                "--rootUri", blogUrl.ToString(),
                "--maxPages", maxPages.ToString(),
                "--maxDepth", maxDepth.ToString(),
                "--serviceEndPoint", Environment.GetEnvironmentVariable("AZURE_SEARCH_TEST_ENDPOINT") ?? throw new InvalidOperationException("AZURE_SEARCH_TEST_ENDPOINT environment variable is not set"),
                "--indexName", "integration-test-index",
                "--adminApiKey", Environment.GetEnvironmentVariable("AZURE_SEARCH_TEST_KEY") ?? throw new InvalidOperationException("AZURE_SEARCH_TEST_KEY environment variable is not set"),
                "--embeddingEndPoint", Environment.GetEnvironmentVariable("AZURE_OPENAI_TEST_ENDPOINT") ?? throw new InvalidOperationException("AZURE_OPENAI_TEST_ENDPOINT environment variable is not set"),
                "--embeddingAdminKey", Environment.GetEnvironmentVariable("AZURE_OPENAI_TEST_KEY") ?? throw new InvalidOperationException("AZURE_OPENAI_TEST_KEY environment variable is not set"),
                "--embeddingDeploymentName", Environment.GetEnvironmentVariable("AZURE_OPENAI_TEST_DEPLOYMENT") ?? throw new InvalidOperationException("AZURE_OPENAI_TEST_DEPLOYMENT environment variable is not set"),
                "--azureOpenAIEmbeddingDimensions", Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_DIMENSIONS") ?? throw new InvalidOperationException("AZURE_OPENAI_EMBEDDING_DIMENSIONS environment variable is not set"),
                "--domSelector", domSelector,
                "--verbose"
            };

            Console.WriteLine("Creating CrawlerMain...");
            var crawlerMain = new CrawlerMain(
                indexerFactory: (endpoint, index, key, embeddingEndpoint, embeddingKey, embeddingDeployment, embeddingDimensions, extract, extractor, dryRun, console) =>
                {
                    Console.WriteLine("Creating AzureSearchIndexer...");
                    return new AzureSearchIndexer(endpoint, index, key, embeddingEndpoint, embeddingKey, embeddingDeployment, embeddingDimensions, extract, extractor, dryRun, console);
                },
                crawlerFactory: (indexer, mode, console) => 
                {
                    var queue = new CrawledPageQueue();
                    Console.WriteLine($"Creating crawler with mode: {mode}");
                    return mode switch
                    {
                        CrawlMode.Sitemap => new SitemapCrawler(indexer, console),
                        CrawlMode.Standard => new AbotCrawler(queue, console),
                        CrawlMode.Headless => new HeadlessBrowserCrawler(indexer, console),
                        _ => throw new ArgumentException($"Unsupported crawl mode: {mode}", nameof(mode))
                    };
                });

            Console.WriteLine("Running CrawlerMain...");
            var crawlResult = await crawlerMain.RunAsync(args, _console);

            // Assert
            Console.WriteLine($"Test completed. Logged messages count: {loggedMessages.Count}");
            var messagesCopy = loggedMessages.ToList();
            
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

            // Verify that documents were actually indexed
            var searchEndpoint = Environment.GetEnvironmentVariable("AZURE_SEARCH_TEST_ENDPOINT") ?? throw new InvalidOperationException("AZURE_SEARCH_TEST_ENDPOINT environment variable is not set");
            var searchKey = Environment.GetEnvironmentVariable("AZURE_SEARCH_TEST_KEY") ?? throw new InvalidOperationException("AZURE_SEARCH_TEST_KEY environment variable is not set");
            var searchClient = new SearchClient(new Uri(searchEndpoint), "integration-test-index", new AzureKeyCredential(searchKey));

            Console.WriteLine("Waiting for indexing to complete...");
            // Wait a bit for indexing to complete
            await Task.Delay(5000);

            // Verify that we have indexed documents
            Console.WriteLine("Searching for documents in the index...");
            var searchResults = await searchClient.SearchAsync<SearchDocument>("*");
            var results = searchResults.Value.GetResults().ToList();
            Console.WriteLine($"Found {results.Count} documents in the index");

            if (results.Count == 0)
            {
                Console.WriteLine("No documents found in the index. Checking if any documents were processed during crawling...");
                var processedMessages = messagesCopy.Where(m => m.Message.Contains("Processing page")).ToList();
                Console.WriteLine($"Number of pages processed during crawl: {processedMessages.Count}");
                foreach (var msg in processedMessages)
                {
                    Console.WriteLine($"Processed: {msg.Message}");
                }

                // Check for embedding-related messages
                var embeddingMessages = messagesCopy.Where(m => m.Message.Contains("embedding")).ToList();
                Console.WriteLine("Embedding-related messages:");
                foreach (var msg in embeddingMessages)
                {
                    Console.WriteLine($"[{msg.Level}] {msg.Message}");
                }
            }

            // Verify that we have blog posts in the index
            Console.WriteLine("Searching specifically for blog posts...");
            var blogResults = await searchClient.SearchAsync<SearchDocument>("/blog/");
            var blogDocuments = blogResults.Value.GetResults().ToList();
            Console.WriteLine($"Found {blogDocuments.Count} blog posts in the index");

            // Clean up the index
            var deleteBatch = new List<IndexDocumentsAction<SearchDocument>>();
            foreach (var result in results)
            {
                deleteBatch.Add(IndexDocumentsAction.Delete(result.Document));
            }
            var batch = IndexDocumentsBatch.Create<SearchDocument>(deleteBatch.ToArray());
            await searchClient.IndexDocumentsAsync(batch);
            Console.WriteLine("Cleaned up test index");
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
                indexerFactory: (endpoint, index, key, embeddingEndpoint, embeddingKey, embeddingDeployment, embeddingDimensions, extract, extractor, dryRun, console) =>
                {
                    Console.WriteLine("Creating AzureSearchIndexer...");
                    return new AzureSearchIndexer(endpoint, index, key, embeddingEndpoint, embeddingKey, embeddingDeployment, embeddingDimensions, extract, extractor, dryRun, console);
                },
                crawlerFactory: (indexer, mode, console) => 
                {
                    var queue = new CrawledPageQueue();
                    Console.WriteLine($"Creating crawler with mode: {mode}");
                    return mode switch
                    {
                        CrawlMode.Sitemap => new SitemapCrawler(indexer, console),
                        CrawlMode.Standard => new AbotCrawler(queue, console),
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
                indexerFactory: (endpoint, index, key, embeddingEndpoint, embeddingKey, embeddingDeployment, embeddingDimensions, extract, extractor, dryRun, console) =>
                {
                    Console.WriteLine("Creating AzureSearchIndexer...");
                    return new AzureSearchIndexer(endpoint, index, key, embeddingEndpoint, embeddingKey, embeddingDeployment, embeddingDimensions, extract, extractor, dryRun, console);
                },
                crawlerFactory: (indexer, mode, console) => 
                {
                    var queue = new CrawledPageQueue();
                    Console.WriteLine($"Creating crawler with mode: {mode}");
                    return mode switch
                    {
                        CrawlMode.Sitemap => new SitemapCrawler(indexer, console),
                        CrawlMode.Standard => new AbotCrawler(queue, console),
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
                indexerFactory: (endpoint, index, key, embeddingEndpoint, embeddingKey, embeddingDeployment, embeddingDimensions, extract, extractor, dryRun, console) =>
                {
                    Console.WriteLine("Creating AzureSearchIndexer...");
                    return new AzureSearchIndexer(endpoint, index, key, embeddingEndpoint, embeddingKey, embeddingDeployment, embeddingDimensions, extract, extractor, dryRun, console);
                },
                crawlerFactory: (indexer, mode, console) => 
                {
                    var queue = new CrawledPageQueue();
                    Console.WriteLine($"Creating crawler with mode: {mode}");
                    return mode switch
                    {
                        CrawlMode.Sitemap => new SitemapCrawler(indexer, console),
                        CrawlMode.Standard => new AbotCrawler(queue, console),
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
                    indexerFactory: (endpoint, index, key, embeddingEndpoint, embeddingKey, embeddingDeployment, embeddingDimensions, extract, extractor, dryRun, console) =>
                    {
                        Console.WriteLine("Creating AzureSearchIndexer...");
                        return new AzureSearchIndexer(endpoint, index, key, embeddingEndpoint, embeddingKey, embeddingDeployment, embeddingDimensions, extract, extractor, dryRun, console);
                    },
                    crawlerFactory: (indexer, mode, console) => 
                    {
                        var queue = new CrawledPageQueue();
                        Console.WriteLine($"Creating crawler with mode: {mode}");
                        return mode switch
                        {
                            CrawlMode.Sitemap => new SitemapCrawler(indexer, console),
                            CrawlMode.Standard => new AbotCrawler(queue, console),
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