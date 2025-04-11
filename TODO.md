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

## Future Enhancements
- [ ] 

## Documentation
- [ ] 

## Testing
- [ ] 

## Notes
- Items are marked with [x] when completed
- Each item should ideally include:
  - Clear description of what needs to be done
  - Why it's important (optional)
  - Any relevant context or suggestions
  - Links to relevant files/issues (optional) 