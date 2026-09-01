namespace AzureSearchCrawler.Interfaces
{
    /// <summary>
    /// Recognizes text from an image stream.
    /// </summary>
    public interface IOcrEngine
    {
        /// <summary>
        /// Extracts text from the given image.
        /// </summary>
        /// <param name="image">A readable image stream (PNG, JPEG, GIF, or WebP).</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>Recognized text, or an empty string when nothing was found.</returns>
        Task<string> RecognizeAsync(Stream image, CancellationToken cancellationToken = default);
    }
}
