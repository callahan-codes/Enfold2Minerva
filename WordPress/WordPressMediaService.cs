using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace enfold2minerva.WordPress
{ 
    // Talks to the NEW site through the WordPress REST API,
    // authenticated with an Application Password.
    //
    // Only use this client for the new site. Downloads from the old site should use a
    // separate, plain HttpClient so your credentials are never sent to the old site.
    public class WordPressMediaService
    {
        private readonly HttpClient _http;

        public Uri SiteUrl { get; }

        public WordPressMediaService(Uri siteUrl, string username, string appPassword)
        {
            if (siteUrl.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException
                (
                    $"{siteUrl} must use https so the application password isn't sent in plain text."
                );            
            }
            SiteUrl = new Uri(siteUrl.ToString().TrimEnd('/') + "/");

            _http = new HttpClient
            {
                // e.g. https://new-site.example -> https://new-site.example/wp-json/wp/v2/
                BaseAddress = new Uri(SiteUrl, "wp-json/wp/v2/"),
                Timeout = TimeSpan.FromMinutes(2) // so we dont get hung up
            };

            // basic auth
            string token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{appPassword}"));
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("enfold2minerva/1.0");
        }

        // Uploads raw image bytes and returns the new media ID and URL
        public async Task<UploadedMedia> UploadAsync(byte[] bytes, string fileName, CancellationToken ct = default)
        {
            string headerName = new(fileName.Where(c => c != '"' && c != '\\' && !char.IsControl(c)).ToArray());

            using var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue(GetMimeType(fileName));
            content.Headers.TryAddWithoutValidation("Content-Disposition", $"attachment; filename=\"{headerName}\"");

            using HttpResponseMessage response = await _http.PostAsync("media", content, ct);
            await ThrowIfFailedAsync(response, $"upload {fileName}", ct);

            return (await response.Content.ReadFromJsonAsync<UploadedMedia>(ct))!;
        }

        // Sets alt text, caption and/or the page the media is attached to; anything left null isn't changed
        public async Task UpdateDetailsAsync
        (
            int mediaId,
            string? altText,
            string? caption = null,
            int? attachToPostId = null,
            CancellationToken ct = default)
        {
            var body = new Dictionary<string, object>();
            if (altText is not null) 
            {
                body["alt_text"] = altText;
            }

            if (caption is not null) 
            {
                body["caption"] = caption;
            }

            if (attachToPostId is not null) 
            {
                body["post"] = attachToPostId.Value;
            }

            if (body.Count == 0) 
            {
                return;
            }

            using HttpResponseMessage response = await PostJsonAsync($"media/{mediaId}", body, ct);
            await ThrowIfFailedAsync(response, $"update media {mediaId}", ct);
        }

        // Creates a draft page
        public async Task<CreatedPage> CreateDraftPageAsync(string title, string content = "", CancellationToken ct = default)
        {
            var body = new Dictionary<string, object>
            {
                ["title"] = title,
                ["content"] = content,
                ["status"] = "draft"
            };

            using HttpResponseMessage response = await PostJsonAsync("pages", body, ct);
            await ThrowIfFailedAsync(response, $"create page \"{title}\"", ct);

            return (await response.Content.ReadFromJsonAsync<CreatedPage>(ct))!;
        }

        // Replaces a pages content with raw block markup
        public async Task UpdatePageContentAsync(int pageId, string content, CancellationToken ct = default)
        {
            var body = new Dictionary<string, object> { ["content"] = content };

            using HttpResponseMessage response = await PostJsonAsync($"pages/{pageId}", body, ct);
            await ThrowIfFailedAsync(response, $"update page {pageId}", ct);
        }

        // Link straight into the block editor for a post or page
        public Uri EditorUrl(int postId) => new(SiteUrl, $"wp-admin/post.php?post={postId}&action=edit");

        // Every image in the media library, oldest first, 100 per request
        public async IAsyncEnumerable<ExistingMedia> ListImagesAsync([EnumeratorCancellation] CancellationToken ct = default)
        {
            for (int page = 1; ; page++)
            {
                using HttpResponseMessage response =
                    await _http.GetAsync($"media?media_type=image&per_page=100&page={page}&orderby=id&order=asc", ct);
                await ThrowIfFailedAsync(response, $"list media (page {page})", ct);

                List<ExistingMedia> items = await response.Content.ReadFromJsonAsync<List<ExistingMedia>>(ct) ?? new();
                foreach (ExistingMedia item in items) yield return item;

                int totalPages = response.Headers.TryGetValues("X-WP-TotalPages", out var values)
                                && int.TryParse(values.FirstOrDefault(), out int t) ? t : page;
                if (items.Count == 0 || page >= totalPages) yield break;
            }
        }

        // Downloads a file from the new site (only the new site, since this client carries the app password)
        public async Task<byte[]> DownloadAsync(Uri url, CancellationToken ct = default)
        {
            if (!url.Host.Equals(SiteUrl.Host, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Refusing to send new-site credentials to {url.Host}.");
            }

            using HttpResponseMessage response = await _http.GetAsync(url, ct);
            await ThrowIfFailedAsync(response, $"download {url}", ct);
            return await response.Content.ReadAsByteArrayAsync(ct);
        }

        // PostAsJsonAsync streams the body without a Content-Length, which some
        // PHP/nginx setups refuse; serializing up front sends a normal sized request.
        private Task<HttpResponseMessage> PostJsonAsync(string path, object body, CancellationToken ct)
        {
            var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            return _http.PostAsync(path, content, ct);
        }

        private static async Task ThrowIfFailedAsync(HttpResponseMessage response, string action, CancellationToken ct)
        {
            if (response.IsSuccessStatusCode) return;

            // WordPress explains the problem in the body, e.g. {"code":"rest_cannot_create",...}
            string body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Failed to {action}: {(int)response.StatusCode} {response.ReasonPhrase} - {body}");
        }

        private static string GetMimeType(string fileName) =>
            Path.GetExtension(fileName).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".svg" => "image/svg+xml",
                _ => "application/octet-stream"
            };
    }
}