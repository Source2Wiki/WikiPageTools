namespace FGDDumper
{
    /// <summary>
    /// Helpers for paths that live inside the wiki.
    ///
    /// Anything written into a JSON dump is a *wiki path*: relative to
    /// <see cref="EntityPageTools.WikiRoot"/> and always separated with '/', because docusaurus
    /// ends up serving it as a URL. Path.Combine must never build one of these, on windows it
    /// produces '\' which then lands verbatim inside an href.
    ///
    /// Disk paths are derived from a wiki path only where we actually touch the filesystem.
    /// </summary>
    public static class WikiPaths
    {
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
    }
}
