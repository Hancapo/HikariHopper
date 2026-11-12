using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>About HikariHopper, shown as a sheet over the main window.</summary>
public partial class AboutDialog : ModalSheet
{
    public static readonly Uri ProjectPage = new("https://github.com/Hancapo/HikariHopper");

    /// <summary>The notices of the code HikariHopper includes, as the repository has them.</summary>
    public static readonly Uri NoticesPage = new("https://github.com/Hancapo/HikariHopper/blob/master/THIRD-PARTY-NOTICES.md");

    /// <summary>The notices' file, which ships beside the app.</summary>
    public const string NoticesFile = "THIRD-PARTY-NOTICES.md";

    public AboutDialog()
    {
        AvaloniaXamlLoader.Load(this);
        this.FindControl<ChromeToolButton>("GitHubButton")!.Click += (_, _) => Open(ProjectPage);
        // The changelog opens in the app, over About: Escape comes back here.
        this.FindControl<ChromeToolButton>("ChangelogButton")!.Click += (_, _) => (Changelog ??= new ChangelogDialog()).Present(this);
        this.FindControl<ChromeToolButton>("LicencesButton")!.Click += (_, _) => OpenNotices();
    }

    /// <summary>The notices, while they are open over About.</summary>
    internal TextViewer? Notices { get; private set; }

    /// <summary>Where the notices' file is looked for; tests point it elsewhere.</summary>
    internal static string NoticesFolder { get; set; } = AppContext.BaseDirectory;

    /// <summary>
    /// Opens the third-party notices in the app's text viewer, over About: Escape comes back here. A build without
    /// the file beside it opens the repository's instead.
    /// </summary>
    private void OpenNotices()
    {
        if (Notices is not null)
            return;
        byte[] text;
        try
        {
            text = File.ReadAllBytes(Path.Combine(NoticesFolder, NoticesFile));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Open(NoticesPage);
            return;
        }
        var viewer = new TextViewer(Core.TextDocument.Load(NoticesFile, text)) { Anchor = this };
        Notices = viewer;
        viewer.Closed += (_, _) => Notices = null;
        viewer.Present();
    }

    /// <summary>The changelog sheet, once it has been opened.</summary>
    internal ChangelogDialog? Changelog { get; private set; }

    /// <summary>Opens a page in the default browser; tests replace it.</summary>
    internal static Action<TopLevel?, Uri> Launch { get; set; } =
        (top, uri) => _ = top?.Launcher.LaunchUriAsync(uri);

    private void Open(Uri page) => Launch(TopLevel.GetTopLevel(this), page);
}
