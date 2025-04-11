# Development Guidelines

## Commit Message Conventions

All commit messages are in English.

We follow the Conventional Commits standard with the following prefixes:

- `feat:` - New functionality
- `fix:` - Bug fixes
- `refactor:` - Code restructuring without changing functionality
- `docs:` - Documentation changes
- `style:` - Formatting changes without changing functionality
- `test:` - Test additions or modifications
- `chore:` - Maintenance tasks (e.g., updating dependencies)
- `perf:` - Performance improvements
- `ci:` - CI/CD configuration changes
- `build:` - Build system changes

Optionally, you can specify a scope in parentheses:
- `feat(crawler): Add support for custom user agents`
- `fix(search): Handle null results in search response`
- `refactor(logging): Implement structured logging standard`

## Logging Standards

We follow a structured logging approach with clear standards for each log level:

### Error (Always shown)
- Critical errors that stop the entire crawling process
- Errors requiring manual intervention
- Examples:
  - Cannot create browser/context
  - Cannot connect to Azure Search
  - Inaccessible root URL
  - Unexpected exceptions in core logic

### Warning (Always shown)
- Issues affecting result quality but not stopping the process
- Examples:
  - Failed page load (404, 500, etc.)
  - Invalid URL structure
  - Page load timeout
  - URL from different domain ignored
  - Circular reference in sitemap
  - Invalid XML format

### Information (Default level)
- Overall process status and milestones
- Examples:
  - Start/end of crawling
  - Number of processed pages
  - Found sitemap
  - Reached limits (pages/depth)
  - Indexing status ("Indexing batch of X pages")
  - Vectorization progress

### Debug (Shown with --verbose)
- Detailed operational information
- Examples:
  - URL being crawled
  - Number of links found on a page
  - Sitemap search attempts
  - DOM selector matching results
  - Page load details (timing, size)

### Verbose (Shown with --verbose)
- Very detailed technical information
- Examples:
  - Exact DOM selector used
  - Headers sent
  - Raw data before processing
  - Detailed timing for each operation
  - Cache hits/misses

### Important Principles
- Each message should be logged on exactly ONE level
- Each level should be meaningful without the levels below
- Critical debugging information should be on Warning or Error
- Progress/status should be on Information
- Technical details should be on Debug or Verbose 

## Primary Constructors

We use primary constructors (C# 12) selectively based on the complexity of the class:

### When to use primary constructors:
- Simple classes with few fields
- Classes with straightforward initialization
- When the constructor body would be empty or very simple

### When to use traditional constructors:
- Classes with many fields
- Complex initialization logic
- When using dependency injection with fixtures
- When the constructor body contains non-trivial logic

### Suppressing IDE0290 warnings
When choosing to use a traditional constructor over a primary constructor, we:
1. Add a clear comment explaining the decision
2. Use `#pragma warning disable IDE0290` to suppress the warning
3. Restore the warning after the constructor

Example:
```csharp
// Suppressing IDE0290 as the traditional constructor provides better readability
// in this case with multiple fields and fixtures. Primary constructor is more suitable for simpler classes.
#pragma warning disable IDE0290 // Use primary constructor
public TestWebsiteIntegrationTests(TestWebsiteFixture fixture, TestWebsite2Fixture fixture2, TestSpaWebsiteFixture spaFixture, TestConsole console)
{
    _webServer = fixture;
    _webServer2 = fixture2;
    _spaWebServer = spaFixture;
    _console = console;
}
#pragma warning restore IDE0290 // Use primary constructor
``` 