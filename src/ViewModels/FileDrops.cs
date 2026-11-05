namespace HikariHopper.ViewModels;

/// <summary>Shared validation for local file drops into the explorer and editors.</summary>
public static class FileDrops
{
    /// <summary>
    /// Resolves dropped items to existing local files. Each value is a local path or a
    /// <c>file://</c> URI; a <c>null</c> stands for a dropped item that has no local path.
    /// </summary>
    /// <exception cref="ArgumentException">An item is not a local file, or nothing was dropped.</exception>
    /// <exception cref="FileNotFoundException">A dropped item no longer exists.</exception>
    public static IReadOnlyList<string> LocalDropPaths(IEnumerable<string?> values)
    {
        var paths = new List<string>();
        foreach (string? value in values)
        {
            string local = LocalPath(value) ?? throw new ArgumentException("Only local files can be imported");
            string path = Resolve(local);
            if (!File.Exists(path) && !Directory.Exists(path))
                throw new ArgumentException($"Not found: {Path.GetFileName(path)}");
            paths.Add(path);
        }
        if (paths.Count == 0)
            throw new ArgumentException("Drop one or more files to import");
        return paths;
    }

    private static string? LocalPath(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;
        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && !IsDrivePath(value))
            return uri.IsFile ? uri.LocalPath : null;
        return value;
    }

    // "C:\x" parses as an absolute file URI too; keep it a path so it is not unescaped.
    private static bool IsDrivePath(string value) =>
        value.Length >= 2 && char.IsAsciiLetter(value[0]) && value[1] == ':';

    /// <summary>Like Python's <c>expanduser().resolve(strict=True)</c>.</summary>
    private static string Resolve(string value)
    {
        string expanded = value == "~" || value.StartsWith("~/") || value.StartsWith(@"~\")
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + value[1..]
            : value;
        string full = Path.GetFullPath(expanded);
        FileSystemInfo info = Directory.Exists(full) ? new DirectoryInfo(full) : new FileInfo(full);
        if (!info.Exists)
            throw new FileNotFoundException($"File not found: {full}", full);
        return info.LinkTarget is not null && info.ResolveLinkTarget(returnFinalTarget: true) is { } target
            ? target.FullName
            : full;
    }
}
