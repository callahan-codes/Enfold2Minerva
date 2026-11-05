using System.Security.Cryptography;
using enfold2minerva.Logs;
using enfold2minerva.Model;
using enfold2minerva.WordPress;

namespace enfold2minerva.Migration
{
    // One-time (safe to re-run) import of images that were already in the new site's media
    // library before this tool existed, so the hash check can reuse them instead of re-uploading
    public static class ManifestSeeder
    {
        public static async Task<int> RunAsync(WordPressMediaService wp, UploadManifest manifest, CancellationToken ct)
        {
            var known = manifest.Entries.Select(e => e.MediaId).ToHashSet();
            int added = 0, alreadyTracked = 0, siteDupes = 0, failed = 0;

            Log.Info($"Reading the media library on {wp.SiteUrl} ...");

            await foreach (ExistingMedia media in wp.ListImagesAsync(ct))
            {
                if (known.Contains(media.Id))
                {
                    alreadyTracked++;
                    continue;
                }

                try
                {
                    byte[] bytes = await DownloadOriginalAsync(wp, media, ct);
                    string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

                    // the library itself already has this exact file; keep the first one
                    ManifestEntry? same = manifest.FindByHash(hash);
                    if (same != null)
                    {
                        siteDupes++;
                        Log.Warn($"  media {media.Id} is an identical copy of media {same.MediaId} (could be deleted)");
                        continue;
                    }

                    manifest.Entries.Add(new ManifestEntry
                    {
                        MediaId = media.Id,
                        NewUrl = media.SourceUrl,
                        Sha256 = hash,
                        AltText = media.AltText ?? "",
                        UploadedAt = new DateTimeOffset(DateTime.SpecifyKind(media.DateGmt, DateTimeKind.Utc)),
                        Origin = "pre-existing"
                    });
                    known.Add(media.Id);
                    added++;
                    Log.Success($"  media {media.Id} {Path.GetFileName(media.OriginalFileUrl.AbsolutePath)}");

                    if (added % 25 == 0) manifest.Save();
                }
                catch (Exception e) when (e is HttpRequestException || (e is TaskCanceledException && !ct.IsCancellationRequested))
                {
                    failed++;
                    Log.Error($"  media {media.Id} skipped: {e.Message}");
                }
            }

            manifest.Save();
            Log.Info($"\nAdded {added} · already in manifest {alreadyTracked} · identical copies on the site {siteDupes} · failed {failed}");
            return failed == 0 ? 0 : 2;
        }

        // Hash the file as it was uploaded; fall back to source_url if the original is gone
        private static async Task<byte[]> DownloadOriginalAsync(WordPressMediaService wp, ExistingMedia media, CancellationToken ct)
        {
            Uri original = media.OriginalFileUrl;
            Uri source = new(media.SourceUrl);

            if (original == source) return await wp.DownloadAsync(source, ct);

            try
            {
                return await wp.DownloadAsync(original, ct);
            }
            catch (HttpRequestException)
            {
                Log.Warn($"  media {media.Id} original file missing, hashing {Path.GetFileName(source.AbsolutePath)} instead");
                return await wp.DownloadAsync(source, ct);
            }
        }
    }
}