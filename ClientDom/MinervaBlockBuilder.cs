using System.Net;
using System.Text.Json.Nodes;
using enfold2minerva.WordPress;
using HtmlAgilityPack;

namespace enfold2minerva.ClientDom
{
    // Builds the block markup, not the rendered front-end HTML
    public static class MinervaBlockBuilder
    {
        public static string ImageBlock
        (
            UploadedMedia media,
            string alt,
            bool lightbox = true,
            string? align = "wide",
            string? shadow = "deep")
        {   // block attributes, stored as JSON in the opening comment

            var blockAttrs = new JsonObject();
            
            // lightbox check
            if (lightbox) 
            {
                blockAttrs["lightbox"] = new JsonObject 
                { 
                    ["enabled"] = true 
                };
            }

            // get wp-image-{id}
            blockAttrs["id"] = media.Id;

            // other image blockAttrs
            blockAttrs["sizeSlug"] = "full";
            blockAttrs["linkDestination"] = "none";

            // "wide", "full", "center", or null
            if (align != null) 
            {
                blockAttrs["align"] = align;
            }
            
            // theme shadow preset slug, or null
            if (shadow != null) 
            {
                blockAttrs["style"] = new JsonObject 
                { 
                    ["shadow"] = $"var:preset|shadow|{shadow}" 
                };
            }

            // convert alignment if present, size-full for minerva img width
            var figureClasses = new List<string> { "wp-block-image" };
            if (align != null) 
            {
                figureClasses.Add("align" + align);
            }
            figureClasses.Add("size-full");

            //convert shadow if present
            string style = shadow != null ? $" style=\"box-shadow:var(--wp--preset--shadow--{shadow})\"" : "";

            // build and return figure content
            string img = $"<img src=\"{Attr(media.SourceUrl)}\" alt=\"{Attr(alt)}\" class=\"wp-image-{media.Id}\"{style}/>";
            return $"<!-- wp:image {blockAttrs.ToJsonString()} -->\n" +
                   $"<figure class=\"{string.Join(' ', figureClasses)}\">{img}</figure>\n" +
                   "<!-- /wp:image -->";
        }

        // Turns one scraped &lt;p&gt; into a paragraph block, dropping Enfolds attributes
        public static string ParagraphBlock(string paragraphOuterHtml)
        {
            var p = HtmlNode.CreateNode(paragraphOuterHtml);
            string inner = p.InnerHtml.Trim();
            if (inner.Length == 0) 
            {
                return "";
            }

            return $"<!-- wp:paragraph -->\n<p>{inner}</p>\n<!-- /wp:paragraph -->";
        }

        // Image block followed by its caption paragraphs
        public static string Build(UploadedMedia media, string alt, IEnumerable<string> captions)
        {
            var blocks = new List<string> { ImageBlock(media, alt) };
            blocks.AddRange(captions.Select(ParagraphBlock).Where(b => b.Length > 0));
            return string.Join("\n\n", blocks);
        }

        // Safe html string
        private static string Attr(string value) => WebUtility.HtmlEncode(value);
    }
}
