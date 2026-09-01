using AngleSharp.Html.Parser;
using AzureSearchCrawler.Models;
using HtmlAgilityPack;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Web;  // For HtmlDecode

namespace AzureSearchCrawler
{
    /// <summary>
    /// Extracts text content from a web page. The default implementation is very simple: it removes all script, style,
    /// svg, and path tags, and then returns the InnerText of the page body, with cleaned up whitespace.
    /// <para/>You can implement your own custom text extraction by overriding the ExtractText method. The protected
    /// helper methods in this class might be useful. GetCleanedUpTextForXpath is the easiest way to get started.
    /// </summary>
    public partial class TextExtractor
    {
        private readonly Regex newlines = MyRegex();
        private readonly Regex spaces = MyRegex1();

        /// <summary>
        /// Extracts title, cleaned body text, effective body text (URLs removed), and images from page content.
        /// Plain text that is not HTML is returned as-is.
        /// </summary>
        /// <param name="contentSelector">
        /// Optional CSS selector for the main content area. When omitted, the entire body is used.
        /// </param>
        public virtual ExtractedPageContent ExtractPage(
            string content,
            Uri? pageUri = null,
            string? contentSelector = null)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return ExtractedPageContent.Empty;
            }

            if (!LooksLikeHtml(content))
            {
                return new ExtractedPageContent(
                    string.Empty,
                    content,
                    StripUrls(content),
                    Array.Empty<PageImage>());
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(content);

            var title = string.Empty;
            var titleNode = doc.DocumentNode.SelectSingleNode("//title");
            if (titleNode != null)
            {
                title = HttpUtility.HtmlDecode(titleNode.InnerText.Trim());
            }

            var contentNode = ResolveContentNode(doc, content, contentSelector, out var contentScope);
            if (contentNode == null)
            {
                return new ExtractedPageContent(title, string.Empty, string.Empty, Array.Empty<PageImage>(), contentScope);
            }

            var text = GetCleanedUpTextForNode(contentNode);
            var images = ExtractImagesFromNode(contentNode, pageUri);

            var altTexts = images
                .Select(image => image.Alt)
                .Where(alt => !string.IsNullOrWhiteSpace(alt))
                .Select(alt => alt!.Trim());
            var altCombined = string.Join(" ", altTexts);
            if (!string.IsNullOrWhiteSpace(altCombined))
            {
                text = string.IsNullOrWhiteSpace(text) ? altCombined : $"{text} {altCombined}";
            }

            return new ExtractedPageContent(title, text, StripUrls(text), images, contentScope);
        }

