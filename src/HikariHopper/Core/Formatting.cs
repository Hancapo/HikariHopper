using System.Globalization;

namespace HikariHopper.Core;

/// <summary>Shared display formatting for the explorer UI.</summary>
public static class Formatting
{
    private static readonly Dictionary<string, string> TextureFormatLabels = new(StringComparer.Ordinal)
    {
        ["BC1"] = "DXT1",
        ["BC2"] = "DXT3",
        ["BC3"] = "DXT5",
        ["BC4"] = "ATI1",
        ["BC5"] = "ATI2",
        ["BC6H"] = "BPTC HDR",
        ["BC7"] = "BPTC RGBA",
        ["A8R8G8B8"] = "ARGB 32-bit",
        ["R8G8B8A8"] = "RGBA 32-bit",
        ["B5G6R5"] = "RGB 565",
        ["B5G5R5A1"] = "RGB5A1",
        ["R10G10B10A2"] = "RGB10A2",
        ["R16_FLOAT"] = "R16F",
        ["R16G16_FLOAT"] = "RG16F",
        ["R16G16B16A16_FLOAT"] = "RGBA16F",
        ["R32_FLOAT"] = "R32F",
        ["R32G32B32A32_FLOAT"] = "RGBA32F",
    };

    /// <summary>Compact, stable file-size label (<c>1,234 B</c>, <c>12.3 KB</c>).</summary>
    public static string FormatSize(long value)
    {
        if (value < 1024)
            return value.ToString("#,0", CultureInfo.InvariantCulture) + " B";
        string[] units = ["KB", "MB", "GB", "TB"];
        double scaled = value;
        foreach (string unit in units)
        {
            scaled /= 1024.0;
            if (scaled < 1024.0 || unit == units[^1])
                return scaled.ToString("0.0", CultureInfo.InvariantCulture) + " " + unit;
        }
        return value.ToString("#,0", CultureInfo.InvariantCulture) + " B";
    }

    public static string FormatTimestamp(DateTimeOffset? value) =>
        value is null ? "—" : value.Value.ToLocalTime().ToString("dd/MM/yyyy  HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Familiar texture names while retaining exact storage terminology.</summary>
    public static string FormatTextureFormat(string value, bool includeExact = false)
    {
        string exact = value.ToUpperInvariant();
        string friendly = TextureFormatLabels.GetValueOrDefault(exact, exact);
        return includeExact && friendly != exact ? $"{friendly} ({exact})" : friendly;
    }

    /// <summary>Compact path for the tab and the status bar.</summary>
    public static string DisplayPath(string value)
    {
        string trimmed = value.TrimEnd('/', '\\');
        string name = System.IO.Path.GetFileName(trimmed);
        string stem = System.IO.Path.GetFileNameWithoutExtension(trimmed);
        if (name.StartsWith('.') && name.IndexOf('.', 1) < 0)
            stem = name;
        return stem.Length > 0 ? stem : name.Length > 0 ? name : "Workspace";
    }
}
