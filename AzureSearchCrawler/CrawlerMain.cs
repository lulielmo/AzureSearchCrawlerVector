using AzureSearchCrawler.Interfaces;
using AzureSearchCrawler.Models;
using AzureSearchCrawler.Adapters;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;  // För SystemConsole
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

        private readonly Func<string, string, string, string, string, string, int, Interfaces.IConsole, CrawledPageQueue, (ICrawledPageProcessor, Task)> _processorFactory;
        private readonly Func<ICrawledPageProcessor, CrawlMode, Interfaces.IConsole, IWebCrawlingStrategy> _crawlerFactory;

        public CrawlerMain(
            Func<string, string, string, string, string, string, int, Interfaces.IConsole, CrawledPageQueue, (ICrawledPageProcessor, Task)>? processorFactory = null,
            Func<ICrawledPageProcessor, CrawlMode, Interfaces.IConsole, IWebCrawlingStrategy>? crawlerFactory = null)
        {
            _processorFactory = processorFactory ?? DefaultProcessorFactory;
            _crawlerFactory = crawlerFactory ?? DefaultCrawlerFactory;
        }

        private (ICrawledPageProcessor, Task) DefaultProcessorFactory(
            string serviceEndPoint, string adminApiKey, string indexName, 
            string embeddingEndpoint, string embeddingKey, string embeddingDeployment, 
            int embeddingDimensions, Interfaces.IConsole console, CrawledPageQueue queue)
        {
            var processor = new VectorizedPageProcessor(
                serviceEndPoint, adminApiKey, indexName, 
                embeddingEndpoint, embeddingKey, embeddingDeployment, 
                embeddingDimensions, console, queue);

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
            return await crawlerMain.RunAsync(args, new System.CommandLine.IO.SystemConsole());
        }

        // Flyttad till en egen metod för testbarhet
        public async Task<int> RunAsync(string[] args, System.CommandLine.IConsole systemConsole)
        {
            var console = new SystemConsoleAdapter(systemConsole);

            #region Site options
            var rootUriOption = new Option<string>(
                aliases: ["--rootUri", "-r"],
                description: "Root URI to start crawling from");


            var maxPagesOption = new Option<int>(
                aliases: ["--maxPages", "-m"],
                getDefaultValue: () => DefaultMaxPagesToIndex,
                description: "Maximum number of pages to index");

            var maxDepthOption = new Option<int>(
                aliases: ["--maxDepth", "-d"],
                getDefaultValue: () => DefaultMaxCrawlDepth,
                description: "Maximum crawl depth");
            #endregion

            #region Search service options
            var serviceEndPointOption = new Option<string>(
                aliases: ["--serviceEndPoint", "-s"],
                description: "Azure Search service endpoint")
            { IsRequired = true };

            var indexNameOption = new Option<string>(
                aliases: ["--indexName", "-i"],
                description: "Name of the search index")
            { IsRequired = true };

            var adminApiKeyOption = new Option<string>(
                aliases: ["--adminApiKey", "-a"],
                description: "Admin API key for Azure Search")
            { IsRequired = true };
            #endregion

            #region Embedding service options

            var embeddingAiEndpointOption = new Option<string>(
                aliases: ["--embeddingEndPoint", "-ee"],
                description: "The Url (service end point) of your Azure AI Embedding service")
            { IsRequired = true };

            var embeddingAiAdminKeyOption = new Option<string>(
                aliases: ["--embeddingAdminKey", "-ek"],
                description: "The admin key for your Azure AI Embedding service")
            { IsRequired = true };

            var embeddingAiDeploymentNameOption = new Option<string>(
                aliases: ["--embeddingDeploymentName", "-ed"],
                description: "The name of the deployment for your Azure AI Embedding service")
            { IsRequired = true };

            var azureOpenAIEmbeddingDimensionsOption = new Option<int>(
                aliases: ["--azureOpenAIEmbeddingDimensions", "-aed"],
                description: "The dimensions of the embedding")
            { IsRequired = true };

            #endregion

            #region General options
            var dryRunOption = new Option<bool>(
                aliases: ["--dryRun", "-dr"],
                getDefaultValue: () => false,
                description: "Test crawling without uploading to index");

            var sitesFileOption = new Option<FileInfo?>(
                aliases: ["--sitesFile", "-f"],
                description: "Path to a JSON file containing sites to crawl");

            var domSelectorOption = new Option<string>(
                aliases: ["--domSelector", "-ds"],
                description: "DOM selector to limit which links to follow (e.g. 'div.blog-container div.blog-main')");

            var verboseOption = new Option<bool>(
                aliases: ["--verbose", "-v"],
                getDefaultValue: () => false,
                description:"Enable verbose output");

            var modeOption = new Option<CrawlMode>(
                aliases: ["--crawlMode", "-cm"],
                getDefaultValue: () => CrawlMode.Standard,
                description: "Crawling mode (Standard, Headless or Sitemap)");
            #endregion

            var rootCommand = new RootCommand("Web crawler that indexes content in Azure Search.")
            {
                rootUriOption,
                maxPagesOption,
                maxDepthOption,
                serviceEndPointOption,
                indexNameOption,
                adminApiKeyOption,
                embeddingAiEndpointOption,
                embeddingAiAdminKeyOption,
                embeddingAiDeploymentNameOption,
                azureOpenAIEmbeddingDimensionsOption,
                dryRunOption,
                sitesFileOption,
                domSelectorOption,
                verboseOption,
                modeOption
            };

            rootCommand.SetHandler(async (InvocationContext context) =>
            {
                try
                {
                    var rootUri = context.ParseResult.GetValueForOption(rootUriOption);
                    var serviceEndPoint = context.ParseResult.GetValueForOption(serviceEndPointOption);
                    var indexName = context.ParseResult.GetValueForOption(indexNameOption);
                    var adminApiKey = context.ParseResult.GetValueForOption(adminApiKeyOption);
                    var maxPages = context.ParseResult.GetValueForOption(maxPagesOption);
                    var maxDepth = context.ParseResult.GetValueForOption(maxDepthOption);
                    var dryRun = context.ParseResult.GetValueForOption(dryRunOption);
                    var sitesFile = context.ParseResult.GetValueForOption(sitesFileOption);
                    var domSelector = context.ParseResult.GetValueForOption(domSelectorOption);
                    var embeddingEndPoint = context.ParseResult.GetValueForOption(embeddingAiEndpointOption);
                    var embeddingAdminKey = context.ParseResult.GetValueForOption(embeddingAiAdminKeyOption);
                    var embeddingDeploymentName = context.ParseResult.GetValueForOption(embeddingAiDeploymentNameOption);
                    var azureOpenAIEmbeddingDimensions = context.ParseResult.GetValueForOption(azureOpenAIEmbeddingDimensionsOption);
                    var verbose = context.ParseResult.GetValueForOption(verboseOption);
                    var mode = context.ParseResult.GetValueForOption(modeOption);
                    
                    if (verbose)
                    {
                        console.SetVerbose(true);
                    }

                    if (rootUri == null && sitesFile == null)
                    {
                        console.WriteError($"Either --rootUri or --sitesFile must be specified{Environment.NewLine}");
                        context.ExitCode = 1;
                        return;
                    }

                    if (!Uri.IsWellFormedUriString(serviceEndPoint, UriKind.Absolute))
                    {
                        console.WriteError($"Invalid service endpoint URL format: {serviceEndPoint}{Environment.NewLine}");
                        context.ExitCode = 1;
                        return;
                    }

                    if (!Uri.IsWellFormedUriString(embeddingEndPoint, UriKind.Absolute))
                    {
                        console.WriteError($"Invalid embedding endpoint URL format: {embeddingEndPoint}{Environment.NewLine}");
                        context.ExitCode = 1;
                        return;
                    }

                    var sites = new List<SiteConfig>();
                    if (sitesFile != null)
                    {
                        if (!sitesFile.Exists)
                        {
                            console.WriteError($"Sites file not found: {sitesFile.FullName}{Environment.NewLine}");
                            context.ExitCode = 1;
                            return;
                        }

                        try
                        {
                            var json = await File.ReadAllTextAsync(sitesFile.FullName);
                            sites = JsonSerializer.Deserialize<List<SiteConfig>>(json, _jsonOptions);
                            if (sites == null || sites.Count == 0)
                            {
                                console.WriteError($"Could not read any sites from file, or the file is empty: {sitesFile.FullName}{Environment.NewLine}");
                                context.ExitCode = 1;
                                return;
                            }
                        }
                        catch (JsonException ex)
                        {
                            console.WriteError($"Error parsing sites file: {ex.Message}{Environment.NewLine}");
                            context.ExitCode = 1;
                            return;
                        }
                    }
                    else if (rootUri != null)
                    {
                        if (!Uri.TryCreate(rootUri, UriKind.Absolute, out var uri))
                        {
                            console.WriteError($"Invalid root URI format: {rootUri}{Environment.NewLine}");
                            context.ExitCode = 1;
                            return;
                        }
                        else
                        {
                            sites.Add(new SiteConfig { Uri = rootUri, MaxDepth = maxDepth, DomSelector = domSelector });
                        }
                    }

                    if (sites == null || sites.Count == 0)
                    {
                        console.WriteLine("No sites to crawl.", LogLevel.Warning);
                        return;
                    }

                    var queue = new CrawledPageQueue();
                    
                    var (processor, processorTask) = _processorFactory(
                        serviceEndPoint, 
                        adminApiKey ?? throw new ArgumentException("Admin API key is required"),
                        indexName ?? throw new ArgumentException("Index name is required"),
                        embeddingEndPoint, 
                        embeddingAdminKey ?? throw new ArgumentException("Embedding admin key is required"),
                        embeddingDeploymentName  ?? throw new ArgumentException("Embedding deployment name is required"),
                        azureOpenAIEmbeddingDimensions, console, queue);

                    if (dryRun)
                    {
                        // I dry run vill vi inte att processorn kör i bakgrunden
                        processorTask = Task.CompletedTask;
                    }

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

                        console.WriteLine($"Starting crawl for {site.Uri}...", LogLevel.Information);
                        console.WriteLine($"Config: MaxPages={siteMaxPages}, MaxDepth={siteMaxDepth}, DomSelector='{siteDomSelector}'", LogLevel.Debug);

                        var crawler = _crawlerFactory(processor, mode, console);

                        try
                        {
                            await crawler.CrawlAsync(
                                new Uri(site.Uri),
                                siteMaxPages,
                                siteMaxDepth,
                                siteDomSelector);
                        }
                        catch (Exception ex)
                        {
                            console.WriteError($"An error occurred while crawling {site.Uri}: {ex.Message}");
                            console.WriteLine($"Technical details: {ex}", LogLevel.Debug);
                            context.ExitCode = 1;
                        }
                    }

                    console.WriteLine("All crawling completed. Waiting for page processing to finish...", LogLevel.Information);
                    
                    // Signalera att inga fler sidor kommer och vänta på att kön bearbetas klart
                    await processor.CrawlFinishedAsync();
                    await processorTask;

                    console.WriteLine("All tasks finished.", LogLevel.Information);

                }
                catch (Exception ex)
                {
                    console.WriteError($"Error: {ex.Message}{Environment.NewLine}");
                    context.ExitCode = 1;
                }
            });

            return await rootCommand.InvokeAsync(args, systemConsole);
        }
    }
}