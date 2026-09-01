using AzureSearchCrawler.Interfaces;
using AzureSearchCrawler.Models;
using AzureSearchCrawler.Ocr;
using Moq;
using Xunit;

namespace AzureSearchCrawler.Tests
{
    [Trait("Category", "Unit")]
    public class OcrPageEnricherTests
    {
        private const string KundoStyleHtml = """
            <html><body>
            <article class="guide_article">
            <h1 id="guide-title">2023-05-23 Utskick företagskunder</h1>
            <div class="guide_updated">Uppdaterad <time>2023-05-24</time></div>
            <div class="guide_text">
            <p><a href="https://app.bwz.se/skekraft/b/m/?l=520ec233">https://app.bwz.se/skekraft/b/m/?l=520ec233</a></p>
            <p></p>
            <img src="https://skekraft.kundo.se/uploaded_files/74/image.png" style="width:540px" width="540" />
            </div>
            </article>
            </body></html>
            """;

        private readonly TextExtractor _extractor = new();
        private readonly Mock<IConsole> _console = new();

        [Fact]
        public async Task EnrichAsync_ForImageOnlyArticle_CallsOcrAndAppendsText()
        {
            var ocr = new Mock<IOcrEngine>();
            ocr.Setup(e => e.RecognizeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("120 år av förnybar el");
            var downloader = new Mock<IImageDownloader>();
            downloader.Setup(d => d.DownloadAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MemoryStream([1, 2, 3]));

            var enricher = CreateEnricher(ocr.Object, downloader.Object);
            var extracted = _extractor.ExtractPage(KundoStyleHtml, new Uri("https://skekraft.kundo.se/guide"));

            var result = await enricher.EnrichAsync(extracted, new Uri("https://skekraft.kundo.se/guide"));

            Assert.Contains("120 år av förnybar el", result);
            ocr.Verify(e => e.RecognizeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task EnrichAsync_ForTextRichArticleWithImage_DoesNotCallOcr()
        {
            var html = "<html><body>" +
                       $"<p>{new string('x', 250)}</p>" +
                       "<img src=\"https://example.com/photo.png\" width=\"540\" />" +
                       "</body></html>";
            var ocr = new Mock<IOcrEngine>();
            var downloader = new Mock<IImageDownloader>();
            var enricher = CreateEnricher(ocr.Object, downloader.Object);
            var extracted = _extractor.ExtractPage(html, new Uri("https://example.com/article"));

            var result = await enricher.EnrichAsync(extracted, new Uri("https://example.com/article"));

            Assert.Contains(new string('x', 250), result);
            ocr.Verify(e => e.RecognizeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
            downloader.Verify(d => d.DownloadAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task EnrichAsync_WhenOcrFailsForOneImage_ContinuesWithRemainingText()
        {
            var html = """
                <html><body>
                <p><a href="https://example.com/x">https://example.com/x</a></p>
                <img src="https://example.com/first.png" width="540" />
                <img src="https://example.com/second.png" width="540" />
                </body></html>
                """;
            var ocr = new Mock<IOcrEngine>();
            ocr.SetupSequence(e => e.RecognizeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("tesseract failed"))
                .ReturnsAsync("Bank change details");
            var downloader = new Mock<IImageDownloader>();
            downloader.Setup(d => d.DownloadAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new MemoryStream([1, 2, 3]));

            var enricher = CreateEnricher(ocr.Object, downloader.Object);
            var extracted = _extractor.ExtractPage(html, new Uri("https://example.com/page"));

            var result = await enricher.EnrichAsync(extracted, new Uri("https://example.com/page"));

            Assert.Contains("Bank change details", result);
            ocr.Verify(e => e.RecognizeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public async Task EnrichAsync_WhenDownloadReturnsNull_KeepsExtractedText()
        {
            var ocr = new Mock<IOcrEngine>();
            var downloader = new Mock<IImageDownloader>();
            downloader.Setup(d => d.DownloadAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Stream?)null);
            var enricher = CreateEnricher(ocr.Object, downloader.Object);
            var extracted = _extractor.ExtractPage(KundoStyleHtml, new Uri("https://skekraft.kundo.se/guide"));

            var result = await enricher.EnrichAsync(extracted, new Uri("https://skekraft.kundo.se/guide"));

            Assert.Equal(extracted.Text, result);
            ocr.Verify(e => e.RecognizeAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        private OcrPageEnricher CreateEnricher(IOcrEngine ocr, IImageDownloader downloader) =>
            new(ocr, downloader, _console.Object, new OcrOptions { Enabled = true, TextThreshold = 200 });
    }
}
