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
            Assert.Equal("First Title", result["title"]); // Ska ta första title-taggen
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
            // Notera den felaktiga div-taggen som är felstavad
            var htmlContent = "<html><body><div>Branch coverage</div><siv>93%</div><div>Line coverage</div><div>100%</div></body></html>";

            // Act
            var result = _extractor.ExtractText(extractText: true, htmlContent);

            // Assert
            // Verifierar att det extra större-än-tecknet hanteras korrekt
            Assert.Equal("Branch coverage 93% Line coverage 100%", result["content"]);
        }

        [Fact]
        public void ExtractText_WhenNoNodesFound_ReturnsEmptyContent()
        {
            // Arrange
            // HTML utan varken body eller title
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
    }
}