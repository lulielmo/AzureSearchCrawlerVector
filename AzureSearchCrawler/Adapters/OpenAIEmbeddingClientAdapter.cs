using Azure.AI.OpenAI;
using AzureSearchCrawler.Interfaces;
using OpenAI.Embeddings;

namespace AzureSearchCrawler.Adapters
{
    /// <summary>
    /// Adapter that wraps the OpenAI EmbeddingClient to implement our IEmbeddingClient interface.
    /// </summary>
    public class OpenAIEmbeddingClientAdapter : IEmbeddingClient
    {
        private readonly AzureOpenAIClient _client;
        private readonly string _deploymentName;

        public OpenAIEmbeddingClientAdapter(AzureOpenAIClient client, string deploymentName)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _deploymentName = deploymentName;
        }

        public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
            List<string> texts,
            int dimensions,
            CancellationToken cancellationToken = default)
        {
            var embeddings = new List<float[]>();
            foreach (var text in texts)
            {
                var embedding = await GenerateEmbeddingAsync(text, dimensions, cancellationToken);
                embeddings.Add(embedding);
            }
            return embeddings;
        }

        public async Task<float[]> GenerateEmbeddingAsync(
            string text,
            int dimensions,
            CancellationToken cancellationToken = default)
        {
            var options = new EmbeddingGenerationOptions { Dimensions = dimensions };
            var result = await _client.GetEmbeddingClient(_deploymentName).GenerateEmbeddingAsync(text, options, cancellationToken);
            return result.Value.ToFloats().ToArray();
        }
    }
} 