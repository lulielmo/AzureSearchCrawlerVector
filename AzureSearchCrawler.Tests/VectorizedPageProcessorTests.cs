using AzureSearchCrawler.Interfaces;
using AzureSearchCrawler.Models;
using AzureSearchCrawler.Tests.Mocks;
using AzureSearchCrawler.Utils;
using Moq;
using Xunit;

namespace AzureSearchCrawler.Tests;

[Trait("Category", "Unit")]
public class VectorizedPageProcessorTests
{
    private readonly Mock<IConsole> _consoleMock;
    private readonly MockEmbeddingClient _embeddingClient;
    private readonly MockSearchClient _searchClient;
    private readonly VectorizedPageProcessor _processor;
    private readonly CrawledPageQueue _queue;

    public VectorizedPageProcessorTests()
    {
        _embeddingClient = new MockEmbeddingClient();
        _searchClient = new MockSearchClient(
            new Uri("https://test-search"),
            "test-index",
            new Azure.AzureKeyCredential("test-key"));
        _queue = new CrawledPageQueue();
        _consoleMock = new Mock<IConsole>();
        _processor = new VectorizedPageProcessor(
            searchServiceEndpoint: "https://test-searchendpoint.azure.com",
            adminApiKey: "test-admin-key",
            indexName: "test-index",
            embeddingAiEndpoint: "https://test-embeddingendpoint.azure.com",
            embeddingAiAdminApiKey: "test-embedding-key",
            embeddingDeployment: "test-embedding",
            azureOpenAIEmbeddingDimensions: 1536,
            console: _consoleMock.Object,
            queue: _queue,
            dryRun: false,
            rateLimitDelay: TimeSpan.Zero,
            embeddingClient: _embeddingClient,
            searchClient: _searchClient);
    }

    [Fact]
    public async Task ProcessQueueAsync_WithValidPages_ProcessesAllPages()
    {
        // Arrange
        var pages = new List<CrawledWebPage>
        {
            new CrawledWebPage(new Uri("https://example.com/1"), "Page 1", "Content 1", 200, null),
            new CrawledWebPage(new Uri("https://example.com/2"), "Page 2", "Content 2", 200, null),
            new CrawledWebPage(new Uri("https://example.com/3"), "Page 3", "Content 3", 200, null)
        };

        foreach (var page in pages)
        {
            _queue.Enqueue(page);
        }
        _queue.MarkAsComplete();

        // Act
        await _processor.ProcessQueueAsync();

        // Assert
        var indexedDocuments = _searchClient.GetIndexedDocuments();
        Assert.Equal(pages.Count, indexedDocuments.Count);
        foreach (var page in pages)
        {
            var expectedId = HashUtils.CreateSHA512(page.Uri.ToString());
            var doc = indexedDocuments.FirstOrDefault(d => d["id"].ToString() == expectedId);
            Assert.NotNull(doc);
            Assert.Equal(page.Title, doc["title"].ToString());
            Assert.Equal(page.Content, doc["content"].ToString());
        }
    }

    [Fact]
    public async Task ProcessQueueAsync_WithEmptyTitle_ProcessesPage()
    {
        // Arrange
        var page = new CrawledWebPage(new Uri("https://example.com"), "", "Content", 200, null);
        _queue.Enqueue(page);
        _queue.MarkAsComplete();

        // Act
        await _processor.ProcessQueueAsync();

        // Assert
        var indexedDocuments = _searchClient.GetIndexedDocuments();
        Assert.Single(indexedDocuments);
        var doc = indexedDocuments[0];
        var expectedId = HashUtils.CreateSHA512(page.Uri.ToString());
        Assert.Equal(expectedId, doc["id"].ToString());
        Assert.Equal("-", doc["title"].ToString());
        Assert.Equal(page.Content, doc["content"].ToString());
    }

    [Fact]
    public async Task ProcessQueueAsync_WithEmptyContent_ProcessesPage()
    {
        // Arrange
        var page = new CrawledWebPage(new Uri("https://example.com"), "Title", "", 200, null);
        _queue.Enqueue(page);
        _queue.MarkAsComplete();

        // Act
        await _processor.ProcessQueueAsync();

        // Assert
        var indexedDocuments = _searchClient.GetIndexedDocuments();
        Assert.Single(indexedDocuments);
        var doc = indexedDocuments[0];
        var expectedId = HashUtils.CreateSHA512(page.Uri.ToString());
        Assert.Equal(expectedId, doc["id"].ToString());
        Assert.Equal(page.Title, doc["title"].ToString());
        Assert.Equal("", doc["content"].ToString());
    }

    [Fact]
    public async Task ProcessQueueAsync_WithEmbeddingError_ThrowsException()
    {
        // Arrange
        var errorClient = new MockEmbeddingClient(shouldThrow: true);
        var processor = new VectorizedPageProcessor(
            searchServiceEndpoint: "https://test-searchendpoint.azure.com",
            adminApiKey: "test-admin-key",
            indexName: "test-index",
            embeddingAiEndpoint: "https://test-embeddingendpoint.azure.com",
            embeddingAiAdminApiKey: "test-embedding-key",
            embeddingDeployment: "test-embedding",
            azureOpenAIEmbeddingDimensions: 1536,
            console: _consoleMock.Object,
            queue: _queue,
            dryRun: false,
            rateLimitDelay: TimeSpan.Zero,
            embeddingClient: errorClient,
            searchClient: _searchClient);

        var page = new CrawledWebPage(new Uri("https://example.com"), "Title", "Content", 200, null);
        _queue.Enqueue(page);
        _queue.MarkAsComplete();

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(() => processor.ProcessQueueAsync());
    }

