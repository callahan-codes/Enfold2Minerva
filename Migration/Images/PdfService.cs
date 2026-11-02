using System.Security.Cryptography;
using enfold2minerva.ClientDom;
using enfold2minerva.Logs;
using enfold2minerva.Model;
using enfold2minerva.WordPress;

namespace enfold2minerva.Migration.Images
{
    public class PdfService
    {
        private readonly MigrationOptions _o;
        private readonly UploadManifest? _manifest; 

        public PdfService(MigrationOptions o, UploadManifest? manifest)
        {
            _o = o;
            _manifest = manifest;
        }

        // sha256 -> path, for documents already in this page's folder
        private Dictionary<string, string>? _folderHashes;

        // Saves the document a cover image links to into this page's folder, named after the image
        public async Task<string> SaveLinkedFileAsync(CaptionedMediaItem item, ManifestEntry? entry, CancellationToken ct)
        {
            string url = item.LinkedFileUrl!;
            string folder = Path.GetFullPath(_o.PageFolder);

            // skip the download if already saved 
            var known = entry?.LinkedFiles.FirstOrDefault(f =>
                f.SourceUrl == url && File.Exists(f.LocalPath) &&
                string.Equals(Path.GetDirectoryName(f.LocalPath), folder, StringComparison.OrdinalIgnoreCase));
            if (known != null)
            {
                Log.Info($"    linked PDF already saved as {Path.GetFileName(known.LocalPath)}");
                return known.LocalPath;
            }

            using var response = await OldSite.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"{url} returned {(int)response.StatusCode} {response.ReasonPhrase}");
            }
            byte[] bytes = await response.Content.ReadAsByteArrayAsync(ct);

            if (url.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) && !bytes.AsSpan().StartsWith("%PDF"u8))
            {
                Log.Warn($"    {Path.GetFileName(url)} doesn't look like a PDF - check it after the run");   
            }

            string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            Directory.CreateDirectory(folder);
            _folderHashes ??= HashExistingFiles(folder);

            // same document already in this folder, reuse it
            if (!_folderHashes.TryGetValue(hash, out string? path))
            {
                path = UniquePath(folder, item.LinkedFileLocalName!);
                await File.WriteAllBytesAsync(path, bytes, ct);
                _folderHashes[hash] = path;
                Log.Success($"    saved linked PDF {Path.GetFileName(url)} as {Path.GetFileName(path)}");
            }
            else
            {
                Log.Info($"    linked PDF {Path.GetFileName(url)} is already saved as {Path.GetFileName(path)}");
            }

            if (entry != null && _manifest != null)
            {
                entry.LinkedFiles.RemoveAll(f => f.SourceUrl == url && f.LocalPath == path);
                entry.LinkedFiles.Add(new LinkedFile { SourceUrl = url, LocalPath = path, Sha256 = hash });
                _manifest.Save();
            }

            return path;
        }

        private static Dictionary<string, string> HashExistingFiles(string folder)
        {
            var map = new Dictionary<string, string>();
            foreach (string file in Directory.EnumerateFiles(folder))
            {
                string ext = Path.GetExtension(file);
                if (!DomSorterService.LinkedFileExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
                map.TryAdd(hash, file);
            }
            return map;
        }

        // Cover.pdf taken by a different document; e.g. "Cover-2.pdf", "Cover-3.pdf", ...
        private static string UniquePath(string folder, string fileName)
        {
            string name = MigrationOptions.SafeName(Path.GetFileNameWithoutExtension(fileName)), ext = Path.GetExtension(fileName);
            string path = Path.Combine(folder, name + ext);
            for (int n = 2; File.Exists(path); n++)
            {
                path = Path.Combine(folder, $"{name}-{n}{ext}");
            }
            return path;
        }

        // Opens in excel; which cover image goes with which PDF
        public string WriteLinkedFilesCsv(List<LinkRow> rows)
        {
            Directory.CreateDirectory(_o.PageFolder);

            string path = Path.Combine(_o.PageFolder, "linked-pdfs.csv");
            var lines = new List<string> { "Image #,Cover image (old site),Media ID,Cover image (new site),PDF (old site),Saved as" };

            lines.AddRange(rows.Select(r => string.Join(",", new[]
            {
                r.Item.Id.ToString(),
                r.Item.Src,
                r.Entry?.MediaId.ToString() ?? "upload failed",
                r.Entry?.NewUrl ?? "",
                r.Item.LinkedFileUrl!,
                Path.GetFileName(r.SavedPath)
            }.Select(Csv))));

            File.WriteAllLines(path, lines);
            return Path.GetFullPath(path);
        }

        private static string Csv(string value) =>
            value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}