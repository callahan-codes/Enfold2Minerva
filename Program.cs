/*
    Version 1.1.2
    enfold2minerva
    
    Moves the images and captions on one old-site page to its development site

        enfold2minerva <url>                         upload images, write block markup to ./output
        enfold2minerva <url> --create-page "Title"   ...and put them on a new draft page
        enfold2minerva <url> --dry-run               show what would happen, change nothing
        enfold2minerva seed                          add images already in the new site's media library to the manifest

    Images already uploaded (same URL, or the same file under another name) are reused,
    tracked in media-manifest.json; don't delete it or dupes will happen.

    Output goes to output/<page name>/ (the --create-page title, or the old URL's last segment):
      blocks.html        block markup for copy/paste
      *.pdf              documents that cover images link to, named after the cover image
      linked-pdfs.csv    which cover image goes with which PDF
*/

using System.CommandLine;
using enfold2minerva.Migration;
using enfold2minerva.WordPress;
using Microsoft.Extensions.Configuration;

static (WordPressMediaService Wp, UploadManifest Manifest) Connect(string manifestPath)
{
    var config = new ConfigurationBuilder().AddUserSecrets<Program>().Build();

    string Require(string key) => config[key] ?? throw new InvalidOperationException
    (
        $"Missing user secret '{key}'. Run: dotnet user-secrets set \"{key}\" \"<value>\""
    );

    var wp = new WordPressMediaService
    (
        new Uri(Require("NewWordPress:Url")),
        Require("NewWordPress:User"),
        Require("NewWordPress:AppPassword")
    );

    return (wp, UploadManifest.Load(manifestPath, wp.SiteUrl));
}

var urlArgument = new Argument<string>("url")
{
    Description = "The page URL to pull images and captions from (e.g. https://old-site.example/page/)"
};

urlArgument.Validators.Add(result =>
{
    string? value = result.GetValueOrDefault<string>();
    bool ok = Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
              && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    if (!ok)
        result.AddError($"'{value}' is not a valid URL. Use a full address like https://old-site.example/page/");
});

var createPageOption = new Option<string?>("--create-page")
{
    Description = "Create a draft page on the new site with this title and place the images and captions on it",
    HelpName = "title"
};

var dryRunOption = new Option<bool>("--dry-run")
{
    Description = "Show what would be uploaded and reused without changing anything"
};

var manifestOption = new Option<string>("--manifest")
{
    Description = "File that tracks uploaded images so they are never uploaded twice",
    DefaultValueFactory = _ => "media-manifest.json",
    HelpName = "path",
    Recursive = true
};

var outputOption = new Option<string>("--output")
{
    Description = "Parent folder for per-page output (block markup, linked PDFs)",
    DefaultValueFactory = _ => "output",
    HelpName = "folder"
};

var seedCommand = new Command("seed", "Add images already in the new site's media library to the manifest so they're reused, not re-uploaded");

var rootCommand = new RootCommand("enfold2minerva - move an Enfold page's images and captions to a new Minerva site")
{
    urlArgument,
    createPageOption,
    dryRunOption,
    manifestOption,
    outputOption
};

rootCommand.SetAction(async (parseResult, ct) =>
{
    var options = new MigrationOptions(
        PageUrl: new Uri(parseResult.GetValue(urlArgument)!),
        CreatePageTitle: parseResult.GetValue(createPageOption),
        DryRun: parseResult.GetValue(dryRunOption),
        ManifestPath: parseResult.GetValue(manifestOption)!,
        OutputDir: parseResult.GetValue(outputOption)!);

    try
    {
        var config = new ConfigurationBuilder().AddUserSecrets<Program>().Build();
        bool hasCredentials = config["NewWordPress:Url"] is not null;

        if (!hasCredentials && !options.DryRun)
        {
            throw new InvalidOperationException
            (
                "Missing user secret 'NewWordPress:Url'. Run: dotnet user-secrets set \"NewWordPress:Url\" \"https://new-site.example\""
            );            
        }

        WordPressMediaService? wp = null;
        UploadManifest? manifest = null;

        if (hasCredentials)
        {
            string Require(string key) => config[key] ?? throw new InvalidOperationException
            (
                $"Missing user secret '{key}'. Run: dotnet user-secrets set \"{key}\" \"<value>\""
            );

            wp = new WordPressMediaService
            (
                new Uri(Require("NewWordPress:Url")),
                Require("NewWordPress:User"),
                Require("NewWordPress:AppPassword")
            );

            manifest = UploadManifest.Load(options.ManifestPath, wp.SiteUrl);
        }

        return await new PageMigrator(options, wp, manifest).RunAsync(ct);
    }
    catch (Exception e) when (e is HttpRequestException or InvalidOperationException or UriFormatException)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine(e.Message);
        Console.ResetColor();
        return 1;
    }
    catch (OperationCanceledException)
    {
        Console.Error.WriteLine("Cancelled. Anything already uploaded is recorded in the manifest.");
        return 1;
    }
});

seedCommand.SetAction(async (parseResult, ct) =>
{
    try
    {
        var (wp, manifest) = Connect(parseResult.GetValue(manifestOption)!);
        return await ManifestSeeder.RunAsync(wp, manifest, ct);
    }
    catch (Exception e) when (e is HttpRequestException or InvalidOperationException)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine(e.Message);
        Console.ResetColor();
        return 1;
    }
});

rootCommand.Subcommands.Add(seedCommand);
return await rootCommand.Parse(args).InvokeAsync();