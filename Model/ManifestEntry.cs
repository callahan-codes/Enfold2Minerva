using enfold2minerva.WordPress;

namespace enfold2minerva.Model
{
    public class ManifestEntry
    {
        public int MediaId { get; set; }
        public string NewUrl { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public string AltText { get; set; } = "";
        public List<string> SourceUrls { get; set; } = new();
        public List<string> FoundOnPages { get; set; } = new();
        public int? AttachedToPageId { get; set; }
        public List<int> PlacedOnPageIds { get; set; } = new();
        public DateTimeOffset UploadedAt { get; set; }
        public List<LinkedFile> LinkedFiles { get; set; } = new();
        public string? Origin { get; set; }
    }
}