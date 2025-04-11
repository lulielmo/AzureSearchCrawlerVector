using Abot2.Poco;
using Azure;
using Azure.AI.OpenAI;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using AzureSearchCrawler.Interfaces;
using AzureSearchCrawler.Models;
using OpenAI.Embeddings;
using System.Collections.Concurrent;
using System.ClientModel;

namespace AzureSearchCrawler
{
    /// <summary>
    /// A ICrawledPageProcessor that indexes crawled pages into Azure Search. Pages are represented by the nested WebPage class.
    /// <para/>To customize what text is extracted and indexed from each page, you implement a custom TextExtractor
    /// and pass it in.
    /// </summary>
    public partial class AzureSearchIndexer : ICrawledPageProcessor
    {
        internal const int IndexingBatchSize = 10;

        private readonly string _searchServiceEndpoint;
        private readonly string _indexName;
        private readonly string _adminApiKey;
        private readonly string _embeddingAiEndpoint;
        private readonly string _embeddingAiAdminApiKey;
        private readonly string _embeddingDeployment;
        private readonly int _azureOpenAIEmbeddingDimensions;
        private readonly bool _extractText;
        private readonly TextExtractor _textExtractor;
        private readonly bool _dryRun;
        private readonly IConsole _console;
        private SearchClient? _searchClient;

        private AzureOpenAIClient? _azureOpenAIClient;
        private EmbeddingClient? _embeddingClient;

        private readonly BlockingCollection<WebPage> _queue = [];
        private readonly SemaphoreSlim indexingLock = new(1, 1);

        private readonly RateLimiter _rateLimiter;

        public AzureSearchIndexer(
            string searchServiceEndpoint,
            string indexName,
            string adminApiKey,
            string embeddingAiEndpoint,
            string embeddingAiAdminApiKey,
            string embeddingDeployment,
            int azureOpenAIEmbeddingDimensions,
            bool extractText,
            TextExtractor textExtractor,
            bool dryRun,
            IConsole console,
            bool enableRateLimiting = true)
        {
            if (string.IsNullOrWhiteSpace(searchServiceEndpoint))
                throw new ArgumentException("Value cannot be null or empty.", nameof(searchServiceEndpoint));
            if (string.IsNullOrWhiteSpace(indexName))
                throw new ArgumentException("Value cannot be null or empty.", nameof(indexName));
            if (string.IsNullOrWhiteSpace(adminApiKey))
                throw new ArgumentException("Value cannot be null or empty.", nameof(adminApiKey));
            if (string.IsNullOrWhiteSpace(embeddingAiEndpoint))
                throw new ArgumentException("Value cannot be null or empty.", nameof(embeddingAiEndpoint));
            if (string.IsNullOrWhiteSpace(embeddingAiAdminApiKey))
                throw new ArgumentException("Value cannot be null or empty.", nameof(embeddingAiAdminApiKey));
            if (string.IsNullOrWhiteSpace(embeddingDeployment))
                throw new ArgumentException("Value cannot be null or empty.", nameof(embeddingDeployment));
            if (azureOpenAIEmbeddingDimensions == 0)
                throw new ArgumentException("Value cannot be 0.", nameof(azureOpenAIEmbeddingDimensions));
            ArgumentNullException.ThrowIfNull(textExtractor);

            _searchServiceEndpoint = searchServiceEndpoint;
            _indexName = indexName;
            _adminApiKey = adminApiKey;
            _embeddingAiEndpoint = embeddingAiEndpoint;
            _embeddingAiAdminApiKey = embeddingAiAdminApiKey;
            _embeddingDeployment = embeddingDeployment;
            _azureOpenAIEmbeddingDimensions = azureOpenAIEmbeddingDimensions;
            _extractText = extractText;
            _textExtractor = textExtractor;
            _dryRun = dryRun;
            _console = console ?? throw new ArgumentNullException(nameof(console));

            _rateLimiter = new RateLimiter(TimeSpan.FromSeconds(4), enableRateLimiting);

            if (!dryRun)
            {
                _searchClient = GetOrCreateSearchClient();
                _azureOpenAIClient = GetOrCreateAiClient();
                _embeddingClient = GetOrCreateEmbeddingClient();
            }
        }

        internal SearchClient GetOrCreateSearchClient()
        {
            if (_searchClient != null) return _searchClient;
            if (_dryRun) return null!;

            _console.WriteLine("Initializing Azure Search client", LogLevel.Information);
            _console.WriteLine($"Connecting to {_searchServiceEndpoint}, index: {_indexName}", LogLevel.Debug);
            
            var endpoint = new Uri(_searchServiceEndpoint);
            var credential = new AzureKeyCredential(_adminApiKey);
            _searchClient = new SearchClient(endpoint, _indexName, credential);
            
            _console.WriteLine("Azure Search client initialized successfully", LogLevel.Debug);
            return _searchClient;
        }

