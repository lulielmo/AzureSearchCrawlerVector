namespace AzureSearchCrawler.Interfaces
{
    /// <summary>
    /// Downloads image bytes for OCR.
    /// </summary>
    public interface IImageDownloader
    {
        /// <summary>
        /// Downloads an image. Returns null when the image cannot be retrieved.
        /// </summary>
        Task<Stream?> DownloadAsync(Uri imageUri, CancellationToken cancellationToken = default);
    }
}
