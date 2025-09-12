using A2A.Integration.Tests.Tck.Utils;

namespace A2A.Integration.Tests.tck
{
    public class TckClientTest
    {
        protected HttpClient HttpClient { get; }

        public TckClientTest()
        {
            this.HttpClient = TransportHelpers.CreateTestApplication().CreateClient();

            var targetUri = new UriBuilder(HttpClient.BaseAddress!);
            targetUri.Path = "/speccompliance";
            HttpClient.BaseAddress = targetUri.Uri;
        }
    }
}
