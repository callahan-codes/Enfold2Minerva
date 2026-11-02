using enfold2minerva.Migration;
using HtmlAgilityPack;

namespace enfold2minerva.ClientDom
{
    public class ClientDomBase
    {
        private readonly Uri _url;

        public ClientDomBase(Uri url)
        {
            _url = url;
        }

        // Returns a page DOM
        public async Task<HtmlDocument> DownloadSourceAsync(CancellationToken ct = default)
        {
            // load page
            using var response = await OldSite.GetAsync(_url, ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"{_url} returned {(int)response.StatusCode} {response.ReasonPhrase}");                
            }

            // return DOM
            string html = await response.Content.ReadAsStringAsync(ct);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            return doc;
        }
    }
}