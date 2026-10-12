using AzureSearchCrawler.Interfaces;
using AzureSearchCrawler.Models;
using System.Diagnostics.CodeAnalysis;

namespace AzureSearchCrawler.Adapters;

/// <summary>
/// Writes crawler log messages to the process console.
/// This class is used only in production code as a fallback when running the actual CLI application.
/// It's excluded from code coverage because:
/// 1. It's a pure adapter with no business logic
/// 2. In tests, we use TestConsole instead to capture and verify console output
/// 3. Testing console output in production would require integration tests with the actual console,
///    which would be unreliable and not provide additional value
/// </summary>
[ExcludeFromCodeCoverage]
public class SystemConsoleAdapter : IConsole, IConsoleStreams
{
    private bool _verbose;

    /// <inheritdoc />
    public TextWriter Output => Console.Out;

    /// <inheritdoc />
    public TextWriter Error => Console.Error;

    /// <inheritdoc />
    public void WriteLine(string message, LogLevel level = LogLevel.Information)
    {
        switch (level)
        {
            case LogLevel.Error:
                Console.Error.WriteLine(message);
                break;
            case LogLevel.Verbose:
                if (_verbose)
                    Console.Out.WriteLine($"VERBOSE: {message}");
                break;
            case LogLevel.Debug:
                if (_verbose)
                    Console.Out.WriteLine($"DEBUG: {message}");
                break;
            default:
                Console.Out.WriteLine(message);
                break;
        }
    }

    /// <inheritdoc />
    public void WriteLine(string format, params object[] args)
        => WriteLine(string.Format(format, args));

    /// <inheritdoc />
    public void WriteError(string message)
        => WriteLine(message, LogLevel.Error);

    /// <inheritdoc />
    public void WriteInfoLine(string format, params object[] args)
        => WriteLine(format, LogLevel.Information, args);

    /// <inheritdoc />
    public void WriteDebugLine(string format, params object[] args)
        => WriteLine(format, LogLevel.Debug, args);

    /// <inheritdoc />
    public void WriteVerboseLine(string format, params object[] args)
        => WriteLine(format, LogLevel.Verbose, args);

    /// <inheritdoc />
    public void WriteWarningLine(string message, params object[] args)
    {
        WriteLine(string.Format(message, args), LogLevel.Warning);
    }

    /// <inheritdoc />
    public void SetVerbose(bool verbose) => _verbose = verbose;
}
