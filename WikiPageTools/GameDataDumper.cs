using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using EntityPageTools;
using Sledge.Formats.FileSystem;
using Sledge.Formats.GameData;
using Sledge.Formats.GameData.Objects;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

namespace FGDDumper
{
    /// <summary>
    /// Reads every installed game's FGD into the JSON under \fgd_dump, unpacking the entity icons
    /// they reference out of the VPKs along the way. The wiki generates its pages from that JSON,
    /// so this only has to run when a game updates.
    /// </summary>
    public static class GameDataDumper
    {
        public static void DumpFGD()
        {
            Logging.Log();
            Logging.Log(Logging.BannerTitle("Dumping FGD to JSON!"));

            // dictionary from entity classname -> page of that entity in every game it exists in
            var pagesDictionary = new Dictionary<string, List<EntityPage>>();

            var gamesList = GameFinder.GameList;

            Logging.Log();
            Logging.Log(Logging.BannerTitle("Current games to dump FGD for"));
            Logging.Log();

            foreach (var game in gamesList)
            {
                Logging.Log($"Name: '{game.Name}' | FileSystemName: '{game.FileSystemName}' | AppId: '{game.AppId}' | GameFolder: '{game.GameFolder}' | GameInfoFolder: '{game.PathToGameinfo}'");
                Logging.LogS("FGDs to read:");
                foreach (var fgd in game.FgdFilesNames)
                {
                    Logging.LogS($" {fgd}");
                }
                Logging.Log("\n");
            }

            Logging.Log(Logging.BannerTitle(string.Empty, 100));

            // only the games we actually read may have their icons cleaned up afterwards
            var dumpedGames = new List<GameFinder.Game>();

            foreach (GameFinder.Game game in gamesList)
            {
                Logging.Log();
                Logging.Log(Logging.BannerTitle($"Processing game '{game.Name}'"));
                Logging.Log();

                var gamePath = game.GetSystemPath();

                if (string.IsNullOrEmpty(gamePath))
                {
                    Logging.Log($"Failed to find game '{game.Name}' on this machine! skipping dumping for this game.", ConsoleColor.Red);
                    Logging.Log();
                    continue;
                }

                dumpedGames.Add(game);

                Logging.Log("Caching VPK content for game");
                game.CacheVPKContent();

                var fileResolver = new FGDFilesResolver(RecursiveFileGetter.GetFiles(gamePath, ".fgd"));

                // dont want to just read all fgds, usually fgds will be included by base fgds which sit in the same folder as gameinfo.
                // this is important because stuff like @overrideclass relies on the order of loading, skipping includes is bad.
                List<string> baseFGDPaths = fileResolver.GetBaseFgdPaths(game);
                List<GameDefinition> FGDs = [];

                foreach (var FGDFile in baseFGDPaths)
                {
                    Logging.Log($"\nProcessing FGD file: {FGDFile}");

                    using var stream = File.OpenRead(FGDFile);
                    using var reader = new StreamReader(stream);

                    var fgdFormatter = new FgdFormat(fileResolver);
                    FGDs.Add(fgdFormatter.Read(reader));
                }

                var validEntityCount = 0;

                if (Logging.Verbose)
                {
                    Logging.Log($"\nProcessing entities into entity pages:\n");
                }
                foreach (var fgd in FGDs)
                {
                    foreach (var Class in fgd.Classes)
                    {
                        var page = EntityPage.GetEntityPage(Class, game);

                        if (page is not null)
                        {
                            validEntityCount++;

                            if (Logging.Verbose)
                            {
                                Logging.Log($"{page.Name}");
                            }

                            if (pagesDictionary.ContainsKey(page.Name))
                            {
                                pagesDictionary[page.Name].Add(page);
                            }
                            else
                            {
                                pagesDictionary[page.Name] = new List<EntityPage> { page };
                            }
                        }
                    }
                }

                Logging.Log($"\nTotal amount of valid entities found: {validEntityCount}");

                Logging.Log($"\nFinished processing {FGDs.Count} FGD file(s)");
                Logging.Log();
            }

            Logging.Log();
            Logging.Log(Logging.BannerTitle("Processing entities into JSON and exporting!"));
            Logging.Log();

            // entities share icons a lot, keeps us from decoding and rewriting the same png over and over
            var extractedIcons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var page in pagesDictionary.Values.SelectMany(pages => pages))
            {
                ExtractPageIcon(page, extractedIcons);
            }

