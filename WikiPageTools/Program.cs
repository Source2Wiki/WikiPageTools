
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using ConsoleAppFramework;
using EntityPageTools;
using FileWatcherEx;

namespace FGDDumper
{
    public static class EntityPageTools
    {
        private const string Version = "1.2.5";

        public static string WikiRoot { get; private set; } = string.Empty;

        public const string DocsFolder = "docs/Entities";
        public static string RootDocsFolder { get; private set; } = string.Empty;

        public const string PagesFolder = "src/pages/Entities";
        public static string RootPagesFolder { get; private set; } = string.Empty;

        public const string DumpFolder = "fgd_dump";
        public static string RootDumpFolder { get; private set; } = string.Empty;

        public const string ConDumpFolder = "con_dump";

        public const string OverridesFolder = "fgd_dump_overrides";
        public static string RootOverridesFolder { get; private set; } = string.Empty;

        public static void Main(string[] args)
        {

#if DEBUG
            //test args
            args = ["--root", "E:/Dev/Source2Wiki", "--dump_fgd", "--verbose"];
#endif
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
        /// An automatic entity documentation page generator for the Source2 Wiki.
        /// </summary>
        /// <param name="root">Folder path for the root of the docusaurus project.</param>
        /// <param name="generate_mdx">Generates the wiki files from the json in \fgd_dump, takes into account the manual overrides from \fgd_dump_overrides.</param>
        /// <param name="dump_fgd">Attempts to find all source2 games on the system and generate json dumps of their FGDs, 
        /// the dumps get saved into \fgd_dump, you usually want to run this program with --generate_mdx after
        /// to generate the actual wiki pages.</param>
        /// <param name="verbose">Enables extra logging which might otherwise be too annoying.</param>
        /// <param name="no_listen">Disables listening for file changes after generate_mdx and quits after first generation.</param>
        /// <param name="cs_script_tablegen">converts point_script.d.ts into an mdx table</param>
        /// <param name="entity_list_to_json">converts a console var/command dump from the `cvarlist` command into a json file</param>
        /// <param name="game">converts a console var/command dump from the `cvarlist` command into a json file</param>
        public static int Run(
            string root,
            bool generate_mdx,
            bool dump_fgd,
            bool verbose,
            bool no_listen,
            string? game = "",
            string? entity_list_to_json = "",
            string? cs_script_tablegen = "")
        {
            //omega stupid parser built in 15 minutes because im lazy
            if (!string.IsNullOrEmpty(cs_script_tablegen))
            {
                if (!File.Exists(cs_script_tablegen))
                {
                    return 1;
                }

                string[] allLines = File.ReadAllLines(cs_script_tablegen);

                string funcDescription = "";
                string funcSignature = "";
                string funcName = "";

                string generatedTable = "";

                foreach (var line in allLines)
                {
                    var trimmedLine = line.Trim();

                    // find one line comments above function signatures
                    // example: /** Log a message to the console. */
                    var startIndex = trimmedLine.IndexOf("/**");
                    var endIndex = trimmedLine.IndexOf("*/");

                    if (startIndex != -1 && endIndex != -1)
                    {
                        // need to offset as it gives the start of the string
                        var offsetStartIndex = startIndex + 4;
                        funcDescription = trimmedLine.Substring(offsetStartIndex, endIndex - offsetStartIndex);
                    }

                    // find the function declaration
                    if (trimmedLine.Contains("("))
                    {
                        funcSignature = trimmedLine;
                        funcName = trimmedLine.Substring(0, trimmedLine.IndexOf("("));

                        generatedTable += $"|{WikiFilesGenerator.SanitizeInputTable(funcName)}|{WikiFilesGenerator.SanitizeInputTable(funcSignature)}|{WikiFilesGenerator.SanitizeInputTable(funcDescription)}|\n";

                        funcDescription = "";
                        funcSignature = "";
                        funcName = "";
                    }
                }

                File.WriteAllText(Path.Combine(Path.GetDirectoryName(cs_script_tablegen)!, "cs_script_doc_output.txt"), generatedTable);
            }

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

            if (!dump_fgd && !generate_mdx && string.IsNullOrEmpty(entity_list_to_json))
            {
                Logging.Log("At least one mode argument must be provided!");
                return 1;
            }

            if (verbose)
            {
                Logging.Verbose = true;
            }

            WikiRoot = root;

            RootDocsFolder = Path.Combine(WikiRoot, DocsFolder);
            RootPagesFolder = Path.Combine(WikiRoot, PagesFolder);
            RootDumpFolder = Path.Combine(WikiRoot, DumpFolder);
            RootOverridesFolder = Path.Combine(WikiRoot, OverridesFolder);

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

                var json = ConvarListToJson.ToJson(entity_list_to_json);
                var path = Path.Combine(WikiRoot, ConDumpFolder);
                var file = $"condump_{gameClass.FileSystemName}.json";
                Directory.CreateDirectory(path);

                File.WriteAllText(Path.Combine(path, file), json);
                Logging.Log($"\nWrote condump {file} to {path}");

                return 0;
            }


            if (dump_fgd)
            {
                WikiFilesGenerator.DumpFGD();
            }

            if (generate_mdx)
            {
                try
                {
                    WikiFilesGenerator.GenerateMDXFromJSONDump();
                }
                catch (Exception ex)
                {
                    Logging.Log($"\nFailed to update MDX files, error: \n{ex.Message}");
                }

                if (!no_listen)
                {
                    var fileWatcher = new FileSystemWatcherEx(RootOverridesFolder);

                    Logging.Log($"\nWatching for file changes in '{Path.Combine(RootOverridesFolder)}'");
                    fileWatcher.OnChanged += UpdateMDX;
                    fileWatcher.OnCreated += UpdateMDX;
                    fileWatcher.OnRenamed += UpdateMDX;

                    fileWatcher.Start();

                    while (Console.ReadKey().KeyChar != 'q')
                    {
                    }
                }
            }

            return 0;
        }

        private static void UpdateMDX(object? sender, FileChangedEvent e)
        {
            Logging.Log($"\nFile '{e.FullPath}' changed, updating MDX.");
            try
            {
                WikiFilesGenerator.GenerateMDXFromJSONDump();
                Logging.Log($"\nWatching for file changes in '{Path.Combine(RootOverridesFolder)}'");
            }
            catch (Exception ex)
            {
                Logging.Log($"\nFailed to live update {e.FullPath}, error: \n{ex.Message}");
            }
        }
    }
}


