namespace AzureSearchCrawler.Adapters;

public class SystemConsoleAdapter : Interfaces.IConsole
{
    private readonly System.CommandLine.IConsole _console;

    public SystemConsoleAdapter(System.CommandLine.IConsole console)
    {
        _console = console;
    }

    public void WriteLine(string message) => _console.Out.Write(message + Environment.NewLine);
    public void WriteLine(string format, params object[] args) => _console.Out.Write(string.Format(format, args) + Environment.NewLine);
    public void WriteError(string message) => _console.Error.Write(message);
}