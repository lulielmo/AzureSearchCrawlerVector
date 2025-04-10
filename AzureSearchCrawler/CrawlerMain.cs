using AzureSearchCrawler.Interfaces;
using AzureSearchCrawler.Models;
using AzureSearchCrawler.Adapters;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;  // För SystemConsole
using System.CommandLine.Parsing;
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
        private readonly Func<string, string, string, string, string, string, int, bool, TextExtractor, bool, Interfaces.IConsole, AzureSearchIndexer> _indexerFactory;
        private readonly Func<AzureSearchIndexer, CrawlMode, Interfaces.IConsole, IWebCrawlingStrategy> _crawlerFactory;

        private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

        // Suppressing IDE0290 as the traditional constructor provides better readability
        // in this case with multiple parameters and complex types. Primary constructor is more suitable for simpler classes.
#pragma warning disable IDE0290 // Use primary constructor
        public CrawlerMain(
            Func<string, string, string, string, string, string, int, bool, TextExtractor, bool, Interfaces.IConsole, AzureSearchIndexer>? indexerFactory = null,
            Func<AzureSearchIndexer, CrawlMode, Interfaces.IConsole, IWebCrawlingStrategy>? crawlerFactory = null)
        {
            _indexerFactory = indexerFactory ?? DefaultIndexerFactory;
            _crawlerFactory = crawlerFactory ?? DefaultCrawlerFactory;
        }
