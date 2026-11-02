namespace enfold2minerva.Migration
{
    // plain client for the OLD site, never carries the new site creds
    public static class OldSite
    {
        private static readonly Lazy<HttpClient> _client = new(CreateOldSiteClient);

        private static HttpClient CreateOldSiteClient()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("enfold2minerva/1.0");
            return http;
        }

        public static Task<HttpResponseMessage> GetAsync(string requestUri, CancellationToken cancellationToken = default)
        {
            return _client.Value.GetAsync(requestUri, cancellationToken);
        }

        public static Task<HttpResponseMessage> GetAsync(Uri requestUri, CancellationToken cancellationToken = default)
        {
            return _client.Value.GetAsync(requestUri, cancellationToken);
        }
    }
}