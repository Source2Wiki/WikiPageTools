using System.Text.Json;
using EntityPageTools;
using FGDDumper;
using ValveResourceFormat.ResourceTypes;

namespace WikiPageTools;

public static class ToolTexturesDumper
{
    public class ToolMaterialDump
    {
        public long Timestamp { get; set; }
        public List<ToolMaterial> ToolMaterials { get; set; } = [];

        public ToolMaterialDump()
        {
            Timestamp = (long)DateTime.UtcNow.Subtract(DateTime.UnixEpoch).TotalSeconds;
        }
    }

    public class ToolMaterial
    {
        public string Name { get; set; } = string.Empty;
        public string TexturePath { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<ToolMaterialAttribute> Attributes { get; set; } = [];
    }

    public class ToolMaterialAttribute
    {
        public string Name { get; set; } = string.Empty;
        public float Value { get; set; }
    }

    public static void DumpToolTexturesToJsonForAllGames()
    {
        Logging.Log();
        Logging.Log(Logging.BannerTitle("Dumping tool textures to JSON for all games!"));
        Logging.Log();

        foreach (var game in GameFinder.GameList)
        {
            DumpGameToolTexturesToJson(game);
        }
    }

    public static void DumpGameToolTexturesToJson(GameFinder.Game game)
    {
        Logging.Log();
        Logging.Log(Logging.BannerTitle($"Dumping tool textures to JSON for game: `{game.Name}`"));
        Logging.Log();

        var ToolMaterialDump = new ToolMaterialDump()
        {
            ToolMaterials = FindGameToolMaterials(game)
        };

        ToolMaterialDump.ToolMaterials.Sort((x, y) => x.Name.CompareTo(y.Name));

        var json = JsonSerializer.Serialize(ToolMaterialDump, JsonContext.Default.ToolMaterialDump);

        var path = Path.Combine(FGDDumper.EntityPageTools.WikiRoot, FGDDumper.EntityPageTools.ToolTextureDumpFolder);
        var file = $"tooltexdump_{game.FileSystemName}.json";
        Directory.CreateDirectory(path);

        File.WriteAllText(Path.Combine(path, file), json);
    }

    public static string ExtractTextureName(string input)
    {
        input = input.Replace(".vtex", "");

        int lastSlashIndex = input.LastIndexOf('/');
        if (lastSlashIndex == -1)
            return input;

        return input.Substring(lastSlashIndex + 1);
    }

    public static List<ToolMaterial> FindGameToolMaterials(GameFinder.Game game)
    {
        Logging.Log();
        Logging.Log(Logging.BannerTitle("Finding tools textures"));
        Logging.Log();

        List<ToolMaterial> returnMaterials = [];

        var vmatEntries = game.GetResourcesByType("vmat_c");

        if (vmatEntries.Count == 0)
        {
            return returnMaterials;
        }

        Logging.Log();
        Logging.Log($"Searching `{vmatEntries.Count}` materials...");
        Logging.Log();

        foreach (var vmat in vmatEntries)
        {
            var material = (Material)vmat.DataBlock!;

            var attributes = new Dictionary<string, float>();

            foreach (var (key, value) in material.FloatAttributes)
            {
                attributes.Add(key, value);
            }

            foreach (var (key, value) in material.IntAttributes)
            {
                // not defined by user, so skip it
                if (key.Equals("representativetexturewidth", StringComparison.OrdinalIgnoreCase)
                || key.Equals("representativetextureheight", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Skip `int` definition if there is a `float` definition
                if (!attributes.ContainsKey(key))
                {
                    attributes.Add(key, value);
                }
            }

            attributes.TryGetValue("tools.toolsmaterial", out var isToolTexture);

            if (isToolTexture == 1)
            {
                Logging.Log($"Found tool material `{material.Name}`");

                var attribs = attributes.ToList()
                .Where((KeyValuePair<string, float> kv) => kv.Key != "tools.toolsmaterial")
                .Select(kv => new ToolMaterialAttribute { Name = kv.Key, Value = kv.Value })
                .ToList();

                var texturePath = "";

                material.TextureParams.TryGetValue("g_tColor", out texturePath);

                if (string.IsNullOrEmpty(texturePath))
                {
                    Logging.Log($"  Tool material has no texture, skipping..`");
                    Logging.Log();
                    continue;
                }

                var toolMaterial = new ToolMaterial
                {
                    Name = material.Name,
                    Attributes = attribs
                };

                var iconTexture = game.LoadVPKResourceCompiled($"{texturePath}_c");

                if (iconTexture == null)
                {
                    Logging.Log($"  Failed to load tool material texture, skipping..`");
                    Logging.Log();
                    continue;
                }

                var imgPath = Path.Combine(FGDDumper.EntityPageTools.ToolTextureImageDumpFolder, game.FileSystemName);

                var rootImgPath = Path.Combine(FGDDumper.EntityPageTools.WikiRoot, FGDDumper.EntityPageTools.ToolTextureImageDumpFolder, game.FileSystemName);
                Directory.CreateDirectory(rootImgPath);

                var textureName = ExtractTextureName(texturePath);

                var finalRootIconPath = $"{Path.Combine(rootImgPath, textureName)}.png";
                var finalIconPath = $"{Path.Combine(imgPath, textureName)}.png";
                WikiFilesGenerator.SavePNGFromTextureResource(iconTexture!, finalRootIconPath);
                Logging.Log($"  Exported tool texture image {textureName} to {finalRootIconPath}`");
                Logging.Log();

                toolMaterial.TexturePath = finalIconPath;
                returnMaterials.Add(toolMaterial);
            }

        }

        Logging.Log($"Found `{returnMaterials.Count}` tool materials.");
        Logging.Log(Logging.BannerTitle(""));

        return returnMaterials;
    }
}
