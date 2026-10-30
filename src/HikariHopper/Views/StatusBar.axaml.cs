using Avalonia;
using Avalonia.Controls;

namespace HikariHopper.Views;

/// <summary>The explorer status bar (port of <c>StatusBar.qml</c>); its DataContext is the active tab's view model.</summary>
public partial class StatusBar : UserControl
{
    public StatusBar()
    {
        InitializeComponent();
        // Layout.leftMargin: statusHintInset, then the RowLayout spacing (14) and a trailing
        // statusHintInset spacer item.
        Hints.Margin = new Thickness(Ui.Theme.StatusHintInset, 0, Hints.Spacing + Ui.Theme.StatusHintInset, 0);
    }
}
