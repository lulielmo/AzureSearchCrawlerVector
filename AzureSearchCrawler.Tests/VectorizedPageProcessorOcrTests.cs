using AzureSearchCrawler.Interfaces;
using AzureSearchCrawler.Models;
using AzureSearchCrawler.Tests.Mocks;
using Moq;
using Xunit;

namespace AzureSearchCrawler.Tests;

[Trait("Category", "Unit")]
public class VectorizedPageProcessorOcrTests
{
    private readonly Mock<IConsole> _consoleMock = new();
    private readonly MockEmbeddingClient _embeddingClient = new();
    private readonly MockSearchClient _searchClient;
    private readonly CrawledPageQueue _queue = new();

    public VectorizedPageProcessorOcrTests()
    {
        _searchClient = new MockSearchClient(
            new Uri("https://test-search"),
            "test-index",
            new Azure.AzureKeyCredential("test-key"));
    }

    [Fact]
    public async Task ProcessQueueAsync_WithHtmlContent_IndexesExtractedTextNotRawHtml()
    {
        var processor = CreateProcessor();
        var page = new CrawledWebPage(
            new Uri("https://example.com/article"),
            "Page Title",
            "<html><body><p>Clean article text</p><script>alert(1)</script></body></html>",
            200);
        _queue.Enqueue(page);
        _queue.MarkAsComplete();

        await processor.ProcessQueueAsync();

        var doc = Assert.Single(_searchClient.GetIndexedDocuments());
        Assert.Equal("Clean article text", doc["content"].ToString());
        Assert.DoesNotContain("<script>", doc["content"].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessQueueAsync_WithEmptyCrawlerTitle_UsesTitleFromHtml()
    {
        var processor = CreateProcessor();
        var page = new CrawledWebPage(
            new Uri("https://example.com/article"),
            "",
            "<html><head><title>From HTML</title></head><body><p>Body</p></body></html>",
            200);
        _queue.Enqueue(page);
        _queue.MarkAsComplete();

        await processor.ProcessQueueAsync();

        var doc = Assert.Single(_searchClient.GetIndexedDocuments());
        Assert.Equal("From HTML", doc["title"].ToString());
    }

    [Fact]
    public async Task ProcessQueueAsync_WhenOcrEnabledForThinPage_IndexesOcrText()
    {
        var ocr = new Mock<IOcrEngine>();
        ocr.Setup(e => e.RecognizeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Vi byter bank: allt du behöver veta");
        var downloader = new Mock<IImageDownloader>();
        downloader.Setup(d => d.DownloadAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream([1, 2, 3]));

        var processor = CreateProcessor(
            new OcrOptions { Enabled = true, TextThreshold = 200 },
            ocr.Object,
            downloader.Object);

        var html = """
            <html><body>
            <p><a href="https://example.com/link">https://example.com/link</a></p>
            <img src="https://example.com/newsletter.png" width="540" />
            </body></html>
            """;
        _queue.Enqueue(new CrawledWebPage(new Uri("https://example.com/thin"), "Utskick", html, 200));
        _queue.MarkAsComplete();

        await processor.ProcessQueueAsync();

        var doc = Assert.Single(_searchClient.GetIndexedDocuments());
        Assert.Contains("Vi byter bank: allt du behöver veta", doc["content"].ToString());
        ocr.Verify(e => e.RecognizeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessQueueAsync_WhenOcrDisabled_DoesNotCallOcrForThinPage()
    {
        var ocr = new Mock<IOcrEngine>();
        var downloader = new Mock<IImageDownloader>();
        var processor = CreateProcessor(OcrOptions.Disabled, ocr.Object, downloader.Object);

        var html = """
            <html><body>
            <p><a href="https://example.com/link">https://example.com/link</a></p>
            <img src="https://example.com/newsletter.png" width="540" />
            </body></html>
            """;
        _queue.Enqueue(new CrawledWebPage(new Uri("https://example.com/thin"), "Utskick", html, 200));
        _queue.MarkAsComplete();

        await processor.ProcessQueueAsync();

        ocr.Verify(e => e.RecognizeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Single(_searchClient.GetIndexedDocuments());
    }

    private VectorizedPageProcessor CreateProcessor(
        OcrOptions? ocrOptions = null,
        IOcrEngine? ocrEngine = null,
        IImageDownloader? imageDownloader = null,
        bool ocrPreview = false)
    {
        return new VectorizedPageProcessor(
            searchServiceEndpoint: "https://test-searchendpoint.azure.com",
            adminApiKey: "test-admin-key",
            indexName: "test-index",
            embeddingAiEndpoint: "https://test-embeddingendpoint.azure.com",
            embeddingAiAdminApiKey: "test-embedding-key",
            embeddingDeployment: "text-embedding",
            azureOpenAIEmbeddingDimensions: 1536,
            console: _consoleMock.Object,
            queue: _queue,
            dryRun: false,
            rateLimitDelay: TimeSpan.Zero,
            embeddingClient: _embeddingClient,
            searchClient: _searchClient,
            ocrOptions: ocrOptions,
            ocrEngine: ocrEngine,
            imageDownloader: imageDownloader,
            ocrPreview: ocrPreview);
    }

    [Fact]
    public async Task ProcessQueueAsync_WithOcrPreview_LogsThresholdDecisionWithoutIndexing()
    {
        var ocr = new Mock<IOcrEngine>();
        var processor = CreateProcessor(
            new OcrOptions { Enabled = false, TextThreshold = 200 },
            ocr.Object,
            ocrPreview: true);

        var html = """
            <html><body>
            <p><a href="https://example.com/link">https://example.com/link</a></p>
            <img src="https://example.com/newsletter.png" width="540" />
            </body></html>
            """;
        _queue.Enqueue(new CrawledWebPage(new Uri("https://example.com/thin"), "Utskick", html, 200));
        _queue.MarkAsComplete();

        await processor.ProcessQueueAsync();

        _consoleMock.Verify(
            c => c.WriteLine(It.Is<string>(s => s.Contains("[OCR PREVIEW] https://example.com/thin")), LogLevel.Information),
            Times.Once);
        _consoleMock.Verify(
            c => c.WriteLine(It.Is<string>(s => s.Contains("Decision: would run OCR")), LogLevel.Information),
            Times.Once);
        _consoleMock.Verify(
            c => c.WriteLine(It.Is<string>(s => s.Contains("Sample:")), LogLevel.Information),
            Times.Once);
        _consoleMock.Verify(
            c => c.WriteLine(It.Is<string>(s => s.Contains("Tesseract not run")), LogLevel.Information),
            Times.Once);
        ocr.Verify(e => e.RecognizeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(_searchClient.GetIndexedDocuments());
        Assert.Equal(0, _embeddingClient.CallCount);
    }

    [Fact]
    public async Task ProcessQueueAsync_WithOcrPreviewAndEnableOcr_RunsTesseractForThinPages()
    {
        var ocr = new Mock<IOcrEngine>();
        ocr.Setup(e => e.RecognizeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("OCR from image");
        var downloader = new Mock<IImageDownloader>();
        downloader.Setup(d => d.DownloadAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream([1, 2, 3]));

        var processor = CreateProcessor(
            new OcrOptions { Enabled = true, TextThreshold = 200 },
            ocr.Object,
            downloader.Object,
            ocrPreview: true);

        var html = """
            <html><body>
            <p><a href="https://example.com/link">https://example.com/link</a></p>
            <img src="https://example.com/newsletter.png" width="540" />
            </body></html>
            """;
        _queue.Enqueue(new CrawledWebPage(new Uri("https://example.com/thin"), "Utskick", html, 200));
        _queue.MarkAsComplete();

        await processor.ProcessQueueAsync();

        ocr.Verify(e => e.RecognizeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
        _consoleMock.Verify(
            c => c.WriteLine(It.Is<string>(s => s.Contains("Prepared content:")), LogLevel.Information),
            Times.Once);
        Assert.Empty(_searchClient.GetIndexedDocuments());
        Assert.Equal(0, _embeddingClient.CallCount);
    }

    [Fact]
    public async Task ProcessQueueAsync_WithOcrPreview_SkipsOcrWhenBodyTextExceedsThreshold()
    {
        var ocr = new Mock<IOcrEngine>();
        var processor = CreateProcessor(
            new OcrOptions { Enabled = true, TextThreshold = 50 },
            ocr.Object,
            ocrPreview: true);

        var html = $"<html><body><p>{new string('x', 80)}</p>" +
                   "<img src=\"https://example.com/photo.png\" width=\"540\" /></body></html>";
        _queue.Enqueue(new CrawledWebPage(new Uri("https://example.com/rich"), "Article", html, 200));
        _queue.MarkAsComplete();

        await processor.ProcessQueueAsync();

        _consoleMock.Verify(
            c => c.WriteLine(It.Is<string>(s => s.Contains("skip OCR (body text at or above threshold)")), LogLevel.Information),
            Times.Once);
        ocr.Verify(e => e.RecognizeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(_searchClient.GetIndexedDocuments());
    }
}
