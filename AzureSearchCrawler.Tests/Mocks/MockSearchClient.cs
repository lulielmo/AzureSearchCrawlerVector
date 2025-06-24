using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;

namespace AzureSearchCrawler.Tests.Mocks
{
    /// <summary>
    /// A mock implementation of SearchClient for testing purposes.
    /// </summary>
    public class MockSearchClient : SearchClient
    {
        private readonly List<SearchDocument> _indexedDocuments = new();

        public MockSearchClient(Uri endpoint, string indexName, AzureKeyCredential credential, SearchClientOptions options = null) 
            : base(endpoint, indexName, credential, options)
        {
        }

        public override async Task<Response<IndexDocumentsResult>> IndexDocumentsAsync<T>(
            IndexDocumentsBatch<T> batch,
            IndexDocumentsOptions options = null,
            CancellationToken cancellationToken = default)
        {
            foreach (var action in batch.Actions)
            {
                if (action.ActionType == IndexActionType.Upload)
                {
                    _indexedDocuments.Add(action.Document as SearchDocument);
                }
            }

            return new MockIndexDocumentsResult(new MockHttpResponse());
        }

        public IReadOnlyList<SearchDocument> GetIndexedDocuments() => _indexedDocuments.AsReadOnly();
    }
} 