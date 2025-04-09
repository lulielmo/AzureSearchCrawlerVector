using System;
using System.Collections.Generic;
using AzureSearchCrawler.Interfaces;
using AzureSearchCrawler.Models;

namespace AzureSearchCrawler.TestUtilities
{
    using AzureSearchCrawler.Models;
    using System.CommandLine;
    using System.CommandLine.IO;
    using System.IO;
    using System.Text;

    public class TestStandardStreamWriter(List<string> output) : IStandardStreamWriter
    {
        private readonly List<string> _output = output;
        private readonly StringBuilder _buffer = new();

        //public TestStandardStreamWriter(List<string> output)
        //{
        //    _output = output;
        //}

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
        private readonly List<string> _output = [];
        private readonly List<string> _errors = [];
        private bool _verbose;

        public event Action<string, LogLevel>? LoggedMessage;

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
                    Error.Write(message + Environment.NewLine);
                    break;
                case LogLevel.Warning:
                    Out.Write("WARNING: " + message + Environment.NewLine);
                    break;
                case LogLevel.Information:
                    Out.Write(message + Environment.NewLine);
                    break;
                case LogLevel.Debug:
                    if (_verbose)
                        Out.Write("DEBUG: " + message + Environment.NewLine);
                    break;
                case LogLevel.Verbose:
                    if (_verbose)
                        Out.Write("VERBOSE: " + message + Environment.NewLine);
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