using Sledge.Formats.GameData.Objects;
using static FGDDumper.GameFinder;

namespace FGDDumper
{
    /// <summary>
    /// One entity as it exists in one game, and the shape it takes in \fgd_dump.
    ///
    /// This is a dump format, not a page: turning it into MDX, and merging \fgd_dump_overrides
    /// on top of it, is the wiki's job and lives in its \tools\entity-pages. Fields the dumper
    /// never fills in (annotations, the legacy and non FGD flags) are still part of the model,
    /// they are what an override file is allowed to set.
    /// </summary>
    public class EntityPage
    {
        public required Game? Game { get; set; }
        public required EntityTypeEnum EntityType { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string IconPath { get; set; } = string.Empty;

        /// <summary>
        /// Model the FGD names for this entity, rendered into its icon. Dump side only, the JSON
        /// converter does not write it, the wiki only ever sees the resulting IconPath.
        /// </summary>
        public string ModelPath { get; set; } = string.Empty;
        public bool NonFGD { get; set; } = false;
        public bool Legacy { get; set; } = false;
        public List<Property> Properties { get; set; } = [];
        public Annotation? PageAnnotation = null;
        public List<InputOutput> InputOutputs { get; set; } = [];

        public enum EntityTypeEnum
        {
            Default,
            Point,
            Mesh
        }

        public class InputOutput
        {
            public enum InputOutputTypeEnum
            {
                Input,
                Output
            }

            public string Name { get; init; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public VariableType? VariableType { get; set; }
            public InputOutputTypeEnum? Type { get; set; }
        }

        public class Annotation
        {
            public enum TypeEnum
            {
                Default,
                note,
                tip,
                info,
                warning,
                danger,
                legacy,
                nonFGD
            }

            public string Message { get; set; } = string.Empty;
            public TypeEnum Type { get; set; } = TypeEnum.Default;
            public string InternalName { get; init; } = string.Empty;
        };

        public class Property
        {
            public class Option
            {
                public string Name { get; init; } = string.Empty;
                public string Description { get; set; } = string.Empty;
                public string? Key { get; set; } = null;
            }

            public string FriendlyName { get; set; } = string.Empty;
            public string InternalName { get; init; } = string.Empty;
            public VariableType? VariableType { get; set; } = null;
            public string Description { get; set; } = string.Empty;

            public List<Option> Options { get; set; } = [];

            public List<Annotation> Annotations { get; set; } = [];
        }

        /// <summary>Wiki folder the extracted entity icons of a game go into.</summary>
        public static string GetIconFolder(Game game)
        {
            return WikiPaths.Combine("static", EntityPageTools.DumpFolder, "img", game.FileSystemName);
        }

        /// <summary>
        /// Wiki path of the png extracted for an icon material. Named after the material rather than
        /// the entity, so the entities sharing an icon share one file instead of writing a copy each.
        /// </summary>
        public static string GetIconPngPath(Game game, string iconMaterialPath)
        {
            return WikiPaths.Combine(GetIconFolder(game), $"{Path.GetFileNameWithoutExtension(iconMaterialPath)}.png");
        }

        public static EntityPage? GetEntityPage(GameDataClass Class, Game game)
        {
            // we want base classes only, users dont care about these
            if (Class.ClassType == ClassType.BaseClass || Class.ClassType == ClassType.OverrideClass)
            {
                return null;
            }

            EntityTypeEnum entityType = EntityTypeEnum.Point;
            if (Class.ClassType == ClassType.SolidClass)
            {
                entityType = EntityTypeEnum.Mesh;
            }

            string iconPath = string.Empty;
            string modelPath = string.Empty;

            foreach (var behavior in Class.Behaviours)
            {
                if (behavior.Values.Count == 0)
                {
                    continue;
                }

                if (behavior.Name == "iconsprite")
                {
                    iconPath = behavior.Values[0];
                }

                // a model to render into an icon. editormodel is what hammer itself draws for the
                // entity, so it is the truest picture of it, and it may sit next to an iconsprite
                if (behavior.Name == "studio" || behavior.Name == "studioprop" || behavior.Name == "model" || behavior.Name == "editormodel")
                {
                    modelPath = behavior.Values[0];
                }
            }

            foreach (var dict in Class.Dictionaries)
            {
                foreach (var kv in dict)
                {
                    if (kv.Key == "image" || kv.Key == "auto_apply_material")
                    {
                        iconPath = (string)kv.Value.Value;
                    }
                }
            }

            var inputOutputs = new List<InputOutput>();
            foreach (var inputOutput in Class.InOuts)
            {
                inputOutputs.Add(new InputOutput
                {

                    Name = inputOutput.Name,
                    Description = GameDataDumper.SanitizeInput(inputOutput.Description),
                    Type = (InputOutput.InputOutputTypeEnum)Enum.Parse(typeof(InputOutput.InputOutputTypeEnum), inputOutput.IOType.ToString()),
                    VariableType = inputOutput.VariableType
                });
            }

            var entityPage = new EntityPage
            {
                Game = game,
                Name = Class.Name,
                Description = Class.Description,
                IconPath = iconPath,
                ModelPath = modelPath,
                EntityType = entityType
            };

            entityPage.InputOutputs.AddRange(inputOutputs);

            foreach (var property in Class.Properties)
            {
                // dont add removed keys pls
                if (property.VariableType == VariableType.RemoveKey)
                {
                    continue;
                }

                var newProperty = new Property
                {
                    FriendlyName = GameDataDumper.SanitizeInput(property.Description),
                    InternalName = GameDataDumper.SanitizeInput(property.Name),
                    Description = GameDataDumper.SanitizeInput(property.Details),
                    VariableType = property.VariableType
                };

                foreach (var option in property.Options)
                {
                    newProperty.Options.Add(new Property.Option
                    {
                        Name = GameDataDumper.SanitizeInput(option.Description),
                        Description = GameDataDumper.SanitizeInput(option.Details),
                        Key = option.Key
                    });
                }

                entityPage.Properties.Add(newProperty);
            }

            return entityPage;
        }
    }
}
