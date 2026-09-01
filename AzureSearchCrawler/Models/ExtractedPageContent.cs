namespace AzureSearchCrawler.Models
{
    /// <summary>
    /// Text and image metadata extracted from a crawled page.
    /// </summary>
    public sealed record ExtractedPageContent(
        string Title,
        string Text,
        string EffectiveBodyText,
        IReadOnlyList<PageImage> Images,
        string ContentScope = "body")
    {
        public static ExtractedPageContent Empty { get; } =
            new(string.Empty, string.Empty, string.Empty, Array.Empty<PageImage>());
    }
}
