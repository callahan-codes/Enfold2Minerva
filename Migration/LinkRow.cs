using enfold2minerva.Model;
using enfold2minerva.WordPress;

namespace enfold2minerva.Migration
{
    public record LinkRow
    (
        CaptionedMediaItem Item, 
        ManifestEntry? Entry, 
        string SavedPath
    );
}