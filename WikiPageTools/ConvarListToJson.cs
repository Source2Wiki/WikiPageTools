using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FGDDumper;
using ValveKeyValue;
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

        ValveKeyValue.KVDocument? whitelistKV3 = null;
        if (game.FileSystemName == "cs2")
        {
            var whitelistStream = game.LoadVPKFile("scripts/workshop_cvar_whitelist.txt");
            whitelistKV3 = KVSerializer.Create(KVSerializationFormat.KeyValues3Text).Deserialize(whitelistStream!);
        }

        string[] allLines = File.ReadAllLines(file);
        var blanked = new List<string>();

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
                flags[i] = GameDataDumper.SanitizeInputTable(flags[i].Trim());
            }

            var conEntry = new ConEntry
            {
                Name = GameDataDumper.SanitizeInputTable(splitLine[0].Trim()),
                DefaultValue = GameDataDumper.SanitizeInputTable(splitLine[1].Trim()),
                flags = flags,
                Description = GameDataDumper.SanitizeInputTable(splitLine[3].Trim())
            };

            if (whitelistKV3 != null && whitelistKV3.Root.GetArray<string>("whitelist_cvars").Contains(conEntry.Name))
            {
                conEntry.Cs2WorkshopWhitelisted = true;
            }

            if (IsSensitive(conEntry))
            {
                conEntry.DefaultValue = string.Empty;
                blanked.Add(conEntry.Name);
            }

            conDump.Entries.Add(conEntry);
        }

        if (blanked.Count > 0)
        {
            Logging.Log($"Blanked {blanked.Count} sensitive value(s): {string.Join(", ", blanked)}");
        }

        return JsonSerializer.Serialize(conDump, JsonContext.Default.ConDump);
    }

    // `cvarlist` prints each convar's current value, not its default, so the dump would otherwise
    // publish whatever the person running it has set, passwords and their player name included.

    // Flags the engine puts on values it keeps from being queried or recorded, like server passwords.
    private static readonly string[] SensitiveFlags = ["prot", "server_cant_query"];

    private static readonly Regex SensitiveNamePattern = new(
        "password|token_secret|encryptdata_key|decryptdata_key|_pkey$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Values that identify the person or machine that made the dump.
    private static readonly HashSet<string> SensitiveNames =
    [
        "name",
        "hostname",
        "soundsystem_device_used",
        "cl_promoted_settings_acknowledged",
        "ui_news_last_read_link",
        "ui_playsettings_maps_workshop",
    ];

    private static bool IsSensitive(ConEntry entry)
    {
        // commands have no value to leak, and a boolean can't hold a secret
        if (entry.DefaultValue is "cmd" or "true" or "false")
        {
            return false;
        }

        return entry.flags.Any(SensitiveFlags.Contains)
            || SensitiveNames.Contains(entry.Name)
            || SensitiveNamePattern.IsMatch(entry.Name);
    }
}
