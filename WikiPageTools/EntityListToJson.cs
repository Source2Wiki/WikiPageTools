using System.Text.Json;
using FGDDumper;

namespace EntityPageTools;

public static class EntityListToJson
{
    public class ConEntry
    {
        public required string Name { get; set; }
        public string DefaultValue { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string[] flags { get; set; } = [];
    }

    public static string? ToJson(string file)
    {
        var entries = new List<ConEntry>();

        if (!File.Exists(file))
        {
            return null;
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

            entries.Add(conEntry);
        }

        return JsonSerializer.Serialize(entries, JsonContext.Default.ListConEntry);
    }
}
