using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.AI.OpenAI;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using AzureSearchCrawler.Interfaces;
using AzureSearchCrawler.Models;
using OpenAI.Embeddings;
using Abot2.Poco;

namespace AzureSearchCrawler
{
    /// <summary>
    /// Processes crawled pages by generating embeddings and indexing them.
    /// </summary>
    public class VectorizedPageProcessor : ICrawledPageProcessor
    {
        private readonly string _searchServiceEndpoint;
        private readonly string _indexName;
        private readonly string _adminApiKey;
        private readonly string _embeddingAiEndpoint;
        private readonly string _embeddingAiAdminApiKey;
        private readonly string _embeddingDeployment;
        private readonly int _azureOpenAIEmbeddingDimensions;
        private readonly IConsole _console;
        private readonly CrawledPageQueue _queue;
        private readonly TimeSpan _rateLimitDelay = TimeSpan.FromSeconds(4);
        private readonly int _batchSize = 10;

        private SearchClient? _searchClient;
        private AzureOpenAIClient? _azureOpenAIClient;
        private EmbeddingClient? _embeddingClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="VectorizedPageProcessor"/> class.
        /// </summary>
        public VectorizedPageProcessor(
            string searchServiceEndpoint,
            string indexName,
            string adminApiKey,
            string embeddingAiEndpoint,
            string embeddingAiAdminApiKey,
            string embeddingDeployment,
            int azureOpenAIEmbeddingDimensions,
            IConsole console,
            CrawledPageQueue queue)
        {
            _searchServiceEndpoint = searchServiceEndpoint ?? throw new ArgumentNullException(nameof(searchServiceEndpoint));
            _indexName = indexName ?? throw new ArgumentNullException(nameof(indexName));
            _adminApiKey = adminApiKey ?? throw new ArgumentNullException(nameof(adminApiKey));
            _embeddingAiEndpoint = embeddingAiEndpoint ?? throw new ArgumentNullException(nameof(embeddingAiEndpoint));
            _embeddingAiAdminApiKey = embeddingAiAdminApiKey ?? throw new ArgumentNullException(nameof(embeddingAiAdminApiKey));
            _embeddingDeployment = embeddingDeployment ?? throw new ArgumentNullException(nameof(embeddingDeployment));
            _azureOpenAIEmbeddingDimensions = azureOpenAIEmbeddingDimensions;
            _console = console ?? throw new ArgumentNullException(nameof(console));
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        }

        /// <summary>
        /// Processes a single crawled page.
        /// </summary>
        public async Task PageCrawledAsync(CrawledPage page)
        {
            ArgumentNullException.ThrowIfNull(page);
            ArgumentNullException.ThrowIfNull(page.Uri);

            try
            {
                var crawledWebPage = new CrawledWebPage(
                    page.Uri,
                    page.AngleSharpHtmlDocument?.QuerySelector("title")?.TextContent ?? string.Empty,
                    page.AngleSharpHtmlDocument?.Body?.TextContent ?? string.Empty,
                    (int)page.HttpResponseMessage.StatusCode,
                    null);

                _queue.Enqueue(crawledWebPage);
            }
            catch (Exception ex)
            {
                _console.WriteLine($"Error processing page {page.Uri}: {ex.Message}", LogLevel.Error);
                _console.WriteLine($"Stack trace: {ex.StackTrace}", LogLevel.Debug);
            }
        }

        /// <summary>
        /// Called when the crawling process is complete.
        /// </summary>
        public async Task CrawlFinishedAsync()
        {
            _queue.MarkAsComplete();
        }

        /// <summary>
        /// Starts processing the queue of crawled pages.
        /// </summary>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task ProcessQueueAsync(CancellationToken cancellationToken = default)
        {
            _console.WriteLine("Starting to process crawled pages...", LogLevel.Information);

            try
            {
                await InitializeClientsAsync();

                var batch = new List<CrawledWebPage>();
                while (!_queue.IsEmptyAndComplete && !cancellationToken.IsCancellationRequested)
                {
                    if (_queue.TryDequeue(out var page))
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

                _console.WriteLine("Finished processing crawled pages.", LogLevel.Information);
            }
            catch (Exception ex)
            {
                _console.WriteLine($"Error processing queue: {ex.Message}", LogLevel.Error);
                _console.WriteLine($"Stack trace: {ex.StackTrace}", LogLevel.Debug);
                throw;
            }
        }

        private async Task InitializeClientsAsync()
        {
            _console.WriteLine("Initializing clients...", LogLevel.Information);

            var endpoint = new Uri(_searchServiceEndpoint);
            var credential = new AzureKeyCredential(_adminApiKey);
            _searchClient = new SearchClient(endpoint, _indexName, credential);

            var embeddingEndpoint = new Uri(_embeddingAiEndpoint);
            var embeddingCredential = new AzureKeyCredential(_embeddingAiAdminApiKey);
            _azureOpenAIClient = new AzureOpenAIClient(embeddingEndpoint, embeddingCredential);
            _embeddingClient = _azureOpenAIClient.GetEmbeddingClient(_embeddingDeployment);

            _console.WriteLine("Clients initialized successfully.", LogLevel.Information);
        }

        private async Task ProcessBatchAsync(List<CrawledWebPage> batch, CancellationToken cancellationToken)
        {
            _console.WriteLine($"Processing batch of {batch.Count} pages...", LogLevel.Information);

            var documents = new List<SearchDocument>();
            foreach (var page in batch)
            {
                try
                {
                    // Generate embeddings for title and content
                    var titleEmbedding = await GenerateEmbeddingAsync(page.Title, cancellationToken);
                    var contentEmbedding = await GenerateEmbeddingAsync(page.Content, cancellationToken);

                    // Create search document
                    var document = new SearchDocument
                    {
                        ["id"] = page.Uri.ToString(),
                        ["title"] = page.Title,
                        ["content"] = page.Content,
                        ["titleVector"] = titleEmbedding,
                        ["contentVector"] = contentEmbedding
                    };

                    documents.Add(document);
                }
                catch (Exception ex)
                {
                    _console.WriteLine($"Error processing page {page.Uri}: {ex.Message}", LogLevel.Error);
                    _console.WriteLine($"Stack trace: {ex.StackTrace}", LogLevel.Debug);
                }
            }

            if (documents.Count > 0)
            {
                await IndexDocumentsAsync(documents, cancellationToken);
            }
        }

        private async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                _console.WriteLine("Skipping empty text for embedding generation", LogLevel.Warning);
                return new float[_azureOpenAIEmbeddingDimensions];
            }

            await Task.Delay(_rateLimitDelay, cancellationToken);

            var options = new EmbeddingGenerationOptions { Dimensions = _azureOpenAIEmbeddingDimensions };
            var embedding = await _embeddingClient!.GenerateEmbeddingAsync(text, options, cancellationToken);
            return embedding.Value.ToFloats().ToArray();
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