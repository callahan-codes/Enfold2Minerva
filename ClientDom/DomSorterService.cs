using System.Text.RegularExpressions;
using enfold2minerva.Model;
using HtmlAgilityPack;

namespace enfold2minerva.ClientDom
{
    public partial class DomSorterService
    {
        private readonly HtmlDocument _document;
        private readonly Uri _pageUrl;

        // Enfold wrappers whose images get migrated; images outside all of these (logo, icons) are skipped
        public static readonly string[] ContainerClasses =
        [
            "avia-image-container",
            "av-masonry",
            "av-horizontal-gallery-slider",
        ];

        // File types that, when an image links to them, save locally 
        public static readonly string[] LinkedFileExtensions = [".pdf"];

        public DomSorterService(HtmlDocument document, Uri pageUrl)
        {
            _document = document;
            _pageUrl = pageUrl;
        }

        // Finds <img>, <p> tags that follow  in the same container until the next one
        public List<CaptionedMediaItem> GetCaptionedMediaItems()
        {
            List<CaptionedMediaItem> items = new();

            // find imgs, exit early if none
            var imageNodes = _document.DocumentNode.SelectNodes("//img");
            if (imageNodes == null) 
            {
                return items;
            }

            foreach (var img in imageNodes)
            {
                // find the imgs wrapper div; skip imgs that arent in one
                var container = img.Ancestors("div").FirstOrDefault(IsContainer);
                if (container == null) 
                {
                    continue;
                }

                // retrieve real source from imgs that are lazy-loaded
                string rawSrc = img.GetAttributeValue("data-src", "");
                if (string.IsNullOrWhiteSpace(rawSrc)) 
                {
                    rawSrc = img.GetAttributeValue("src", "");
                }
                if (string.IsNullOrWhiteSpace(rawSrc)) 
                {
                    continue;
                }

                // resolve relative URLs against the page ("/files/x.jpg" -> "https://my-wp/files/x.jpg")
                if (!Uri.TryCreate(_pageUrl, WebUtilityDecode(rawSrc), out var srcUri)
                    || (srcUri.Scheme != Uri.UriSchemeHttp && srcUri.Scheme != Uri.UriSchemeHttps))
                {
                    continue;
                }
                string src = srcUri.ToString();

                // the text after a gallery belongs to it; last image gets it
                bool isLastInContainer = container.Descendants("img").Last() == img;
                List<string> captions = isLastInContainer ? GetFollowingParagraphs(container) : new();

                // ensure alt
                (string alt, bool isFallback) = GetAltText(img, src);

                items.Add(new CaptionedMediaItem
                {
                    Id = items.Count + 1,
                    Src = src,
                    OriginalSrc = ToOriginalUrl(src),
                    Alt = alt,
                    AltIsFallback = isFallback,
                    Captions = captions,
                    LinkedFileUrl = GetLinkedFileUrl(img)
                });
            }

            return items;
        }

        // Collect <p> tags after the container, until next container
        private static List<string> GetFollowingParagraphs(HtmlNode container)
        {
            List<string> captions = new();

            for (var sib = container.NextSibling; sib != null; sib = sib.NextSibling)
            {
                // skip whitespace/comments; hit next img
                if (sib.NodeType != HtmlNodeType.Element) 
                {
                    continue;
                }
                if (IsContainer(sib) || sib.Descendants("div").Any(IsContainer)) 
                {
                    break;
                }

                // get non-empty p tag values
                var paragraphs = sib.SelectNodes(".//p");
                if (paragraphs != null)
                {
                    captions.AddRange(paragraphs.Where(p => !IsEmptyParagraph(p)).Select(p => p.OuterHtml.Trim()));

                }
            }

            return captions;
        }

        // "Cover" images link to a full document; returns that documents absolute URL or null
        private string? GetLinkedFileUrl(HtmlNode img)
        {
            // nearest <a> around the image with a non-empty href; none means it's not a cover
            var link = img.Ancestors("a").FirstOrDefault(a => a.GetAttributeValue("href", "") != "");
            if (link == null)
            {
                return null;
            }

            // skip malformed links; make the href absolute ("/files/x.pdf" -> "https://wp-site/files/x.pdf")
            if (!Uri.TryCreate(_pageUrl, WebUtilityDecode(link.GetAttributeValue("href", "")), out var href))
            {
                return null;
            }

            // only keep links to file types in LinkedFileExtensions
            string ext = Path.GetExtension(href.AbsolutePath);
            return LinkedFileExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase) ? href.ToString() : null;
        }

        // Checks if passed HtmlNode is a container
        private static bool IsContainer(HtmlNode node) => ContainerClasses.Any(node.HasClass);

        // Uses the image's alt text, or builds one from the file name ("lab-tour_2024.jpg" -> "lab tour 2024")
        private static (string Alt, bool IsFallback) GetAltText(HtmlNode image, string src)
        {
            string alt = WebUtilityDecode(image.GetAttributeValue("alt", "")).Trim();
            if (!string.IsNullOrWhiteSpace(alt)) 
            {
                return (alt, false);
            }

            string fileName = SizeSuffix().Replace(Uri.UnescapeDataString(Path.GetFileName(new Uri(src).AbsolutePath)), "");
            string fallback = Path.GetFileNameWithoutExtension(fileName).Replace('-', ' ').Replace('_', ' ').Trim();
            return (fallback, true);
        }

        // Strip "-widthxheight" before the extension
        public static string ToOriginalUrl(string url)
        {
            var uri = new Uri(url);
            string path = SizeSuffix().Replace(uri.AbsolutePath, "");

            return new UriBuilder(uri) 
            { 
                Path = path, 
                Query = "" 
            }.Uri.ToString();
        }

        // Enfold leaves spacer paragraphs like <p>&nbsp;</p> behind
        private static bool IsEmptyParagraph(HtmlNode p) =>
            string.IsNullOrWhiteSpace(WebUtilityDecode(p.InnerText).Replace(' ', ' '))
            && !p.Descendants().Any(d => d.NodeType == HtmlNodeType.Element && d.Name is "img" or "iframe" or "video");

        // Convert to literal html
        private static string WebUtilityDecode(string s) => System.Net.WebUtility.HtmlDecode(s);

        // "-1030x687" immediately before the file extension
        [GeneratedRegex(@"-\d+x\d+(?=\.[A-Za-z0-9]+$)")]
        private static partial Regex SizeSuffix();
    }
}