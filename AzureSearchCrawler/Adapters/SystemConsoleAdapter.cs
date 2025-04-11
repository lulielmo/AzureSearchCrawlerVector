using AzureSearchCrawler.Models;
using System.Diagnostics.CodeAnalysis;

namespace AzureSearchCrawler.Adapters;

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
public class SystemConsoleAdapter(System.CommandLine.IConsole console) : Interfaces.IConsole
{
    private readonly System.CommandLine.IConsole _console = console;
    private bool _verbose;

    public void WriteLine(string message, LogLevel level = LogLevel.Information)
    {
        switch (level)
        {
            case LogLevel.Error:
                _console.Error.Write(message + Environment.NewLine);
                break;
            case LogLevel.Verbose:
                if (_verbose)
                    _console.Out.Write($"VERBOSE: {message}{Environment.NewLine}");
                break;
            case LogLevel.Debug:
                if (_verbose)
                    _console.Out.Write($"DEBUG: {message}{Environment.NewLine}");
                break;
            default:
                _console.Out.Write(message + Environment.NewLine);
                break;
        }
    }

    public void WriteLine(string format, params object[] args) 
        => WriteLine(string.Format(format, args));

    public void WriteError(string message) 
        => WriteLine(message, LogLevel.Error);

    public void WriteInfoLine(string format, params object[] args)
        => WriteLine(format, LogLevel.Information, args);

    public void WriteDebugLine(string format, params object[] args)
        => WriteLine(format, LogLevel.Debug, args);

    public void WriteVerboseLine(string format, params object[] args)
        => WriteLine(format, LogLevel.Verbose, args);

    public void WriteWarningLine(string message, params object[] args)
    {
        WriteLine(string.Format(message, args), LogLevel.Warning);
    }

    public void SetVerbose(bool verbose) => _verbose = verbose;
}