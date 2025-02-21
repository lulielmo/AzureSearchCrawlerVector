using AzureSearchCrawler.Models;

namespace AzureSearchCrawler.Interfaces
{
    public interface IConsole
    {
        void WriteLine(string message, LogLevel level = LogLevel.Info);

        void WriteInfoLine(string format, params object[] args);

        void WriteDebugLine(string format, params object[] args);

        void WriteVerboseLine(string format, params object[] args);

        void WriteError(string message);
    }
}