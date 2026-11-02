using System.Net;
using System.Security.Cryptography;
using enfold2minerva.Logs;
using enfold2minerva.Model;
using enfold2minerva.WordPress;

namespace enfold2minerva.Migration.Images
{
    public class ImageService
    {
        private readonly MigrationOptions _o;
        private readonly WordPressMediaService? _wp;  
        private readonly UploadManifest? _manifest; 

        public int Uploaded { get; private set; }
        public int ReusedKnown { get; private set; }
        public int ReusedCopy { get; private set; }

        public ImageService(MigrationOptions o, WordPressMediaService? wp, UploadManifest? manifest)
        {
            _o = o;
            _wp = wp;
            _manifest = manifest;
        }

        // Finds this image in the manifest, or downloads and uploads it
        public async Task<ManifestEntry> ResolveAsync(CaptionedMediaItem item, int? pageId, CancellationToken ct)
        {
            var manifest = _manifest!;
            var wp = _wp!;

            // seen this exact URL before? skip the download entirely
            ManifestEntry? entry = manifest.FindBySourceUrl(item.OriginalSrc) ?? manifest.FindBySourceUrl(item.Src);
            if (entry != null)
            {
                ReusedKnown++;
                Log.Info($"  #{item.Id} already uploaded -> media {entry.MediaId}");
            }
            else
            {
                (byte[] bytes, string usedUrl) = await DownloadAsync(item, ct);
                string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

                // same bytes under a different name ("photo-1.jpg", "photo (2).jpg") reuse it
                entry = manifest.FindByHash(hash);
                if (entry != null)
                {
                    ReusedCopy++;
                    Log.Info($"  #{item.Id} duplicate of media {entry.MediaId} ({Path.GetFileName(usedUrl)})");
                }
                else
                {
                    string fileName = Uri.UnescapeDataString(Path.GetFileName(new Uri(usedUrl).AbsolutePath));
                    UploadedMedia media = await wp.UploadAsync(bytes, fileName, ct);

                    entry = new ManifestEntry
                    {
                        MediaId = media.Id,
                        NewUrl = media.SourceUrl,
                        Sha256 = hash,
                        AltText = item.Alt,
                        UploadedAt = DateTimeOffset.Now
                    };
                    manifest.Entries.Add(entry);
                    manifest.Save();

                    Uploaded++;
                    Log.Success($"  #{item.Id} uploaded {fileName} -> media {media.Id}");

                    await TryUpdateAsync(wp, entry, () => wp.UpdateDetailsAsync(media.Id, item.Alt, ct: ct), "set alt text");
                }

                UploadManifest.AddUnique(entry.SourceUrls, usedUrl);
            }

            UploadManifest.AddUnique(entry.SourceUrls, item.Src);
            UploadManifest.AddUnique(entry.SourceUrls, item.OriginalSrc);
            UploadManifest.AddUnique(entry.FoundOnPages, _o.PageUrl.ToString());

            if (pageId is int id)
            {
                UploadManifest.AddUnique(entry.PlacedOnPageIds, id);

                // wp allows one "Uploaded to" page per item; keep the first one
                if (entry.AttachedToPageId == null)
                {
                    bool ok = await TryUpdateAsync(wp, entry,
                        () => wp.UpdateDetailsAsync(entry.MediaId, altText: null, attachToPostId: id, ct: ct),
                        "attach to page");
                    if (ok) entry.AttachedToPageId = id;
                }
            }

            manifest.Save();
            return entry;
        }

        // Alt text, warn instead of failing the image
        private static async Task<bool> TryUpdateAsync(WordPressMediaService wp, ManifestEntry entry, Func<Task> update, string what)
        {
            try
            {
                await update();
                return true;
            }
            catch (HttpRequestException e)
            {
                Log.Warn($"    could not {what} on media {entry.MediaId}: {e.Message}");
                return false;
            }
        }

        // Tries the full-size original first, then the URL exactly as it appeared on the page
        private static async Task<(byte[] Bytes, string Url)> DownloadAsync(CaptionedMediaItem item, CancellationToken ct)
        {
            if (item.OriginalSrc != item.Src)
            {
                using var response = await OldSite.GetAsync(item.OriginalSrc, ct);
                if (response.IsSuccessStatusCode)
                {
                    return (await response.Content.ReadAsByteArrayAsync(ct), item.OriginalSrc);        
                }

                if (response.StatusCode != HttpStatusCode.NotFound)
                {
                    throw new HttpRequestException($"{item.OriginalSrc} returned {(int)response.StatusCode} {response.ReasonPhrase}");
                }

                Log.Warn($"  #{item.Id} no full-size original, using the resized copy");
            }

            using var fallback = await OldSite.GetAsync(item.Src, ct);
            if (!fallback.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"{item.Src} returned {(int)fallback.StatusCode} {fallback.ReasonPhrase}");   
            }

            return (await fallback.Content.ReadAsByteArrayAsync(ct), item.Src);
        }
    }
}