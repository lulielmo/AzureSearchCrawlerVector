# About

This is a fork of [thomas11/AzureSearchCrawler:master](https://github.com/thomas11/AzureSearchCrawler) with additions of crawling multiple sites at once and using vector fields in the AI Search Index.

[Azure AI Search](https://azure.microsoft.com/en-us/products/ai-services/ai-search/) delivers accurate, hyper-personalized responses in your Gen AI applications. This project helps you get content from a website into an Azure AI Search index. It uses [Abot](https://github.com/sjdirect/abot) to crawl websites. For each page it extracts the content in a customizable way and indexes it into Azure Search.

This project is intended as a demo or a starting point for a real crawler. At a minimum, you'll want to replace the console messages with proper logging, and customize the text extraction to improve results for your use case.

# Howto: quick start

- Create an Azure Search search service. If you're new to Azure Search, follow [this guide](https://docs.microsoft.com/en-us/azure/search/search-create-service-portal).
- Create an index in your search service with three string fields: "id", "url", "title" and "content". Make them searchable. You can also use the [sample definition](./index.json) as a starter. I have configured the title and content fields to use the "English" analyzer.
- Run CrawlerMain, either from Visual Studio after opening the .sln file, or from the command line after compiling using msbuild.
- You will need to pass a few command-line arguments, such as your search service endpoint information (Url) and the root URL of the site you'd like to crawl. Calling the program without arguments or with -h will list the arguments.

# Running the application

## Command line options
These are the different options that are available.
> [!IMPORTANT]
> Even though `-r, --rootUri` and `-f, --sitesFile` are not listed as required the application will require you to use one of them
```
Description:
  Web crawler that indexes content in Azure Search.

Usage:
  AzureSearchCrawler [options]

Options:
  -r, --rootUri <rootUri>                                    Root URI to start crawling from
  -m, --maxPages <maxPages>                                  Maximum number of pages to index [default: 100]
  -d, --maxDepth <maxDepth>                                  Maximum crawl depth [default: 10]
  -s, --serviceEndPoint <serviceEndPoint> (REQUIRED)         Azure Search service endpoint
  -i, --indexName <indexName> (REQUIRED)                     Name of the search index
  -a, --adminApiKey <adminApiKey> (REQUIRED)                 Admin API key for Azure Search
  -ee, --embeddingEndPoint <embeddingEndPoint> (REQUIRED)    The Url (service end point) of your Azure AI Embedding
                                                             service
  -ek, --embeddingAdminKey <embeddingAdminKey> (REQUIRED)    The admin key for your Azure AI Embedding service
  -ed, --embeddingDeploymentName <embeddingDeploymentName>   The name of the deployment for your Azure AI Embedding
  (REQUIRED)                                                 service
  -aed, --azureOpenAIEmbeddingDimensions                     The dimensions of the embedding
  <azureOpenAIEmbeddingDimensions> (REQUIRED)
  -e, --extractText                                          Extract text from HTML (true) or save raw HTML (false)
                                                             [default: True]
  -dr, --dryRun                                              Test crawling without uploading to index [default: False]
  -f, --sitesFile <sitesFile>                                Path to a JSON file containing sites to crawl
  -ds, --domSelector <domSelector>                           DOM selector to limit which links to follow (e.g.
                                                             'div.blog-container div.blog-main')
  -v, --verbose                                              Enable verbose output [default: False]
  -cm, --crawlMode <Headless|Sitemap|Standard>               Crawling mode (Standard, Headless or Sitemap) [default:
                                                             Standard]
  --version                                                  Show version information
  -?, -h, --help                                             Show help and usage information
```
> [!TIP]
> By using the option `-dr, --dryRun` you can test the crawling without sending the extracted information to the Azure AI Search Index.

> [!NOTE]
> When using the DOM selector option, the root page of the website will still be crawled even if it doesn't match the selector. This is a known behavior due to how the crawler evaluates links. The DOM selector will effectively filter all other pages based on the specified selector.

## Site json file
By using the command line switch `-f, --sitesFile <sitesFile>` you can specify a number of sites and the desired maximum maximum crawl depth for each site. The format of the file is json as exemplified below:
```json
[
  {
    "uri": "https://example.com/blog",
    "maxDepth": 3,
    "domSelector": "div.blog-content"
  },
  {
    "uri": "https://another-site.com",
    "maxDepth": 5
  }
]
```

# Howto: customize it for your project

## Text extraction

To adjust what content is extracted and indexed from each page, implement your own TextExtractor subclass. See the class documentation for more information.

## CrawlerConfig

The Abot crawler is configured by the method Crawler.CreateCrawlConfiguration, which you can adjust to your liking.

# Code overview

- CrawlerMain contains the setup information such as the Azure Search service information, and the main method that runs the crawler.
- The Crawler class uses Abot to crawl the given website, based off of the Abot sample. It uses a passed-in CrawlHandler to process each page it finds.
- CrawlHandler is a simple interface decoupling crawling from processing each page.
- AzureSearchIndexer is a CrawlHandler that indexes page content into AzureSearch.
- Pages are modeled by the inner class AzureSearchIndexer.WebPage. The schema of your Azure Search index must match this class.

# Azure Search Crawler

## Integration Testing with Azure Services

To run integration tests that interact with Azure services, you need to set up the following user-specific environment variables:

### Required Environment Variables

```powershell
# Azure Search
$env:AZURE_SEARCH_TEST_ENDPOINT="https://your-search-service.search.windows.net"
$env:AZURE_SEARCH_TEST_KEY="your-search-service-admin-key"

# Azure OpenAI
$env:AZURE_OPENAI_TEST_ENDPOINT="https://your-openai-service.openai.azure.com"
$env:AZURE_OPENAI_TEST_KEY="your-openai-service-key"
$env:AZURE_OPENAI_TEST_DEPLOYMENT="your-embedding-deployment-name"
$env:AZURE_OPENAI_EMBEDDING_DIMENSIONS="1536"  # or whatever dimension your embedding model uses
```

### Setting Up Variables

1. **Temporary (Current Session)**
   ```powershell
   # Set variables for current PowerShell session
   $env:AZURE_SEARCH_TEST_ENDPOINT="..."
   $env:AZURE_OPENAI_EMBEDDING_DIMENSIONS="1536"  # or your model's dimension
   ```

2. **Permanent (User Level)**
   ```powershell
   # Set variables permanently for current user
   [System.Environment]::SetEnvironmentVariable('AZURE_SEARCH_TEST_ENDPOINT', '...', [System.EnvironmentVariableTarget]::User)
   [System.Environment]::SetEnvironmentVariable('AZURE_OPENAI_EMBEDDING_DIMENSIONS', '1536', [System.EnvironmentVariableTarget]::User)
   ```

3. **Using a Script**
   Create a PowerShell script (e.g., `Set-TestEnvironment.ps1`):
   ```powershell
   # Set-TestEnvironment.ps1
   $env:AZURE_SEARCH_TEST_ENDPOINT="..."
   $env:AZURE_OPENAI_EMBEDDING_DIMENSIONS="1536"  # or your model's dimension
   # ... other variables ...
   ```

### Important Notes

- These variables are user-specific and should not be committed to source control
- Each developer should use their own test Azure resources
- The test index name is hardcoded to `integration-test-index`
- Tests will clean up the index after running

### Test Index Setup

1. Create a new search index named `integration-test-index` in your Azure Search service
2. Use the same schema as your production index
3. The test will automatically clean up documents after running