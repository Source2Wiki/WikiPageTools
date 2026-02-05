using System.Text.RegularExpressions;
using EntityPageTools;
using Microsoft.Win32;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;

namespace FGDDumper
{
    public static class GameFinder
    {
        public static string? SteamPath { get; }
        public static List<string> SteamLibraryPaths { get; } = [];

        static GameFinder()
        {
            try
            {
                SteamPath = GetSteamInstallPath()!;
            }
            catch (Exception)
            {
            }

            if (string.IsNullOrEmpty(SteamPath))
            {
                Logging.Log("Failed to find Steam on this machine! dumping FGD is disabled.", ConsoleColor.Red);
                return;
            }

            Logging.Log();
            Logging.Log($"Steam path: {SteamPath}");
            Logging.Log();
            Logging.Log("Getting steam libraries!");
            Logging.Log();

            SteamLibraryPaths = GetSteamLibraries(SteamPath);

            Logging.Log("Steam libraries:");

            foreach (var lib in SteamLibraryPaths)
            {
                Logging.Log(lib);
            }

        }

        private const string GameInfo = "gameinfo.gi";

        // could read name from gameinfo but not going to bother with all that when adding it here manually is trivial
        // file system name is just a file system safe name to use when writing out the files for the pages
        public class Game
        {
            public string Name { get; init; }
            public string FileSystemName { get; init; }
            public string GameFolder { get; init; }
            public string PathToGameinfo { get; init; }
            public string[] FgdFilesNames { get; init; }

            private List<GameFileLoader> GameFileLoaders = [];
            private bool CachedGameFileLoaders = false;

            public void CacheVPKContent()
            {
                if (string.IsNullOrEmpty(GetSystemPathForGame(this)))
                {
                    return;
                }

                if (!CachedGameFileLoaders)
                {
                    CachedGameFileLoaders = true;

                    var gameinfoPath = Path.Combine(GetSystemPathForGame(this)!, PathToGameinfo, "gameinfo.gi");
                    var gameEntries = ExtractGameEntries(gameinfoPath);

                    foreach (var game in gameEntries)
                    {
                        var package = new Package();
                        package.Read(Path.Combine(GetSystemPathForGame(this)!, game, "pak01_dir.vpk"));
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
                    var resource = loader.LoadFile(filePath);

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

            public Game(string name, string fileSystemName, string gameFolder, string pathToGameinfo, string[] fgdFilesNames)
            {
                Name = name;
                FileSystemName = fileSystemName;
                GameFolder = gameFolder;
                PathToGameinfo = pathToGameinfo;
                FgdFilesNames = fgdFilesNames;
            }
        }

        public static readonly List<Game> GameList = new()
        {
            new Game("Counter-Strike 2", "cs2", "Counter-Strike Global Offensive\\game", "csgo", ["csgo.fgd"]),
            new Game("Half-Life: Alyx", "hla", "Half-Life Alyx\\game", "hlvr", ["hlvr.fgd"]),
            new Game("Dota 2", "dota2", "dota 2 beta\\game", "dota", ["dota.fgd"]),
            new Game("SteamVR Home", "steamvr", "SteamVR\\tools\\steamvr_environments\\game", "steamtours", ["steamtours.fgd"]),
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

        private static string? GetSteamInstallPath()
        {
            string? steamPath = null;
            using (RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Wow6432Node\Valve\Steam"))
            {
                if (key != null)
                {
                    steamPath = key.GetValue("InstallPath") as string;
                }
            }

            return steamPath;
        }

        private static List<string> GetSteamLibraries(string steamPath)
        {
            List<string> libraries = new List<string>();
            string vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");

            if (File.Exists(vdfPath))
            {
                string[] lines = File.ReadAllLines(vdfPath);
                Regex regex = new Regex(@"\""path\""\s+\""(.+?)\""");

                foreach (string line in lines)
                {
                    Match match = regex.Match(line);
                    if (match.Success)
                    {
                        string libraryPath = match.Groups[1].Value.Replace(@"\\", @"\");
                        libraries.Add(libraryPath);
                    }
                }
            }

            return libraries;
        }

        public static string? GetSystemPathForGame(Game game)
        {
            foreach (string steamLibraryPath in SteamLibraryPaths)
            {
                string gamePath = Path.Combine(steamLibraryPath, "steamapps", "common", game.GameFolder);
                if (Path.Exists(gamePath))
                {
                    if (File.Exists(Path.Combine(gamePath, game.PathToGameinfo, GameInfo)))
                    {
                        return gamePath;
                    }
                }
            }

            return string.Empty;
        }
    }
}
