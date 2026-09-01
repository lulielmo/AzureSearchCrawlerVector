using Xunit;

namespace AzureSearchCrawler.Tests
{
    public class TextExtractorTests
    {
        private readonly TextExtractor _extractor;

        public TextExtractorTests()
        {
            _extractor = new TextExtractor();
        }

        [Fact]
        public void ExtractText_WhenExtractTextIsFalse_ReturnsRawHtml()
        {
            // Arrange
            var htmlContent = "<html><body><p>Test content</p></body></html>";

            // Act
            var result = _extractor.ExtractText(extractText: false, htmlContent);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("<p>Test content</p>", result["content"]);
        }

        [Fact]
        public void ExtractText_WhenExtractTextIsTrue_ReturnsCleanedText()
        {
            // Arrange
            var htmlContent = "<html><body><p>Test content</p><script>alert('hello');</script></body></html>";

            // Act
            var result = _extractor.ExtractText(extractText: true, htmlContent);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Test content", result["content"].Trim());
        }

        [Theory]
        [InlineData("<html><body><h1>Title</h1><p>Content</p></body></html>", "Title Content")]
        [InlineData("<html><body><div>First</div><div>Second</div><p>Third</p></body></html>", "First Second Third")]
        [InlineData("<html><body><style>css{}</style><p>Content</p></body></html>", "Content")]
        [InlineData("<html><body><svg>vector</svg><p>Content</p></body></html>", "Content")]
        [InlineData("<html><body><path>path</path><p>Content</p></body></html>", "Content")]
        public void ExtractText_RemovesUnwantedTags_AndPreservesSpacing(string input, string expectedContent)
        {
            // Act
            var result = _extractor.ExtractText(extractText: true, input);

            // Assert
            Assert.Equal(expectedContent, result["content"].Trim());
        }

        [Fact]
        public void ExtractText_HandlesEmptyInput()
        {
            // Act
            var result = _extractor.ExtractText(extractText: true, "");

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result["content"].Trim());
        }

        [Fact]
        public void ExtractText_HandlesInvalidHtml()
        {
            // Arrange
            var invalidHtml = "<html><body><p>Unclosed paragraph";

            // Act
            var result = _extractor.ExtractText(extractText: true, invalidHtml);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Unclosed paragraph", result["content"].Trim());
        }

        [Fact]
        public void ExtractText_PreservesWhitespaceCorrectly()
        {
            // Arrange
            var html = "<html><body><p>First line</p>\n<p>Second line</p></body></html>";

            // Act
            var result = _extractor.ExtractText(extractText: true, html);

            // Assert
            Assert.Equal("First line Second line", result["content"].Trim());
        }

        [Fact]
        public void ExtractText_ExtractsTitle_WhenExtractTextIsFalse()
        {
            // Arrange
            var htmlContent = "<html><head><title>Page Title</title></head><body><p>Content</p></body></html>";

            // Act
            var result = _extractor.ExtractText(extractText: false, htmlContent);

            // Assert
            Assert.Equal("Page Title", result["title"]);
            Assert.Equal("<p>Content</p>", result["content"]);
        }

        [Theory]
        [InlineData("<html><body><p>Content</p></body></html>", "<p>Content</p>")]
        [InlineData("<html><body><script>alert('hi');</script><p>Content</p></body></html>", "<script>alert('hi');</script><p>Content</p>")]
        public void ExtractText_WhenExtractTextIsFalse_ReturnsBodyInnerHtml(string input, string expectedContent)
        {
            // Act
            var result = _extractor.ExtractText(extractText: false, input);

            // Assert
            Assert.Equal(expectedContent, result["content"]);
        }

        [Fact]
        public void ExtractText_WhenExtractTextIsFalse_ReturnsBodyInnerHtml_()
        {
            // Arrange
            var htmlContent = "<html><body><p>Test content</p></body></html>";

            // Act
            var result = _extractor.ExtractText(extractText: false, htmlContent);

            // Assert
            Assert.Equal("<p>Test content</p>", result["content"]);
        }

