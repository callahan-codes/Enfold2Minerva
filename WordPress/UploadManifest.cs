using System.Text.Json;
using System.Text.Json.Serialization;
using enfold2minerva.Model;

namespace enfold2minerva.WordPress
{
    // A JSON file that tracks what has already been uploaded; re-runs and repeated
    // images reuse the existing media item instead of duplicating
    public class UploadManifest
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public string Site { get; set; } = "";

        public List<ManifestEntry> Entries { get; set; } = new();

        [JsonIgnore] public string FilePath { get; private set; } = "";

        public static UploadManifest Load(string path, Uri site)
        {
            string siteKey = site.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
            UploadManifest manifest;

            if (File.Exists(path))
            {
                manifest = JsonSerializer.Deserialize<UploadManifest>(File.ReadAllText(path), JsonOptions) ?? new();
                if (!string.IsNullOrEmpty(manifest.Site) && !string.Equals(manifest.Site, siteKey, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException
                    (
                        $"{path} belongs to {manifest.Site}, but you are uploading to {siteKey}. Use --manifest to pick a different file."
                    );   
                }
            }
            else
            {
                manifest = new UploadManifest();
            }

            manifest.Site = siteKey;
            manifest.FilePath = Path.GetFullPath(path);
            return manifest;
        }

        public ManifestEntry? FindBySourceUrl(string url) =>
            Entries.FirstOrDefault(e => e.SourceUrls.Any(s => SameUrl(s, url)));

        public ManifestEntry? FindByHash(string sha256) =>
            Entries.FirstOrDefault(e => e.Sha256.Equals(sha256, StringComparison.OrdinalIgnoreCase));

        // Writes to a temp file then swaps it in, so a crash can't leave a half-written manifest
        public void Save()
        {
            string? dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(temp, FilePath, overwrite: true);
        }

        public static void AddUnique<T>(List<T> list, T value)
        {
            if (!list.Contains(value)) list.Add(value);
        }

        private static bool SameUrl(string a, string b) =>
            string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

        // Ignore http vs https and any query string
        private static string Normalize(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host + u.AbsolutePath : url;
    }
}
