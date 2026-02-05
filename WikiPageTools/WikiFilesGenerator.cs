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
    public static class WikiFilesGenerator
    {
        public class EntityIndexEntry
        {
            public string Classname { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string Icon { get; set; } = string.Empty;
            public List<string> Games { get; set; } = [];
        }

        public static void GenerateMDXFromJSONDump()
        {
            Logging.Log();
            Logging.Log(Logging.BannerTitle("Generating MDX pages from JSON dump!"));

            var gamesList = GameFinder.GameList;

            var docsDictionary = new Dictionary<string, EntityDocument>();

            Logging.Log();
            Logging.Log(Logging.BannerTitle("Loading JSON docs!"));
            string[] jsonDocs = Directory.GetFiles(EntityPageTools.RootDumpFolder);
            Logging.Log($"Found '{jsonDocs.Length}' JSON doc(s)");

            Logging.Log();
            Logging.Log(Logging.BannerTitle("Loading page overrides!"));
            string[] overrides = Directory.GetFiles(EntityPageTools.RootOverridesFolder);
            Logging.Log($"Found '{overrides.Length}' page override(s)");

            Logging.Log();
            Logging.Log(Logging.BannerTitle("Deserialising JSON docs into page docs!"));
            foreach (var jsonDoc in jsonDocs)
            {
                if (Path.GetFileNameWithoutExtension(jsonDoc) == "timestamp")
                {
                    continue;
                }

                var doc = JsonSerializer.Deserialize(File.ReadAllText(jsonDoc), JsonContext.Default.EntityDocument);

                if (doc is null)
                {
                    throw new InvalidDataException("Failed to deserialise json document!");
                }

                docsDictionary.Add(doc.Name, doc);
            }
            Logging.Log("Finished deserialising");

            Logging.Log();
            Logging.Log(Logging.BannerTitle("Handling page overrides!"));
            HandleOverrides(overrides, ref docsDictionary);

            Directory.CreateDirectory(EntityPageTools.RootDocsFolder);

            Logging.Log();
            Logging.Log(Logging.BannerTitle("Writing out MDX files from classes!"));

            var skippedDocs = 0;
            var wroteDocs = 0;
            var skippedPages = 0;
            var wrotePages = 0;

            // write index for search
            var entityIndex = new List<EntityIndexEntry>();

            foreach ((string docName, EntityDocument doc) in docsDictionary)
            {
                var docPath = Path.Combine(EntityPageTools.RootDocsFolder, $"{doc.Name}.mdx");
                var docText = doc.GetMDXText();

                var wroteFile = WriteFileIfContentsChanged(docPath, docText);

                if (wroteFile)
                {
                    wroteDocs++;
                    Logging.Log($"Wrote document '{docPath}'");
                }
                else
                {
                    skippedDocs++;
                    if (Logging.Verbose)
                    {
                        Logging.Log($"Skipped writing document '{docPath}' because the file contents did not change.");
                    }
                }

                var EntityIndexEntry = new EntityIndexEntry
                {
                    Classname = docName,
                };

                entityIndex.Add(EntityIndexEntry);

                foreach (var page in doc.Pages)
                {
                    if (!string.IsNullOrEmpty(page.Description) && page.Description.Length > EntityIndexEntry.Description.Length)
                    {
                        EntityIndexEntry.Description = SanitizeInputTable(page.Description).Replace("\n", "<br/>");
                    }

                    if (page.Game != null)
                    {
                        EntityIndexEntry.Games.Add(page.Game.FileSystemName);
                    }

                    if (!string.IsNullOrEmpty(page.IconPath))
                    {
                        if (File.Exists(Path.Combine(EntityPageTools.WikiRoot, page.GetImageRelativePath())))
                        {
                            EntityIndexEntry.Icon = page.GetImageRelativePath();
                        }
                        else if (File.Exists(Path.Combine(EntityPageTools.WikiRoot, page.IconPath)))
                        {
                            EntityIndexEntry.Icon = page.IconPath;
                        }

                        if (EntityIndexEntry.Icon.StartsWith("static/"))
                        {
                            EntityIndexEntry.Icon = EntityIndexEntry.Icon.Remove(0, 6);
                        }
                    }

                    var pagePath = Path.Combine(EntityPageTools.RootPagesFolder, page.GetPageRelativePath());
                    Directory.CreateDirectory(Path.GetDirectoryName(pagePath)!);

                    var wrotePage = WriteFileIfContentsChanged(pagePath, page.GetMDXText());

                    if (wrotePage)
                    {
                        wrotePages++;
                        Logging.Log($"\nWrote page '{pagePath}' because file contents changed");
                    }
                    else
                    {
                        skippedPages++;
                        if (Logging.Verbose)
                        {
                            Logging.Log($"Skipped writing page '{pagePath}' because the file contents did not change.");
                        }
                    }
                }
            }

            var entityIndexJsonText = JsonSerializer.Serialize(entityIndex, JsonContext.Default.ListEntityIndexEntry);
            File.WriteAllText(Path.Combine(EntityPageTools.WikiRoot, "static", "fgd_dump", "entityIndex.json"), entityIndexJsonText);


            Logging.Log($"\nWrote '{wroteDocs}' document(s), skipped '{skippedDocs}' document(s) with contents that did not change");
            Logging.Log($"Wrote '{wrotePages}' page(s), skipped '{skippedPages}' page(s) with contents that did not change");
        }

        // the format for overrides filename is 'entityClassname'-'gameFileSystemName'.json or just 'entityClassname'.json
        // if only entityClassname is provided, we treat the override as being global
        // global overrides get processed first, then game specific ones
        public static void HandleOverrides(string[] files, ref Dictionary<string, EntityDocument> docsDictionary)
        {
            List<(string classname, EntityPage)> globalPageOverrides = [];
            List<(string classname, EntityPage)> gameSpecificPageOverrides = [];

            foreach (var file in files)
            {
                if (Path.GetExtension(file) != ".json")
                {
                    continue;
                }

                var fileName = Path.GetFileNameWithoutExtension(file);
                var splitFilename = fileName.Split("-");
                //if (splitFilename.Length > 2)
                //{
                //    throw new InvalidDataException("Invalid override entity filename! correct format is {entityClassname}.json or {entityClassname}-{gameFileSystemName}.json\n");
                //}

                var entityClass = splitFilename[0];
                List<GameFinder.Game> entityGames = [];

                docsDictionary.TryGetValue(entityClass, out EntityDocument? docToOverride);

                if (splitFilename.Length >= 2)
                {
                    for (int i = 1; i < splitFilename.Length; i++)
                    {
                        var gameString = splitFilename[i];
                        var game = GameFinder.GetGameByFileSystemName(gameString);

                        if (game == null)
                        {
                            var error = $"Invalid override entity game '{gameString}'! valid game names are: \n\n";

                            foreach (var gameListGame in GameFinder.GameList)
                            {
                                error += $"{gameListGame.FileSystemName}\n";
                            }

                            error += "\nIn case you meant to make this a global override for all games, simply remove the - at the end, and make the filename be {entityClassname}.json\n";
                            throw new InvalidDataException(error);
                        }

                        entityGames.Add(game);
                    }
                }

                if (docToOverride == null)
                {
                    Logging.Log($"Could not match any entity to '{entityClass}' override, adding as non-FGD entity!");

                    var nonFGDDoc = new EntityDocument { Name = entityClass };
                    docsDictionary.Add(entityClass, nonFGDDoc);

                    // add the same page to all games if no games are specificed
                    if (entityGames.Count == 0)
                    {
                        foreach (var gameDef in GameFinder.GameList)
                        {
                            var overrideEntitypage = EntityPage.GetEntityPageFromJson(file);
                            overrideEntitypage.NonFGD = true;
                            overrideEntitypage.Game = gameDef;
                            overrideEntitypage.Name = entityClass;
                            nonFGDDoc.Pages.Add(overrideEntitypage);
                        }
                    }
                    else
                    {
                        foreach (var gameDef in entityGames)
                        {
                            var overrideEntitypage = EntityPage.GetEntityPageFromJson(file);
                            overrideEntitypage.NonFGD = true;
                            overrideEntitypage.Game = gameDef;
                            overrideEntitypage.Name = entityClass;
                            nonFGDDoc.Pages.Add(overrideEntitypage);
                        }
                    }

                    continue;
                }

                if (entityGames.Count == 0)
                {
                    var overrideEntitypage = EntityPage.GetEntityPageFromJson(file);

                    globalPageOverrides.Add((entityClass, overrideEntitypage));
                }
                else
                {
                    foreach (var page in docToOverride.Pages)
                    {
                        foreach (var game in entityGames)
                        {
                            if (page.Game == game)
                            {
                                var overrideEntitypage = EntityPage.GetEntityPageFromJson(file);

                                overrideEntitypage!.Game = game;
                                gameSpecificPageOverrides.Add((entityClass, overrideEntitypage!));
                            }
                        }
                    }
                }
            }

            Logging.Log($"\nLoaded '{globalPageOverrides.Count}' global page override(s).");
            foreach ((string globalOverrideClassname, EntityPage globalOverride) in globalPageOverrides)
            {
                docsDictionary.TryGetValue(globalOverrideClassname, out var doc);

                foreach (var page in doc!.Pages)
                {
                    Logging.Log($"Overriding page '{page.Name}' from game '{page.Game!.FileSystemName}'");
                    page.OverrideFrom(globalOverride);
                }
            }

            Logging.Log($"\nLoaded '{gameSpecificPageOverrides.Count}' game specific page override(s).");
            foreach ((string gameSpecificOverrideClassname, EntityPage gameSpecificOverride) in gameSpecificPageOverrides)
            {
                docsDictionary.TryGetValue(gameSpecificOverrideClassname, out var doc);

                foreach (var page in doc!.Pages)
                {
                    if (page.Game == gameSpecificOverride.Game)
                    {
                        Logging.Log($"Overriding page '{page.Name}' from game '{page.Game!.FileSystemName}'");
                        page.OverrideFrom(gameSpecificOverride);
                    }
                }
            }
        }

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
                Logging.Log($"Name: '{game.Name}' | FileSystemName: '{game.FileSystemName}' | GameFolder: '{game.GameFolder}' | GameInfoFolder: '{game.PathToGameinfo}'");
                Logging.LogS("FGDs to read:");
                foreach (var fgd in game.FgdFilesNames)
                {
                    Logging.LogS($" {fgd}");
                }
                Logging.Log("\n");
            }

            Logging.Log(Logging.BannerTitle(string.Empty, 100));

            foreach (GameFinder.Game game in gamesList)
            {
                Logging.Log();
                Logging.Log(Logging.BannerTitle($"Processing game '{game.Name}'"));
                Logging.Log();

                var gamePath = GameFinder.GetSystemPathForGame(game);

                if (string.IsNullOrEmpty(gamePath))
                {
                    Logging.Log($"Failed to find game '{game.Name}' on this machine! skipping dumping for this game.", ConsoleColor.Red);
                    Logging.Log();
                    continue;
                }

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

                foreach (var page in doc.Pages)
                {
                    if (!string.IsNullOrEmpty(page.IconPath))
                    {
                        if (Logging.Verbose)
                        {
                            Logging.Log($"\nPage has entity icon path '{page.IconPath}' , attempting to dump icon image:");
                        }
                        string iconPath = string.Empty;
                        if (page.IconPath.Contains("materials/"))
                        {
                            iconPath = page.IconPath;
                        }
                        else
                        {
                            iconPath = $"materials/{page.IconPath}";
                        }

                        if (!page.IconPath.Contains(".vmat"))
                        {
                            iconPath += ".vmat";
                        }

                        var entityIconVmatResource = page.Game!.LoadVPKResourceCompiled(iconPath);

                        if (entityIconVmatResource?.DataBlock != null)
                        {
                            var iconMaterial = (Material)entityIconVmatResource.DataBlock;
                            var iconTexturePath = GetMaterialColorTexture(iconMaterial);

                            if (string.IsNullOrEmpty(iconTexturePath))
                            {
                                throw new InvalidDataException("Failed to get color texture for entity material!");
                            }

                            var iconTexture = page.Game.LoadVPKResourceCompiled(iconTexturePath);

                            if (Logging.Verbose)
                            {
                                Logging.Log($"Read '{iconTexture!.FileName}', extracting:");
                            }

                            Directory.CreateDirectory(Path.Combine(EntityPageTools.WikiRoot, page.GetImageRelativeFolder()));
                            var finalIconPath = Path.Combine(EntityPageTools.WikiRoot, page.IconPath);
                            SavePNGFromTextureResource(iconTexture!, finalIconPath);

                            if (Logging.Verbose)
                            {
                                Logging.Log($"Saved icon texture to '{finalIconPath}'!");
                            }
                        }
                        else
                        {
                            if (Logging.Verbose)
                            {
                                Logging.Log($"Failed to load entity icon material '{iconPath}'", ConsoleColor.Red);
                            }
                        }
                    }
                }

                var jsonText = JsonSerializer.Serialize(doc, JsonContext.Default.EntityDocument);
                File.WriteAllText(docPath, jsonText);

                if (Logging.Verbose)
                {
                    Logging.Log($"\nSaved document JSON to {docPath}!");
                }
            }

            var timestamp = (long)DateTime.UtcNow.Subtract(DateTime.UnixEpoch).TotalSeconds;
            File.WriteAllText(Path.Combine(EntityPageTools.RootDumpFolder, "timestamp.json"), timestamp.ToString(CultureInfo.InvariantCulture));

            Logging.Log($"\nProcessed and exported {pagesDictionary.Count} documents!");
        }

        private static string? GetMaterialColorTexture(Material material)
        {
            foreach (var textureParam in material.TextureParams)
            {
                if (textureParam.Key == "g_tColor")
                {
                    return textureParam.Value;
                }

                if (textureParam.Key == "g_tColorA")
                {
                    return textureParam.Value;
                }

                if (textureParam.Key == "g_tColorB")
                {
                    return textureParam.Value;
                }

                if (textureParam.Key == "g_tColorC")
                {
                    return textureParam.Value;
                }
            }

            return string.Empty;
        }

        private static bool WriteFileIfContentsChanged(string path, string? contents)
        {
            if (File.Exists(path))
            {
                var oldFileText = File.ReadAllText(path);
                if (oldFileText == contents)
                {
                    return false;
                }
            }

            File.WriteAllText(path, contents);
            return true;
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

            using var stream = File.OpenWrite(pathToSaveTo);
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
