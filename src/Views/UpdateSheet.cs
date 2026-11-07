using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using HikariHopper.Core;
using HikariHopper.Views.Controls;
using T = HikariHopper.Ui.Theme;

namespace HikariHopper.Views;

/// <summary>
/// HikariHopper's updates, as a sheet. A new version shows the version it goes to, what changed in it, and the
/// choice to update now, later or never for that version, then the download's progress. Checking by hand shows
/// the check, and then that the app is up to date, or why it couldn't tell.
/// </summary>
/// <remarks>The sheet only shows; <see cref="UpdateFlow"/> checks, downloads and answers its buttons.</remarks>
public sealed class UpdateSheet : ModalSheet
{
    private static readonly Uri Icon = new("avares://HikariHopper/Assets/Branding/icon-256.png");

    private readonly string current;
    private ThinProgress? progress;
    private TextBlock? progressText;

    /// <summary>A sheet about updating <paramref name="current"/>, the version running.</summary>
    public UpdateSheet(string current)
    {
        this.current = current;
        Heading = "UPDATE";
        ShowChecking();
    }

    /// <summary>Update and restart.</summary>
    public event EventHandler? UpdateRequested;

    /// <summary>Skip this version.</summary>
    public event EventHandler? SkipRequested;

    /// <summary>Cancel, during the download.</summary>
    public event EventHandler? CancelRequested;

    /// <summary>What the sheet shows: checking, up to date, unavailable, failed, available or downloading.</summary>
    internal string State { get; private set; } = "";

    /// <summary>The line under the app's name, the note in the footer, or the download's progress, as shown.</summary>
    internal string Detail { get; private set; } = "";

    /// <summary>The version offered, while one is.</summary>
    internal string? Offered { get; private set; }

    internal void ShowChecking() => Small("checking", "Checking for updates…");

    internal void ShowUpToDate() => Small("up to date", "Up to date");

    internal void ShowUnavailable() => Small("unavailable", "Only release builds update themselves");

    internal void ShowFailed() => Small("failed", "Couldn't check for updates");

    /// <summary>
    /// Offers <paramref name="next"/> as <paramref name="version"/>, a download of <paramref name="size"/> bytes;
    /// <paramref name="note"/>, if any, says in the footer why it can't be installed yet.
    /// </summary>
    internal void ShowAvailable(string version, ChangelogRelease next, long size, string? note = null)
    {
        State = "available";
        Offered = version;
        progress = null;
        WindowShare = new Size(0.42, 0.7);
        MinSize = new Size(460, 380);
        MaxSize = new Size(560, 560);
        string released = next.Date.Length > 0 ? $"Released {next.Date} · " : "";
        string detail = $"{released}{Megabytes(size)} download";
        Detail = note ?? detail;
        Content = Available(version, next, detail);
        var update = new ChromeToolButton { Width = 150, Height = 28, Primary = true, Text = "Update and restart" };
        update.Click += (_, _) => UpdateRequested?.Invoke(this, EventArgs.Empty);
        var later = Button("Later", raised: true);
        later.Click += (_, _) => Close();
        Control lead;
        if (note is null)
        {
            var skip = Button("Skip this version", raised: false);
            skip.Click += (_, _) => SkipRequested?.Invoke(this, EventArgs.Empty);
            lead = skip;
        }
        else
            lead = new TextBlock
            {
                Text = note, Foreground = T.Error, FontFamily = T.UiFont, FontSize = T.SmallFontSize,
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            };
        Footer = Bar(lead, later, update);
    }

    /// <summary>Shows the download under way: <paramref name="share"/> (0 to 1) of <paramref name="size"/> bytes.</summary>
    internal void ShowProgress(double share, long size)
    {
        if (progress is null)
        {
            State = "downloading";
            progress = new ThinProgress { Width = 140, VerticalAlignment = VerticalAlignment.Center };
            progressText = Faint("");
            var lead = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
            lead.Children.Add(progress);
            lead.Children.Add(progressText);
            var cancel = Button("Cancel", raised: true);
            cancel.Click += (_, _) => CancelRequested?.Invoke(this, EventArgs.Empty);
            Footer = Bar(lead, cancel);
        }
        progress.Value = share;
        Detail = progressText!.Text = string.Create(CultureInfo.InvariantCulture, $"Downloading · {share * 100:0}% of {Megabytes(size)}");
    }

