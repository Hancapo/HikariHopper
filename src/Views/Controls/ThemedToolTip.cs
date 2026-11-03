using Avalonia;
using Avalonia.Controls;

namespace HikariHopper.Views.Controls;

/// <summary>A tooltip in the app's own look: the raised chrome with a hairline border, like its menus.</summary>
public static class ThemedToolTip
{
    public static ToolTip Create(string text) => new()
    {
        Content = new TextBlock
        {
            Text = text,
            Foreground = Ui.Theme.Text,
            FontFamily = Ui.Theme.UiFont,
            FontSize = Ui.Theme.FontSize,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        },
        Background = Ui.Theme.ChromeRaised,
        BorderBrush = Ui.Theme.Border,
        BorderThickness = new Thickness(1),
        CornerRadius = Ui.Theme.ControlCorner,
        Padding = new Thickness(8, 5),
        MaxWidth = 360,
    };

    /// <summary>Gives <paramref name="control"/> a themed tooltip.</summary>
    public static void Set(Control control, string text) => ToolTip.SetTip(control, Create(text));
}
