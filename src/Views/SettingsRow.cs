using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Metadata;
using T = HikariHopper.Ui.Theme;

namespace HikariHopper.Views;

/// <summary>
/// One setting in a <see cref="SettingsWell"/>: its <see cref="Title"/> and a line saying what it does on the left,
/// and its control (<see cref="Actions"/>) on the right.
/// </summary>
public sealed class SettingsRow : Control
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<SettingsRow, string>(nameof(Title), "");

    public static readonly StyledProperty<string> DetailProperty =
        AvaloniaProperty.Register<SettingsRow, string>(nameof(Detail), "");

    public static readonly StyledProperty<Control?> ActionsProperty =
        AvaloniaProperty.Register<SettingsRow, Control?>(nameof(Actions));

    private readonly TextBlock titleText = new()
    {
        Foreground = T.Text, FontFamily = T.UiFont, FontSize = 13, FontWeight = FontWeight.SemiBold,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private readonly TextBlock detailText = new()
    {
        Foreground = T.TextDim, FontFamily = T.UiFont, FontSize = T.SmallFontSize, Margin = new Thickness(0, 4, 0, 0),
        TextWrapping = TextWrapping.Wrap,
    };

    private readonly ContentControl actions = new()
    {
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0), [Grid.ColumnProperty] = 1,
    };

    private readonly Grid grid = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

    public SettingsRow()
    {
        Margin = new Thickness(14, 11, 12, 11);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(titleText);
        text.Children.Add(detailText);
        grid.Children.Add(text);
        grid.Children.Add(actions);
        LogicalChildren.Add(grid);
        VisualChildren.Add(grid);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        grid.Measure(availableSize);
        return grid.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        grid.Arrange(new Rect(finalSize));
        return finalSize;
    }

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>What the setting does, or what it holds now.</summary>
    public string Detail
    {
        get => GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    /// <summary>The setting's control: a switch, a button or a few.</summary>
    [Content]
    public Control? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TitleProperty)
        {
            titleText.Text = Title;
            // A switch is named after the setting it turns on and off.
            if (Actions is Controls.OnOffSwitch toggle)
                AutomationProperties.SetName(toggle, Title);
        }
        else if (change.Property == DetailProperty)
        {
            detailText.Text = Detail;
            detailText.IsVisible = Detail.Length > 0;
        }
        else if (change.Property == ActionsProperty)
        {
            actions.Content = Actions;
            if (Actions is Controls.OnOffSwitch toggle)
                AutomationProperties.SetName(toggle, Title);
        }
    }
}