    [Fact]
    public async Task ProcessQueueAsync_WithDryRun_LogsPagesButDoesNotProcess()
    {
        // Arrange
        var dryRunProcessor = new VectorizedPageProcessor(
            searchServiceEndpoint: "https://test-searchendpoint.azure.com",
            adminApiKey: "test-admin-key",
            indexName: "test-index",
            embeddingAiEndpoint: "https://test-embeddingendpoint.azure.com",
            embeddingAiAdminApiKey: "test-embedding-key",
            embeddingDeployment: "test-embedding",
            azureOpenAIEmbeddingDimensions: 1536,
            console: _consoleMock.Object,
            queue: _queue,
            dryRun: true,
            rateLimitDelay: TimeSpan.Zero,
            embeddingClient: _embeddingClient,
            searchClient: _searchClient);

        var pages = new List<CrawledWebPage>
        {
            new CrawledWebPage(new Uri("https://example.com/1"), "Page 1", "Content 1", 200, null),
            new CrawledWebPage(new Uri("https://example.com/2"), "Page 2", "Content 2", 200, null)
        };

        foreach (var page in pages)
        {
            _queue.Enqueue(page);
        }
        _queue.MarkAsComplete();

        // Act
        await dryRunProcessor.ProcessQueueAsync();

        // Assert
        // Verifiera att dry run-meddelanden loggades
        _consoleMock.Verify(c => c.WriteLine("Starting to process crawled pages in DRY RUN mode...", LogLevel.Information), Times.Once);
        _consoleMock.Verify(c => c.WriteLine("[DRY RUN] Would process batch of 2 pages...", LogLevel.Information), Times.Once);
        _consoleMock.Verify(c => c.WriteLine("[DRY RUN] Would index page: https://example.com/1", LogLevel.Information), Times.Once);
        _consoleMock.Verify(c => c.WriteLine("[DRY RUN] Would index page: https://example.com/2", LogLevel.Information), Times.Once);
        _consoleMock.Verify(c => c.WriteLine("Finished processing crawled pages in DRY RUN mode.", LogLevel.Information), Times.Once);

        // Verifiera att inga dokument faktiskt indexerades
        var indexedDocuments = _searchClient.GetIndexedDocuments();
        Assert.Empty(indexedDocuments);

        // Verifiera att inga embedding-anrop gjordes
        Assert.Equal(0, _embeddingClient.CallCount);
    }

    [Fact]
    public async Task ProcessQueueAsync_WithLongText_TruncatesContent()
    {
        // Arrange
        var longContent = new string('x', 10000); // 10k chars, över 8000 limit
        var longTitle = new string('y', 9000);    // 9k chars, över 8000 limit
        
        var page = new CrawledWebPage(new Uri("https://example.com"), longTitle, longContent, 200, null);
        _queue.Enqueue(page);
        _queue.MarkAsComplete();

        // Act
        await _processor.ProcessQueueAsync();

        // Assert
        var indexedDocuments = _searchClient.GetIndexedDocuments();
        Assert.Single(indexedDocuments);
        var doc = indexedDocuments[0];
        
        // Verifiera att innehållet trunkerades till 8000 chars
        Assert.Equal(8000, doc["content"]?.ToString()?.Length ?? 0);
        Assert.Equal(8000, doc["title"]?.ToString()?.Length ?? 0);
        
        // Verifiera att trunkeringen loggades
        _consoleMock.Verify(c => c.WriteLine(It.Is<string>(s => s.Contains("Truncated content for") && s.Contains("10000 -> 8000")), LogLevel.Debug), Times.Once);
        _consoleMock.Verify(c => c.WriteLine(It.Is<string>(s => s.Contains("Truncated title for") && s.Contains("9000 -> 8000")), LogLevel.Debug), Times.Once);
    }

    [Fact]
    public async Task ProcessQueueAsync_WithMultipleBatches_LogsQueueStatus()
    {
        // Arrange - Lägg till fler sidor än batch-storleken (10) för att få flera batches
        var pages = new List<CrawledWebPage>();
        for (int i = 0; i < 15; i++)
        {
            pages.Add(new CrawledWebPage(new Uri($"https://example.com/{i}"), $"Page {i}", $"Content {i}", 200, null));
        }

        foreach (var page in pages)
        {
            _queue.Enqueue(page);
        }
        _queue.MarkAsComplete();

        // Act
        await _processor.ProcessQueueAsync();

        // Assert
        // Verifiera att kö-status loggades efter första batchen (15 - 10 = 5 items kvar)
        _consoleMock.Verify(c => c.WriteLine("5 items left in queue", LogLevel.Information), Times.Once);
        
        // Verifiera att alla sidor indexerades
        var indexedDocuments = _searchClient.GetIndexedDocuments();
        Assert.Equal(15, indexedDocuments.Count);
    }
} 