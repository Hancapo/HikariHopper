using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace HikariHopper.Views.Controls;

/// <summary>
/// A keyboard key printed in the interface (the search field's F3, the status bar's Enter): bold
/// mono 10 on a raised plate with 4px corners, a 1px outer ring and a dark lower edge, so it reads
/// as a key rather than a badge.
/// </summary>
public class KeyCap : Border
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<KeyCap, string>(nameof(Text), "");

    /// <summary>A Lucide mark printed instead of <see cref="Text"/> (the Backspace key's arrow).</summary>
    public static readonly StyledProperty<string> IconNameProperty =
        AvaloniaProperty.Register<KeyCap, string>(nameof(IconName), "");

    private readonly LucideIcon icon = new()
    {
        Width = 11,
        Height = 11,
        Weight = 1.4,
        Stroke = Ui.Theme.TextRow,
        IsVisible = false,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly TextBlock label = new()
    {
        FontFamily = Ui.Theme.MonoFont,
        FontSize = Ui.Theme.SmallFontSize,
        FontWeight = FontWeight.Bold,
        Foreground = Ui.Theme.TextRow,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    static KeyCap()
    {
        AutomationProperties.AccessibilityViewProperty.OverrideDefaultValue<KeyCap>(AccessibilityView.Raw);
    }

    public KeyCap()
    {
        Height = 18;
        MinWidth = 18;
        VerticalAlignment = VerticalAlignment.Center;
        CornerRadius = Ui.Theme.KeyCapCorner;
        Background = Ui.Theme.ChromeRaised;
        BorderBrush = Ui.Theme.Border;
        BorderThickness = new Thickness(1);
        Padding = new Thickness(5, 0);
        var lowerEdge = new Border
        {
            Height = 1,
            Margin = new Thickness(-2, 0),
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = Ui.Theme.BorderHard,
        };
        Child = new Panel { Children = { label, icon, lowerEdge } };
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string IconName
    {
        get => GetValue(IconNameProperty);
        set => SetValue(IconNameProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(Border);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
            label.Text = Text;
        else if (change.Property == IconNameProperty)
        {
            icon.IconName = IconName;
            icon.IsVisible = IconName.Length > 0;
            label.IsVisible = IconName.Length == 0;
        }
    }
}
