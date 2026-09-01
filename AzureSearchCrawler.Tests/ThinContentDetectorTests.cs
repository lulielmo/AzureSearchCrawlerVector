using AzureSearchCrawler.Models;
using AzureSearchCrawler.Ocr;
using Xunit;

namespace AzureSearchCrawler.Tests
{
    [Trait("Category", "Unit")]
    public class ThinContentDetectorTests
    {
        private readonly ThinContentDetector _detector = new();

        [Fact]
        public void ShouldUseOcr_WhenTextIsBelowThresholdAndImagesExist_ReturnsTrue()
        {
            var images = new[]
            {
                new PageImage("https://example.com/image.png", new Uri("https://example.com/image.png"), 540, null, null)
            };

            Assert.True(_detector.ShouldUseOcr("short", images, textThreshold: 200));
        }

        [Fact]
        public void ShouldUseOcr_WhenTextExceedsThreshold_ReturnsFalse()
        {
            var images = new[]
            {
                new PageImage("https://example.com/image.png", new Uri("https://example.com/image.png"), 540, null, null)
            };
            var longText = new string('a', 200);

            Assert.False(_detector.ShouldUseOcr(longText, images, textThreshold: 200));
        }

        [Fact]
        public void ShouldUseOcr_WhenNoContentImages_ReturnsFalse()
        {
            Assert.False(_detector.ShouldUseOcr(string.Empty, Array.Empty<PageImage>(), textThreshold: 200));
        }

        [Fact]
        public void IsContentImage_SkipsTinyImages()
        {
            var tiny = new PageImage(
                "https://example.com/photo.png",
                new Uri("https://example.com/photo.png"),
                1,
                1,
                null);

            Assert.False(_detector.IsContentImage(tiny));
        }

        [Fact]
        public void IsContentImage_SkipsDataUriAndDecorativeSrc()
        {
            var dataUri = new PageImage("data:image/gif;base64,AAAA", null, 540, 400, null);
            var logo = new PageImage(
                "https://example.com/assets/logo.png",
                new Uri("https://example.com/assets/logo.png"),
                200,
                80,
                null);

            Assert.False(_detector.IsContentImage(dataUri));
            Assert.False(_detector.IsContentImage(logo));
        }

        [Fact]
        public void SelectContentImages_KeepsLargeContentImagesInDomOrder()
        {
            var images = new[]
            {
                new PageImage("https://example.com/icon.png", new Uri("https://example.com/icon.png"), 16, 16, null),
                new PageImage(
                    "https://example.com/uploaded/newsletter.png",
                    new Uri("https://example.com/uploaded/newsletter.png"),
                    540,
                    null,
                    null),
                new PageImage(
                    "https://example.com/uploaded/second.png",
                    new Uri("https://example.com/uploaded/second.png"),
                    800,
                    600,
                    null)
            };

            var selected = _detector.SelectContentImages(images, maxImages: 1);

            Assert.Single(selected);
            Assert.Equal("https://example.com/uploaded/newsletter.png", selected[0].Src);
        }
    }
}
