namespace AzureSearchCrawler.Interfaces
{
    /// <summary>
    /// Exposes the text writers used for command-line output and errors.
    /// </summary>
    public interface IConsoleStreams
    {
        /// <summary>
        /// Gets the writer for standard output.
        /// </summary>
        TextWriter Output { get; }

        /// <summary>
        /// Gets the writer for standard error.
        /// </summary>
        TextWriter Error { get; }
    }
}
