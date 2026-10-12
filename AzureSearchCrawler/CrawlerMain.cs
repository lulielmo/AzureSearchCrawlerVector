using AzureSearchCrawler.Adapters;
using AzureSearchCrawler.Interfaces;
using AzureSearchCrawler.Models;
using System.CommandLine;
using System.Text.Json;

namespace AzureSearchCrawler
{
    /// <summary>
    /// The entry point of the crawler. Adjust the constants at the top and run.
    /// </summary>
    public class CrawlerMain
    {
        private const int DefaultMaxPagesToIndex = 100;
        private const int DefaultMaxCrawlDepth = 10;

        private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

        private readonly Func<string, string, string, string, string, string, int, Interfaces.IConsole, CrawledPageQueue, bool, OcrOptions, bool, (ICrawledPageProcessor, Task)> _processorFactory;
        private readonly Func<ICrawledPageProcessor, CrawlMode, Interfaces.IConsole, IWebCrawlingStrategy> _crawlerFactory;

        public CrawlerMain(
            Func<string, string, string, string, string, string, int, Interfaces.IConsole, CrawledPageQueue, bool, (ICrawledPageProcessor, Task)>? processorFactory = null,
            Func<ICrawledPageProcessor, CrawlMode, Interfaces.IConsole, IWebCrawlingStrategy>? crawlerFactory = null)
        {
            _processorFactory = processorFactory != null
                ? (serviceEndPoint, adminApiKey, indexName, embeddingEndpoint, embeddingKey, embeddingDeployment,
                    embeddingDimensions, console, queue, dryRun, _, _) =>
                    processorFactory(
                        serviceEndPoint, adminApiKey, indexName, embeddingEndpoint, embeddingKey, embeddingDeployment,
                        embeddingDimensions, console, queue, dryRun)
                : DefaultProcessorFactory;
            _crawlerFactory = crawlerFactory ?? DefaultCrawlerFactory;
        }

        private (ICrawledPageProcessor, Task) DefaultProcessorFactory(
            string serviceEndPoint, string adminApiKey, string indexName,
            string embeddingEndpoint, string embeddingKey, string embeddingDeployment,
            int embeddingDimensions, Interfaces.IConsole console, CrawledPageQueue queue, bool dryRun,
            OcrOptions ocrOptions, bool ocrPreview)
        {
            var processor = new VectorizedPageProcessor(
                serviceEndPoint, adminApiKey, indexName,
                embeddingEndpoint, embeddingKey, embeddingDeployment,
                embeddingDimensions, console, queue, dryRun, rateLimitDelay: null,
                ocrOptions: ocrOptions,
                ocrPreview: ocrPreview);

            return (processor, processor.ProcessQueueAsync());
        }

        private IWebCrawlingStrategy DefaultCrawlerFactory(ICrawledPageProcessor processor, CrawlMode mode, Interfaces.IConsole console)
        {
            return mode switch
            {
                CrawlMode.Standard => new AbotCrawler(processor, console),
                CrawlMode.Sitemap => new SitemapCrawler(processor, console),
                CrawlMode.Headless => new HeadlessBrowserCrawler(processor, console),
                _ => throw new ArgumentException($"Unsupported crawl mode: {mode}", nameof(mode)),
            };
        }

        // Entry point
        public static async Task<int> Main(string[] args)
        {
            var crawlerMain = new CrawlerMain();
            return await crawlerMain.RunAsync(args, new SystemConsoleAdapter());
        }

