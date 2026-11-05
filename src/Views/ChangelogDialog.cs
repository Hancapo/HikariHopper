using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using HikariHopper.Core;
using HikariHopper.Views.Controls;
using T = HikariHopper.Ui.Theme;

namespace HikariHopper.Views;

/// <summary>
/// The app's changelog, as a sheet: each version with its date, its groups in the sheets' small caps, and
/// their entries. It is read from the <c>CHANGELOG.md</c> built into the app, so it is always this build's.
/// </summary>
public sealed class ChangelogDialog : ModalSheet
{
    private static readonly Uri Source = new("avares://HikariHopper/Assets/CHANGELOG.md");

    public ChangelogDialog() : this(ForBuild(Load(), Infrastructure.BuildInfo.IsRelease))
    {
    }

    public ChangelogDialog(IReadOnlyList<ChangelogRelease> releases)
    {
        Releases = releases;
        Heading = "CHANGELOG";
        WindowShare = new Size(0.45, 0.8);
        MinSize = new Size(460, 420);
        MaxSize = new Size(620, 760);
        var list = new StackPanel();
        for (int index = 0; index < releases.Count; index++)
            list.Children.Add(Release(releases[index], first: index == 0));
        var scroller = new ScrollViewer
        {
            Theme = Application.Current?.FindResource("QuietScrollViewer") as ControlTheme,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            // A background, so the wheel scrolls over the gaps between and beside the text too, not only over it.
            Content = new Border { Background = Brushes.Transparent, Padding = new Thickness(0, 0, 14, 8), Child = list },
        };
        SmoothScrolling.Attach(scroller);
        Content = scroller;
    }

    /// <summary>The versions shown, newest first.</summary>
    public IReadOnlyList<ChangelogRelease> Releases { get; }

    /// <summary>
    /// What a build shows: a release build only the versions released, a dev build the Unreleased changes too,
    /// so whoever tests it can see what is new.
    /// </summary>
    public static IReadOnlyList<ChangelogRelease> ForBuild(IReadOnlyList<ChangelogRelease> releases, bool release) =>
        release ? [.. releases.Where(entry => !entry.IsUnreleased)] : releases;

    /// <summary>The changelog built into the app; empty if it is missing.</summary>
    public static IReadOnlyList<ChangelogRelease> Load()
    {
        try
        {
            using var reader = new StreamReader(AssetLoader.Open(Source));
            return Changelog.Parse(reader.ReadToEnd());
        }
        catch (Exception error) when (error is IOException or FileNotFoundException or InvalidOperationException)
        {
            return [];
        }
    }

    private static Control Release(ChangelogRelease release, bool first)
    {
        var panel = new StackPanel();
        if (!first)
            panel.Children.Add(new DottedRule { Horizontal = true, Margin = new Thickness(0, 18, 0, 14) });
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 0, 0, 10) };
        title.Children.Add(new TextBlock
        {
            Text = release.Version, Foreground = T.Text, FontFamily = T.MonoFont, FontSize = 14, FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (release.Date.Length > 0)
            title.Children.Add(new TextBlock
            {
                Text = release.Date, Foreground = T.TextFaint, FontFamily = T.MonoFont, FontSize = T.SmallFontSize,
                VerticalAlignment = VerticalAlignment.Center,
            });
        panel.Children.Add(title);
        foreach (ChangelogGroup group in release.Groups)
        {
            panel.Children.Add(new TextBlock
            {
                Text = group.Name.ToUpperInvariant(), Foreground = T.TextFaint, FontFamily = T.MonoFont, FontSize = 9,
                FontWeight = FontWeight.Bold, LetterSpacing = 1.2, Margin = new Thickness(0, 6, 0, 6),
            });
            foreach (string entry in group.Entries)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("14,*"), Margin = new Thickness(0, 0, 0, 6) };
                row.Children.Add(new TextBlock { Text = "•", Foreground = T.TextFaint, FontSize = T.FontSize, LineHeight = 18 });
                row.Children.Add(new TextBlock
                {
                    Text = entry, Foreground = T.TextRow, FontFamily = T.UiFont, FontSize = T.FontSize, LineHeight = 18,
                    TextWrapping = TextWrapping.Wrap, [Grid.ColumnProperty] = 1,
                });
                panel.Children.Add(row);
            }
        }
        return panel;
    }
}
