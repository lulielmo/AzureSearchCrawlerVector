using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AzureSearchCrawler.Interfaces;

namespace AzureSearchCrawler.Tests.Mocks
{
    /// <summary>
    /// A mock implementation of IEmbeddingClient for testing purposes.
    /// </summary>
    public class MockEmbeddingClient : IEmbeddingClient
    {
        private readonly float[][] _embeddingValues;
        private readonly bool _shouldThrow;

        public MockEmbeddingClient(bool shouldThrow = false, params float[][] embeddingValues)
        {
            _embeddingValues = embeddingValues;
            _shouldThrow = shouldThrow;
        }

        public Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
            List<string> texts,
            int dimensions,
            CancellationToken cancellationToken = default)
        {
            if (_shouldThrow)
            {
                throw new Exception("Embedding error");
            }

            // Generera ett embedding för varje text
            var embeddings = new List<float[]>();
            for (int i = 0; i < texts.Count; i++)
            {
                // Använd ett av de fördefinierade värdena om det finns, annars skapa ett nytt
                var embedding = i < _embeddingValues.Length 
                    ? _embeddingValues[i] 
                    : new float[dimensions].Select((_, j) => (float)j / dimensions).ToArray();
                embeddings.Add(embedding);
            }

            return Task.FromResult<IReadOnlyList<float[]>>(embeddings);
        }

        public Task<float[]> GenerateEmbeddingAsync(
            string text,
            int dimensions,
            CancellationToken cancellationToken = default)
        {
            if (_shouldThrow)
            {
                throw new Exception("Embedding error");
            }

            // Använd första fördefinierade värdet om det finns, annars skapa ett nytt
            var embedding = _embeddingValues.Length > 0 
                ? _embeddingValues[0] 
                : new float[dimensions].Select((_, i) => (float)i / dimensions).ToArray();

            return Task.FromResult(embedding);
        }
    }
} 