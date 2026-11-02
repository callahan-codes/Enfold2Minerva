namespace enfold2minerva.Model
{
    public class CaptionedMediaItem
    {
        public int Id { get; set; }
        public string Src { get; set; } = "";
        public string OriginalSrc { get; set; } = "";
        public string Alt { get; set; } = "";
        public bool AltIsFallback { get; set; }
        public List<string> Captions { get; set; } = new();
        public string? LinkedFileUrl { get; set; }
        public string? LinkedFileLocalName =>
            LinkedFileUrl == null ? null :
            Uri.UnescapeDataString(Path.GetFileNameWithoutExtension(new Uri(Src).AbsolutePath))
            + Path.GetExtension(new Uri(LinkedFileUrl).AbsolutePath).ToLowerInvariant();
    }
}
