using enfold2minerva.ClientDom;
using enfold2minerva.Logs;
using enfold2minerva.Migration.Images;
using enfold2minerva.Model;
using enfold2minerva.WordPress;
using HtmlAgilityPack;

namespace enfold2minerva.Migration
{
    public class PageMigrator
    {
        private readonly MigrationOptions _o;
        private readonly WordPressMediaService? _wp;  // null in a dry run without creds
        private readonly UploadManifest? _manifest;   // null in a dry run without creds
        private readonly ImageService _images;
        private readonly PdfService _pdfs;
        private readonly List<string> _failures = new();

        public PageMigrator(MigrationOptions options, WordPressMediaService? wp, UploadManifest? manifest)
        {
            _o = options;
            _wp = wp;
            _manifest = manifest;
            _images = new ImageService(options, wp, manifest);
            _pdfs = new PdfService(options, manifest);
        }

        // Scrape one old-site page; upload its images to site (reusing anything already uploaded);
        // write the block markup to a file; optionally put it on a new draft page
        public async Task<int> RunAsync(CancellationToken ct)
        {
            // scrape
            Log.Info($"Fetching {_o.PageUrl} ...");
            HtmlDocument doc = await new ClientDomBase(_o.PageUrl).DownloadSourceAsync(ct);
            List<CaptionedMediaItem> items = new DomSorterService(doc, _o.PageUrl).GetCaptionedMediaItems();

            // none found, early exit
            if (items.Count == 0)
            {
                Log.Warn($"No images found inside any of: {string.Join(", ", DomSorterService.ContainerClasses)}. Nothing to do.");
                return 0;
            }

            Log.Info($"Found {items.Count} image(s).\n");
            ReportContentWarnings(items);

            if (_o.DryRun) return DryRun(items);

            // create the draft page first, so each upload can be attached to it
            CreatedPage? page = null;
            if (_o.CreatePageTitle != null)
            {
                page = await _wp!.CreateDraftPageAsync(_o.CreatePageTitle, ct: ct);
                Log.Success($"Created draft page \"{_o.CreatePageTitle}\" (id {page.Id})");
            }

            // upload/reuse each image, and save any PDF it is a cover for
            var blocks = new List<string>();
            var linkRows = new List<LinkRow>();

            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();

                ManifestEntry? entry = null;
                try
                {
                    entry = await _images.ResolveAsync(item, page?.Id, ct);
                    blocks.Add(MinervaBlockBuilder.Build(new UploadedMedia(entry.MediaId, entry.NewUrl), item.Alt, item.Captions));
                }
                catch (Exception e) when (IsRecoverable(e, ct))
                {
                    _failures.Add($"#{item.Id} {item.Src}: {e.Message}");
                    Log.Error($"  #{item.Id} failed: {e.Message}");
                }

                // saved even if the image upload failed, so the document isn't lost
                if (item.LinkedFileUrl != null)
                {
                    try
                    {
                        string saved = await _pdfs.SaveLinkedFileAsync(item, entry, ct);
                        linkRows.Add(new LinkRow(item, entry, saved));
                    }
                    catch (Exception e) when (IsRecoverable(e, ct) || e is IOException)
                    {
                        _failures.Add($"#{item.Id} linked file {item.LinkedFileUrl}: {e.Message}");
                        Log.Error($"  #{item.Id} could not save linked file: {e.Message}");
                    }
                }
            }

            // save the block markup, the cover linked in PDF list, and fill the page
            string markup = string.Join("\n\n", blocks);
            Log.Info($"\nBlock markup saved to {WriteBlocksFile(markup)}");

            if (linkRows.Count > 0)
            {
                Log.Info($"{linkRows.Count} linked PDF(s) listed in {_pdfs.WriteLinkedFilesCsv(linkRows)}");
            }

            if (page != null)
            {
                await _wp!.UpdatePageContentAsync(page.Id, markup, ct);
                Log.Success($"Page filled with {blocks.Count} image block(s).");
                Log.Info($"Edit it here: {_wp.EditorUrl(page.Id)}");
            }

