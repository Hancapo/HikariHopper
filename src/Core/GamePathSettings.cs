using System.Runtime.Versioning;
using CommunityToolkit.Mvvm.ComponentModel;
using HikariHopper.Infrastructure;

namespace HikariHopper.Core;

/// <summary>Editable GTA V installation paths shown in the Settings window.</summary>
[SupportedOSPlatform("windows")]
public sealed class GamePathSettings : ObservableObject
{
    private readonly Dictionary<string, string> paths = new();

    public GamePathSettings()
    {
        foreach (string edition in GameSettings.AllEditions)
            paths[edition] = GameSettings.ConfiguredGameRoot(edition);
    }

    /// <summary>Raised whenever any path changes (the Qt <c>pathsChanged</c> signal).</summary>
    public event EventHandler? PathsChanged;

    public string LegacyPath => paths[GameSettings.LegacyEdition];

    public string EnhancedPath => paths[GameSettings.EnhancedEdition];

    /// <summary>The configured path of <paramref name="edition"/>, "" when none is set.</summary>
    public string PathFor(string edition) => paths.TryGetValue(edition, out string? path) ? path : "";

    public bool LegacyPathValid => GameSettings.IsGameRoot(LegacyPath, GameSettings.LegacyEdition);

    public bool EnhancedPathValid => GameSettings.IsGameRoot(EnhancedPath, GameSettings.EnhancedEdition);

    public void SetLegacyPath(string value) => SetPath(GameSettings.LegacyEdition, value);

    public void SetEnhancedPath(string value) => SetPath(GameSettings.EnhancedEdition, value);

    public Task BrowseLegacyPath() => Browse(GameSettings.LegacyEdition);

    public Task BrowseEnhancedPath() => Browse(GameSettings.EnhancedEdition);

    public void Refresh()
    {
        bool changed = false;
        foreach (string edition in GameSettings.AllEditions)
        {
            string value = GameSettings.ConfiguredGameRoot(edition);
            changed |= paths[edition] != value;
            paths[edition] = value;
        }
        if (changed)
            PublishPaths();
    }

    private void SetPath(string edition, string value)
    {
        string normalized = value.Trim().Trim('"');
        if (normalized == paths[edition])
            return;
        string previous = paths[edition];
        paths[edition] = normalized;
        if (normalized.Length > 0)
            AppSettings.SetString(GameSettings.GameRootKey(edition), normalized);
        else
            AppSettings.Remove(GameSettings.GameRootKey(edition));
        string lastRoot = AppSettings.GetString(GameSettings.LastGameRootKey);
        string lastEdition = GameSettings.GameEditionForPath(lastRoot);
        if (GameSettings.IsGameRoot(normalized, edition) && (lastEdition.Length == 0 || lastEdition == edition))
            AppSettings.SetString(GameSettings.LastGameRootKey, normalized);
        else if (lastRoot == previous)
            AppSettings.Remove(GameSettings.LastGameRootKey);
        PublishPaths();
    }

    private async Task Browse(string edition)
    {
        string start = paths[edition];
        if (start.Length == 0 || !Directory.Exists(start))
            start = AppSettings.GetString(GameSettings.LastGameRootKey);
        string? path = await AppServices.Dialogs.PickFolderAsync(GameSettings.BrowseTitle(edition), start);
        if (!string.IsNullOrEmpty(path))
            SetPath(edition, path);
    }

    private void PublishPaths()
    {
        OnPropertyChanged(nameof(LegacyPath));
        OnPropertyChanged(nameof(EnhancedPath));
        OnPropertyChanged(nameof(LegacyPathValid));
        OnPropertyChanged(nameof(EnhancedPathValid));
        PathsChanged?.Invoke(this, EventArgs.Empty);
    }
}
