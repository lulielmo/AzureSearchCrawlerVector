namespace AzureSearchCrawler.Models
{
    public enum LogLevel
    {
        Verbose,    // Detailed technical information, method entry/exit, timing data
        Debug,      // Information useful for debugging and development
        Information, // General operational events, startup/shutdown, configuration
        Warning,    // Abnormal or unexpected events that don't stop execution
        Error       // Critical issues that require immediate attention
    } 
}