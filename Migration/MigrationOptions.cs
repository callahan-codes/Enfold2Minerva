namespace enfold2minerva.Migration
{
    public record MigrationOptions
    (
        Uri PageUrl,
        string? CreatePageTitle,
        bool DryRun,
        string ManifestPath,
        string OutputDir
    )
    {
        // Produces locally, goes in one folder per page: output/&lt;page name&gt;/
        // Named after --create-page when given, otherwise after the old page's URL ("exhibitions").
        public string PageFolder
        {
            get
            {
                string? name = CreatePageTitle;
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = Path.GetFileNameWithoutExtension(PageUrl.Segments.LastOrDefault()?.Trim('/') ?? "");
                    if (string.IsNullOrEmpty(name)) name = "home";
                }
                return Path.Combine(OutputDir, SafeName(name));
            }
        }

        // Windows rejects these in file and folder names, even when running elsewhere
        public static string SafeName(string name)
        {
            char[] invalid = "<>:\"/\\|?*".ToCharArray();
            string safe = new(name.Select(c => invalid.Contains(c) || char.IsControl(c) ? '-' : c).ToArray());
            safe = safe.Trim().TrimEnd('.');
            return safe.Length == 0 ? "untitled" : safe;
        }
    }
}