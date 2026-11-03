using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>About HikariHopper, shown as a sheet over the main window.</summary>
public partial class AboutDialog : ModalSheet
{
    public static readonly Uri ProjectPage = new("https://github.com/Hancapo/HikariHopper");
    public static readonly Uri ChangelogPage = new("https://github.com/Hancapo/HikariHopper/blob/master/CHANGELOG.md");

    public AboutDialog()
    {
        AvaloniaXamlLoader.Load(this);
        this.FindControl<ChromeToolButton>("GitHubButton")!.Click += (_, _) => Open(ProjectPage);
        this.FindControl<ChromeToolButton>("ChangelogButton")!.Click += (_, _) => Open(ChangelogPage);
    }

    /// <summary>Opens a page in the default browser; tests replace it.</summary>
    internal static Action<TopLevel?, Uri> Launch { get; set; } =
        (top, uri) => _ = top?.Launcher.LaunchUriAsync(uri);

    private void Open(Uri page) => Launch(TopLevel.GetTopLevel(this), page);
}
