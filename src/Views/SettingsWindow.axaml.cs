using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using HikariHopper.Core;

namespace HikariHopper.Views;

/// <summary>
/// Settings (port of <c>SettingsWindow.qml</c>), bound to the shared <see cref="Core.GamePathSettings"/>
/// (QML <c>required property var gamePaths</c>). Changes are saved immediately by the controller.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class SettingsWindow : ToolWindow
{
    public SettingsWindow()
        : this(new GamePathSettings())
    {
    }

    public SettingsWindow(GamePathSettings gamePaths)
    {
        GamePaths = gamePaths;
        AvaloniaXamlLoader.Load(this);
        DataContext = this;
        GamePathRow legacy = this.FindControl<GamePathRow>("LegacyRow")!;
        GamePathRow enhanced = this.FindControl<GamePathRow>("EnhancedRow")!;
        legacy.PathCommitted += (_, path) => GamePaths.SetLegacyPath(path);
        legacy.BrowseRequested += async (_, _) => await GamePaths.BrowseLegacyPath();
        enhanced.PathCommitted += (_, path) => GamePaths.SetEnhancedPath(path);
        enhanced.BrowseRequested += async (_, _) => await GamePaths.BrowseEnhancedPath();
    }

    public GamePathSettings GamePaths { get; }

    /// <summary>Titles listed for future support, in PC release order.</summary>
    public IReadOnlyList<string> FutureGames { get; } =
    [
        "Grand Theft Auto III",
        "Grand Theft Auto: Vice City",
        "Grand Theft Auto: San Andreas",
        "Bully: Scholarship Edition",
        "Grand Theft Auto IV",
        "Red Dead Redemption 2",
        "Red Dead Redemption",
    ];
}
