using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using HikariHopper.Core;
using HikariHopper.Views.Controls;
using T = HikariHopper.Ui.Theme;

namespace HikariHopper.Views;

/// <summary>
/// One file's or folder's properties, as a sheet: its icon, name and type, then where it is, how big it is and,
/// for a resource or an RPF, what is in it, each group under the sheets' small caps.
/// </summary>
public sealed class PropertiesDialog : ModalSheet
{
    public PropertiesDialog(EntryProperties properties)
    {
        Properties = properties;
        Heading = "PROPERTIES";
        FooterText = "Alt+Enter";
        WindowShare = new Size(0.42, 0.8);
        MinSize = new Size(440, 320);
        MaxSize = new Size(520, 640);
        // A file's are a few rows, an RPF's or a model's many: as tall as they are, not a sheet mostly empty.
        FitsContent = true;

        var list = new StackPanel();
        list.Children.Add(Identity(properties));
        foreach (PropertySection section in properties.Sections)
        {
            list.Children.Add(new DottedRule { Horizontal = true, Margin = new Thickness(0, 14, 0, 12) });
            list.Children.Add(Section(section));
        }
        var scroller = new ScrollViewer
        {
            Theme = Application.Current?.FindResource("QuietScrollViewer") as ControlTheme,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = new Border { Background = Brushes.Transparent, Padding = new Thickness(0, 0, 14, 8), Child = list },
        };
        SmoothScrolling.Attach(scroller);
        Content = scroller;
    }

    /// <summary>What it shows.</summary>
    public EntryProperties Properties { get; }

    private static Control Identity(EntryProperties properties)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        row.Children.Add(new FileGlyph
        {
            Kind = properties.Glyph, FileKind = properties.Kind, Width = 32, Height = 32,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0),
        });
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, [Grid.ColumnProperty] = 1 };
        text.Children.Add(new SelectableTextBlock
        {
            Text = properties.Name, Foreground = T.Text, FontFamily = T.UiFont, FontSize = 14, FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        text.Children.Add(new TextBlock
        {
            Text = properties.Kind, Foreground = T.TextFaint, FontFamily = T.UiFont, FontSize = T.SmallFontSize,
        });
        row.Children.Add(text);
        return row;
    }

    private static Control Section(PropertySection section)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = section.Heading, Foreground = T.TextFaint, FontFamily = T.MonoFont, FontSize = 9,
            FontWeight = FontWeight.Bold, LetterSpacing = 1.2, Margin = new Thickness(0, 0, 0, 8),
        });
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("112,*") };
        for (int index = 0; index < section.Rows.Count; index++)
        {
            (string label, string value) = section.Rows[index];
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.Children.Add(new TextBlock
            {
                Text = label, Foreground = T.TextDim, FontFamily = T.UiFont, FontSize = 12, LineHeight = 18,
                Margin = new Thickness(0, 0, 8, 6), [Grid.RowProperty] = index,
            });
            grid.Children.Add(new SelectableTextBlock
            {
                Text = value, Foreground = T.TextRow, FontFamily = T.MonoFont, FontSize = 11, LineHeight = 18,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6),
                [Grid.RowProperty] = index, [Grid.ColumnProperty] = 1,
            });
        }
        panel.Children.Add(grid);
        return panel;
    }
}