#pragma warning restore IDE0290 // Use primary constructor

        internal static IWebCrawlingStrategy DefaultCrawlerFactory(AzureSearchIndexer indexer, CrawlMode mode, Interfaces.IConsole console)
        {
            return mode switch
            {
                CrawlMode.Sitemap => new SitemapCrawler(indexer, console),
                CrawlMode.Standard => new AbotCrawler(indexer, console),
                CrawlMode.Headless => new HeadlessBrowserCrawler(indexer, console),
                _ => throw new ArgumentException($"Unsupported crawl mode: {mode}", nameof(mode))
            };
        }

        private static AzureSearchIndexer DefaultIndexerFactory(
            string endpoint, string index, string key, 
            string embeddingEndpoint, string embeddingKey, string embeddingDeployment,
            int embeddingDimensions, bool extract, TextExtractor extractor, 
            bool dryRun, Interfaces.IConsole console)
        {
            return new AzureSearchIndexer(
                endpoint, index, key, 
                embeddingEndpoint, embeddingKey, embeddingDeployment,
                embeddingDimensions, extract, extractor, 
                dryRun, console);
        }

        // Entry point
        public static async Task<int> Main(string[] args)
        {
            var crawlerMain = new CrawlerMain();
            return await crawlerMain.RunAsync(args, new SystemConsole());
        }

        // Flyttad till en egen metod för testbarhet
        public async Task<int> RunAsync(string[] args, System.CommandLine.IConsole console)
        {
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
            var extractTextOption = new Option<bool>(
                aliases: ["--extractText", "-e"],
                getDefaultValue: () => true,
                description: "Extract text from HTML (true) or save raw HTML (false)");

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
                extractTextOption,
                dryRunOption,
                sitesFileOption,
                domSelectorOption,
                verboseOption,
                modeOption
            };

            int exitCode = 0;
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
                    var extractText = context.ParseResult.GetValueForOption(extractTextOption);
                    var dryRun = context.ParseResult.GetValueForOption(dryRunOption);
                    var sitesFile = context.ParseResult.GetValueForOption(sitesFileOption);
                    var domSelector = context.ParseResult.GetValueForOption(domSelectorOption);
                    var embeddingEndPoint = context.ParseResult.GetValueForOption(embeddingAiEndpointOption);
                    var embeddingAdminKey = context.ParseResult.GetValueForOption(embeddingAiAdminKeyOption);
                    var embeddingDeploymentName = context.ParseResult.GetValueForOption(embeddingAiDeploymentNameOption);
                    var azureOpenAIEmbeddingDimensions = context.ParseResult.GetValueForOption(azureOpenAIEmbeddingDimensionsOption);
                    var verbose = context.ParseResult.GetValueForOption(verboseOption);
                    var mode = context.ParseResult.GetValueForOption(modeOption);
                    var logLevel = verbose ? LogLevel.Verbose : LogLevel.Information;

                    console.WriteLine($"Verbose mode: {verbose}");  // Debug-utskrift
                    console.WriteLine($"Crawl mode: {mode}");      // Debug-utskrift

                    if (rootUri == null && sitesFile == null)
                    {
                        console.Error.Write($"Either --rootUri or --sitesFile must be specified{Environment.NewLine}");
                        exitCode = 1;
                        return;
                    }

                    if (!Uri.IsWellFormedUriString(serviceEndPoint, UriKind.Absolute))
                    {
                        console.Error.Write($"Invalid service endpoint URL format: {serviceEndPoint}{Environment.NewLine}");
                        exitCode = 1;
                        return;
                    }

                    if (!Uri.IsWellFormedUriString(embeddingEndPoint, UriKind.Absolute))
                    {
                        console.Error.Write($"Invalid service endpoint URL format: {embeddingEndPoint}{Environment.NewLine}");
                        exitCode = 1;
                        return;
                    }

                    // Använd den inskickade konsolen om den implementerar IConsole, annars skapa en adapter
                    var consoleToUse = console as Interfaces.IConsole ?? new SystemConsoleAdapter(console);
                    if (verbose)
                    {
                        consoleToUse.SetVerbose(true);
                    }

                    var indexer = _indexerFactory(
                            serviceEndPoint, 
                            indexName!, 
                            adminApiKey!,
                            embeddingEndPoint,
                            embeddingAdminKey!,
                            embeddingDeploymentName!,
                            azureOpenAIEmbeddingDimensions,
                            extractText,
                            new TextExtractor(), 
                            dryRun, 
                            consoleToUse);
                    var crawler = _crawlerFactory(indexer, mode, consoleToUse);

                    if (sitesFile != null)
                    {
                        if (!sitesFile.Exists)
                        {
                            console.Error.Write($"Sites file not found: {sitesFile.FullName}{Environment.NewLine}");
                            exitCode = 1;
                            return;
                        }

                        try
                        {
                            var sites = JsonSerializer.Deserialize<List<SiteConfig>>(
                                await File.ReadAllTextAsync(sitesFile.FullName),
                                _jsonOptions
                            ) ?? throw new InvalidOperationException("Failed to deserialize sites file");

                            if (sites == null || sites.Count == 0)
                            {
                                console.Error.Write($"Could not read sites from file: {sitesFile.FullName}{Environment.NewLine}");
                                exitCode = 1;
                                return;
                            }

                            foreach (var site in sites)
                            {
                                if (!Uri.TryCreate(site.Uri, UriKind.Absolute, out var uri))
                                {
                                    console.Error.Write($"Invalid URI in sites file: {site.Uri}{Environment.NewLine}");
                                    continue;
                                }

                                console.WriteLine($"Crawling {site.Uri} with depth {site.MaxDepth} ({site.DomSelector})...");
                                await crawler.CrawlAsync(new Uri(site.Uri), maxPages, site.MaxDepth, site.DomSelector);
                            }
                        }
                        catch (JsonException ex)
                        {
                            console.Error.Write($"Error parsing sites file: {ex.Message}{Environment.NewLine}");
                            exitCode = 1;
                            return;
                        }
                    }
                    else if (!Uri.TryCreate(rootUri, UriKind.Absolute, out var uri))
                    {
                        console.Error.Write($"Invalid root URI format: {rootUri}{Environment.NewLine}");
                        exitCode = 1;
                        return;
                    }
                    else
                    {
                        console.WriteLine($"Crawling {rootUri} with depth {maxDepth} ({domSelector})...");
                        await crawler.CrawlAsync(new Uri(rootUri), maxPages, maxDepth, domSelector);
                    }
                }
                catch (Exception ex)
                {
                    console.Error.Write($"Error: {ex.Message}{Environment.NewLine}");
                    exitCode = 1;
                }
            });

            var parseResult = await rootCommand.InvokeAsync(args, console);
            return parseResult != 0 ? parseResult : exitCode;
        }
    }
}