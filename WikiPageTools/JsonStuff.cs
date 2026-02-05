using System.Text.Json;
using System.Text.Json.Serialization;
using EntityPageTools;
using WikiPageTools;
using static FGDDumper.JsonStuff;
using static FGDDumper.WikiFilesGenerator;

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
    [JsonSerializable(typeof(List<EntityIndexEntry>))]
    [JsonSerializable(typeof(EntityIndexEntry))]
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
            public override EntityPage Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType != JsonTokenType.StartObject)
                {
                    throw new JsonException("Expected StartObject token");
                }

                GameFinder.Game? game = null;
                EntityPage.EntityTypeEnum entityType = EntityPage.EntityTypeEnum.Default;
                string? name = string.Empty;
                string description = string.Empty;
                string iconPath = string.Empty;
                bool isLegacy = false;
                bool nonFGD = false;
                EntityPage.Annotation? pageAnnotation = null;
                List<EntityPage.Property> properties = [];
                List<EntityPage.InputOutput> inputOutputs = [];

                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                    {
                        break;
                    }

                    if (reader.TokenType != JsonTokenType.PropertyName)
                    {
                        throw new JsonException("Expected PropertyName token");
                    }

                    string? propertyName = reader.GetString();
                    reader.Read();

                    switch (propertyName)
                    {
                        case "Game":
                            game = GameFinder.GetGameByFileSystemName(reader.GetString());
                            break;
                        case "EntityType":
                            entityType = Enum.Parse<EntityPage.EntityTypeEnum>(reader.GetString() ?? string.Empty);
                            break;
                        case "Name":
                            name = reader.GetString();
                            break;
                        case "Description":
                            description = reader.GetString() ?? string.Empty;
                            break;
                        case "IconPath":
                            iconPath = reader.GetString() ?? string.Empty;
                            break;
                        case "Legacy":
                            isLegacy = reader.GetBoolean();
                            break;
                        case "NonFGD":
                            nonFGD = reader.GetBoolean();
                            break;
                        case "PageAnnotation":
                            pageAnnotation = JsonSerializer.Deserialize(ref reader, JsonContext.Default.Annotation);
                            break;
                        case "Properties":
                            properties = JsonSerializer.Deserialize(ref reader, JsonContext.Default.ListProperty) ?? [];
                            break;
                        case "InputOutputs":
                            inputOutputs = JsonSerializer.Deserialize(ref reader, JsonContext.Default.ListInputOutput) ?? [];
                            break;
                        default:
                            reader.Skip();
                            break;
                    }
                }

                return new EntityPage
                {
                    Game = game,
                    EntityType = entityType,
                    Name = name ?? string.Empty,
                    Description = description,
                    IconPath = iconPath,
                    Legacy = isLegacy,
                    NonFGD = nonFGD,
                    PageAnnotation = pageAnnotation,
                    Properties = properties,
                    InputOutputs = inputOutputs
                };

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
