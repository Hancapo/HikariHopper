using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using HikariHopper.Core;
using T = HikariHopper.Ui.Theme;

namespace HikariHopper.Views;

/// <summary>
/// What a texture tool would do, at the foot of its dialog: what the selection is now and what it would be after,
/// side by side, each with the memory it takes and a bar for it, so the saving is seen without reading the numbers.
/// Under them, a line for anything about the result worth a second look, only when there is one.
/// </summary>
public sealed class TextureOutcome : StackPanel
{
    private readonly Cell now = new("NOW", outlined: false);
    private readonly Cell after = new("AFTER", outlined: true);
    private readonly StackPanel warnings = new() { Spacing = 5, Margin = new Thickness(0, 9, 0, 0), IsVisible = false };

    public TextureOutcome()
    {
        var cells = new Grid { ColumnDefinitions = new ColumnDefinitions("*,22,*") };
        cells.Children.Add(now.Root);
        var arrow = new TextBlock
        {
            Text = "→", Foreground = T.TextFaint, FontFamily = T.MonoFont, FontSize = T.FontSize,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(arrow, 1);
        cells.Children.Add(arrow);
        Grid.SetColumn(after.Root, 2);
        cells.Children.Add(after.Root);
        Children.Add(cells);
        Children.Add(warnings);
    }

    /// <summary>What the selection is now, as shown.</summary>
    internal string NowValue => now.Value.Text ?? "";

    /// <summary>The memory it takes now, as shown; empty when it isn't known.</summary>
    internal string NowMemory => now.Memory.Text ?? "";

    /// <summary>What it would be after, as shown.</summary>
    internal string AfterValue => after.Value.Text ?? "";

    /// <summary>The memory it would take, as shown; empty when it isn't known.</summary>
    internal string AfterMemory => after.Memory.Text ?? "";

    /// <summary>The share of its track each bar fills, 0 to 1: the larger of the two fills it.</summary>
    internal (double Now, double After) Bars => (now.Share, after.Share);

    /// <summary>Whether the after bar wears the colour of a saving: it would take less than now.</summary>
    internal bool Saves => ReferenceEquals(after.Fill.Background, T.Selection);

    /// <summary>The lines under the cells, as shown.</summary>
    internal IReadOnlyList<string> Warnings => [.. warnings.Children.OfType<Grid>().Select(row => ((TextBlock)row.Children[1]).Text ?? "")];

    /// <summary>
    /// Shows what the selection is now and what it would be: each a value (a size, a format, a count of mips) and the
    /// bytes it takes, which a negative count leaves out; then the warnings, if any.
    /// </summary>
    public void Show(string nowValue, long nowBytes, string afterValue, long afterBytes, IReadOnlyList<string>? notes = null)
    {
        long most = Math.Max(nowBytes, afterBytes);
        now.Show(nowValue, nowBytes, most, T.TextDim);
        // Green for a saving; what stays the same or grows is drawn as now is.
        after.Show(afterValue, afterBytes, most, afterBytes >= 0 && afterBytes < nowBytes ? T.Selection : T.TextDim);
        warnings.Children.Clear();
        foreach (string note in notes ?? [])
            warnings.Children.Add(Warning(note));
        warnings.IsVisible = warnings.Children.Count > 0;
        AutomationProperties.SetName(this, $"Now {nowValue}, {NowMemory}. After {afterValue}, {AfterMemory}."
            + string.Concat((notes ?? []).Select(note => " " + note)));
    }

    /// <summary>A line worth a second look, as the outcome draws its own: a small mark and the text, in the warning colour or another.</summary>
    internal static Grid Warning(string text, IImmutableSolidColorBrush? ink = null)
    {
        ink ??= T.Warning;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("13,7,*") };
        row.Children.Add(new Border
        {
            Width = 13, Height = 13, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 1, 0, 0),
            Background = new ImmutableSolidColorBrush(T.WithAlpha(ink.Color, .18)),
            Child = new TextBlock
            {
                Text = "!", Foreground = ink, FontFamily = T.MonoFont, FontSize = 9, FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        });
        var line = new TextBlock
        {
            Text = text, Foreground = ink, FontFamily = T.UiFont, FontSize = T.FontSize,
            TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(line, 2);
        row.Children.Add(line);
        return row;
    }

    /// <summary>
    /// A cell in the outcome's look that counts something and names it: how many images an import adds, and which.
    /// </summary>
    internal static Border Count(string caption, int count, IReadOnlyList<string> names, bool outlined) => new()
    {
        Background = T.InsetBg,
        CornerRadius = T.ControlCorner,
        BorderBrush = outlined ? T.Border : Brushes.Transparent,
        BorderThickness = new Thickness(1),
        Padding = new Thickness(9, 7, 9, 8),
        Child = new StackPanel
        {
            Children =
            {
                new TextBlock
                {
                    Text = caption, Foreground = T.TextFaint, FontFamily = T.MonoFont, FontSize = T.CapsFontSize,
                    FontWeight = FontWeight.Bold, LetterSpacing = 1.3, Margin = new Thickness(0, 0, 0, 6),
                },
                new TextBlock
                {
                    Text = count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    FontFamily = T.MonoFont, FontSize = 13, Foreground = count > 0 ? T.Text : T.TextFaint,
                },
                new TextBlock
                {
                    Text = Names(names), Margin = new Thickness(0, 6, 0, 0), IsVisible = names.Count > 0,
                    FontFamily = T.MonoFont, FontSize = T.SmallFontSize, Foreground = T.TextFaint,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
            },
        },
    };

    /// <summary>Names on a line: the first few, then how many more.</summary>
    internal static string Names(IReadOnlyList<string> names, int most = 3) => names.Count <= most
        ? string.Join(" · ", names)
        : string.Join(" · ", names.Take(most)) + $" · +{names.Count - most} more";

    /// <summary>One side: its caption, its value, and the memory with its bar.</summary>
    private sealed class Cell
    {
        private readonly Grid track = new() { Height = 4, VerticalAlignment = VerticalAlignment.Center };
        private readonly Border well;

        public Cell(string caption, bool outlined)
        {
            Value = new TextBlock
            {
                FontFamily = T.MonoFont, FontSize = 13, Foreground = outlined ? T.Text : T.TextDim,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Memory = new TextBlock
            {
                FontFamily = T.MonoFont, FontSize = T.SmallFontSize, Foreground = T.TextFaint,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
            };
            well = new Border { CornerRadius = new CornerRadius(2), Background = T.Border, Opacity = .45 };
            Fill = new Border { CornerRadius = new CornerRadius(2), Background = T.TextDim, HorizontalAlignment = HorizontalAlignment.Stretch };
            track.Children.Add(well);
            track.Children.Add(Fill);
            var memory = new DockPanel { Margin = new Thickness(0, 7, 0, 0) };
            DockPanel.SetDock(Memory, Dock.Left);
            memory.Children.Add(Memory);
            memory.Children.Add(track);
            Root = new Border
            {
                Background = T.InsetBg,
                CornerRadius = T.ControlCorner,
                BorderBrush = outlined ? T.Border : Brushes.Transparent,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 7, 9, 8),
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = caption, Foreground = T.TextFaint, FontFamily = T.MonoFont, FontSize = T.CapsFontSize,
                            FontWeight = FontWeight.Bold, LetterSpacing = 1.3, Margin = new Thickness(0, 0, 0, 6),
                        },
                        Value,
                        memory,
                    },
                },
            };
        }

        public Border Root { get; }

        public TextBlock Value { get; }

        public TextBlock Memory { get; }

        public Border Fill { get; }

        public double Share { get; private set; }

        public void Show(string value, long bytes, long most, IBrush ink)
        {
            Value.Text = value;
            Memory.Text = bytes >= 0 ? Formatting.FormatSize(bytes) : "";
            Share = bytes > 0 && most > 0 ? Math.Clamp((double)bytes / most, 0, 1) : 0;
            track.IsVisible = bytes >= 0;
            // The fill takes its share of the track: a column of that weight beside one of the rest.
            track.ColumnDefinitions = new ColumnDefinitions($"{Math.Max(Share, .0001).ToString(System.Globalization.CultureInfo.InvariantCulture)}*,"
                + $"{Math.Max(1 - Share, .0001).ToString(System.Globalization.CultureInfo.InvariantCulture)}*");
            Grid.SetColumnSpan(well, 2);
            Fill.Background = ink;
            Fill.IsVisible = Share > 0;
        }
    }
}
