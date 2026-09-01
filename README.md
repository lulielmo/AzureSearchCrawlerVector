# About

This is a fork of [thomas11/AzureSearchCrawler:master](https://github.com/thomas11/AzureSearchCrawler) with additions of crawling multiple sites at once and using vector fields in the AI Search Index.

[Azure AI Search](https://azure.microsoft.com/en-us/products/ai-services/ai-search/) delivers accurate, hyper-personalized responses in your Gen AI applications. This project helps you get content from a website into an Azure AI Search index. It supports multiple crawling strategies (Abot, Sitemap, and Headless Browser) and uses vector embeddings for enhanced search capabilities.

This project is intended as a demo or a starting point for a real crawler. At a minimum, you'll want to replace the console messages with proper logging, and customize the text extraction to improve results for your use case.

# Architecture Overview

The crawler uses a modern, scalable architecture with the following key components:

- **Crawlers** (`IWebCrawlingStrategy`): Responsible for discovering and downloading web pages
  - `AbotCrawler`: Traditional web crawling using Abot framework
  - `SitemapCrawler`: Efficient crawling using XML sitemaps
  - `HeadlessBrowserCrawler`: JavaScript-rendered content using Playwright
- **Queue System** (`CrawledPageQueue`): Thread-safe queue for storing crawled pages
- **Processor** (`VectorizedPageProcessor`): Handles embedding generation and indexing with batch processing and rate limiting
- **Factory Pattern**: Enables easy testing and dependency injection

## Processing Flow

1. **Crawling Phase**: Crawlers discover pages and add HTML to a queue
2. **Processing Phase**: VectorizedPageProcessor processes pages in batches:
   - Extracts clean text from HTML
   - Optionally runs local Tesseract OCR when a page has little body text and content images
   - Generates embeddings for titles and content
   - Indexes documents with vector fields
   - Handles rate limiting and error recovery

# Howto: quick start

- Create an Azure Search search service. If you're new to Azure Search, follow [this guide](https://docs.microsoft.com/en-us/azure/search/search-create-service-portal).
- Create an index in your search service with the following fields:
  - `id` (string, key): Unique identifier (SHA512 hash of URL)
  - `url` (string): The page URL
  - `title` (string): Page title
  - `content` (string): Page content
  - `title_vector` (vector): Title embeddings
  - `content_vector` (vector): Content embeddings
- Create an Azure OpenAI service for embeddings
- Run CrawlerMain, either from Visual Studio after opening the .sln file, or from the command line after compiling using msbuild.
- You will need to pass a few command-line arguments, such as your search service endpoint information and the root URL of the site you'd like to crawl. Calling the program without arguments or with -h will list the arguments.

Refer to the **[Wiki](https://github.com/lulielmo/AzureSearchCrawlerVector/wiki)** for more detailed instructions for setting up the needed services in Azure.

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
  -dr, --dryRun                                              Test crawling and link selection without uploading to
                                                             the index [default: False]
  --ocrPreview                                               Preview OCR threshold decisions without embeddings or
                                                             indexing. Add --enableOcr to also run Tesseract
                                                             [default: False]
  -f, --sitesFile <sitesFile>                                Path to a JSON file containing sites to crawl
  -ds, --domSelector <domSelector>                           DOM selector to limit which links to follow (e.g.
                                                             'div.blog-container div.blog-main')
  -cs, --contentSelector <contentSelector>                   CSS selector for the main content area to extract text
                                                             and images from (e.g. 'article.guide_article'). When
                                                             omitted, the entire body is used
  -v, --verbose                                              Enable verbose output [default: False]
  -cm, --crawlMode <Headless|Sitemap|Standard>               Crawling mode (Standard, Headless or Sitemap) [default:
                                                             Standard]
  --enableOcr                                                Enable Tesseract OCR fallback for pages with little body
                                                             text [default: False]
  --ocrLanguage <ocrLanguage>                                Tesseract language codes (e.g. swe+eng) [default: swe+eng]
  --ocrTesseractPath <ocrTesseractPath>                      Path to the tesseract executable [default: tesseract]
  --ocrTessDataPath <ocrTessDataPath>                        Optional path to a tessdata directory
  --ocrTextThreshold <ocrTextThreshold>                      Run OCR when effective body text is shorter than this many
                                                             characters [default: 200]
  --ocrMaxImagesPerPage <ocrMaxImagesPerPage>                Maximum number of content images to OCR on a single page
                                                             [default: 10]
  --version                                                  Show version information
  -?, -h, --help                                             Show help and usage information
```
> [!TIP]
> `--dryRun` tests navigation and link selection (`maxDepth`, `domSelector`) without calling Azure.
> `--contentSelector` limits text and image extraction to a CSS region (for example `article.guide_article`). When omitted, the entire `body` is used. The same value can be set per site in the sites file.
> `--ocrPreview` tests OCR threshold decisions on crawled pages without embeddings or indexing.
> Combine `--ocrPreview --enableOcr` to also run Tesseract and see how much text is extracted from images.

> [!NOTE]
> Use `--enableOcr` when knowledge articles store most of their text inside images. OCR runs only for pages whose extracted body text is below `--ocrTextThreshold` and that contain content-sized images. Tesseract must be installed locally (Swedish language data recommended: `swe+eng`). Tune `--ocrTextThreshold` with `--ocrPreview` before a full indexing run.

> [!NOTE]
> When using the DOM selector option, the root page of the website will still be crawled even if it doesn't match the selector. This is a known behavior due to how the crawler evaluates links. The DOM selector will effectively filter all other pages based on the specified selector.

## Crawling Modes

- **Standard**: Uses Abot framework for traditional web crawling
- **Sitemap**: Efficiently crawls using XML sitemaps when available
- **Headless**: Uses Playwright for JavaScript-rendered content

## Site json file
By using the command line switch `-f, --sitesFile <sitesFile>` you can specify a number of sites and the desired maximum crawl depth for each site. The format of the file is json as exemplified below:
```json
[
  {
    "uri": "https://example.com/blog",
    "maxDepth": 3,
    "domSelector": "div.blog-content",
    "contentSelector": "article"
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

## Crawler Configuration

The Abot crawler is configured by the method Crawler.CreateCrawlConfiguration, which you can adjust to your liking.

## Processing Configuration

The `VectorizedPageProcessor` supports several configuration options:
- **Batch Size**: Number of pages processed together (default: 10)
- **Rate Limiting**: Delay between batches (default: 4 seconds)
- **Embedding Dimensions**: Vector size for embeddings
- **Error Handling**: Automatic retry and error recovery

# Code overview

- **CrawlerMain**: Contains the setup information and orchestrates the crawling process
- **Crawlers** (`IWebCrawlingStrategy`): Different strategies for discovering web pages
  - `AbotCrawler`: Traditional crawling using Abot framework
  - `SitemapCrawler`: XML sitemap-based crawling
  - `HeadlessBrowserCrawler`: JavaScript-rendered content using Playwright
- **CrawledPageQueue**: Thread-safe queue for storing crawled pages
- **VectorizedPageProcessor**: Handles embedding generation and indexing with batch processing
- **TextExtractor**: Extracts and processes text content from HTML
- **OcrPageEnricher**: Optional Tesseract OCR fallback for image-heavy pages with little body text
- **Models**: Data structures for crawled pages and site configuration

## Key Interfaces

- `IWebCrawlingStrategy`: Defines crawling behavior
- `ICrawledPageProcessor`: Defines page processing behavior
- `IConsole`: Abstraction for logging and output
- `IOcrEngine`: Abstraction for local OCR (Tesseract by default)
- `IImageDownloader`: Abstraction for downloading images used by OCR

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
2. Use the same schema as your production index (including vector fields)
3. The test will automatically clean up documents after running

## Performance and Scalability

The new architecture provides several performance benefits:

- **Asynchronous Processing**: Crawling and indexing happen in parallel
- **Batch Processing**: Pages are processed in configurable batches
- **Rate Limiting**: Built-in rate limiting to respect API limits
- **Memory Efficiency**: Queue-based processing prevents memory overflow
- **Error Recovery**: Automatic retry and error handling

## Logging and Monitoring

The application uses structured logging with different levels:
- **Error**: Critical errors that stop the process
- **Warning**: Issues affecting result quality but not stopping the process
- **Information**: Overall process status and milestones
- **Debug**: Detailed operational information
