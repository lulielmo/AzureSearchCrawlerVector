using Azure.AI.OpenAI;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using AzureSearchCrawler.Adapters;
using AzureSearchCrawler.Interfaces;
using AzureSearchCrawler.Models;
using AzureSearchCrawler.Utils;

namespace AzureSearchCrawler
{
    /// <summary>
    /// Processes crawled pages by generating embeddings and indexing them.
    /// </summary>
    public class VectorizedPageProcessor : ICrawledPageProcessor
    {
        private readonly string _searchServiceEndpoint;
        private readonly string _adminApiKey;
        private readonly string _indexName;
        private readonly string _embeddingAiEndpoint;
        private readonly string _embeddingAiAdminApiKey;
        private readonly string _embeddingDeployment;
        private readonly int _azureOpenAIEmbeddingDimensions;
        private readonly IEmbeddingClient _embeddingClient;
        private SearchClient? _searchClient;
        private readonly IConsole _console;
        private readonly CrawledPageQueue _queue;
        private readonly int _batchSize = 10;
        private readonly TimeSpan _rateLimitDelay;
        private readonly bool _dryRun;

        /// <summary>
        /// Initializes a new instance of the <see cref="VectorizedPageProcessor"/> class.
        /// </summary>
        public VectorizedPageProcessor(
            string searchServiceEndpoint,
            string adminApiKey,
            string indexName,
            string embeddingAiEndpoint,
            string embeddingAiAdminApiKey,
            string embeddingDeployment,
            int azureOpenAIEmbeddingDimensions,
            IConsole console,
            CrawledPageQueue queue,
            bool dryRun = false,
            TimeSpan? rateLimitDelay = null,
            IEmbeddingClient? embeddingClient = null,
            SearchClient? searchClient = null)
        {
            _searchServiceEndpoint = searchServiceEndpoint ?? throw new ArgumentNullException(nameof(searchServiceEndpoint));
            _adminApiKey = adminApiKey ?? throw new ArgumentNullException(nameof(adminApiKey));
            _indexName = indexName ?? throw new ArgumentNullException(nameof(indexName));
            _embeddingAiEndpoint = embeddingAiEndpoint ?? throw new ArgumentNullException(nameof(embeddingAiEndpoint));
            _embeddingAiAdminApiKey = embeddingAiAdminApiKey ?? throw new ArgumentNullException(nameof(embeddingAiAdminApiKey));
            _embeddingDeployment = embeddingDeployment ?? throw new ArgumentNullException(nameof(embeddingDeployment));
            _azureOpenAIEmbeddingDimensions = azureOpenAIEmbeddingDimensions;
            _console = console ?? throw new ArgumentNullException(nameof(console));
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _dryRun = dryRun;
            _rateLimitDelay = rateLimitDelay ?? TimeSpan.FromSeconds(4);
            _embeddingClient = embeddingClient ?? new OpenAIEmbeddingClientAdapter(
                new AzureOpenAIClient(
                    new Uri(_embeddingAiEndpoint),
                    new Azure.AzureKeyCredential(_embeddingAiAdminApiKey)),
                _embeddingDeployment);
            _searchClient = searchClient;
        }