    private void Small(string state, string detail)
    {
        State = state;
        Detail = detail;
        Offered = null;
        progress = null;
        WindowShare = new Size(0.36, 0.3);
        MinSize = new Size(420, 230);
        MaxSize = new Size(460, 230);
        Content = Lockup(null, detail);
        Footer = null;
    }

    private Control Available(string version, ChangelogRelease next, string detail)
    {
        var body = new DockPanel();
        Control top = Lockup(version, detail);
        top.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(top, Dock.Top);
        body.Children.Add(top);
        var rule = new DottedRule { Horizontal = true, Margin = new Thickness(0, 16, 0, 12) };
        DockPanel.SetDock(rule, Dock.Top);
        body.Children.Add(rule);
        var notes = new StackPanel();
        notes.Children.Add(new TextBlock
        {
            Text = "WHAT'S NEW", Foreground = T.TextDim, FontFamily = T.MonoFont, FontSize = T.SmallFontSize,
            FontWeight = FontWeight.Bold, LetterSpacing = 1.2, Margin = new Thickness(0, 0, 0, 2),
        });
        ChangelogDialog.AddGroups(notes, next);
        var scroller = new ScrollViewer
        {
            Theme = Application.Current?.FindResource("QuietScrollViewer") as ControlTheme,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = new Border { Background = Brushes.Transparent, Padding = new Thickness(0, 0, 14, 4), Child = notes },
        };
        SmoothScrolling.Attach(scroller);
        body.Children.Add(scroller);
        return body;
    }

    /// <summary>The app's icon and name, over the version running (and the one offered) and <paramref name="detail"/>.</summary>
    private Control Lockup(string? next, string detail)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), VerticalAlignment = VerticalAlignment.Center };
        var icon = new Image { Width = 44, Height = 44, VerticalAlignment = VerticalAlignment.Top, Source = new Bitmap(AssetLoader.Open(Icon)) };
        RenderOptions.SetBitmapInterpolationMode(icon, BitmapInterpolationMode.HighQuality);
        grid.Children.Add(icon);
        var text = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, [Grid.ColumnProperty] = 1 };
        text.Children.Add(new TextBlock
        {
            Text = "HikariHopper", Foreground = T.Text, FontFamily = T.UiFont, FontSize = 15, FontWeight = FontWeight.Bold,
        });
        // The versions in sunken chips: the one running, and the one it goes to.
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
        chips.Children.Add(Chip(current, next is null ? T.TextRow : T.TextDim));
        if (next is not null)
        {
            chips.Children.Add(new ChromeIcon { Kind = "chevron", Width = 10, Height = 10, Stroke = T.TextFaint, VerticalAlignment = VerticalAlignment.Center });
            chips.Children.Add(Chip(next, T.Accent));
        }
        chips.Children.Add(Faint(detail, new Thickness(6, 0, 0, 0)));
        text.Children.Add(chips);
        grid.Children.Add(text);
        return grid;
    }

    private static Grid Bar(Control lead, params Control[] actions)
    {
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        lead.HorizontalAlignment = HorizontalAlignment.Left;
        bar.Children.Add(lead);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(12, 0, 0, 0), [Grid.ColumnProperty] = 1 };
        foreach (Control action in actions)
            right.Children.Add(action);
        bar.Children.Add(right);
        return bar;
    }

    private static ChromeToolButton Button(string text, bool raised) =>
        new() { Height = 28, Width = raised ? 88 : 128, Raised = raised, Bordered = raised, Text = text };

    private static Border Chip(string text, IBrush ink) => new()
    {
        Padding = new Thickness(8, 3), Background = T.InsetBg, CornerRadius = T.ControlCorner, VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, Foreground = ink, FontFamily = T.MonoFont, FontSize = T.SmallFontSize, FontWeight = FontWeight.Bold },
    };

    private static TextBlock Faint(string text, Thickness margin = default) => new()
    {
        Text = text, Foreground = T.TextFaint, FontFamily = T.MonoFont, FontSize = T.SmallFontSize,
        VerticalAlignment = VerticalAlignment.Center, Margin = margin,
    };

    private static string Megabytes(long bytes) => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1048576.0:0.0} MB");
}
