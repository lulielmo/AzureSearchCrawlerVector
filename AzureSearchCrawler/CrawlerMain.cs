using AzureSearchCrawler.Interfaces;
using AzureSearchCrawler.Models;
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
        private readonly Func<string, string, string, bool, TextExtractor, bool, AzureSearchIndexer> _indexerFactory;
        private readonly Func<AzureSearchIndexer, ICrawler> _crawlerFactory;

        public CrawlerMain(
            Func<string, string, string, bool, TextExtractor, bool, AzureSearchIndexer>? indexerFactory = null,
            Func<AzureSearchIndexer, ICrawler>? crawlerFactory = null)
        {
            _indexerFactory = indexerFactory ?? ((endpoint, index, key, extract, extractor, dryRun) =>
                new AzureSearchIndexer(endpoint, index, key, extract, extractor, dryRun, new SystemConsoleAdapter(new SystemConsole())));
            _crawlerFactory = crawlerFactory ?? (Func<AzureSearchIndexer, ICrawler>)((indexer) =>
                new Crawler(indexer, new SystemConsoleAdapter(new SystemConsole())));
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
            var rootUriOption = new Option<string>(
                aliases: new[] { "--rootUri", "-r" },
                description: "Root URI to start crawling from");

            var maxPagesOption = new Option<int>(
                aliases: new[] { "--maxPages", "-m" },
                getDefaultValue: () => DefaultMaxPagesToIndex,
                description: "Maximum number of pages to index");

            var maxDepthOption = new Option<int>(
                aliases: new[] { "--maxDepth", "-d" },
                getDefaultValue: () => DefaultMaxCrawlDepth,
                description: "Maximum crawl depth");

            var serviceEndPointOption = new Option<string>(
                aliases: new[] { "--serviceEndPoint", "-s" },
                description: "Azure Search service endpoint")
            { IsRequired = true };

            var indexNameOption = new Option<string>(
                aliases: new[] { "--indexName", "-i" },
                description: "Name of the search index")
            { IsRequired = true };

            var adminApiKeyOption = new Option<string>(
                aliases: new[] { "--adminApiKey", "-a" },
                description: "Admin API key for Azure Search")
            { IsRequired = true };

            var extractTextOption = new Option<bool>(
                aliases: new[] { "--extractText", "-e" },
                getDefaultValue: () => true,
                description: "Extract text from HTML (true) or save raw HTML (false)");

            var dryRunOption = new Option<bool>(
                aliases: new[] { "--dryRun", "-dr" },
                getDefaultValue: () => false,
                description: "Test crawling without uploading to index");

            var sitesFileOption = new Option<FileInfo?>(
                aliases: new[] { "--sitesFile", "-f" },
                description: "Path to a JSON file containing sites to crawl");

            var rootCommand = new RootCommand("Web crawler that indexes content in Azure Search.")
            {
                rootUriOption,
                maxPagesOption,
                maxDepthOption,
                serviceEndPointOption,
                indexNameOption,
                adminApiKeyOption,
                extractTextOption,
                dryRunOption,
                sitesFileOption
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

                    var indexer = _indexerFactory(serviceEndPoint, indexName!, adminApiKey!, extractText,
                        new TextExtractor(), dryRun);
                    var crawler = _crawlerFactory(indexer);

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
                                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                            );

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

                                console.WriteLine($"Crawling {site.Uri} with depth {site.MaxDepth}...");
                                await crawler.CrawlAsync(uri, maxPages, site.MaxDepth);
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
                        await crawler.CrawlAsync(uri, maxPages, maxDepth);
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