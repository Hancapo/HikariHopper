using HikariHopper.Rendering;

namespace HikariHopper.Tests;

/// <summary>The tests' own shader modules, listed in tests/Shaders/modules.txt as the app lists its own.</summary>
internal static class TestShaders
{
    private static readonly Lazy<Dictionary<string, string[]>> Listed = new(() =>
    {
        var modules = new Dictionary<string, string[]>();
        foreach (string line in Read("modules.txt").Split('\n'))
        {
            string entry = line.Trim();
            if (entry.Length == 0 || entry[0] == '#')
                continue;
            int equals = entry.IndexOf('=');
            modules[entry[..equals].Trim()] = entry[(equals + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }
        return modules;
    });

    /// <summary>A test module's source: its files joined with a newline between each, as the app joins its own.</summary>
    internal static string Module(string name) => string.Join("\n", Listed.Value[name]
        .Select(file => file.StartsWith("Test/", StringComparison.Ordinal) ? Read(file["Test/".Length..]) : ViewportRenderer.Read(file)));

    /// <summary>One of the tests' shader files (tests/Shaders), by its file name.</summary>
    internal static string Read(string name)
    {
        using Stream stream = typeof(TestShaders).Assembly.GetManifestResourceStream("HikariHopper.Tests.Shaders." + name)
            ?? throw new FileNotFoundException(name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
