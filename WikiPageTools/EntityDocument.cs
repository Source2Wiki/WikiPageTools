namespace FGDDumper
{
    /// <summary>
    /// Every game's version of one entity, which is what a single file in \fgd_dump holds.
    /// The wiki turns this into the tabbed page a reader sees.
    /// </summary>
    public class EntityDocument
    {
        public string Name { get; init; } = string.Empty;
        public List<EntityPage> Pages { get; init; } = new();

        public static EntityDocument GetDocument(string classname, List<EntityPage> pages)
        {
            if (pages.Count == 0)
            {
                throw new InvalidDataException("Cant have an entity document with 0 entity pages!");
            }

            return new EntityDocument
            {
                Name = classname,
                Pages = pages
            };
        }
    }
}
