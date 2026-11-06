using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Metadata;
using T = HikariHopper.Ui.Theme;

namespace HikariHopper.Views;

/// <summary>
/// A group of settings: its rows on a card of the chrome's colour, a seam between each two. The chrome, like the
/// toolbar, is what the switches' sunken wells and the raised buttons are drawn to stand out against.
/// </summary>
public sealed class SettingsCard : Control
{
    private readonly StackPanel stack = new();
    private readonly Border card;

    public SettingsCard()
    {
        card = new Border
        {
            Background = T.ChromeBg, BorderBrush = T.BorderHard, BorderThickness = new Thickness(1), CornerRadius = T.PanelCorner,
            Child = stack,
        };
        LogicalChildren.Add(card);
        VisualChildren.Add(card);
        Rows.CollectionChanged += (_, _) => Rebuild();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        card.Measure(availableSize);
        return card.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        card.Arrange(new Rect(finalSize));
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
                var hairline = new Border { Height = 1, Background = T.BorderHard, Margin = new Thickness(14, 0) };
                // The hairline above a row goes with it, so a hidden row leaves no double line.
                hairline.Bind(IsVisibleProperty, row.GetObservable(IsVisibleProperty));
                stack.Children.Add(hairline);
            }
            stack.Children.Add(row);
        }
    }
}
