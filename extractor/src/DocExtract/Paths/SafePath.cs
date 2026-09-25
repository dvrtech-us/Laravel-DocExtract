using DocExtract.Contract;

namespace DocExtract.Paths;

public static class SafePath
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static bool IsReservedDeviceName(string segment)
    {
        if (string.IsNullOrEmpty(segment))
        {
            return false;
        }

        var name = segment.TrimEnd(' ', '.');
        var dot = name.IndexOf('.');
        if (dot >= 0)
        {
            name = name[..dot];
        }

        name = name.TrimEnd(' ', '.');
        return Reserved.Contains(name);
    }

    public static bool IsUnsafeRelative(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative))
        {
            return true;
        }

        if (relative.Contains(':') || relative.Contains('\0'))
        {
            return true;
        }

        if (relative[0] is '/' or '\\' || Path.IsPathRooted(relative))
        {
            return true;
        }

        var normalized = relative.Replace('\\', '/');
        foreach (var segment in normalized.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or "..")
            {
                return true;
            }

            if (IsReservedDeviceName(segment))
            {
                return true;
            }
        }

        return false;
    }

    public static string Combine(string root, string relative)
    {
        if (IsUnsafeRelative(relative))
        {
            throw ExtractionException.UnsafePath();
        }

        var rootFull = Path.GetFullPath(root);
        var rootPrefix = rootFull.EndsWith(Path.DirectorySeparatorChar)
            ? rootFull
            : rootFull + Path.DirectorySeparatorChar;
        var combined = Path.GetFullPath(Path.Combine(rootFull, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!combined.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(combined, rootFull, StringComparison.OrdinalIgnoreCase))
        {
            throw ExtractionException.UnsafePath();
        }

        return combined;
    }
}
