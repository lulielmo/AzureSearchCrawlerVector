namespace AzureSearchCrawler.Models
{
    /// <summary>
    /// An image referenced from a crawled page.
    /// </summary>
    public sealed record PageImage(
        string Src,
        Uri? AbsoluteUri,
        int? Width,
        int? Height,
        string? Alt);
}