        internal AzureOpenAIClient GetOrCreateAiClient()
        {
            if (_azureOpenAIClient != null) return _azureOpenAIClient;
            if (_dryRun) return null!;

            _console.WriteLine("Initializing Azure OpenAI client", LogLevel.Information);
            _console.WriteLine($"Connecting to {_embeddingAiEndpoint}", LogLevel.Debug);

            Uri embeddingEndpoint = new(_embeddingAiEndpoint);
            AzureKeyCredential embeddingCredential = new(_embeddingAiAdminApiKey);
            _azureOpenAIClient = new AzureOpenAIClient(embeddingEndpoint, embeddingCredential);
            
            _console.WriteLine("Azure OpenAI client initialized successfully", LogLevel.Debug);
            return _azureOpenAIClient;
        }

        private EmbeddingClient GetOrCreateEmbeddingClient()
        {
            if (_embeddingClient != null) { return _embeddingClient; }
            if (_dryRun) return null!;
            ArgumentNullException.ThrowIfNull(_azureOpenAIClient);

            _console.WriteLine($"Creating embedding client for deployment {_embeddingDeployment}", LogLevel.Information);
            _console.WriteLine($"Using dimensions: {_azureOpenAIEmbeddingDimensions}", LogLevel.Debug);

            _embeddingClient = _azureOpenAIClient.GetEmbeddingClient(_embeddingDeployment);
            
            _console.WriteLine("Embedding client created successfully", LogLevel.Debug);
            return _embeddingClient;
        }

        public async Task PageCrawledAsync(CrawledPage crawledPage)
        {
            ArgumentNullException.ThrowIfNull(crawledPage);
            ArgumentNullException.ThrowIfNull(crawledPage.Uri);
            if (!_dryRun) ArgumentNullException.ThrowIfNull(_embeddingClient);

            try
            {
                if (_dryRun)
                {
                    _console.WriteLine($"[DRY RUN] Would index page: {crawledPage.Uri}", LogLevel.Information);
                    return;
                }

                if (_rateLimiter != null)
                {
                    _console.WriteLine("Applying rate limiting before processing page", LogLevel.Verbose);
                    await _rateLimiter.WaitAsync();
                }

                var metadata = ExtractPageContent(crawledPage);
                if (metadata == null || string.IsNullOrEmpty(metadata["content"]))
                {
                    _console.WriteLine($"No content extracted from {crawledPage.Uri}", LogLevel.Warning);
                    return;
                }

                _console.WriteLine($"Processing page: {crawledPage.Uri}", LogLevel.Information);
                _console.WriteLine($"Content details - Size: {metadata["content"].Length} bytes, Title length: {metadata["title"].Length} chars", LogLevel.Debug);
                _console.WriteLine($"Content metadata: {string.Join(", ", metadata.Select(kv => $"{kv.Key}: {kv.Value.Length} chars"))}", LogLevel.Verbose);

                // Truncate text to max 8000 characters to be safe
                const int maxLength = 8000;
                string truncatedText = metadata["content"].Length > maxLength ? metadata["content"][..maxLength] : metadata["content"];
                string truncatedTitle = metadata["title"].Length > maxLength ? metadata["title"][..maxLength] : metadata["title"];

                var startTime = DateTime.Now;

                ArgumentNullException.ThrowIfNull(_embeddingClient); // Double-check to ensure client is available

                // Wait before first embedding call (for title)
                if (_rateLimiter != null) await _rateLimiter.WaitAsync();
                var titleEmbedding = await _embeddingClient.GenerateEmbeddingAsync(truncatedTitle, new EmbeddingGenerationOptions { Dimensions = _azureOpenAIEmbeddingDimensions });
                _console.WriteLine($"Title embedding generated with {titleEmbedding.Value.ToFloats().Length} dimensions", LogLevel.Debug);

                // Wait before second embedding call (for content)
                if (_rateLimiter != null) await _rateLimiter.WaitAsync();
                var contentEmbedding = await _embeddingClient.GenerateEmbeddingAsync(truncatedText, new EmbeddingGenerationOptions { Dimensions = _azureOpenAIEmbeddingDimensions });
                _console.WriteLine($"Content embedding generated with {contentEmbedding.Value.ToFloats().Length} dimensions", LogLevel.Debug);

                var processingTime = DateTime.Now - startTime;
                _console.WriteLine($"Embedding generation timing: {processingTime.TotalSeconds:F2} seconds", LogLevel.Verbose);

                var webPage = new WebPage(
                    crawledPage.Uri.ToString(),
                    metadata["title"],
                    metadata["content"],
                    titleEmbedding.Value.ToFloats().ToArray(),
                    contentEmbedding.Value.ToFloats().ToArray()
                );

                _queue.Add(webPage);
                _console.WriteLine($"Added page to indexing queue (size: {_queue.Count}/{IndexingBatchSize})", LogLevel.Debug);

                if (_queue.Count >= IndexingBatchSize)
                {
                    await IndexBatchIfNecessary();
                }
            }
            catch (Exception ex)
            {
                if (ex is RequestFailedException rfe && rfe.Status == 429)
                {
                    _console.WriteLine($"Rate limit exceeded while processing {crawledPage?.Uri}. Waiting 5 seconds...", LogLevel.Warning);
                    await Task.Delay(TimeSpan.FromSeconds(5));
                }
                else
                {
                    _console.WriteLine($"Critical error processing page {crawledPage?.Uri}: {ex.Message}", LogLevel.Error);
                    _console.WriteLine($"Technical details: {ex}", LogLevel.Debug);
                    throw;
                }
            }
        }

