using Azure;
using Azure.Search.Documents.Models;

namespace AzureSearchCrawler.Tests.Mocks
{
    internal class MockIndexingResult : Response<IndexingResult>
    {
        private readonly Response _rawResponse;

        public MockIndexingResult()
        {
            _rawResponse = new MockHttpResponse();
        }

        public override IndexingResult Value => null!;
        public override Response GetRawResponse() => _rawResponse;
    }
}
