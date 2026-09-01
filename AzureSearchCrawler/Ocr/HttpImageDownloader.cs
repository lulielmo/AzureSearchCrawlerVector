using AzureSearchCrawler.Interfaces;

namespace AzureSearchCrawler.Ocr
{
    /// <summary>
    /// Downloads images over HTTP(S) into a memory stream for OCR.
    /// </summary>
    public class HttpImageDownloader : IImageDownloader
    {
        private const long MaxBytes = 20 * 1024 * 1024;
        private readonly HttpClient _httpClient;

        public HttpImageDownloader(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
            {
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (compatible; AzureSearchCrawler/1.0)");
            }
        }

        public async Task<Stream?> DownloadAsync(Uri imageUri, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(imageUri);

            if (imageUri.Scheme != Uri.UriSchemeHttp && imageUri.Scheme != Uri.UriSchemeHttps)
            {
                return null;
            }

            using var response = await _httpClient.GetAsync(
                imageUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            if (response.Content.Headers.ContentLength > MaxBytes)
            {
                return null;
            }

            var memory = new MemoryStream();
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            {
                await source.CopyToAsync(memory, cancellationToken);
            }

            if (memory.Length == 0 || memory.Length > MaxBytes)
            {
                return null;
            }

            memory.Position = 0;
            return memory;
        }
    }
}
