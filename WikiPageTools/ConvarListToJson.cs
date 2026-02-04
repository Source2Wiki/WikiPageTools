using System.Text;
using System.Text.Json;
using FGDDumper;
using ValveResourceFormat.Serialization.KeyValues;

namespace EntityPageTools;

public static class ConvarListToJson
{
    public class ConDump
    {
        public long Timestamp { get; set; }
        public List<ConEntry> Entries { get; set; } = [];

        public ConDump()
        {
            Timestamp = (long)DateTime.UtcNow.Subtract(DateTime.UnixEpoch).TotalSeconds;
        }
    }

    public class ConEntry
    {
        public required string Name { get; set; }
        public string DefaultValue { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string[] flags { get; set; } = [];
        public bool Cs2WorkshopWhitelisted { get; set; } = false;
    }

    public static string? ToJson(string file, GameFinder.Game game)
    {
        var conDump = new ConDump();

        if (!File.Exists(file))
        {
            return null;
        }

        KV3File? whitelistKV3 = null;
        if (game.FileSystemName == "cs2")
        {
            var whitelistStream = game.LoadVPKFile("scripts/workshop_cvar_whitelist.txt");
            whitelistKV3 = KeyValues3.ParseKVFile(whitelistStream!);
        }

        string[] allLines = File.ReadAllLines(file);

        foreach (var line in allLines)
        {
            var splitLine = line.Split(" : ");

            if (splitLine.Length != 4)
            {
                continue;
            }

            string[] flags = splitLine[2].Split(", ");

            for (int i = 0; i < flags.Count(); i++)
            {
                flags[i] = WikiFilesGenerator.SanitizeInputTable(flags[i].Trim());
            }

            var conEntry = new ConEntry
            {
                Name = WikiFilesGenerator.SanitizeInputTable(splitLine[0].Trim()),
                DefaultValue = WikiFilesGenerator.SanitizeInputTable(splitLine[1].Trim()),
                flags = flags,
                Description = WikiFilesGenerator.SanitizeInputTable(splitLine[3].Trim())
            };

            if (whitelistKV3 != null && whitelistKV3.Root.GetArray<string>("whitelist_cvars").Contains(conEntry.Name))
            {
                conEntry.Cs2WorkshopWhitelisted = true;
            }

            conDump.Entries.Add(conEntry);
        }

        return JsonSerializer.Serialize(conDump, JsonContext.Default.ConDump);
    }
}
