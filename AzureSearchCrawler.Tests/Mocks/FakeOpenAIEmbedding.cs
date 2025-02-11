using System;
using System.Runtime.Serialization;
using System.Reflection;
using OpenAI.Embeddings;

namespace AzureSearchCrawler.Tests.Mocks
{
    public static class FakeOpenAIEmbedding
    {
        public static OpenAIEmbedding Create(float[] values)
        {
            // Skapa en instans utan att anropa någon konstruktor
            var instance = (OpenAIEmbedding)FormatterServices.GetUninitializedObject(typeof(OpenAIEmbedding));
            // Sätt fältet _vector med våra värden
            var field = typeof(OpenAIEmbedding).GetField("_vector", BindingFlags.NonPublic | BindingFlags.Instance);
            field.SetValue(instance, new ReadOnlyMemory<float>(values));
            return instance;
        }
    }
}