        [Fact]
        public void ExtractText_HandlesHtmlWithoutBody()
        {
            // Arrange
            var htmlContent = "<html><p>Content without body tag</p></html>";

            // Act
            var result = _extractor.ExtractText(extractText: true, htmlContent);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result["content"]);
        }

        [Fact]
        public void ExtractText_HandlesMultipleTitleTags()
        {
            // Arrange
            var htmlContent = "<html><head><title>First Title</title><title>Second Title</title></head><body><p>Content</p></body></html>";

            // Act
            var result = _extractor.ExtractText(extractText: true, htmlContent);

            // Assert
            Assert.Equal("First Title", result["title"]); // Should take the first title tag
        }

        [Fact]
        public void ExtractText_HandlesSpecialCharacters()
        {
            // Arrange
            var htmlContent = "<html><head><title>Title &amp; Special &lt;Characters&gt;</title></head><body><p>Content &copy; 2024</p></body></html>";

            // Act
            var result = _extractor.ExtractText(extractText: true, htmlContent);

            // Assert
            Assert.Equal("Title & Special <Characters>", result["title"]);
            Assert.Contains("Content © 2024", result["content"]);
        }

        [Fact]
        public void ExtractText_HandlesNestedElements()
        {
            // Arrange
            var htmlContent = "<html><body><div>Outer <span>Inner</span> Text</div></body></html>";

            // Act
            var result = _extractor.ExtractText(extractText: true, htmlContent);

            // Assert
            Assert.Equal("Outer Inner Text", result["content"].Trim());
        }

        [Fact]
        public void ExtractText_HandlesMisspelledDivTags()
        {
            // Arrange
            // Note the misspelled div tag
            var htmlContent = "<html><body><div>Branch coverage</div><siv>93%</div><div>Line coverage</div><div>100%</div></body></html>";

            // Act
            var result = _extractor.ExtractText(extractText: true, htmlContent);

            // Assert
            // Verifies that the extra greater-than character is handled correctly
            Assert.Equal("Branch coverage 93% Line coverage 100%", result["content"]);
        }

        [Fact]
        public void ExtractText_WhenNoNodesFound_ReturnsEmptyContent()
        {
            // Arrange
            // HTML without either body or title
            var htmlContent = "<html><div>Some content</div></html>";

            // Act
            var result = _extractor.ExtractText(extractText: true, htmlContent);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.ContainsKey("content"));
            Assert.True(result.ContainsKey("title"));
            Assert.Empty(result["content"]);
            Assert.Empty(result["title"]);
        }

        [Fact]
        public void ExtractText_WhenExtractTextIsFalseAndBodyNull_ReturnsEmptyContent()
        {
            // Arrange
            var htmlContent = "<html><div>No body tag</div></html>";

            // Act
            var result = _extractor.ExtractText(extractText: false, htmlContent);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result["content"]);
        }

        [Fact]
        public void ExtractText_WhenNoScriptTags_StillProcessesContent()
        {
            // Arrange
            var htmlContent = "<html><body><p>Clean content without script tags</p></body></html>";

            // Act
            var result = _extractor.ExtractText(extractText: true, htmlContent);

            // Assert
            Assert.Equal("Clean content without script tags", result["content"]);
        }

        [Fact]
        public void ExtractPage_WhenPlainText_ReturnsContentAsIs()
        {
            var result = _extractor.ExtractPage("Just plain content");

            Assert.Equal("Just plain content", result.Text);
            Assert.Empty(result.Images);
        }

        [Fact]
        public void ExtractPage_WhenHtmlWithImageAndUrlOnly_HasThinEffectiveBodyText()
        {
            var html = """
                <html><body>
                <p><a href="https://app.bwz.se/item">https://app.bwz.se/item</a></p>
                <img src="https://example.com/uploaded/image.png" width="540" alt="" />
                </body></html>
                """;

            var result = _extractor.ExtractPage(html, new Uri("https://example.com/article"));

            Assert.True(result.EffectiveBodyText.Length < 200);
            Assert.DoesNotContain("https://", result.EffectiveBodyText, StringComparison.OrdinalIgnoreCase);
            Assert.Single(result.Images);
            Assert.Equal("https://example.com/uploaded/image.png", result.Images[0].Src);
            Assert.Equal(540, result.Images[0].Width);
        }

        [Fact]
        public void ExtractPage_ResolvesRelativeImageUrls()
        {
            var html = "<html><body><img src=\"/uploaded/image.png\" width=\"400\" /></body></html>";

            var result = _extractor.ExtractPage(html, new Uri("https://example.com/guides/page"));

            Assert.Single(result.Images);
            Assert.Equal("https://example.com/uploaded/image.png", result.Images[0].AbsoluteUri?.ToString());
        }

        [Fact]
        public void ExtractPage_IncludesAltTextInContent()
        {
            var html = "<html><body><p>Intro</p><img src=\"https://example.com/a.png\" alt=\"Dam at Finnforsen\" /></body></html>";

            var result = _extractor.ExtractPage(html);

            Assert.Contains("Intro", result.Text);
            Assert.Contains("Dam at Finnforsen", result.Text);
        }

        [Fact]
        public void ExtractPage_WhenHtml_StripsScriptsFromIndexedText()
        {
            var html = "<html><body><p>Article</p><script>alert('x')</script></body></html>";

            var result = _extractor.ExtractPage(html);

            Assert.Equal("Article", result.Text);
        }

        [Fact]
        public void ExtractPage_IgnoresSiteChrome_WhenArticleHasLittleText()
        {
            var html = """
                <html><body>
                <nav>Start Aktuellt Kunskapsbank Fakturor Elavtal Kontakt</nav>
                <article class="guide_article">
                  <h1>2026-06-25 Utskick företagskunder</h1>
                  <div class="guide_updated">Uppdaterad 2026-06-24</div>
                  <div class="guide_text">
                    <p><a href="https://app.bwz.se/item">https://app.bwz.se/item</a></p>
                    <img src="https://example.com/uploaded/newsletter.png" width="540" />
                  </div>
                </article>
                <footer>Cookies Personuppgifter Öppettider Kundservice lång sidfotstext</footer>
                </body></html>
                """;

            var result = _extractor.ExtractPage(html, new Uri("https://example.com/guide"), "article.guide_article");

            Assert.DoesNotContain("Kunskapsbank", result.EffectiveBodyText);
            Assert.DoesNotContain("Personuppgifter", result.EffectiveBodyText);
            Assert.Contains("Utskick företagskunder", result.EffectiveBodyText);
            Assert.True(result.EffectiveBodyText.Length < 200);
            Assert.Equal("article.guide_article", result.ContentScope);
            Assert.Single(result.Images);
            Assert.Equal("https://example.com/uploaded/newsletter.png", result.Images[0].Src);
        }

        [Fact]
        public void ExtractPage_DoesNotCountNavigationImages()
        {
            var html = """
                <html><body>
                <nav><img src="https://example.com/logo.png" width="200" height="80" /></nav>
                <article>
                  <img src="https://example.com/newsletter.png" width="540" />
                </article>
                </body></html>
                """;

            var result = _extractor.ExtractPage(html, contentSelector: "article");

            Assert.Single(result.Images);
            Assert.Equal("https://example.com/newsletter.png", result.Images[0].Src);
        }

        [Fact]
        public void ExtractPage_WithoutContentSelector_IncludesSiteChrome()
        {
            var html = """
                <html><body>
                <nav>Kunskapsbank</nav>
                <article class="guide_article"><p>Short</p></article>
                <footer>Personuppgifter</footer>
                </body></html>
                """;

            var result = _extractor.ExtractPage(html);

            Assert.Contains("Kunskapsbank", result.EffectiveBodyText);
            Assert.Contains("Personuppgifter", result.EffectiveBodyText);
            Assert.Equal("body", result.ContentScope);
        }
    }
}