        // Flyttad till en egen metod för testbarhet
        public async Task<int> RunAsync(string[] args, Interfaces.IConsole console)
        {

            #region Site options
            var rootUriOption = new Option<string>("--rootUri", "-r")
            {
                Description = "Root URI to start crawling from"
            };

            var maxPagesOption = new Option<int>("--maxPages", "-m")
            {
                Description = "Maximum number of pages to index",
                DefaultValueFactory = _ => DefaultMaxPagesToIndex
            };

            var maxDepthOption = new Option<int>("--maxDepth", "-d")
            {
                Description = "Maximum crawl depth",
                DefaultValueFactory = _ => DefaultMaxCrawlDepth
            };
            #endregion

            #region Search service options
            var serviceEndPointOption = new Option<string>("--serviceEndPoint", "-s")
            {
                Description = "Azure Search service endpoint",
                Required = true
            };

            var indexNameOption = new Option<string>("--indexName", "-i")
            {
                Description = "Name of the search index",
                Required = true
            };

            var adminApiKeyOption = new Option<string>("--adminApiKey", "-a")
            {
                Description = "Admin API key for Azure Search",
                Required = true
            };
            #endregion

            #region Embedding service options

            var embeddingAiEndpointOption = new Option<string>("--embeddingEndPoint", "-ee")
            {
                Description = "The Url (service end point) of your Azure AI Embedding service",
                Required = true
            };

            var embeddingAiAdminKeyOption = new Option<string>("--embeddingAdminKey", "-ek")
            {
                Description = "The admin key for your Azure AI Embedding service",
                Required = true
            };

            var embeddingAiDeploymentNameOption = new Option<string>("--embeddingDeploymentName", "-ed")
            {
                Description = "The name of the deployment for your Azure AI Embedding service",
                Required = true
            };

            var azureOpenAIEmbeddingDimensionsOption = new Option<int>("--azureOpenAIEmbeddingDimensions", "-aed")
            {
                Description = "The dimensions of the embedding",
                Required = true
            };

            #endregion

            #region General options
            var dryRunOption = new Option<bool>("--dryRun", "-dr")
            {
                Description = "Test crawling and link selection without uploading to the index",
                DefaultValueFactory = _ => false
            };

            var ocrPreviewOption = new Option<bool>("--ocrPreview")
            {
                Description =
                    "Preview OCR threshold decisions without embeddings or indexing. " +
                    "Add --enableOcr to also run Tesseract",
                DefaultValueFactory = _ => false
            };

            var sitesFileOption = new Option<FileInfo?>("--sitesFile", "-f")
            {
                Description = "Path to a JSON file containing sites to crawl"
            };

            var domSelectorOption = new Option<string>("--domSelector", "-ds")
            {
                Description = "DOM selector to limit which links to follow (e.g. 'div.blog-container div.blog-main')"
            };

            var contentSelectorOption = new Option<string>("--contentSelector", "-cs")
            {
                Description =
                    "CSS selector for the main content area to extract text and images from " +
                    "(e.g. 'article.guide_article'). When omitted, the entire body is used"
            };

            var verboseOption = new Option<bool>("--verbose", "-v")
            {
                Description = "Enable verbose output",
                DefaultValueFactory = _ => false
            };

            var modeOption = new Option<CrawlMode>("--crawlMode", "-cm")
            {
                Description = "Crawling mode (Standard, Headless or Sitemap)",
                DefaultValueFactory = _ => CrawlMode.Standard
            };

            var enableOcrOption = new Option<bool>("--enableOcr")
            {
                Description = "Enable Tesseract OCR fallback for pages with little body text",
                DefaultValueFactory = _ => false
            };

            var ocrLanguageOption = new Option<string>("--ocrLanguage")
            {
                Description = "Tesseract language codes (e.g. swe+eng)",
                DefaultValueFactory = _ => "swe+eng"
            };

            var ocrTesseractPathOption = new Option<string>("--ocrTesseractPath")
            {
                Description = "Path to the tesseract executable",
                DefaultValueFactory = _ => "tesseract"
            };

            var ocrTessDataPathOption = new Option<string?>("--ocrTessDataPath")
            {
                Description = "Optional path to a tessdata directory"
            };

            var ocrTextThresholdOption = new Option<int>("--ocrTextThreshold")
            {
                Description = "Run OCR when effective body text is shorter than this many characters",
                DefaultValueFactory = _ => 200
            };

            var ocrMaxImagesOption = new Option<int>("--ocrMaxImagesPerPage")
            {
                Description = "Maximum number of content images to OCR on a single page",
                DefaultValueFactory = _ => 10
            };
            #endregion

            var rootCommand = new RootCommand("Web crawler that indexes content in Azure Search.");
            rootCommand.Options.Add(rootUriOption);
            rootCommand.Options.Add(maxPagesOption);
            rootCommand.Options.Add(maxDepthOption);
            rootCommand.Options.Add(serviceEndPointOption);
            rootCommand.Options.Add(indexNameOption);
            rootCommand.Options.Add(adminApiKeyOption);
            rootCommand.Options.Add(embeddingAiEndpointOption);
            rootCommand.Options.Add(embeddingAiAdminKeyOption);
            rootCommand.Options.Add(embeddingAiDeploymentNameOption);
            rootCommand.Options.Add(azureOpenAIEmbeddingDimensionsOption);
            rootCommand.Options.Add(dryRunOption);
            rootCommand.Options.Add(sitesFileOption);
            rootCommand.Options.Add(domSelectorOption);
            rootCommand.Options.Add(contentSelectorOption);
            rootCommand.Options.Add(verboseOption);
            rootCommand.Options.Add(modeOption);
            rootCommand.Options.Add(enableOcrOption);
            rootCommand.Options.Add(ocrPreviewOption);
            rootCommand.Options.Add(ocrLanguageOption);
            rootCommand.Options.Add(ocrTesseractPathOption);
            rootCommand.Options.Add(ocrTessDataPathOption);
            rootCommand.Options.Add(ocrTextThresholdOption);
            rootCommand.Options.Add(ocrMaxImagesOption);

            rootCommand.SetAction((parseResult, cancellationToken) =>
            {
                return ExecuteAsync(parseResult, cancellationToken);
            });

            var output = console is Interfaces.IConsoleStreams streams ? streams.Output : Console.Out;
            var error = console is Interfaces.IConsoleStreams streamsError ? streamsError.Error : Console.Error;
            return await rootCommand.Parse(args).InvokeAsync(new InvocationConfiguration
            {
                Output = output,
                Error = error
            });

            async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
            {
                var exitCode = 0;
                try
                {
                    var rootUri = parseResult.GetValue(rootUriOption);
                    var serviceEndPoint = parseResult.GetValue(serviceEndPointOption);
                    var indexName = parseResult.GetValue(indexNameOption);
                    var adminApiKey = parseResult.GetValue(adminApiKeyOption);
                    var maxPages = parseResult.GetValue(maxPagesOption);
                    var maxDepth = parseResult.GetValue(maxDepthOption);
                    var dryRun = parseResult.GetValue(dryRunOption);
                    var ocrPreview = parseResult.GetValue(ocrPreviewOption);
                    var sitesFile = parseResult.GetValue(sitesFileOption);
                    var domSelector = parseResult.GetValue(domSelectorOption);
                    var contentSelector = parseResult.GetValue(contentSelectorOption);
                    var embeddingEndPoint = parseResult.GetValue(embeddingAiEndpointOption);
                    var embeddingAdminKey = parseResult.GetValue(embeddingAiAdminKeyOption);
                    var embeddingDeploymentName = parseResult.GetValue(embeddingAiDeploymentNameOption);
                    var azureOpenAIEmbeddingDimensions = parseResult.GetValue(azureOpenAIEmbeddingDimensionsOption);
                    var verbose = parseResult.GetValue(verboseOption);
                    var mode = parseResult.GetValue(modeOption);
                    var enableOcr = parseResult.GetValue(enableOcrOption);
                    var ocrLanguage = parseResult.GetValue(ocrLanguageOption);
                    var ocrTesseractPath = parseResult.GetValue(ocrTesseractPathOption);
                    var ocrTessDataPath = parseResult.GetValue(ocrTessDataPathOption);
                    var ocrTextThreshold = parseResult.GetValue(ocrTextThresholdOption);
                    var ocrMaxImagesPerPage = parseResult.GetValue(ocrMaxImagesOption);
                    
                    if (verbose)
                    {
                        console.SetVerbose(true);
                    }

                    if (rootUri == null && sitesFile == null)
                    {
                        console.WriteError($"Either --rootUri or --sitesFile must be specified{Environment.NewLine}");
                        return 1;
                    }

                    if (!Uri.IsWellFormedUriString(serviceEndPoint, UriKind.Absolute))
                    {
                        console.WriteError($"Invalid service endpoint URL format: {serviceEndPoint}{Environment.NewLine}");
                        return 1;
                    }

                    if (!Uri.IsWellFormedUriString(embeddingEndPoint, UriKind.Absolute))
                    {
                        console.WriteError($"Invalid embedding endpoint URL format: {embeddingEndPoint}{Environment.NewLine}");
                        return 1;
                    }

                    var sites = new List<SiteConfig>();
                    if (sitesFile != null)
                    {
                        if (!sitesFile.Exists)
                        {
                            console.WriteError($"Sites file not found: {sitesFile.FullName}{Environment.NewLine}");
                            return 1;
                        }

                        try
                        {
                            var json = await File.ReadAllTextAsync(sitesFile.FullName, cancellationToken);
                            sites = JsonSerializer.Deserialize<List<SiteConfig>>(json, _jsonOptions);
                            if (sites == null || sites.Count == 0)
                            {
                                console.WriteError($"Could not read any sites from file, or the file is empty: {sitesFile.FullName}{Environment.NewLine}");
                                return 1;
                            }
                        }
                        catch (JsonException ex)
                        {
                            console.WriteError($"Error parsing sites file: {ex.Message}{Environment.NewLine}");
                            return 1;
                        }
                    }
                    else if (rootUri != null)
                    {
                        if (!Uri.TryCreate(rootUri, UriKind.Absolute, out var uri))
                        {
                            console.WriteError($"Invalid root URI format: {rootUri}{Environment.NewLine}");
                            return 1;
                        }
                        else
                        {
                            sites.Add(new SiteConfig
                            {
                                Uri = rootUri,
                                MaxDepth = maxDepth,
                                DomSelector = domSelector,
                                ContentSelector = contentSelector
                            });
                        }
                    }

                    if (sites == null || sites.Count == 0)
                    {
                        console.WriteLine("No sites to crawl.", LogLevel.Warning);
                        return exitCode;
                    }

                    var queue = new CrawledPageQueue();
                    var ocrOptions = new OcrOptions
                    {
                        Enabled = enableOcr,
                        Language = ocrLanguage ?? "swe+eng",
                        TesseractPath = ocrTesseractPath ?? "tesseract",
                        TessDataPath = ocrTessDataPath,
                        TextThreshold = ocrTextThreshold,
                        MaxImagesPerPage = ocrMaxImagesPerPage
                    };

                    if (ocrPreview)
                    {
                        console.WriteLine(
                            $"OCR preview is enabled (text threshold: {ocrOptions.TextThreshold}). " +
                            "Embeddings and indexing will be skipped.",
                            LogLevel.Information);
                    }

                    if (ocrOptions.Enabled)
                    {
                        console.WriteLine(
                            $"OCR fallback is enabled (language: {ocrOptions.Language}, " +
                            $"text threshold: {ocrOptions.TextThreshold})",
                            LogLevel.Information);
                    }

                    var (processor, processorTask) = _processorFactory(
                        serviceEndPoint, 
                        adminApiKey ?? throw new ArgumentException("Admin API key is required"),
                        indexName ?? throw new ArgumentException("Index name is required"),
                        embeddingEndPoint, 
                        embeddingAdminKey ?? throw new ArgumentException("Embedding admin key is required"),
                        embeddingDeploymentName  ?? throw new ArgumentException("Embedding deployment name is required"),
                        azureOpenAIEmbeddingDimensions, console, queue, dryRun, ocrOptions, ocrPreview);

                    foreach (var site in sites)
                    {
                        if (!Uri.TryCreate(site.Uri, UriKind.Absolute, out var uri))
                        {
                            console.WriteError($"Invalid URI in sites file: {site.Uri}{Environment.NewLine}");
                            continue;
                        }
                        var siteMaxPages = maxPages; 
                        var siteMaxDepth = site.MaxDepth;
                        var siteDomSelector = site.DomSelector ?? domSelector;
                        var siteContentSelector = site.ContentSelector ?? contentSelector;

                        console.WriteLine($"Starting crawl for {site.Uri}...", LogLevel.Information);
                        console.WriteLine(
                            $"Config: MaxPages={siteMaxPages}, MaxDepth={siteMaxDepth}, " +
                            $"DomSelector='{siteDomSelector}', ContentSelector='{siteContentSelector}'",
                            LogLevel.Debug);

                        var crawler = _crawlerFactory(processor, mode, console);

                        try
                        {
                            await crawler.CrawlAsync(
                                new Uri(site.Uri),
                                siteMaxPages,
                                siteMaxDepth,
                                siteDomSelector,
                                siteContentSelector);
                        }
                        catch (Exception ex)
                        {
                            console.WriteError($"An error occurred while crawling {site.Uri}: {ex.Message}");
                            console.WriteLine($"Technical details: {ex}", LogLevel.Debug);
                            exitCode = 1;
                        }
                    }

                    console.WriteLine("All crawling completed. Waiting for page processing to finish...", LogLevel.Information);
                    
                    // Signalera att inga fler sidor kommer och vänta på att kön bearbetas klart
                    await processor.CrawlFinishedAsync();
                    await processorTask;

                    console.WriteLine("All tasks finished.", LogLevel.Information);
                    return exitCode;
                }
                catch (Exception ex)
                {
                    console.WriteError($"Error: {ex.Message}{Environment.NewLine}");
                    return 1;
                }
            }
        }
    }
}