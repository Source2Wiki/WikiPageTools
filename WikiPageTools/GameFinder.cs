using System.Text.RegularExpressions;
using EntityPageTools;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;

namespace FGDDumper
{
    public static class GameFinder
    {
        private const string GameInfo = "gameinfo.gi";

        // could read name from gameinfo but not going to bother with all that when adding it here manually is trivial
        // file system name is just a file system safe name to use when writing out the files for the pages
        public class Game
        {
            public string Name { get; init; }
            public string FileSystemName { get; init; }
            public int AppId { get; init; }

            /// <summary>Folder holding the game's content, relative to the steam install folder of <see cref="AppId"/>.</summary>
            public string GameFolder { get; init; }
            public string PathToGameinfo { get; init; }
            public string[] FgdFilesNames { get; init; }

            private List<GameFileLoader> GameFileLoaders = [];
            private bool CachedGameFileLoaders = false;

            // empty means we looked and the game is not installed, null means we have not looked yet
            private string? CachedSystemPath;

            /// <summary>
            /// Content folder of this game on this machine, or an empty string when it is not installed.
            /// The steam install folder is resolved by app id, so a renamed or relocated library is fine.
            /// </summary>
            public string GetSystemPath()
            {
                if (CachedSystemPath != null)
                {
                    return CachedSystemPath;
                }

                CachedSystemPath = string.Empty;

                var steamGame = GameFolderLocator.FindSteamGameByAppId(AppId);

                if (steamGame is null)
                {
                    return CachedSystemPath;
                }

                var gamePath = Path.Combine(steamGame.Value.GamePath, GameFolder);

                // an installed app is not necessarily a usable one, the game content can be a separate download
                if (File.Exists(Path.Combine(gamePath, PathToGameinfo, GameInfo)))
                {
                    CachedSystemPath = gamePath;
                }

                return CachedSystemPath;
            }

            public void CacheVPKContent()
            {
                var systemPath = GetSystemPath();

                if (string.IsNullOrEmpty(systemPath))
                {
                    return;
                }

                if (!CachedGameFileLoaders)
                {
                    CachedGameFileLoaders = true;

                    var gameinfoPath = Path.Combine(systemPath, PathToGameinfo, GameInfo);
                    var gameEntries = ExtractGameEntries(gameinfoPath);

                    foreach (var game in gameEntries)
                    {
                        var package = new Package();
                        package.Read(Path.Combine(systemPath, game, "pak01_dir.vpk"));
                        GameFileLoaders.Add(new GameFileLoader(package, package.FileName));
                    }
                }

            }

            public Stream? LoadVPKFile(string filePath)
            {
                CacheVPKContent();

                foreach (var loader in GameFileLoaders)
                {
                    var stream = loader.GetFileStream(filePath);

                    if (stream != null)
                    {
                        return stream;
                    }
                }

                return null;
            }

            public List<Resource> GetResourcesByType(string fileType)
            {
                CacheVPKContent();

                List<Resource> materials = [];

                foreach (var loader in GameFileLoaders)
                {
                    if (loader.CurrentPackage?.Entries == null)
                    {
                        continue;
                    }

                    foreach (var entry in loader.CurrentPackage.Entries)
                    {
                        if (entry.Key == fileType)
                        {
                            foreach (var packageEntry in entry.Value)
                            {
                                var material = new Resource();
                                material.Read(loader.CurrentPackage.GetMemoryMappedStreamIfPossible(packageEntry));
                                materials.Add(material);
                            }
                        }
                    }
                }

                return materials;
            }

            public Resource? LoadVPKResourceCompiled(string filePath)
            {
                CacheVPKContent();

                foreach (var loader in GameFileLoaders)
                {
                    var resource = loader.LoadFile($"{filePath}_c");

                    if (resource != null)
                    {
                        return resource;
                    }
                }

                return null;
            }

            public static List<string> ExtractGameEntries(string filePath)
            {
                var gameEntries = new List<string>();

                try
                {
                    if (!File.Exists(filePath))
                    {
                        throw new FileNotFoundException($"File not found: {filePath}");
                    }

                    string[] lines = File.ReadAllLines(filePath);

                    foreach (string line in lines)
                    {
                        string trimmedLine = line.Trim();

                        // Skip empty lines and comments
                        if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("//"))
                            continue;

                        // Use regex to match lines that start with "Game" but not "Game_"
                        // This ensures we get "Game" entries but exclude "Game_LowViolence", "Game_Something", etc.
                        if (Regex.IsMatch(trimmedLine, @"^Game\s+"))
                        {
                            // Extract the value after "Game"
                            string[] parts = trimmedLine.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2)
                            {
                                gameEntries.Add(parts[1]);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Error reading file: {ex.Message}", ex);
                }

                return gameEntries;
            }

            public Game(string name, string fileSystemName, int appId, string gameFolder, string pathToGameinfo, string[] fgdFilesNames)
            {
                Name = name;
                FileSystemName = fileSystemName;
                AppId = appId;
                GameFolder = gameFolder;
                PathToGameinfo = pathToGameinfo;
                FgdFilesNames = fgdFilesNames;
            }
        }

        public static readonly List<Game> GameList = new()
        {
            new Game("Counter-Strike 2", "cs2", 730, "game", "csgo", ["csgo.fgd"]),
            new Game("Half-Life: Alyx", "hla", 546560, "game", "hlvr", ["hlvr.fgd"]),
            new Game("Dota 2", "dota2", 570, "game", "dota", ["dota.fgd"]),
            new Game("SteamVR Home", "steamvr", 250820, "tools\\steamvr_environments\\game", "steamtours", ["steamtours.fgd"]),
        };

        public static Game? GetGameByFileSystemName(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            foreach (var game in GameList)
            {
                if (game.FileSystemName == name)
                {
                    return game;
                }
            }

            return null;
        }

        public static string GetValidGames()
        {
            var outputString = "Valid games:\n\n";
            foreach (var game in GameList)
            {
                outputString += $"- {game.FileSystemName}\n";
            }

            return outputString;
        }
    }
}
