using Azure;
using Azure.Core;
using System.Diagnostics.CodeAnalysis;

namespace AzureSearchCrawler.Tests.Mocks
{
    internal class MockResponse<T> : Response<T>
    {
        private readonly T? _value;
        private readonly Response _rawResponse;

        public MockResponse(T? value, Response rawResponse)
        {
            _value = value;
            _rawResponse = rawResponse;
        }

        public override T Value => _value ?? throw new InvalidOperationException("Value cannot be null");
        public override Response GetRawResponse() => _rawResponse;
    }

    internal class MockHttpResponse : Response
    {
        private static readonly ResponseHeaders EmptyHeaders = new ResponseHeaders();

        public override int Status => 200;
        public override string ReasonPhrase => "OK";

        [AllowNull]
        public override Stream ContentStream { get => Stream.Null; set { } }
        public override string ClientRequestId { get => "test-request-id"; set { } }

        protected override bool TryGetHeader(string name, [NotNullWhen(true)] out string? value)
        {
            value = null;
            return false;
        }

        protected override bool TryGetHeaderValues(string name, [NotNullWhen(true)] out IEnumerable<string>? values)
        {
            values = null;
            return false;
        }

        protected override bool ContainsHeader(string name) => false;

        protected override IEnumerable<HttpHeader> EnumerateHeaders() => Array.Empty<HttpHeader>();

        public override ResponseHeaders Headers => EmptyHeaders;

        protected virtual void Dispose(bool disposing) { }

        public override void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}