using System.Text.Json.Serialization;

namespace enfold2minerva.WordPress
{
    public record UploadedMedia
    (
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("source_url")] string SourceUrl
    );
}