using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>One key and what it does.</summary>
public sealed record ShortcutEntry(string Key, string Action);

/// <summary>A titled block of the keyboard reference.</summary>
public sealed record ShortcutGroup(string Name, IReadOnlyList<ShortcutEntry> Keys, bool IsFirst = false);

/// <summary>
/// The keyboard reference, shown as a sheet over the main window. Every entry is a shortcut the
/// app actually binds.
/// </summary>
public partial class ShortcutsDialog : ModalSheet
{
    public ShortcutsDialog()
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = this;
        FlickScrolling.Attach(this.FindControl<ScrollViewer>("Flick")!);
    }

    public IReadOnlyList<ShortcutGroup> Groups { get; } =
    [
        new("FILE",
        [
            new("Ctrl+T", "New tab"),
            new("Ctrl+W", "Close tab"),
            new("Ctrl+O", "Open configured game"),
            new("Ctrl+Shift+O", "Open RPF archive"),
            new("Alt+F4", "Exit"),
        ], IsFirst: true),
        new("GO",
        [
            new("Alt+Left", "Back"),
            new("Alt+Right", "Forward"),
            new("Backspace", "Up one level"),
            new("Enter", "Open the selected entry"),
            new("Ctrl+Shift+F", "Search the whole game"),
            new("Ctrl+Enter", "Open a search result's location"),
        ]),
        new("EDIT",
        [
            new("Ctrl+A", "Select all visible entries"),
            new("Ctrl+C", "Copy"),
            new("Ctrl+Shift+C", "Copy path"),
            new("Del", "Delete the selected items"),
            new("F3 · Ctrl+F", "Search this folder"),
            new("Esc", "Leave the search"),
        ]),
    ];
}
