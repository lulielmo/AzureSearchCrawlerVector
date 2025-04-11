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

## Language Policy

We maintain a clear language policy to ensure consistency and maintainability:

### Documentation (English)
All documentation must be in English, including:
- XML documentation comments
- README files
- Development guidelines
- API documentation
- Code comments

### Communication (Swedish allowed)
Team communication can be in Swedish, including:
- Pull request discussions
- Code review comments
- Team meetings
- Internal documentation

### Code (English)
All code-related content must be in English, including:
- Variable names
- Method names
- Class names
- Namespace names
- Error messages
- Log messages

This policy ensures that:
1. Code remains accessible to international developers
2. Documentation is consistent and searchable
3. Team communication can be in the most natural language
4. Error messages and logs are universally understandable

## Test Categories

We maintain clear separation between different types of tests:

### Integration Tests
Tests that require external dependencies or services:
- Marked with `[Trait("Category", "Integration")]`
- Examples:
  - TestWebsiteIntegrationTests
  - TestSpaWebsiteFixture
  - TestWebServer
- Used for testing:
  - External service interactions
  - Full system workflows
  - End-to-end scenarios

### Unit Tests
Tests that run in isolation with mocked dependencies:
- Marked with `[Trait("Category", "Unit")]`
- Examples:
  - HeadlessBrowserCrawlerTests
  - SitemapCrawlerTests
  - TextExtractorTests
- Used for testing:
  - Individual components
  - Business logic
  - Edge cases

## Code Coverage

We maintain high code coverage while being pragmatic about exclusions:

### When to Exclude from Coverage
Classes can be excluded from code coverage when they are:
- Pure adapter classes with no business logic
- Used only in production as fallback mechanisms
- Would require unreliable integration tests
- Don't provide additional value when tested

### Documentation Requirements
When excluding a class from coverage, we must:
1. Add the `[ExcludeFromCodeCoverage]` attribute
2. Provide a detailed XML documentation comment explaining:
   - The class's purpose
   - Why it's excluded
   - Specific reasons for the exclusion

Example:
```csharp
/// <summary>
/// Adapter class that bridges System.CommandLine.IConsole to our own IConsole interface.
/// This class is used only in production code as a fallback when running the actual CLI application.
/// It's excluded from code coverage because:
/// 1. It's a pure adapter with no business logic
/// 2. In tests, we use TestConsole instead to capture and verify console output
/// 3. Testing console output in production would require integration tests with the actual console,
///    which would be unreliable and not provide additional value
/// </summary>
[ExcludeFromCodeCoverage]
public class SystemConsoleAdapter
```

## Test Structure

We maintain organized and maintainable test files:

### File Size Limits
- Maximum file size: 500 lines
- Action: Split into smaller, focused test files
- Example: AzureSearchIndexerTests.cs (1429 lines) should be split into multiple files

### Organization Rules
1. One test class per production class
2. Group related tests in separate files
3. Use descriptive test method names
4. Follow Arrange-Act-Assert pattern
5. Keep test setup and teardown clear and minimal
6. Use fixtures for shared test resources
7. Document test dependencies and assumptions 