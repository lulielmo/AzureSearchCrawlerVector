namespace AzureSearchCrawler.Interfaces
{
    public interface IConsole
    {
        void WriteLine(string message);
        void WriteLine(string format, params object[] args);
        void WriteError(string message);
    }
}