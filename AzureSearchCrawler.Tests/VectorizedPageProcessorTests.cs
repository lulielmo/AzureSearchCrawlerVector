using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.AI.OpenAI;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using AzureSearchCrawler.Interfaces;
using AzureSearchCrawler.Models;
using AzureSearchCrawler.Tests.Mocks;
using AzureSearchCrawler.Tests.Models;
using AzureSearchCrawler.Utils;
using Moq;
using Xunit;
using LogLevel = AzureSearchCrawler.Models.LogLevel;

namespace AzureSearchCrawler.Tests;

[Trait("Category", "Unit")]
public class VectorizedPageProcessorTests
{
    private readonly Mock<IConsole> _consoleMock;
    private readonly Mock<AzureOpenAIClient> _azureOpenAIClientMock;
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
            embeddingClient: errorClient,
            searchClient: _searchClient);

        var page = new CrawledWebPage(new Uri("https://example.com"), "Title", "Content", 200, null);
        _queue.Enqueue(page);
        _queue.MarkAsComplete();

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(() => processor.ProcessQueueAsync());
    }
} 