            RenderModelIcons(pagesDictionary, dumpedGames, extractedIcons);

            foreach ((string pageName, List<EntityPage> pages) in pagesDictionary)
            {
                var doc = EntityDocument.GetDocument(pageName, pages);

                if (Logging.Verbose)
                {
                    Logging.Log();
                    Logging.Log(Logging.BannerTitle($"Generating document {doc.Name} from pages:", 70));
                    foreach (var page in doc.Pages)
                    {
                        Logging.Log($"Page: '{page.Name}', from game: '{page.Game!.FileSystemName}'");
                    }
                }

                Directory.CreateDirectory(EntityPageTools.RootDumpFolder);
                var docPath = Path.Combine(EntityPageTools.RootDumpFolder, $"{doc.Name}.json");

                var jsonText = JsonSerializer.Serialize(doc, JsonContext.Default.EntityDocument);
                File.WriteAllText(docPath, jsonText);

                if (Logging.Verbose)
                {
                    Logging.Log($"\nSaved document JSON to {docPath}!");
                }
            }

            var removedIcons = 0;
            foreach (var game in dumpedGames)
            {
                removedIcons += RemoveStaleIcons(game, extractedIcons);
            }

            if (removedIcons > 0)
            {
                Logging.Log($"\nRemoved {removedIcons} icon(s) that nothing references any more");
            }

            var timestamp = (long)DateTime.UtcNow.Subtract(DateTime.UnixEpoch).TotalSeconds;
            File.WriteAllText(Path.Combine(EntityPageTools.RootDumpFolder, "timestamp.json"), timestamp.ToString(CultureInfo.InvariantCulture));

            Logging.Log($"\nProcessed and exported {pagesDictionary.Count} documents!");
        }

        /// <summary>
        /// Extracts a page's icon material into a png under the wiki's static folder and rewrites
        /// <see cref="EntityPage.IconPath"/> into the wiki path of that png. The path is cleared when
        /// the icon cannot be resolved, since a raw material reference is unusable to the wiki and
        /// would otherwise leak into page frontmatter as a broken image.
        /// </summary>
        /// <summary>
        /// Renders an icon for every entity that names a model. Done a game at a time, because
        /// standing up an OpenGL context and loading the renderer's shaders is the expensive part
        /// and one context serves all of that game's models.
        ///
        /// A model wins over an icon material when an entity has both: the model is what hammer
        /// draws for it, the sprite is a fallback for when hammer cannot. The material icon is
        /// still extracted first, so an entity whose model cannot be loaded or rendered keeps it.
        /// </summary>
        private static void RenderModelIcons(Dictionary<string, List<EntityPage>> pagesDictionary, List<GameFinder.Game> games, HashSet<string> extractedIcons)
        {
            var pending = pagesDictionary.Values
                .SelectMany(pages => pages)
                .Where(page => !string.IsNullOrEmpty(page.ModelPath))
                .ToList();

            if (pending.Count == 0)
            {
                return;
            }

            Logging.Log();
            Logging.Log(Logging.BannerTitle("Rendering icons for entities that name a model"));
            Logging.Log();

            var rendered = 0;

            foreach (var game in games)
            {
                var forThisGame = pending.Where(page => page.Game == game).ToList();

                if (forThisGame.Count == 0)
                {
                    continue;
                }

                var fileLoader = game.GetFileLoader();

                if (fileLoader == null)
                {
                    continue;
                }

                using var renderer = ModelIconRenderer.TryCreate(fileLoader);

                if (renderer == null)
                {
                    // no OpenGL context on this machine, and that will not change for the next game
                    return;
                }

                foreach (var page in forThisGame)
                {
                    rendered += RenderModelIcon(renderer, page, extractedIcons) ? 1 : 0;
                }
            }

            var withIcon = pending.Count(page => !string.IsNullOrEmpty(page.IconPath));

            // rendered counts pngs written, several entities can share one, so the second number
            // is what actually matters to the wiki
            Logging.Log($"\nRendered {rendered} model png(s), {withIcon} of {pending.Count} entities that name a model have an icon");
        }

