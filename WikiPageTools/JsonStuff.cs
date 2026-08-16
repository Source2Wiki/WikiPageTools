using System.Text.Json;
using System.Text.Json.Serialization;
using EntityPageTools;
using WikiPageTools;
using static FGDDumper.JsonStuff;

namespace FGDDumper
{
    [JsonSourceGenerationOptions(
               PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified,
               WriteIndented = true,
               Converters = [typeof(EntityPageJsonConverter), typeof(JsonStringEnumConverter)]
           )]
    [JsonSerializable(typeof(EntityPage))]
    [JsonSerializable(typeof(EntityPage.Property))]
    [JsonSerializable(typeof(EntityDocument))]
    [JsonSerializable(typeof(ConvarListToJson.ConEntry))]
    [JsonSerializable(typeof(List<ConvarListToJson.ConEntry>))]
    [JsonSerializable(typeof(ConvarListToJson.ConDump))]
    [JsonSerializable(typeof(List<ToolTexturesDumper.ToolMaterial>))]
    [JsonSerializable(typeof(ToolTexturesDumper.ToolMaterialDump))]
    public partial class JsonContext : JsonSerializerContext
    {
    }

    public static class JsonStuff
    {
        public class EntityPageJsonConverter : JsonConverter<EntityPage>
        {
            // the dumps are only ever read by the wiki, by its tools/entity-pages/model.ts
            public override EntityPage Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                throw new NotSupportedException("Entity pages are written here and read by the wiki, not the other way around.");
            }

            public override void Write(Utf8JsonWriter writer, EntityPage value, JsonSerializerOptions options)
            {
                writer.WriteStartObject();

                writer.WriteString("Game", value.Game?.FileSystemName);
                writer.WriteString("EntityType", value.EntityType.ToString());
                writer.WriteString("Name", value.Name);
                writer.WriteString("Description", value.Description);
                writer.WriteString("IconPath", value.IconPath);

                if (value.Legacy)
                    writer.WriteBoolean("Legacy", value.Legacy);

                if (value.NonFGD)
                    writer.WriteBoolean("NonFGD", value.NonFGD);

                writer.WritePropertyName("PageAnnotation");
                JsonSerializer.Serialize(writer, value.PageAnnotation, JsonContext.Default.Annotation);

                writer.WritePropertyName("Properties");
                JsonSerializer.Serialize(writer, value.Properties, JsonContext.Default.ListProperty);

                writer.WritePropertyName("InputOutputs");
                JsonSerializer.Serialize(writer, value.InputOutputs, JsonContext.Default.ListInputOutput);

                writer.WriteEndObject();
            }
        }
    }
}
