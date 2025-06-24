using System;
using OpenAI.Embeddings;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Reflection;
using System.ClientModel;
using Moq;
using System.ClientModel.Primitives;

namespace AzureSearchCrawler.Tests.Mocks
{
    /// <summary>
    /// En fake implementation av OpenAIEmbeddingCollection som vi kan använda i testerna.
    /// </summary>
    public class FakeOpenAIEmbeddingCollection : ReadOnlyCollection<OpenAIEmbedding>, IReadOnlyList<OpenAIEmbedding>
    {
        private readonly OpenAIEmbeddingCollection _realCollection;

        public FakeOpenAIEmbeddingCollection(IList<OpenAIEmbedding> embeddings) : base(embeddings)
        {
            _realCollection = RuntimeHelpers.GetUninitializedObject(typeof(OpenAIEmbeddingCollection)) as OpenAIEmbeddingCollection;
            var field = typeof(OpenAIEmbeddingCollection).GetField("_embeddings", BindingFlags.NonPublic | BindingFlags.Instance);
            field!.SetValue(_realCollection, embeddings);
        }

        public static FakeOpenAIEmbeddingCollection Create(params float[][] embeddingValues)
        {
            var embeddings = embeddingValues.Select(values => FakeOpenAIEmbedding.Create(values)).ToList();
            return new FakeOpenAIEmbeddingCollection(embeddings);
        }

        public static implicit operator OpenAIEmbeddingCollection(FakeOpenAIEmbeddingCollection fake)
        {
            return fake._realCollection;
        }

        public static implicit operator ClientResult<OpenAIEmbeddingCollection>(FakeOpenAIEmbeddingCollection fake)
        {
            return ClientResult.FromValue(fake._realCollection, Mock.Of<PipelineResponse>());
        }

        public static implicit operator Task<ClientResult<OpenAIEmbeddingCollection>>(FakeOpenAIEmbeddingCollection fake)
        {
            return Task.FromResult((ClientResult<OpenAIEmbeddingCollection>)fake);
        }

        public static implicit operator Task<ClientResult<OpenAIEmbedding>>(FakeOpenAIEmbeddingCollection fake)
        {
            return Task.FromResult(ClientResult.FromValue(fake._realCollection[0], Mock.Of<PipelineResponse>()));
        }
    }
}