        private static bool RenderModelIcon(ModelIconRenderer renderer, EntityPage page, HashSet<string> extractedIcons)
        {
            var modelPath = GetModelResourcePath(page.ModelPath);
            var pngWikiPath = EntityPage.GetIconPngPath(page.Game!, modelPath);

            // several entities point at the same model, and it only has to be drawn once
            if (extractedIcons.Contains(pngWikiPath))
            {
                page.IconPath = pngWikiPath;
                return false;
            }

            var model = page.Game!.LoadVPKResourceCompiled(modelPath);

            if (model == null)
            {
                Logging.Log($"Could not load model '{modelPath}' for '{page.Name}'", ConsoleColor.Red);
                return false;
            }

            try
            {
                if (!renderer.TryRenderToFile(model, WikiPaths.ToDisk(pngWikiPath)))
                {
                    Logging.Log($"Model '{page.ModelPath}' for '{page.Name}' has nothing to draw", ConsoleColor.Red);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logging.Log($"Failed to render '{page.ModelPath}' for '{page.Name}': {ex.Message}", ConsoleColor.Red);
                return false;
            }

            extractedIcons.Add(pngWikiPath);
            page.IconPath = pngWikiPath;

            if (Logging.Verbose)
            {
                Logging.Log($"Rendered icon for '{page.Name}' from '{page.ModelPath}'");
            }

            return true;
        }

        /// <summary>
        /// Deletes the pngs of icons this dump did not produce. An entity or a material that goes
        /// away upstream would otherwise leave its image behind for good, and these are committed
        /// to the wiki. Only games that were actually read are touched, so running with a game
        /// uninstalled never throws away its icons.
        /// </summary>
        private static int RemoveStaleIcons(GameFinder.Game game, HashSet<string> extractedIcons)
        {
            var iconFolder = EntityPage.GetIconFolder(game);
            var folder = WikiPaths.ToDisk(iconFolder);

            if (!Directory.Exists(folder))
            {
                return 0;
            }

            var removed = 0;

            foreach (var file in Directory.GetFiles(folder, "*.png"))
            {
                if (extractedIcons.Contains(WikiPaths.Combine(iconFolder, Path.GetFileName(file))))
                {
                    continue;
                }

                File.Delete(file);
                removed++;

                if (Logging.Verbose)
                {
                    Logging.Log($"Removed stale icon '{file}'");
                }
            }

            return removed;
        }

        private static void ExtractPageIcon(EntityPage page, HashSet<string> extractedIcons)
        {
            if (string.IsNullOrEmpty(page.IconPath))
            {
                return;
            }

            if (Logging.Verbose)
            {
                Logging.Log($"\nPage has entity icon path '{page.IconPath}' , attempting to dump icon image:");
            }

            var materialPath = GetIconMaterialPath(page.IconPath);
            var pngWikiPath = EntityPage.GetIconPngPath(page.Game!, page.IconPath);

            if (extractedIcons.Contains(pngWikiPath))
            {
                page.IconPath = pngWikiPath;
                return;
            }

            page.IconPath = string.Empty;

            if (page.Game!.LoadVPKResourceCompiled(materialPath)?.DataBlock is not Material iconMaterial)
            {
                if (Logging.Verbose)
                {
                    Logging.Log($"Failed to load entity icon material '{materialPath}'", ConsoleColor.Red);
                }
                return;
            }

            var iconTexturePath = GetMaterialColorTexture(iconMaterial);

            if (string.IsNullOrEmpty(iconTexturePath))
            {
                Logging.Log($"Entity icon material '{materialPath}' has no color texture, skipping icon.", ConsoleColor.Red);
                return;
            }

            var iconTexture = page.Game.LoadVPKResourceCompiled(iconTexturePath);

            if (iconTexture is null)
            {
                Logging.Log($"Failed to load entity icon texture '{iconTexturePath}', skipping icon.", ConsoleColor.Red);
                return;
            }

            var pngDiskPath = WikiPaths.ToDisk(pngWikiPath);
            Directory.CreateDirectory(Path.GetDirectoryName(pngDiskPath)!);
            SavePNGFromTextureResource(iconTexture, pngDiskPath);

            extractedIcons.Add(pngWikiPath);
            page.IconPath = pngWikiPath;

            if (Logging.Verbose)
            {
                Logging.Log($"Saved icon texture to '{pngDiskPath}'!");
            }
        }

        /// <summary>
        /// The compiled model an FGD reference means. They come as 'models/foo', 'models/foo.vmdl'
        /// and, left over from source 1, 'models/foo.mdl' — on disk it is always the .vmdl.
        /// </summary>
        private static string GetModelResourcePath(string modelPath)
        {
            var path = modelPath.Replace('\\', '/');

            if (!path.EndsWith(".vmdl", StringComparison.OrdinalIgnoreCase))
            {
                path = $"{Path.ChangeExtension(path, null)}.vmdl";
            }

            return path;
        }

        // icon references out of an FGD range from 'editor/foo' to 'materials/editor/foo.vmat',
        // and source 1 leftovers still point at a .vmt
        private static string GetIconMaterialPath(string iconPath)
        {
            var materialPath = iconPath.Replace('\\', '/');

            if (!materialPath.StartsWith("materials/", StringComparison.OrdinalIgnoreCase))
            {
                materialPath = $"materials/{materialPath}";
            }

            if (!materialPath.EndsWith(".vmat", StringComparison.OrdinalIgnoreCase))
            {
                materialPath = $"{Path.ChangeExtension(materialPath, null)}.vmat";
            }

            return materialPath;
        }

        private static readonly string[] ColorTextureParams = ["g_tColor", "g_tColorA", "g_tColorB", "g_tColorC"];

        private static string? GetMaterialColorTexture(Material material)
        {
            foreach (var colorParam in ColorTextureParams)
            {
                if (material.TextureParams.TryGetValue(colorParam, out var texturePath))
                {
                    return texturePath;
                }
            }

            return null;
        }

        private static string EscapeInvalidTags(string input, string[] allowedTags)
        {
            var allowedPattern = string.Join("|", allowedTags.Select(Regex.Escape));

            // match opening tags that are NOT in the allowed list
            var invalidOpenTagPattern = $@"<(?!/?(?:{allowedPattern})\b)[^>]*>";

            return Regex.Replace(input, invalidOpenTagPattern, match =>
                WebUtility.HtmlEncode(match.Value), RegexOptions.IgnoreCase);
        }

        public static string SanitizeInput(string input)
        {
            // make this newline so stuff displays nicely
            input = input.Replace("<br>", "\n");

            // no clue what this does in hammer, seems to be nothing
            // a lot of these are just broken so im removing them outright to avoid confusion
            input = input.Replace("<original name>", "");
            input = input.Replace("<Award Text>", "");
            input = input.Replace("<picker>", "");
            input = input.Replace("<None>", "None");

            // escape any funky tags
            var allowedTags = new[] { "b", "br", "strong" };
            input = EscapeInvalidTags(input, allowedTags);
            // escape unclosed tags at the end
            input = Regex.Replace(input, @"<([^>]*)$", "&lt;$1");
            // escape unclosed tags followed by another opening tag
            input = Regex.Replace(input, @"<([^>]*)(?=<)", "&lt;$1");
            // escape unmatched closing brackets at start
            input = Regex.Replace(input, @"^([^<]*?)>", "$1&gt;");
            // escape unmatched closing brackets after other closing brackets
            input = Regex.Replace(input, @"(?<=>)([^<]*?)>", "$1&gt;");

            input = input.Replace("{", "\\{");
            input = input.Replace("}", "\\}");

            return input;
        }

        public static string SanitizeInputTable(string input)
        {
            return SanitizeInput(input).Replace("|", "\\|");
        }

        public static void SavePNGFromTextureResource(Resource texture, string pathToSaveTo)
        {
            if (Logging.Verbose)
            {
                Logging.Log($"Read '{texture!.FileName}', extracting:");
            }
            TextureContentFile textureExtract = (TextureContentFile)new TextureExtract(texture).ToContentFile();
            using var bitmap = textureExtract.Bitmap;
            using var data = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);

            // File.Create and not OpenWrite, the latter does not truncate and would leave the tail
            // of a larger existing png behind
            using var stream = File.Create(pathToSaveTo);
            data.SaveTo(stream);
        }

    }

    public static class RecursiveFileGetter
    {
        public static List<string> GetFiles(string folder, string filenameFilter)
        {
            if (Directory.Exists(folder))
            {
                return ProcessDirectory(folder, filenameFilter);
            }

            throw new InvalidDataException($"RecursiveFileProcessor: Input path '{folder}' seems to not be a valid directory.");
        }

        public static List<string> ProcessDirectory(string targetDirectory, string filenameFilter)
        {
            List<string> fileList = [];

            string[] fileEntries = Directory.GetFiles(targetDirectory);
            foreach (string fileName in fileEntries)
            {
                var file = ProcessFile(fileName, filenameFilter);

                if (!string.IsNullOrEmpty(file))
                {
                    fileList.Add(file);
                }
            }

            string[] subdirectoryEntries = Directory.GetDirectories(targetDirectory);
            foreach (string subdirectory in subdirectoryEntries)
            {
                fileList.AddRange(ProcessDirectory(subdirectory, filenameFilter));
            }

            return fileList;
        }

        public static string? ProcessFile(string path, string filenameFilter)
        {
            if (Path.GetFileName(path).Contains(filenameFilter))
                return path;

            return null;
        }
    }

    // the fgd library makes you implement this by yourself from the interface, dont really need the 2 other functions so far for our usecase
    public class FGDFilesResolver(List<string> Paths) : IFileResolver
    {
        Stream IFileResolver.OpenFile(string path)
        {
            foreach (var fullpath in Paths)
            {
                if (File.Exists(fullpath))
                {
                    // checking path against file name is needed for FGD includes, they usually only specify the filename
                    if (Equals(fullpath, path) || fullpath.Contains(path))
                    {
                        return File.Open(fullpath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    }
                }
            }

            throw new InvalidDataException($"Failed to find '{path}'");
        }

        public List<string> GetBaseFgdPaths(GameFinder.Game game)
        {
            List<string> paths = [];

            foreach (var fgdFileName in game.FgdFilesNames)
            {
                foreach (var fgdPath in Paths)
                {
                    if (fgdPath.Contains(Path.Combine(game.PathToGameinfo, fgdFileName)))
                    {
                        paths.Add(fgdPath);
                    }
                }
            }

            return paths;
        }

        IEnumerable<string> IFileResolver.GetFiles(string path)
        {
            return Paths;
        }

        // these are not really needed rn
        bool IFileResolver.FileExists(string path)
        {
            throw new NotImplementedException();
        }

        IEnumerable<string> IFileResolver.GetFolders(string path)
        {
            throw new NotImplementedException();
        }

        public bool FolderExists(string path)
        {
            throw new NotImplementedException();
        }

        public long FileSize(string path)
        {
            throw new NotImplementedException();
        }
    }

}
