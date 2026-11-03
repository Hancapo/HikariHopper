namespace HikariHopper.Core;

/// <summary>Validation of names typed or dropped into the explorer.</summary>
public static class EntryNames
{
    private const int MaxEntryNameLength = 255;
    private const string InvalidNameCharacters = "<>:\"/\\|?*";

    private static readonly HashSet<string> WindowsReservedNames = new(
        new[] { "aux", "con", "nul", "prn" }
            .Concat(Enumerable.Range(1, 9).Select(index => $"com{index}"))
            .Concat(Enumerable.Range(1, 9).Select(index => $"lpt{index}")),
        StringComparer.OrdinalIgnoreCase);

    public static string NormalizeEntryName(string name)
    {
        string normalized = name.Trim();
        if (normalized.Length == 0)
            throw new ArgumentException("Enter a name");
        if (normalized is "." or "..")
            throw new ArgumentException("That name is reserved");
        if (normalized.Length > MaxEntryNameLength)
            throw new ArgumentException("Names cannot exceed 255 characters");
        if (normalized.EndsWith(' ') || normalized.EndsWith('.'))
            throw new ArgumentException("Names cannot end with a space or period");
        if (WindowsReservedNames.Contains(normalized.Split('.', 2)[0]))
            throw new ArgumentException("That name is reserved by Windows");
        if (normalized.Any(character => InvalidNameCharacters.Contains(character) || character < 32))
            throw new ArgumentException("Names cannot contain < > : \" / \\ | ? *");
        return normalized;
    }

    public static string NormalizeRpfName(string name) => NormalizeSuffixedName(name, ".rpf", "RPF");

    public static string NormalizeYtdName(string name) => NormalizeSuffixedName(name, ".ytd", "YTD");

    public static string NormalizeSuffixedName(string name, string suffix, string label)
    {
        string normalized = NormalizeEntryName(name);
        if (string.Equals(normalized, suffix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Enter a name before {suffix}");
        string candidate = normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? normalized
            : normalized + suffix;
        if (candidate.Length > MaxEntryNameLength)
            throw new ArgumentException($"{label} names cannot exceed 255 characters including {suffix}");
        return candidate;
    }

    /// <exception cref="EntryExistsException">An existing name matches case-insensitively.</exception>
    public static void EnsureEntryNameAvailable(string name, IEnumerable<string> existingNames)
    {
        if (existingNames.Any(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)))
            throw new EntryExistsException("An entry with this name already exists");
    }

    /// <summary><c>"."</c> for the root, otherwise the path without surrounding slashes.</summary>
    public static string NormalizedDirectory(string? path) =>
        string.IsNullOrEmpty(path) || path == "/" ? "." : path.Trim('/');

    public static string ArchiveChildPath(string directory, string name) =>
        directory == "." ? name : $"{directory}/{name}";
}

/// <summary>The Python <c>FileExistsError</c>: a name collides with an existing entry.</summary>
public sealed class EntryExistsException(string message) : IOException(message);
