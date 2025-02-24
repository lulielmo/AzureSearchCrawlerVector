using AzureSearchCrawler.Models;
using System.CommandLine.IO;
using System.Text;

public class TestStandardStreamWriter : IStandardStreamWriter
{
    private readonly List<string> _output;
    private readonly StringBuilder _buffer = new();

    public TestStandardStreamWriter(List<string> output)
    {
        _output = output;
    }

    public void Write(string? value)
    {
        if (value != null)
        {
            _buffer.Append(value);
            if (value.EndsWith(Environment.NewLine))
            {
                _output.Add(_buffer.ToString().TrimEnd());
                _buffer.Clear();
            }
        }
    }

    public override string ToString() => string.Join(Environment.NewLine, _output);
}

public class TestConsole : System.CommandLine.IConsole, AzureSearchCrawler.Interfaces.IConsole, IDisposable
{
    private readonly List<string> _output = new();
    private readonly List<string> _errors = new();
    private bool _verbose;

    public IReadOnlyList<string> Output => _output;
    public IReadOnlyList<string> Errors => _errors;

    public IStandardStreamWriter Out { get; }
    public bool IsOutputRedirected => false;
    public IStandardStreamWriter Error { get; }
    public bool IsErrorRedirected => false;
    public bool IsInputRedirected => false;

    public TestConsole()
    {
        Out = new TestStandardStreamWriter(_output);
        Error = new TestStandardStreamWriter(_errors);
    }

    public void WriteLine(string message, LogLevel level = LogLevel.Info)
    {
        switch (level)
        {
            case LogLevel.Error:
                Error.Write(message + Environment.NewLine);
                break;
            case LogLevel.Verbose:
                if (_verbose)
                    Out.Write($"VERBOSE: {message}{Environment.NewLine}");
                break;
            case LogLevel.Debug:
                if (_verbose)
                    Out.Write($"DEBUG: {message}{Environment.NewLine}");
                break;
            default:
                Out.Write(message + Environment.NewLine);
                break;
        }
    }

    public void WriteLine(string message) => Out.Write(message + Environment.NewLine);
    public void WriteError(string message) => Error.Write(message + Environment.NewLine);
    public void Clear()
    {
        _output.Clear();
        _errors.Clear();
    }

    public void WriteLine(string format, params object[] args) =>
        Out.Write(string.Format(format, args) + Environment.NewLine);

    public void WriteInfoLine(string format, params object[] args)
        => WriteLine(string.Format(format, args), LogLevel.Info);

    public void WriteDebugLine(string format, params object[] args)
        => WriteLine(string.Format(format, args), LogLevel.Debug);

    public void WriteVerboseLine(string format, params object[] args)
        => WriteLine(string.Format(format, args), LogLevel.Verbose);

    public void WriteWarningLine(string message, params object[] args)
        => WriteLine(string.Format(message, args), LogLevel.Warning);

    public void SetVerbose(bool verbose) => _verbose = verbose;

    public void Dispose()
    {
        // Inget att städa upp
    }

    
}