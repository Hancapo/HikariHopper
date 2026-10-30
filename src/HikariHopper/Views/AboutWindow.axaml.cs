using Avalonia.Markup.Xaml;

namespace HikariHopper.Views;

/// <summary>About HikariHopper (port of <c>AboutWindow.qml</c>).</summary>
public partial class AboutWindow : ToolWindow
{
    public AboutWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
