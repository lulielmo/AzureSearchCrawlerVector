namespace AzureSearchCrawler.IntegrationTests
{
    public class TestWebsiteFixture : TestWebServer
    {
        public TestWebsiteFixture() : base("TestWebsite", 5002)
        {
        }
    }
} 