        /// <summary>
        /// Processes a single crawled page.
        /// </summary>
        public Task PageCrawledAsync(CrawledWebPage page)
        {
            ArgumentNullException.ThrowIfNull(page);
            ArgumentNullException.ThrowIfNull(page.Uri);

            try
            {
                _queue.Enqueue(page);
            }
            catch (Exception ex)
            {
                _console.WriteLine($"Error processing page {page.Uri}: {ex.Message}", LogLevel.Error);
                _console.WriteLine($"Stack trace: {ex.StackTrace}", LogLevel.Debug);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Called when the crawling process is complete.
        /// </summary>
        public Task CrawlFinishedAsync()
        {
            _queue.MarkAsComplete();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Processes the queue of crawled pages asynchronously.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task ProcessQueueAsync(CancellationToken cancellationToken = default)
        {
            if (_dryRun)
            {
                _console.WriteLine("Starting to process crawled pages in DRY RUN mode...", LogLevel.Information);
            }
            else
            {
                _console.WriteLine("Starting to process crawled pages...", LogLevel.Information);
            }

            try
            {
                if (!_dryRun)
                {
                    await InitializeClientsAsync();
                }

                var batch = new List<CrawledWebPage>();
                while (!_queue.IsEmptyAndComplete && !cancellationToken.IsCancellationRequested)
                {
                    if (_queue.TryDequeue(out var page) && page != null)
                    {
                        batch.Add(page);

                        if (batch.Count >= _batchSize)
                        {
                            await ProcessBatchAsync(batch, cancellationToken);
                            batch.Clear();
                        }
                    }
                    else
                    {
                        await Task.Delay(100, cancellationToken);
                    }
                }

                // Process any remaining pages
                if (batch.Count > 0)
                {
                    await ProcessBatchAsync(batch, cancellationToken);
                }

                if (_dryRun)
                {
                    _console.WriteLine("Finished processing crawled pages in DRY RUN mode.", LogLevel.Information);
                }
                else
                {
                    _console.WriteLine("Finished processing crawled pages.", LogLevel.Information);
                }
            }
            catch (Exception ex)
            {
                _console.WriteLine($"Error processing queue: {ex.Message}", LogLevel.Error);
                _console.WriteLine($"Stack trace: {ex.StackTrace}", LogLevel.Debug);
                throw;
            }
        }

        private Task InitializeClientsAsync()
        {
            if (_searchClient == null)
            {
                var searchClientOptions = new SearchClientOptions
                {
                    Retry =
                    {
                        MaxRetries = 4,
                        Mode = Azure.Core.RetryMode.Exponential,
                        Delay = TimeSpan.FromSeconds(1),
                        MaxDelay = TimeSpan.FromSeconds(10)
                    }
                };

                _searchClient = new SearchClient(
                    new Uri(_searchServiceEndpoint),
                    _indexName,
                    new Azure.AzureKeyCredential(_adminApiKey),
                    searchClientOptions);
            }
            return Task.CompletedTask;
        }

        private async Task ProcessBatchAsync(List<CrawledWebPage> batch, CancellationToken cancellationToken)
        {
            if (_dryRun)
            {
                _console.WriteLine($"[DRY RUN] Would process batch of {batch.Count} pages...", LogLevel.Information);
                foreach (var page in batch)
                {
                    _console.WriteLine($"[DRY RUN] Would index page: {page.Uri}", LogLevel.Information);
                }
                return;
            }

            _console.WriteLine($"Processing batch of {batch.Count} pages...", LogLevel.Information);

            try
            {
                // Dela upp batchen i mindre grupper om 5 sidor
                var pageGroups = batch
                    .Select((page, index) => new { Page = page, Index = index })
                    .GroupBy(x => x.Index / 5)
                    .Select(g => g.Select(x => x.Page).ToList())
                    .ToList();

                foreach (var pageGroup in pageGroups)
                {
                    // Förbered titlar och innehåll för denna grupp
                    var texts = new List<string>();
                    var pageIndices = new List<int>();
                    
                    foreach (var page in pageGroup)
                    {
                        // Ersätt tomma titlar med ett bindestreck
                        var title = string.IsNullOrWhiteSpace(page.Title) ? "-" : page.Title;
                        if (title != page.Title)
                        {
                            _console.WriteLine($"Empty title found for {page.Uri}, replacing with '-'", LogLevel.Warning);
                        }

                        // Verifiera att innehållet inte är tomt (bör inte hända)
                        if (string.IsNullOrWhiteSpace(page.Content))
                        {
                            _console.WriteLine($"Warning: Empty content found for {page.Uri}, this should have been filtered out earlier", LogLevel.Warning);
                        }

                        // Truncate text to max 8000 characters to be safe (well below 8192 token limit)
                        const int maxLength = 8000;
                        var truncatedContent = page.Content.Length > maxLength ? page.Content[..maxLength] : page.Content;
                        var truncatedTitle = title.Length > maxLength ? title[..maxLength] : title;

                        if (page.Content.Length > maxLength)
                        {
                            _console.WriteLine($"Truncated content for {page.Uri}: {page.Content.Length} -> {truncatedContent.Length} chars", LogLevel.Debug);
                        }
                        if (title.Length > maxLength)
                        {
                            _console.WriteLine($"Truncated title for {page.Uri}: {title.Length} -> {truncatedTitle.Length} chars", LogLevel.Debug);
                        }

                        texts.Add(truncatedTitle);
                        texts.Add(truncatedContent);
                        pageIndices.Add(pageGroup.IndexOf(page));
                    }

                    // Generera embeddings för gruppen
                    _console.WriteLine($"Generating embeddings for group of {pageGroup.Count} pages...", LogLevel.Debug);
                    var embeddings = await GenerateEmbeddingsAsync(texts, cancellationToken);
                    _console.WriteLine($"Successfully generated {embeddings.Count} embeddings.", LogLevel.Information);

                    // Skapa sökdokument för denna grupp
                    var documents = new List<SearchDocument>();
                    for (int i = 0; i < pageGroup.Count; i++)
                    {
                        var page = pageGroup[i];
                        var titleEmbedding = embeddings[i * 2];
                        var contentEmbedding = embeddings[i * 2 + 1];

                        // Använd samma trunkering som för embedding
                        const int maxLength = 8000;
                        var title = string.IsNullOrWhiteSpace(page.Title) ? "-" : page.Title;
                        var truncatedContent = page.Content.Length > maxLength ? page.Content[..maxLength] : page.Content;
                        var truncatedTitle = title.Length > maxLength ? title[..maxLength] : title;

                        var document = new SearchDocument
                        {
                            ["id"] = HashUtils.CreateSHA512(page.Uri.ToString()),
                            ["url"] = page.Uri.ToString(),
                            ["title"] = truncatedTitle,
                            ["content"] = truncatedContent,
                            ["title_vector"] = titleEmbedding,
                            ["content_vector"] = contentEmbedding
                        };
                        documents.Add(document);
                    }

                    // Indexera dokumenten för denna grupp
                    if (documents.Count > 0)
                    {
                        await IndexDocumentsAsync(documents, cancellationToken);
                    }

                    // Vänta på rate limit innan nästa grupp
                    if (pageGroup != pageGroups.Last())
                    {
                        await Task.Delay(_rateLimitDelay, cancellationToken);
                    }
                }

                // Logga antalet items kvar i kön efter att denna batch är klar
                var remainingItems = _queue.Count;
                if (remainingItems > 0)
                {
                    _console.WriteLine($"{remainingItems} items left in queue", LogLevel.Information);
                }
            }
            catch (Exception ex)
            {
                _console.WriteLine($"Error processing batch: {ex.Message}", LogLevel.Error);
                _console.WriteLine($"Stack trace: {ex.StackTrace}", LogLevel.Debug);
                throw;
            }
        }

        private async Task<List<float[]>> GenerateEmbeddingsAsync(List<string> texts, CancellationToken cancellationToken)
        {
            if (texts == null || !texts.Any())
            {
                _console.WriteLine("No texts provided for embedding generation", LogLevel.Warning);
                return new List<float[]>();
            }

            // Skapa en lista med tomma vektorer för alla texter
            var result = new List<float[]>(texts.Count);
            for (int i = 0; i < texts.Count; i++)
            {
                result.Add(new float[_azureOpenAIEmbeddingDimensions]);
            }

            // Filtrera bort tomma texter men behåll deras index
            var validTextsWithIndex = texts
                .Select((text, index) => new { Text = text, Index = index })
                .Where(x => !string.IsNullOrWhiteSpace(x.Text))
                .ToList();

            if (validTextsWithIndex.Count == 0)
            {
                _console.WriteLine("All texts were empty, skipping embedding generation", LogLevel.Warning);
                return result;
            }

            var embeddings = await _embeddingClient!.GenerateEmbeddingsAsync(
                validTextsWithIndex.Select(x => x.Text).ToList(),
                _azureOpenAIEmbeddingDimensions,
                cancellationToken);

            // Uppdatera resultatet med de genererade embeddingarna
            for (int i = 0; i < validTextsWithIndex.Count; i++)
            {
                result[validTextsWithIndex[i].Index] = embeddings[i];
            }

            return result;
        }

        private async Task IndexDocumentsAsync(List<SearchDocument> documents, CancellationToken cancellationToken)
        {
            _console.WriteLine($"Indexing {documents.Count} documents...", LogLevel.Information);

            var batch = IndexDocumentsBatch.Create(documents.Select(d => IndexDocumentsAction.Upload(d)).ToArray());
            await _searchClient!.IndexDocumentsAsync(batch, cancellationToken: cancellationToken);

            _console.WriteLine($"Successfully indexed {documents.Count} documents.", LogLevel.Information);
        }
    }
} 