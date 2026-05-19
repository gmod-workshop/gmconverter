namespace GMConverter.Common;

// Format-agnostic path utilities shared across importers, exporters, and explorer caches. The
// only entry point today is TryResolveUnderRoot, but the file exists so future helpers
// (sanitization, canonicalization, etc.) have a home that doesn't sit inside any one consumer.
internal static class PathHelpers
{
    // Resolve a caller-supplied "relative" path against a trusted root, rejecting rooted inputs
    // and verifying that the resolved full path stays inside the root. The intent is to defend
    // against tampered manifest / sentinel / config files whose path fields might otherwise let
    // Path.Combine silently drop the base directory or escape via "..".
    //
    // Returns true and yields the canonical full path under root on success. Returns false on
    // every failure mode (rooted input, traversal escape, malformed segments, IO errors during
    // normalization). Callers decide how to react — manifest readers typically throw with
    // context; cache readers prefer to fall back to a miss.
    public static bool TryResolveUnderRoot(string root, string relative, out string resolved)
    {
        resolved = string.Empty;
        if (string.IsNullOrEmpty(relative) || Path.IsPathRooted(relative))
        {
            return false;
        }

        string rootFull;
        string candidate;
        try
        {
            rootFull = Path.GetFullPath(root);
            candidate = Path.GetFullPath(Path.Join(rootFull, relative));
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return false;
        }

        // Accept exact equality with the root or any descendant via the directory separator
        // prefix. Case-insensitive on Windows (the primary target); on Linux the comparison
        // degenerates to a normal substring match since the inputs are case-correct.
        var separator = Path.DirectorySeparatorChar;
        var rootWithSep = rootFull.EndsWith(separator) ? rootFull : rootFull + separator;
        if (!candidate.Equals(rootFull, StringComparison.OrdinalIgnoreCase) &&
            !candidate.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        resolved = candidate;
        return true;
    }
}
