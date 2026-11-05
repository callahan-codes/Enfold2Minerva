using System.Text.Json;
using System.Text.Json.Serialization;

namespace enfold2minerva.WordPress
{
    // A media item already in the new site's library, as returned by GET /wp/v2/media
    public record ExistingMedia
    (
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("source_url")] string SourceUrl,
        [property: JsonPropertyName("alt_text")] string? AltText,
        [property: JsonPropertyName("date_gmt")] DateTime DateGmt,
        // usually an object, but WP returns [] when an item has no metadata, so keep it loose
        [property: JsonPropertyName("media_details")] JsonElement MediaDetails
    )
    {
        // WP serves a "-scaled" (or rotated) copy for big images; the bytes you actually
        // uploaded live next to it under media_details.original_image
        public Uri OriginalFileUrl
        {
            get
            {
                var source = new Uri(SourceUrl);

                if (MediaDetails.ValueKind == JsonValueKind.Object
                    && MediaDetails.TryGetProperty("original_image", out JsonElement original)
                    && original.ValueKind == JsonValueKind.String
                    && !string.IsNullOrEmpty(original.GetString()))
                {
                    return new Uri(source, Uri.EscapeDataString(original.GetString()!));
                }

                return source;
            }
        }
    }
}