            // summary
            Log.Info($"\nUploaded {_images.Uploaded} new · reused {_images.ReusedKnown} already uploaded · " +
                     $"reused {_images.ReusedCopy} duplicate copies · {linkRows.Count} linked PDF(s) · {_failures.Count} failed");

            foreach (var f in _failures) 
            {
                Log.Error("  " + f);
            }
            return _failures.Count == 0 ? 0 : 2;
        }

        // A timeout surfaces as TaskCanceledException; only Ctrl+C should stop the whole run
        private static bool IsRecoverable(Exception e, CancellationToken ct) =>
            e is HttpRequestException || (e is TaskCanceledException && !ct.IsCancellationRequested);


        // Dry run
        private int DryRun(List<CaptionedMediaItem> items)
        {
            Log.Info("Dry run - nothing will be uploaded or created.\n");

            var firstSeen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var firstPdf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in items)
            {
                ManifestEntry? known = _manifest?.FindBySourceUrl(item.OriginalSrc) ?? _manifest?.FindBySourceUrl(item.Src);
                string status =
                    known != null ? $"already uploaded -> media {known.MediaId}" :
                    firstSeen.TryGetValue(item.OriginalSrc, out int first) ? $"same image as #{first}, will reuse it" :
                    "would upload (unless an identical file was already uploaded under another name)";
                firstSeen.TryAdd(item.OriginalSrc, item.Id);

                Log.Info($"#{item.Id} {status}");
                Log.Info($"    {item.OriginalSrc}" + (item.OriginalSrc != item.Src ? $"  (page shows {Path.GetFileName(item.Src)})" : ""));
                Log.Info($"    alt: \"{item.Alt}\"   captions: {item.Captions.Count} paragraph(s)");

                if (item.LinkedFileUrl != null)
                {
                    string pdf = Path.GetFileName(new Uri(item.LinkedFileUrl).AbsolutePath);
                    Log.Info(firstPdf.TryGetValue(item.LinkedFileUrl, out int pdfOwner)
                        ? $"    cover for {pdf} (same PDF as #{pdfOwner}, saved once)"
                        : $"    cover for {pdf} -> would save as {Path.Combine(_o.PageFolder, item.LinkedFileLocalName!)}");
                    firstPdf.TryAdd(item.LinkedFileUrl, item.Id);
                }
            }

            if (_manifest == null)
                Log.Warn("\nNo new-site credentials found, so already-uploaded images couldn't be checked.");
            if (_o.CreatePageTitle != null)
                Log.Info($"\nWould create draft page \"{_o.CreatePageTitle}\".");

            return 0;
        }

        // Warns about missing alt text and links to the old site, both need a look
        private void ReportContentWarnings(List<CaptionedMediaItem> items)
        {
            string oldHost = _o.PageUrl.Host;

            foreach (var item in items)
            {
                // fallback alt text warn
                if (item.AltIsFallback)
                {
                    Log.Warn($"#{item.Id} has no alt text - using \"{item.Alt}\" (review this)");
                }

                // caption(s) check
                foreach (string caption in item.Captions)
                {
                    // links found, hit check; otherwise bounce to next caption
                    var links = HtmlNode.CreateNode(caption).SelectNodes(".//a[@href]");
                    if (links == null) 
                    {
                        continue;
                    }

                    // a element(s) in caption
                    foreach (var a in links)
                    {
                        // compare current and old hosts links
                        string href = a.GetAttributeValue("href", "");
                        if (Uri.TryCreate(_o.PageUrl, href, out var abs) && abs.Host.Equals(oldHost, StringComparison.OrdinalIgnoreCase))
                        {
                            Log.Warn($"#{item.Id} caption links to the old site: {abs}");            
                        }
                    }
                }
            }

            Console.WriteLine();
        }

        // Writes new/to blocks.html
        private string WriteBlocksFile(string markup)
        {
            Directory.CreateDirectory(_o.PageFolder);

            string path = Path.Combine(_o.PageFolder, "blocks.html");
            File.WriteAllText(path, markup);
            return Path.GetFullPath(path);
        }
    }
}