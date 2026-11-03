using Avalonia.Markup.Xaml;

namespace HikariHopper.Views;

/// <summary>About HikariHopper, shown as a sheet over the main window.</summary>
public partial class AboutDialog : ModalSheet
{
    public AboutDialog()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
