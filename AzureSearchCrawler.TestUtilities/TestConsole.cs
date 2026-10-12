namespace AzureSearchCrawler.TestUtilities
{
    using AzureSearchCrawler.Interfaces;
    using AzureSearchCrawler.Models;
    using System.Text;

    public class TestStandardStreamWriter(List<string> output) : TextWriter
    {
        private readonly List<string> _output = output;
        private readonly StringBuilder _buffer = new();
        private const int MaxBufferSize = 10 * 1024 * 1024; // 10MB

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(string? value)
        {
            if (value != null)
            {
                _buffer.Append(value);
                if (value.EndsWith(Environment.NewLine) || _buffer.Length > MaxBufferSize)
                {
                    _output.Add(_buffer.ToString().TrimEnd());
                    _buffer.Clear();
                }
            }
        }

        public override string ToString() => string.Join(Environment.NewLine, _output);
    }

    public class TestConsole : IConsole, IConsoleStreams, IDisposable
    {
        private readonly List<string> _output = [];
        private readonly List<string> _errors = [];
        private bool _verbose;

        public event Action<string, LogLevel>? LoggedMessage;

        private readonly TextWriter _outputWriter;
        private readonly TextWriter _errorWriter;

        public IReadOnlyList<string> Output => _output;
        public IReadOnlyList<string> Errors => _errors;

        TextWriter IConsoleStreams.Output => _outputWriter;
        TextWriter IConsoleStreams.Error => _errorWriter;

        public TestConsole()
        {
            _outputWriter = new TestStandardStreamWriter(_output);
            _errorWriter = new TestStandardStreamWriter(_errors);
        }

        private bool _disposed = false;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Free any other managed objects here.
                }

                // Free any unmanaged objects here.
                _disposed = true;
            }
        }

        ~TestConsole()
        {
            Dispose(false);
        }

        public void WriteLine(string message, LogLevel level = LogLevel.Information)
        {
            if (level == LogLevel.Verbose && !_verbose) return;
            
            LoggedMessage?.Invoke(message, level);
            
            switch (level)
            {
                case LogLevel.Error:
                    _errorWriter.Write(message + Environment.NewLine);
                    break;
                case LogLevel.Warning:
                    _outputWriter.Write("WARNING: " + message + Environment.NewLine);
                    break;
                case LogLevel.Information:
                    _outputWriter.Write(message + Environment.NewLine);
                    break;
                case LogLevel.Debug:
                    if (_verbose)
                        _outputWriter.Write("DEBUG: " + message + Environment.NewLine);
                    break;
                case LogLevel.Verbose:
                    if (_verbose)
                        _outputWriter.Write("VERBOSE: " + message + Environment.NewLine);
                    break;
            }
        }

        public void WriteLine(string message) => _outputWriter.Write(message + Environment.NewLine);
        public void WriteError(string message) => _errorWriter.Write(message + Environment.NewLine);
        public void Clear()
        {
            _output.Clear();
            _errors.Clear();
        }

        public void WriteLine(string format, params object[] args) =>
            _outputWriter.Write(string.Format(format, args) + Environment.NewLine);

        public void WriteInfoLine(string format, params object[] args)
            => WriteLine(string.Format(format, args), LogLevel.Information);

        public void WriteDebugLine(string format, params object[] args)
            => WriteLine(string.Format(format, args), LogLevel.Debug);

        public void WriteVerboseLine(string format, params object[] args)
            => WriteLine(string.Format(format, args), LogLevel.Verbose);

        public void WriteWarningLine(string message, params object[] args)
        {
            WriteLine(string.Format(message, args), LogLevel.Warning);
        }

        public void SetVerbose(bool verbose) => _verbose = verbose;
    }
}