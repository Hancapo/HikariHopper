using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace HikariHopper.Core;

/// <summary>
/// Persistent settings in <c>HKCU\Software\HikariHopper\HikariHopper</c>, the exact layout the
/// Qt (<c>QSettings</c>) version used, so existing installations keep their configuration.
/// A key such as <c>gameRoots/legacy</c> is the value <c>legacy</c> under subkey <c>gameRoots</c>;
/// string lists are <c>REG_MULTI_SZ</c> and integers <c>REG_DWORD</c>.
/// </summary>
[SupportedOSPlatform("windows")]
public static class AppSettings
{
    public const string ApplicationName = "HikariHopper";
    public const string OrganizationName = "HikariHopper";

    /// <summary>Registry path below HKCU; tests point this at a scratch key.</summary>
    public static string RegistryPath { get; set; } = $@"Software\{OrganizationName}\{ApplicationName}";

    public static string GetString(string key, string defaultValue = "") => Read(key) switch
    {
        null => defaultValue,
        string[] list => list.Length > 0 ? list[0] : defaultValue,
        object value => Convert.ToString(value, CultureInfo.InvariantCulture) ?? defaultValue,
    };

    public static int GetInt(string key, int defaultValue = 0) => Read(key) switch
    {
        int value => value,
        long value => (int)value,
        string text when int.TryParse(text, CultureInfo.InvariantCulture, out int parsed) => parsed,
        _ => defaultValue,
    };

    public static IReadOnlyList<string> GetStringList(string key) => ToList(Read(key));

    public static void SetString(string key, string value) => Write(key, value, RegistryValueKind.String);

    public static void SetInt(string key, int value) => Write(key, value, RegistryValueKind.DWord);

    public static void SetStringList(string key, IEnumerable<string> values) =>
        Write(key, values.ToArray(), RegistryValueKind.MultiString);

    public static void Remove(string key)
    {
        var (subkey, name) = Split(RegistryPath, key);
        using RegistryKey? registry = Registry.CurrentUser.OpenSubKey(subkey, writable: true);
        registry?.DeleteValue(name, throwOnMissingValue: false);
    }

    /// <summary>Reads a value from another QSettings scope, used only for migration.</summary>
    internal static object? ReadFrom(string registryPath, string key)
    {
        var (subkey, name) = Split(registryPath, key);
        using RegistryKey? registry = Registry.CurrentUser.OpenSubKey(subkey);
        return registry?.GetValue(name);
    }

    internal static IReadOnlyList<string> ToList(object? value) => value switch
    {
        string[] list => list,
        // QSettings writes a single-item list as REG_SZ and an empty one as "@Invalid()".
        string text when text.Length > 0 && text != "@Invalid()" => [text],
        _ => [],
    };

    private static object? Read(string key) => ReadFrom(RegistryPath, key);

    private static void Write(string key, object value, RegistryValueKind kind)
    {
        var (subkey, name) = Split(RegistryPath, key);
        using RegistryKey registry = Registry.CurrentUser.CreateSubKey(subkey, writable: true);
        registry.SetValue(name, value, kind);
    }

    private static (string Subkey, string Name) Split(string root, string key)
    {
        int slash = key.LastIndexOf('/');
        return slash < 0
            ? (root, key)
            : (root + "\\" + key[..slash].Replace('/', '\\'), key[(slash + 1)..]);
    }
}