        /// <summary>
        /// Collects img elements from HTML and resolves relative URLs against the page URI.
        /// </summary>
        public virtual IReadOnlyList<PageImage> ExtractImages(string html, Uri? pageUri = null)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            return ExtractImagesFromNode(doc.DocumentNode, pageUri);
        }

        private static HtmlNode? ResolveContentNode(
            HtmlDocument doc,
            string html,
            string? contentSelector,
            out string contentScope)
        {
            var body = doc.DocumentNode.SelectSingleNode("//body") ?? doc.DocumentNode;
            if (string.IsNullOrWhiteSpace(contentSelector))
            {
                contentScope = "body";
                return body;
            }

            try
            {
                var parser = new HtmlParser();
                using var angleDoc = parser.ParseDocument(html);
                var element = angleDoc.QuerySelector(contentSelector);
                if (element == null)
                {
                    contentScope = $"{contentSelector} (no match, using body)";
                    return body;
                }

                var fragment = new HtmlDocument();
                fragment.LoadHtml(element.OuterHtml);
                var node = fragment.DocumentNode.FirstChild;
                if (node == null)
                {
                    contentScope = $"{contentSelector} (no match, using body)";
                    return body;
                }

                contentScope = contentSelector;
                return node;
            }
            catch (Exception)
            {
                contentScope = $"{contentSelector} (invalid selector, using body)";
                return body;
            }
        }

        private static IReadOnlyList<PageImage> ExtractImagesFromNode(HtmlNode node, Uri? pageUri)
        {
            var imgNodes = node.SelectNodes(".//img");
            if (imgNodes == null || imgNodes.Count == 0)
            {
                return Array.Empty<PageImage>();
            }

            var images = new List<PageImage>(imgNodes.Count);
            foreach (var imgNode in imgNodes)
            {
                var src = imgNode.GetAttributeValue("src", string.Empty);
                if (string.IsNullOrWhiteSpace(src))
                {
                    continue;
                }

                Uri? absoluteUri = null;
                if (Uri.TryCreate(src, UriKind.Absolute, out var absolute))
                {
                    absoluteUri = absolute;
                }
                else if (pageUri != null && Uri.TryCreate(pageUri, src, out var resolved))
                {
                    absoluteUri = resolved;
                }

                images.Add(new PageImage(
                    src,
                    absoluteUri,
                    ParseDimension(imgNode.GetAttributeValue("width", null)),
                    ParseDimension(imgNode.GetAttributeValue("height", null)),
                    NullIfEmpty(imgNode.GetAttributeValue("alt", null))));
            }

            return images;
        }

        internal static bool LooksLikeHtml(string content)
        {
            var trimmed = content.AsSpan().TrimStart();
            return trimmed.Length > 0 && trimmed[0] == '<';
        }

        internal static string StripUrls(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var stripped = UrlRegex().Replace(text, " ");
            stripped = ExtraWhitespaceRegex().Replace(stripped, " ");
            return stripped.Trim();
        }

        private static int? ParseDimension(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var digits = new string(value.TakeWhile(char.IsDigit).ToArray());
            if (int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
            {
                return parsed;
            }

            return null;
        }

        private static string? NullIfEmpty(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value;

        public virtual Dictionary<string, string> ExtractText(bool extractText, string content)
        {
            var result = new Dictionary<string, string>
            {
                ["title"] = string.Empty,  // Initialize with empty string
                ["content"] = string.Empty // Initialize with empty string
            };

            HtmlDocument doc = new();
            doc.LoadHtml(content);

            // Extract title
            var titleNode = doc.DocumentNode.SelectSingleNode("//title");
            if (titleNode != null)
            {
                result["title"] = HttpUtility.HtmlDecode(titleNode.InnerText.Trim());
            }

            // Content depends on extractText flag
            var bodyNode = doc.DocumentNode.SelectSingleNode("//body");
            if (bodyNode != null)
            {
                if (extractText)
                {
                    result["content"] = GetCleanedUpTextForXpath(doc, "//body");
                }
                else
                {
                    result["content"] = bodyNode.InnerHtml;
                }
            }

            return result;
        }

        protected string GetCleanedUpTextForXpath(HtmlDocument doc, string xpath)
        {
            var node = doc.DocumentNode.SelectSingleNode(xpath);
            return GetCleanedUpTextForNode(node);
        }

        private string GetCleanedUpTextForNode(HtmlNode? node)
        {
            if (node == null)
            {
                return string.Empty;
            }

            foreach (var script in node.SelectNodes(".//script|.//style|.//svg|.//path")?.ToList() ?? [])
            {
                script.Remove();
            }

            var textParts = node.Descendants()
                .Where(n => !n.HasChildNodes && !string.IsNullOrWhiteSpace(n.InnerText))
                .Select(n => HttpUtility.HtmlDecode(n.InnerText.Trim()))
                .Where(t => !string.IsNullOrWhiteSpace(t));

            var text = string.Join(" ", textParts);
            text = newlines.Replace(text, " ");
            text = spaces.Replace(text, " ");
            return text.Trim();
        }

        [ExcludeFromCodeCoverage]
        [GeneratedRegex("[\r\n]+")]
        private static partial Regex MyRegex();

        [ExcludeFromCodeCoverage]
        [GeneratedRegex("[ \t]+")]
        private static partial Regex MyRegex1();

        [GeneratedRegex(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase)]
        private static partial Regex UrlRegex();

        [GeneratedRegex(@"\s+")]
        private static partial Regex ExtraWhitespaceRegex();
    }
}
