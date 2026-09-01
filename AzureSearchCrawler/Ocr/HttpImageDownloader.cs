using AzureSearchCrawler.Interfaces;

namespace AzureSearchCrawler.Ocr
{
    /// <summary>
    /// Downloads images over HTTP(S) into a memory stream for OCR.
    /// </summary>
    public sealed class HttpImageDownloader : IImageDownloader
    {
        private const long MaxBytes = 20 * 1024 * 1024;
        private static readonly HttpClient SharedHttpClient = CreateSharedHttpClient();
        private readonly HttpClient _httpClient;

        public HttpImageDownloader(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? SharedHttpClient;

            if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
            {
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (compatible; AzureSearchCrawler/1.0)");
            }
        }

        private static HttpClient CreateSharedHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (compatible; AzureSearchCrawler/1.0)");
            return client;
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
                await memory.DisposeAsync();
                return null;
            }

            memory.Position = 0;
            return memory;
        }
    }
}
