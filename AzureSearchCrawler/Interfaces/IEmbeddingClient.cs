using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AzureSearchCrawler.Interfaces
{
    /// <summary>
    /// Interface for generating embeddings from text.
    /// </summary>
    public interface IEmbeddingClient
    {
        /// <summary>
        /// Generates embeddings for a list of texts.
        /// </summary>
        /// <param name="texts">The texts to generate embeddings for.</param>
        /// <param name="dimensions">The number of dimensions in the embedding vectors.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>A list of embedding vectors, where each vector is represented as an array of floats.</returns>
        Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
            List<string> texts,
            int dimensions,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Generates an embedding for a single text.
        /// </summary>
        /// <param name="text">The text to generate an embedding for.</param>
        /// <param name="dimensions">The number of dimensions in the embedding vector.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>An embedding vector represented as an array of floats.</returns>
        Task<float[]> GenerateEmbeddingAsync(
            string text,
            int dimensions,
            CancellationToken cancellationToken = default);
    }
} 