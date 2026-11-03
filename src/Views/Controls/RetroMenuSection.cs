using Avalonia;
using Avalonia.Controls;

namespace HikariHopper.Views.Controls;

/// <summary>A non-interactive mono caps heading inside a menu (port of <c>RetroMenuSection.qml</c>).</summary>
public class RetroMenuSection : Separator
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<RetroMenuSection, string>(nameof(Text), "");

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}
