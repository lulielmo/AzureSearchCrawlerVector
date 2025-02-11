using System;
using System.Runtime.Serialization;
using System.Reflection;
using OpenAI.Embeddings;
using System.Runtime.CompilerServices;

namespace AzureSearchCrawler.Tests.Mocks
{
    public static class FakeOpenAIEmbedding
    {
        public static OpenAIEmbedding Create(float[] values)
        {
            // Skapa en instans utan att anropa någon konstruktor
            var instance = RuntimeHelpers.GetUninitializedObject(typeof(OpenAIEmbedding)) as OpenAIEmbedding;
            // Sätt fältet _vector med våra värden
            var field = typeof(OpenAIEmbedding).GetField("_vector", BindingFlags.NonPublic | BindingFlags.Instance);
            field!.SetValue(instance, new ReadOnlyMemory<float>(values));
            return instance!;
        }
    }
}
