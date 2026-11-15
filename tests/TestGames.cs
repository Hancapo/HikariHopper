using System.Text.RegularExpressions;
using HikariHopper.Core;
using Microsoft.Win32;

namespace HikariHopper.Tests;

/// <summary>
/// The real game the real-asset tests read from, found as the app would: <c>HIKARI_TEST_LEGACY</c> when set, else the
/// Legacy folder set in HikariHopper's settings, else Steam's own install of it. Read only: a test copies what it
/// changes. Without one, those tests are skipped.
/// </summary>
internal static partial class TestGames
{
    private const string LegacySteamApp = "271590";
    private const string Rdr2SteamApp = "1174180";
    private static readonly Lazy<string?> legacy = new(FindLegacy);
    private static readonly Lazy<string?> rdr2 = new(FindRdr2);

    /// <summary>
    /// The Red Dead Redemption 2 folder (<c>HIKARI_TEST_RDR2</c>, its folder in HikariHopper's settings, or Steam's),
    /// or skips the test that needs it.
    /// </summary>
    internal static string Rdr2()
    {
        if (rdr2.Value is not { } root)
        {
            Assert.Skip("No RDR2 found: set HIKARI_TEST_RDR2, its folder in HikariHopper's settings, or install it with Steam.");
            throw new InvalidOperationException();
        }
        return root;
    }

    private static string? FindRdr2()
    {
        static bool IsRdr2(string? folder) => !string.IsNullOrEmpty(folder) && File.Exists(Path.Combine(folder, "RDR2.exe"));
        string? named = Environment.GetEnvironmentVariable("HIKARI_TEST_RDR2");
        if (!string.IsNullOrEmpty(named))
            return IsRdr2(named) ? named : null;
        if (AppSettings.ReadFrom($@"Software\{AppSettings.OrganizationName}\{AppSettings.ApplicationName}", GameSettings.Rdr2GameRootKey) is string set
            && IsRdr2(set))
            return set;
        return SteamLibraries(Rdr2SteamApp).Select(library => Path.Combine(library, "steamapps", "common", "Red Dead Redemption 2")).FirstOrDefault(IsRdr2);
    }

    /// <summary>The GTA V Legacy folder, or skips the test that needs it.</summary>
    internal static string Legacy()
    {
        string? named = Environment.GetEnvironmentVariable("HIKARI_TEST_LEGACY");
        if (!string.IsNullOrEmpty(named))
            Assert.True(Directory.Exists(named), $"HIKARI_TEST_LEGACY does not exist: {named}");
        if (legacy.Value is not { } root)
        {
            Assert.Skip("No GTA V Legacy found: set HIKARI_TEST_LEGACY, its folder in HikariHopper's settings, or install it with Steam.");
            throw new InvalidOperationException();
        }
        return root;
    }

    private static string? FindLegacy()
    {
        static bool IsLegacy(string? folder) => !string.IsNullOrEmpty(folder) && File.Exists(Path.Combine(folder, "GTA5.exe"));
        string? named = Environment.GetEnvironmentVariable("HIKARI_TEST_LEGACY");
        if (!string.IsNullOrEmpty(named))
            return named;
        // The app's own settings, under its real key: tests point AppSettings at scratch keys of their own.
        if (AppSettings.ReadFrom($@"Software\{AppSettings.OrganizationName}\{AppSettings.ApplicationName}", GameSettings.LegacyGameRootKey) is string set
            && IsLegacy(set))
            return set;
        return SteamLibraries(LegacySteamApp).Select(library => Path.Combine(library, "steamapps", "common", "Grand Theft Auto V")).FirstOrDefault(IsLegacy);
    }

    /// <summary>Steam's library folders that hold the game <paramref name="app"/>, as its libraryfolders.vdf lists them.</summary>
    private static IEnumerable<string> SteamLibraries(string app)
    {
        string steam;
        using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
            steam = key?.GetValue("SteamPath") as string
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        string list = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(list))
            yield break;
        // Each library: "path" "D:\\Steam", then its "apps" { "271590" "..." }.
        string text = File.ReadAllText(list);
        MatchCollection paths = LibraryPath().Matches(text);
        for (int i = 0; i < paths.Count; i++)
        {
            int end = i + 1 < paths.Count ? paths[i + 1].Index : text.Length;
            if (text.AsSpan(paths[i].Index, end - paths[i].Index).Contains($"\"{app}\"", StringComparison.Ordinal))
                yield return paths[i].Groups[1].Value.Replace(@"\\", @"\");
        }
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"")]
    private static partial Regex LibraryPath();
}
