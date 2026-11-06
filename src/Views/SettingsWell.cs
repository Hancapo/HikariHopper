using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Metadata;
using T = HikariHopper.Ui.Theme;

namespace HikariHopper.Views;

/// <summary>A group of settings: its rows in a well, a hairline between each two.</summary>
public sealed class SettingsWell : Control
{
    private readonly StackPanel stack = new();
    private readonly Border well;

    public SettingsWell()
    {
        well = new Border
        {
            Background = T.WindowBg, BorderBrush = T.BorderHard, BorderThickness = new Thickness(1), CornerRadius = T.PanelCorner,
            Child = stack,
        };
        LogicalChildren.Add(well);
        VisualChildren.Add(well);
        Rows.CollectionChanged += (_, _) => Rebuild();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        well.Measure(availableSize);
        return well.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        well.Arrange(new Rect(finalSize));
        return finalSize;
    }

    /// <summary>The rows, first to last; a row that isn't visible takes no hairline either.</summary>
    [Content]
    public AvaloniaList<Control> Rows { get; } = [];

    private void Rebuild()
    {
        stack.Children.Clear();
        for (int index = 0; index < Rows.Count; index++)
        {
            Control row = Rows[index];
            if (index > 0)
            {
                var hairline = new Border { Height = 1, Background = T.BorderSoft, Margin = new Thickness(14, 0) };
                // The hairline above a row goes with it, so a hidden row leaves no double line.
                hairline.Bind(IsVisibleProperty, row.GetObservable(IsVisibleProperty));
                stack.Children.Add(hairline);
            }
            stack.Children.Add(row);
        }
    }
}
