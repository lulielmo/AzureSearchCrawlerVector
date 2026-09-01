using Azure.AI.OpenAI;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using AzureSearchCrawler.Adapters;
using AzureSearchCrawler.Interfaces;
using AzureSearchCrawler.Models;
using AzureSearchCrawler.Ocr;
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
        private readonly bool _ocrPreview;
        private readonly bool _skipAzure;
        private readonly TextExtractor _textExtractor;
        private readonly OcrOptions _ocrOptions;
        private readonly ThinContentDetector _thinContentDetector;
        private readonly OcrPageEnricher? _ocrEnricher;

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
            SearchClient? searchClient = null,
            TextExtractor? textExtractor = null,
            OcrOptions? ocrOptions = null,
            IOcrEngine? ocrEngine = null,
            IImageDownloader? imageDownloader = null,
            bool ocrPreview = false)
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
            _ocrPreview = ocrPreview;
            _skipAzure = dryRun || ocrPreview;
            _rateLimitDelay = rateLimitDelay ?? TimeSpan.FromSeconds(4);
            _embeddingClient = embeddingClient ?? new OpenAIEmbeddingClientAdapter(
                new AzureOpenAIClient(
                    new Uri(_embeddingAiEndpoint),
                    new Azure.AzureKeyCredential(_embeddingAiAdminApiKey)),
                _embeddingDeployment);
            _searchClient = searchClient;
            _textExtractor = textExtractor ?? new TextExtractor();
            _ocrOptions = ocrOptions ?? OcrOptions.Disabled;
            _thinContentDetector = new ThinContentDetector();
            if (_ocrOptions.Enabled)
            {
                _ocrEnricher = new OcrPageEnricher(
                    ocrEngine ?? new TesseractOcrEngine(_ocrOptions),
                    imageDownloader ?? new HttpImageDownloader(),
                    _console,
                    _ocrOptions,
                    _thinContentDetector);
            }
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
            if (_ocrPreview)
            {
                _console.WriteLine(
                    "Starting OCR preview (no embeddings or indexing)...",
                    LogLevel.Information);
            }
            else if (_dryRun)
            {
                _console.WriteLine("Starting to process crawled pages in DRY RUN mode...", LogLevel.Information);
            }
            else
            {
                _console.WriteLine("Starting to process crawled pages...", LogLevel.Information);
            }

            try
            {
                if (!_skipAzure)
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

                if (_ocrPreview)
                {
                    _console.WriteLine("Finished OCR preview.", LogLevel.Information);
                }
                else if (_dryRun)
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
            if (_ocrPreview)
            {
                await PreviewOcrBatchAsync(batch, cancellationToken);
                return;
            }

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
                    var preparedPages = new List<(CrawledWebPage Page, string Title, string Content)>();
                    foreach (var page in pageGroup)
                    {
                        var (title, content) = await PreparePageContentAsync(page, cancellationToken);
                        preparedPages.Add((page, title, content));
                    }

                    var texts = new List<string>();
                    const int maxLength = 8000;
                    var truncatedPages = new List<(CrawledWebPage Page, string Title, string Content)>();

                    foreach (var prepared in preparedPages)
                    {
                        var title = string.IsNullOrWhiteSpace(prepared.Title) ? "-" : prepared.Title;
                        if (title != prepared.Title)
                        {
                            _console.WriteLine($"Empty title found for {prepared.Page.Uri}, replacing with '-'", LogLevel.Warning);
                        }

                        if (string.IsNullOrWhiteSpace(prepared.Content))
                        {
                            _console.WriteLine(
                                $"Warning: Empty content found for {prepared.Page.Uri}, this should have been filtered out earlier",
                                LogLevel.Warning);
                        }

                        var truncatedContent = prepared.Content.Length > maxLength
                            ? prepared.Content[..maxLength]
                            : prepared.Content;
                        var truncatedTitle = title.Length > maxLength ? title[..maxLength] : title;

                        if (prepared.Content.Length > maxLength)
                        {
                            _console.WriteLine(
                                $"Truncated content for {prepared.Page.Uri}: {prepared.Content.Length} -> {truncatedContent.Length} chars",
                                LogLevel.Debug);
                        }
                        if (title.Length > maxLength)
                        {
                            _console.WriteLine(
                                $"Truncated title for {prepared.Page.Uri}: {title.Length} -> {truncatedTitle.Length} chars",
                                LogLevel.Debug);
                        }

                        texts.Add(truncatedTitle);
                        texts.Add(truncatedContent);
                        truncatedPages.Add((prepared.Page, truncatedTitle, truncatedContent));
                    }

                    _console.WriteLine($"Generating embeddings for group of {pageGroup.Count} pages...", LogLevel.Debug);
                    var embeddings = await GenerateEmbeddingsAsync(texts, cancellationToken);
                    _console.WriteLine($"Successfully generated {embeddings.Count} embeddings.", LogLevel.Information);

                    var documents = new List<SearchDocument>();
                    for (int i = 0; i < truncatedPages.Count; i++)
                    {
                        var prepared = truncatedPages[i];
                        var document = new SearchDocument
                        {
                            ["id"] = HashUtils.CreateSHA512(prepared.Page.Uri.ToString()),
                            ["url"] = prepared.Page.Uri.ToString(),
                            ["title"] = prepared.Title,
                            ["content"] = prepared.Content,
                            ["title_vector"] = embeddings[i * 2],
                            ["content_vector"] = embeddings[i * 2 + 1]
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

        private async Task PreviewOcrBatchAsync(List<CrawledWebPage> batch, CancellationToken cancellationToken)
        {
            _console.WriteLine($"[OCR PREVIEW] Evaluating {batch.Count} pages...", LogLevel.Information);

            foreach (var page in batch)
            {
                var extracted = _textExtractor.ExtractPage(page.Content, page.Uri, page.ContentSelector);
                var contentImages = _thinContentDetector.SelectContentImages(
                    extracted.Images,
                    _ocrOptions.MaxImagesPerPage);
                var wouldRunOcr = _thinContentDetector.ShouldUseOcr(
                    extracted.EffectiveBodyText,
                    contentImages,
                    _ocrOptions.TextThreshold);

                var decision = wouldRunOcr
                    ? "would run OCR"
                    : contentImages.Count == 0
                        ? "skip OCR (no content images)"
                        : "skip OCR (body text at or above threshold)";

                _console.WriteLine($"[OCR PREVIEW] {page.Uri}", LogLevel.Information);
                _console.WriteLine(
                    $"[OCR PREVIEW]   Content selector: {extracted.ContentScope}",
                    LogLevel.Information);
                _console.WriteLine(
                    $"[OCR PREVIEW]   Effective body text: {extracted.EffectiveBodyText.Length} chars " +
                    $"(threshold: {_ocrOptions.TextThreshold})",
                    LogLevel.Information);
                _console.WriteLine(
                    $"[OCR PREVIEW]   Sample: {FormatPreviewSample(extracted.EffectiveBodyText)}",
                    LogLevel.Information);
                _console.WriteLine(
                    $"[OCR PREVIEW]   Content images: {contentImages.Count} " +
                    $"(of {extracted.Images.Count} total, max {_ocrOptions.MaxImagesPerPage})",
                    LogLevel.Information);
                _console.WriteLine($"[OCR PREVIEW]   Decision: {decision}", LogLevel.Information);

                if (!wouldRunOcr)
                {
                    continue;
                }

                if (_ocrEnricher == null)
                {
                    _console.WriteLine(
                        "[OCR PREVIEW]   Tesseract not run (add --enableOcr to extract image text)",
                        LogLevel.Information);
                    continue;
                }

                var enriched = await _ocrEnricher.EnrichAsync(extracted, page.Uri, cancellationToken);
                _console.WriteLine(
                    $"[OCR PREVIEW]   Prepared content: {enriched.Length} chars after OCR",
                    LogLevel.Information);
            }
        }

        private async Task<(string Title, string Content)> PreparePageContentAsync(
            CrawledWebPage page,
            CancellationToken cancellationToken)
        {
            var extracted = _textExtractor.ExtractPage(page.Content, page.Uri, page.ContentSelector);
            var title = string.IsNullOrWhiteSpace(page.Title) ? extracted.Title : page.Title;

            if (_ocrEnricher != null)
            {
                var content = await _ocrEnricher.EnrichAsync(extracted, page.Uri, cancellationToken);
                return (title, content);
            }

            return (title, extracted.Text);
        }

        private static string FormatPreviewSample(string text, int maxLength = 160)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "(empty)";
            }

            if (text.Length <= maxLength)
            {
                return text;
            }

            return text[..maxLength] + "...";
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