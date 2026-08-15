namespace FGDDumper
{
    /// <summary>
    /// Helpers for paths that live inside the wiki.
    ///
    /// Anything written into a JSON dump or an MDX page is a *wiki path*: relative to
    /// <see cref="EntityPageTools.WikiRoot"/> and always separated with '/', because docusaurus
    /// ends up serving it as a URL. Path.Combine must never build one of these, on windows it
    /// produces '\' which then lands verbatim inside an href.
    ///
    /// Disk paths are derived from a wiki path only where we actually touch the filesystem,
    /// URLs only where we write markup.
    /// </summary>
    public static class WikiPaths
    {
        private const string StaticFolder = "static";

        public static string Combine(params string[] segments)
        {
            return string.Join('/', segments
                .Where(segment => !string.IsNullOrEmpty(segment))
                .Select(segment => segment.Replace('\\', '/').Trim('/')));
        }

        /// <summary>Absolute path on this machine for a wiki path.</summary>
        public static string ToDisk(string wikiPath)
        {
            return Path.Combine(EntityPageTools.WikiRoot, wikiPath.Replace('/', Path.DirectorySeparatorChar));
        }

        public static bool Exists(string wikiPath)
        {
            return !string.IsNullOrEmpty(wikiPath) && File.Exists(ToDisk(wikiPath));
        }

        /// <summary>
        /// Site URL for a wiki path. Docusaurus serves \static from the site root, so that
        /// segment gets dropped.
        /// </summary>
        public static string ToUrl(string wikiPath)
        {
            var url = wikiPath.Replace('\\', '/').TrimStart('/');

            if (url.StartsWith($"{StaticFolder}/", StringComparison.OrdinalIgnoreCase))
            {
                url = url[(StaticFolder.Length + 1)..];
            }

            return $"/{url}";
        }
    }
}
