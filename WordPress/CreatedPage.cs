using System.Text.Json.Serialization;

namespace enfold2minerva.WordPress
{
    public record CreatedPage
    (
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("link")] string Link
    );
}