namespace AzureSearchCrawler.IntegrationTests
{
    public class TestWebsiteFixture : TestWebServer
    {
        public TestWebsiteFixture() : base("TestWebsite", 5141)
        {
        }
    }

    public class TestWebsite2Fixture : TestWebServer
    {
        public TestWebsite2Fixture() : base("TestWebsite2", 5142)
        {
        }
    }
} 