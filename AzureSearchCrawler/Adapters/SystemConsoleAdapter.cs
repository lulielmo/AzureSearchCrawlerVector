using AzureSearchCrawler.Models;
using System.Diagnostics.CodeAnalysis;

namespace AzureSearchCrawler.Adapters;

[ExcludeFromCodeCoverage]
public class SystemConsoleAdapter : Interfaces.IConsole
{
    private readonly System.CommandLine.IConsole _console;
    private bool _verbose;

    public SystemConsoleAdapter(System.CommandLine.IConsole console)
    {
        _console = console;
    }

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