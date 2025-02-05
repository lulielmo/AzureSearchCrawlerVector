using Abot2.Poco;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using System.Collections.Concurrent;

namespace AzureSearchCrawler
{
    /// <summary>
    /// A CrawlHandler that indexes crawled pages into Azure Search. Pages are represented by the nested WebPage class.
    /// <para/>To customize what text is extracted and indexed from each page, you implement a custom TextExtractor
    /// and pass it in.
    /// </summary>
    public partial class AzureSearchIndexer : CrawlHandler
    {
        internal const int IndexingBatchSize = 10;

        private readonly string _searchServiceEndpoint;
        private readonly string _indexName;
        private readonly string _adminApiKey;
        private readonly bool _extractText;
        private readonly TextExtractor _textExtractor;
        private readonly bool _dryRun;
        private readonly Interfaces.IConsole _console;
        private SearchClient? _searchClient;

        private readonly BlockingCollection<WebPage> _queue = [];
        private readonly SemaphoreSlim indexingLock = new(1, 1);

        public AzureSearchIndexer(
            string searchServiceEndpoint,
            string indexName,
            string adminApiKey,
            bool extractText,
            TextExtractor textExtractor,
            bool dryRun,
            Interfaces.IConsole console)
        {
            if (string.IsNullOrWhiteSpace(searchServiceEndpoint))
                throw new ArgumentException("Value cannot be null or empty.", nameof(searchServiceEndpoint));
            if (string.IsNullOrWhiteSpace(indexName))
                throw new ArgumentException("Value cannot be null or empty.", nameof(indexName));
            if (string.IsNullOrWhiteSpace(adminApiKey))
                throw new ArgumentException("Value cannot be null or empty.", nameof(adminApiKey));
            ArgumentNullException.ThrowIfNull(textExtractor);

            _searchServiceEndpoint = searchServiceEndpoint;
            _indexName = indexName;
            _adminApiKey = adminApiKey;
            _extractText = extractText;
            _textExtractor = textExtractor;
            _dryRun = dryRun;
            _console = console ?? throw new ArgumentNullException(nameof(console));

            if (!dryRun)
            {
                _searchClient = GetOrCreateSearchClient();
            }
        }

        private SearchClient GetOrCreateSearchClient()
        {
            if (_searchClient != null) return _searchClient;
            if (_dryRun) return null!;

            if (string.IsNullOrEmpty(_searchServiceEndpoint) || string.IsNullOrEmpty(_indexName) || string.IsNullOrEmpty(_adminApiKey))
            {
                throw new InvalidOperationException("SearchClient cannot be initialized: Missing configuration");
            }

            var endpoint = new Uri(_searchServiceEndpoint);
            var credential = new AzureKeyCredential(_adminApiKey);
            _searchClient = new SearchClient(endpoint, _indexName, credential);
            return _searchClient;
        }

        public async Task PageCrawledAsync(CrawledPage crawledPage)
        {
            ArgumentNullException.ThrowIfNull(crawledPage);
            ArgumentNullException.ThrowIfNull(crawledPage.Uri);

            var page = ExtractPageContent(crawledPage);
            string? text = page.GetValueOrDefault("content");
            string? title = page.GetValueOrDefault("title");

            if (string.IsNullOrEmpty(text))
            {
                _console.WriteLine("No content for page {0}", crawledPage.Uri.AbsoluteUri);
                return;
            }

            if (_dryRun)
                _console.WriteLine($"[DRY RUN] Would index page: {crawledPage.Uri.AbsoluteUri}");
            else
                _queue.Add(new WebPage(
                    crawledPage.Uri.AbsoluteUri,
                    title ?? string.Empty,
                    text));

            if (_queue.Count > IndexingBatchSize)
            {
                await IndexBatchIfNecessary();
            }
        }

        public async Task CrawlFinishedAsync()
        {
            try
            {
                await IndexBatchIfNecessary();
            }
            catch
            {
                if (_queue.Count > 0)
                {
                    _console.WriteLine("Error: indexing queue is still not empty at the end.");
                }
                throw;
            }

            // sanity check
            if (_queue.Count > 0)
            {
                _console.WriteLine("Error: indexing queue is still not empty at the end.");
            }
        }

        private async Task<IndexDocumentsResult> IndexBatchIfNecessary()
        {
            await indexingLock.WaitAsync();

            try
            {
                if (_queue.Count == 0 || _searchClient == null)
                {
                    // Returnera ett tomt resultat
                    return await Task.FromResult<IndexDocumentsResult>(null!);
                }

                int batchSize = Math.Min(_queue.Count, IndexingBatchSize);
                _console.WriteLine("Indexing batch of {0}", batchSize);

                var pages = new List<WebPage>(batchSize);
                for (int i = 0; i < batchSize; i++)
                {
                    pages.Add(_queue.Take());
                }

                try
                {
                    return await _searchClient.MergeOrUploadDocumentsAsync(pages);
                }
                catch (Exception ex)
                {
                    _console.WriteLine($"Error indexing batch: {ex.Message}");
                    // Lägg tillbaka sidorna i kön
                    foreach (var page in pages)
                    {
                        _queue.Add(page);
                    }
                    throw;
                }
            }
            finally
            {
                indexingLock.Release();
            }
        }

        internal Dictionary<string, string> ExtractPageContent(CrawledPage crawledPage)
        {
            if (crawledPage?.Content?.Text == null)
            {
                return new Dictionary<string, string>
                {
                    ["title"] = string.Empty,
                    ["content"] = string.Empty
                };
            }

            // TextExtractor garanterar att både 'title' och 'content' finns i resultatet
            return _textExtractor.ExtractText(_extractText, crawledPage.Content.Text);
        }

        public async Task IndexPageAsync(string url, Dictionary<string, string> content)
        {
            ArgumentNullException.ThrowIfNull(url);
            ArgumentNullException.ThrowIfNull(content);

            if (_dryRun)
            {
                _console.WriteLine($"[DRY RUN] Would index page: {url}");
                return;
            }

            var searchClient = GetOrCreateSearchClient(); // Kommer kasta exception om den inte kan initialiseras

            var batch = new IndexDocumentsBatch<SearchDocument>();
            batch.Actions.Add(IndexDocumentsAction.Upload(
                new SearchDocument
                {
                    ["id"] = url,
                    ["content"] = content["content"],
                    ["title"] = content["title"]
                }));

            await searchClient.IndexDocumentsAsync(batch);
        }
    }
}