        public async Task CrawlFinishedAsync()
        {
            try
            {
                _console.WriteLine("Processing remaining items in indexing queue", LogLevel.Information);
                await IndexBatchIfNecessary();
                _console.WriteLine($"Indexing completed successfully", LogLevel.Information);
            }
            catch (Exception ex)
            {
                _console.WriteLine($"Critical error: {_queue.Count} items remain in indexing queue", LogLevel.Error);
                _console.WriteLine($"Error details: {ex.Message}", LogLevel.Error);
                _console.WriteLine($"Technical details: {ex}", LogLevel.Debug);
                throw;
            }
        }

        internal async Task<IndexDocumentsResult> IndexBatchIfNecessary()
        {
            await indexingLock.WaitAsync();

            try
            {
                if (_queue.Count == 0 || _searchClient == null)
                {
                    return await Task.FromResult<IndexDocumentsResult>(null!);
                }

                var batch = new List<WebPage>();
                while (_queue.Count > 0 && batch.Count < IndexingBatchSize)
                {
                    if (_queue.TryTake(out var page))
                    {
                        batch.Add(page);
                    }
                }

                if (_dryRun)
                {
                    _console.WriteLine($"[DRY RUN] Would index batch of {batch.Count} pages", LogLevel.Information);
                    return await Task.FromResult<IndexDocumentsResult>(null!);
                }

                _console.WriteLine($"Indexing batch of {batch.Count} pages", LogLevel.Information);
                var startTime = DateTime.Now;
                var result = await _searchClient.MergeOrUploadDocumentsAsync(batch);
                var processingTime = DateTime.Now - startTime;
                
                _console.WriteLine($"Batch details - Size: {batch.Count}, Average content length: {batch.Average(p => p.Content.Length):F0} chars", LogLevel.Debug);
                _console.WriteLine($"Batch indexing timing: {processingTime.TotalSeconds:F2} seconds", LogLevel.Verbose);

                return result;
            }
            finally
            {
                indexingLock.Release();
            }
        }

        internal Dictionary<string, string> ExtractPageContent(CrawledPage crawledPage)
        {
            ArgumentNullException.ThrowIfNull(_textExtractor);
            
            if (crawledPage?.Content?.Text == null)
            {
                return new Dictionary<string, string>
                {
                    ["title"] = string.Empty,
                    ["content"] = string.Empty
                };
            }
            
            return _textExtractor.ExtractText(_extractText, crawledPage.Content.Text);
        }

        public async Task IndexPageAsync(string url, Dictionary<string, string> content)
        {
            ArgumentNullException.ThrowIfNull(url);
            ArgumentNullException.ThrowIfNull(content);

            try 
            {
                if (_dryRun)
                {
                    _console.WriteLine($"[DRY RUN] Would index page: {url}", LogLevel.Information);
                    return;
                }

                var searchClient = GetOrCreateSearchClient();
                _console.WriteLine($"Indexing single page: {url}", LogLevel.Information);
                _console.WriteLine($"Content size: {content["content"].Length} bytes, Title length: {content["title"].Length} chars", LogLevel.Debug);

                var batch = new IndexDocumentsBatch<SearchDocument>();
                batch.Actions.Add(IndexDocumentsAction.Upload(
                    new SearchDocument
                    {
                        ["id"] = url,
                        ["content"] = content["content"],
                        ["title"] = content["title"]
                    }));

                var startTime = DateTime.Now;
                await searchClient.IndexDocumentsAsync(batch);
                var processingTime = DateTime.Now - startTime;
                
                _console.WriteLine($"Page indexed successfully", LogLevel.Information);
                _console.WriteLine($"Indexing timing: {processingTime.TotalSeconds:F2} seconds", LogLevel.Verbose);
            }
            catch (Exception ex)
            {
                _console.WriteLine($"Failed to index page {url}: {ex.Message}", LogLevel.Error);
                _console.WriteLine($"Technical details: {ex}", LogLevel.Debug);
                throw;
            }
        }
    }
}
