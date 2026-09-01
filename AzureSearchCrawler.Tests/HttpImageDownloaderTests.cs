using AzureSearchCrawler.Ocr;
using Moq;
using Moq.Protected;
using System.Net;
using Xunit;

namespace AzureSearchCrawler.Tests
{
    [Trait("Category", "Unit")]
    public class HttpImageDownloaderTests
    {
        [Fact]
        public async Task DownloadAsync_WhenHttpImage_ReturnsContent()
        {
            var handler = new Mock<HttpMessageHandler>();
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([137, 80, 78, 71])
                });

            var downloader = new HttpImageDownloader(new HttpClient(handler.Object));
            await using var stream = await downloader.DownloadAsync(new Uri("https://example.com/image.png"));

            Assert.NotNull(stream);
            Assert.Equal(4, stream!.Length);
        }

        [Fact]
        public async Task DownloadAsync_WhenNotFound_ReturnsNull()
        {
            var handler = new Mock<HttpMessageHandler>();
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NotFound));

            var downloader = new HttpImageDownloader(new HttpClient(handler.Object));
            var stream = await downloader.DownloadAsync(new Uri("https://example.com/missing.png"));

            Assert.Null(stream);
        }

        [Fact]
        public async Task DownloadAsync_WhenSchemeIsNotHttp_ReturnsNull()
        {
            var downloader = new HttpImageDownloader();
            var stream = await downloader.DownloadAsync(new Uri("file:///C:/temp/image.png"));

            Assert.Null(stream);
        }
    }
}
