using HtmlAgilityPack;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;  // För HtmlDecode

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

        public virtual Dictionary<string, string> ExtractText(bool extractText, string content)
        {
            var result = new Dictionary<string, string>
            {
                ["title"] = string.Empty,  // Initiera med tom sträng
                ["content"] = string.Empty // Initiera med tom sträng
            };

            HtmlDocument doc = new();
            doc.LoadHtml(content);

            // Extrahera title
            var titleNode = doc.DocumentNode.SelectSingleNode("//title");
            if (titleNode != null)
            {
                result["title"] = HttpUtility.HtmlDecode(titleNode.InnerText.Trim());
            }

            // Content beror på extractText-flaggan
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

            foreach (var script in node.SelectNodes(".//script|.//style|.//svg|.//path")?.ToList() ?? new List<HtmlNode>())
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

        [GeneratedRegex("[\r\n]+")]
        private static partial Regex MyRegex();
        [GeneratedRegex("[ \t]+")]
        private static partial Regex MyRegex1();
    }
}
