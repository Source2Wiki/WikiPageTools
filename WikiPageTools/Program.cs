
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using ConsoleAppFramework;
using EntityPageTools;
using WikiPageTools;

namespace FGDDumper
{
    public static class EntityPageTools
    {
        private const string Version = "2.1.0";

        public static string WikiRoot { get; private set; } = string.Empty;

        public const string DumpFolder = "fgd_dump";
        public static string RootDumpFolder { get; private set; } = string.Empty;

        public const string ConDumpFolder = "con_dump";

        public const string ToolTextureDumpFolder = "tooltex_dump";
        public const string ToolTextureImageDumpFolder = "static/tooltex_dump/img";

        public static void Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

            // https://github.com/Cysharp/ConsoleAppFramework
            ConsoleApp.Version = GetVersion();

            // Go to definition on this method to see the generated source code
            ConsoleApp.Run(args, Run);
        }

        private static string GetVersion()
        {
            var info = new StringBuilder();
            info.Append($"Version: {Version}");
            info.AppendLine($"OS: {RuntimeInformation.OSDescription}");
            return info.ToString();
        }

        /// <summary>
        /// Dumps game data into the JSON the Source2 Wiki builds its pages out of. Reading the FGDs
        /// and unpacking icons needs the games installed, which is why the dumps are checked into
        /// the wiki. Turning them into pages is the wiki's own job and lives in its \tools folder.
        /// </summary>
        /// <param name="root">Folder path for the root of the docusaurus project.</param>
        /// <param name="dump_fgd">Attempts to find all source2 games on the system and generate json dumps of their FGDs,
        /// the dumps get saved into \fgd_dump, which is what the wiki generates its entity pages from.</param>
        /// <param name="verbose">Enables extra logging which might otherwise be too annoying.</param>
        /// <param name="entity_list_to_json">converts a console var/command dump from the `cvarlist` command into a json file</param>
        /// <param name="game">converts a console var/command dump from the `cvarlist` command into a json file</param>
        /// <param name="dump_tool_tex">Dumps tool textures for all games as json,
        public static int Run(
            string root,
            bool dump_fgd,
            bool verbose,
            bool dump_tool_tex,
            string? game = "",
            string? entity_list_to_json = ""
            )
        {
            if (string.IsNullOrEmpty(root))
            {
                Logging.Log("Docs output path can't be empty");
                return 1;
            }

            if (File.Exists(root))
            {
                Logging.Log("Docs output path can't be a file, it must be a folder");
                return 1;
            }

            if (!File.Exists(Path.Combine(root, "docusaurus.config.ts")))
            {
                Logging.Log($"Selected folder is not a docusaurus project, this should be the folder containing the docusaurus.config.ts file.");
                return 1;
            }

            if (!dump_fgd && !dump_tool_tex && string.IsNullOrEmpty(entity_list_to_json))
            {
                Logging.Log("At least one mode argument must be provided!");
                return 1;
            }

            if (verbose)
            {
                Logging.Verbose = true;
            }

            WikiRoot = root;

            RootDumpFolder = Path.Combine(WikiRoot, DumpFolder);

            Logging.Log($"Wiki Page Tools, Version {Version}.");
            Logging.Log("Starting...");

            if (!string.IsNullOrEmpty(entity_list_to_json))
            {
                if (string.IsNullOrEmpty(game))
                {
                    Logging.Log("--entity_list_to_json needs `--game` param", ConsoleColor.Red);
                    Logging.Log(GameFinder.GetValidGames());
                    return 1;
                }

                var gameClass = GameFinder.GetGameByFileSystemName(game);

                if (gameClass == null)
                {
                    Logging.Log("\n--game is invalid", ConsoleColor.Red);
                    Logging.Log(GameFinder.GetValidGames());
                    return 1;
                }

                var json = ConvarListToJson.ToJson(entity_list_to_json, gameClass);
                var path = Path.Combine(WikiRoot, ConDumpFolder);
                var file = $"condump_{gameClass.FileSystemName}.json";
                Directory.CreateDirectory(path);

                File.WriteAllText(Path.Combine(path, file), json);
                Logging.Log($"\nWrote condump {file} to {path}");

                return 0;
            }


            if (dump_fgd)
            {
                GameDataDumper.DumpFGD();
            }

            if (dump_tool_tex)
            {
                ToolTexturesDumper.DumpToolTexturesToJsonForAllGames();
            }

            return 0;
        }
    }
}
