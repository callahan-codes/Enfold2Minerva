# enfold2minerva

A .NET command-line tool that moves the images and captions from a page built with the **Enfold** WordPress theme onto a new WordPress site that uses **Minerva** blocks, without filling the new media library with duplicates.

Point it at one page on the old site. It will:

1. Scrape every image inside Enfold's image, masonry and gallery containers, plus the `<p>` paragraphs that follow them (the captions).
2. Upload each image to the new site's media library, **once**. If an image was already uploaded, whether by URL or as the same file under another name (`photo-1.jpg`, `photo (2).jpg`), the existing media item is reused.
3. Write ready-to-paste block markup (image block + paragraph blocks) to a file.
4. Optionally create a **draft** page on the new site and fill it with those blocks.
5. Save any PDF that an image links to (a "cover" image) to a local folder, with a CSV mapping each cover to its document.

---

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A new WordPress site reachable over **https**
- A WordPress user on that site with permission to upload media and create pages, and an **Application Password** for it (*Users → Profile → Application Passwords*)

---

## Setup

Clone and build:

```bash
git clone <this-repo-url>
cd enfold2minerva
dotnet build
```

Store the new site's credentials with .NET user secrets. They are saved in your user profile, **never in the project folder**:

```bash
dotnet user-secrets set "NewWordPress:Url"         "https://new-site.example"
dotnet user-secrets set "NewWordPress:User"        "your-migration-user"
dotnet user-secrets set "NewWordPress:AppPassword" "xxxx xxxx xxxx xxxx xxxx xxxx"
```

The URL must be `https`; the tool refuses to send the password over plain http.

---

## Usage

```bash
dotnet run -- <url> [options]
```

| Command | What it does |
|---|---|
| `dotnet run -- https://old-site.example/page/ --dry-run` | Shows what would be uploaded, reused and saved. Changes nothing. |
| `dotnet run -- https://old-site.example/page/` | Uploads images and writes block markup to `output/page/blocks.html`. |
| `dotnet run -- https://old-site.example/page/ --create-page "Page Title"` | Does the above, plus creates a draft page with the blocks on it. |

### Options

| Option | Default | Description |
|---|---|---|
| `<url>` | *(required)* | The old-site page to pull images and captions from |
| `--create-page <title>` | none | Create a draft page on the new site with this title and place the blocks on it |
| `--dry-run` | off | Preview only. Works without credentials, but can't check what's already uploaded |
| `--manifest <path>` | `media-manifest.json` | File that tracks uploaded media (see below) |
| `--output <folder>` | `output` | Parent folder for per-page output |

Start every page with `--dry-run`.

### Install as a global tool (optional)

```bash
dotnet pack
dotnet tool install --global --add-source ./nupkg Enfold2Minerva
enfold2minerva https://old-site.example/page/ --dry-run
```

Run the installed tool from the project folder so it picks up your user secrets and the same manifest.

---

## Output

Everything for a page goes into `output/<page name>/`. The folder is named after `--create-page`, or otherwise the last segment of the old page's URL.

| File | Contents |
|---|---|
| `blocks.html` | Block markup to paste into the block editor's Code editor |
| `*.pdf` | Documents that cover images link to, named after the cover image |
| `linked-pdfs.csv` | Which cover image (old and new URL, media ID) goes with which PDF |

PDFs are **not** uploaded to WordPress; they are saved locally for you to handle.

### Block format

Each image becomes a core image block with lightbox enabled, `wide` alignment, full size and the theme's `deep` shadow preset. Each caption paragraph after it becomes a plain paragraph block, with Enfold's attributes stripped.

---

## How duplicates are avoided

`media-manifest.json` records every upload: new media ID and URL, SHA-256 of the file, every old-site URL it was seen under, and the pages it was found on and placed on.

For each image the tool:

1. Strips Enfold's size suffix (`photo-1030x687.jpg` → `photo.jpg`) and tries the full-size original first, falling back to the resized copy.
2. Checks the manifest for that URL. If it's found, the image is reused without being downloaded.
3. Otherwise downloads it and checks the manifest for the same file hash. If it's found, the image is reused under the new name.
4. Otherwise uploads it, sets alt text, and records it in the manifest immediately, so a crash can't cause a re-upload.

The first page an image is placed on becomes its "Uploaded to" page in the media library.

> **Keep `media-manifest.json`.** Deleting it means the next run can't see what's already uploaded and will create duplicates. A manifest is tied to one site and refuses to be used against another.

---

## Warnings to review

The tool prints warnings for things that need a human look:

- **No alt text.** A fallback is built from the file name (`lab-tour_2024.jpg` → "lab tour 2024").
- **Caption links to the old site.** These need updating after migration.
- **A linked "PDF" that isn't a PDF.** Usually an error page.

---

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success (or nothing to do) |
| `1` | Fatal error (bad URL, missing secrets, network/auth failure) or cancelled |
| `2` | Finished, but one or more images or PDFs failed (listed at the end) |

Ctrl+C is safe: anything already uploaded is in the manifest.

---

## Security notes

- Credentials live only in user secrets and are sent only to the new site, over https.
- The old site is fetched with a separate, credential-free HTTP client.
- `media-manifest.json`, `output/` and `launchSettings.json` contain real site URLs and are git-ignored. Don't commit them.
- Caption HTML is copied as-is. If your migration user can post unfiltered HTML, review captions copied from untrusted sources.

---

## Project layout

| File | Role |
|---|---|
| `Program.cs` | CLI arguments, secrets, entry point |
| `PageMigrator.cs` | Runs one page: scrape → upload → markup → draft page |
| `DomSorterService.cs` | Finds images, captions and linked PDFs in the page HTML |
| `ClientDomBase.cs` | Downloads the old page |
| `OldSite.cs` | Credential-free HTTP client for the old site |
| `ImageService.cs` | Manifest lookup, download, hash dedupe, upload |
| `PdfService.cs` | Saves linked PDFs and writes `linked-pdfs.csv` |
| `MinervaBlockBuilder.cs` | Builds image and paragraph block markup |
| `WordPressMediaService.cs` | WordPress REST API client (media, pages) |
| `UploadManifest.cs` | The dedupe manifest |
| `MigrationOptions.cs` | Run options and output folder naming |
| `CaptionedMediaItem.cs`, `LinkRow.cs` | Data models |
| `Log.cs` | Coloured console output |