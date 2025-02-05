using Azure;
using Azure.Search.Documents.Models;

namespace AzureSearchCrawler.Tests.Mocks
{
    internal class MockIndexDocumentsResult : Response<IndexDocumentsResult>
    {
        private readonly Response _rawResponse;

        public MockIndexDocumentsResult(Response rawResponse)
        {
            _rawResponse = rawResponse;
        }

        public override IndexDocumentsResult Value => null!;
        public override Response GetRawResponse() => _rawResponse;
    }
}