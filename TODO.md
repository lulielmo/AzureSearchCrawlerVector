# TODO List

## Code Quality Improvements
- [ ] Investigate and improve Crap Score (41) for CrawlPageAsync method in unit test coverage report
      - Current score indicates high complexity combined with incomplete test coverage
      - Consider refactoring the method into smaller parts
      - Identify untested code paths

## Technical Debt
- [ ] Split AzureSearchIndexerTests.cs (1429 lines) into smaller, more focused test files:
      - AzureSearchIndexerConstructorTests.cs - Constructor and initialization tests
      - AzureSearchIndexerCrawlTests.cs - Page crawling and processing tests
      - AzureSearchIndexerEmbeddingTests.cs - Embedding generation and handling tests
      - AzureSearchIndexerDryRunTests.cs - Dry-run mode specific tests
      Improves:
      - Code organization and maintainability
      - Test discovery and execution
      - File readability
      - Easier parallel development

## System Redesign
- [x] Implement new crawler architecture with separation of concerns:
      - [x] Create CrawledWebPage model to avoid naming conflicts
      - [x] Implement CrawledPageQueue for thread-safe data handling
      - [x] Create VectorizedPageProcessor for embedding and indexing
      - [x] Update AbotCrawler to use new queue-based approach
      - [x] Update SitemapCrawler to use new queue-based approach
      - [x] Update HeadlessBrowserCrawler to use new queue-based approach
      - [ ] Implement batch processing for embeddings
      - [ ] Add rate limiting for embedding API calls
      - [ ] Create integration tests for new components
      Benefits:
      - Improved performance through separation of crawling and processing
      - Better handling of rate limits
      - More maintainable and testable code structure
      - Clearer separation of concerns

## System Design Details
- [x] Implement two-phase processing:
      - Phase 1: Fast crawling without blocking
        - Focus on performance
        - No embedding or indexing
        - Populate queue without rate limits
      - Phase 2: Controlled processing
        - Handle rate limiting
        - Batch processing for embeddings
        - Controlled indexing
- [x] Queue Management:
      - [x] Use in-memory ConcurrentQueue for single program
      - [ ] Consider message queues (mq/msmq/Redis) if split into two programs
      - [ ] Simple error handling (no complex recovery needed)
- [ ] Monitoring and Logging:
      - [x] Console output for basic monitoring
      - [x] Follow logging levels from guidelines:
        - Error: Critical errors stopping the process
        - Warning: Issues affecting result quality
        - Information: Overall process status
        - Debug: Detailed operational info
- [ ] Performance Optimization:
      - [ ] Use GenerateEmbeddingsAsync for batch processing
      - [ ] Implement rate limiting with 4-second delay
      - [ ] Process pages in batches of 10

## Next Steps
- [ ] Update remaining crawlers (SitemapCrawler and HeadlessBrowserCrawler) to use the new queue-based approach
- [ ] Implement batch processing in VectorizedPageProcessor
- [ ] Add rate limiting to VectorizedPageProcessor
- [ ] Create integration tests for the new components
- [ ] Test the complete pipeline with a real website

## Documentation
- [ ] Update README.md with new architecture
- [ ] Document rate limiting and batch processing strategies
- [ ] Add examples of different crawling modes

## Testing
- [ ] Create unit tests for VectorizedPageProcessor
- [ ] Create integration tests for the complete pipeline
- [ ] Test rate limiting and batch processing

## Notes
- Items are marked with [x] when completed
- Each item should ideally include:
  - Clear description of what needs to be done
  - Why it's important (optional)
  - Any relevant context or suggestions
  - Links to relevant files/issues (optional) 