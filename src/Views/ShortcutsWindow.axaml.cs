using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>One key and what it does.</summary>
public sealed record ShortcutEntry(string Key, string Action);

/// <summary>A titled block of the keyboard reference.</summary>
public sealed record ShortcutGroup(string Name, IReadOnlyList<ShortcutEntry> Keys, bool IsFirst = false);

/// <summary>
/// The keyboard reference (port of <c>ShortcutsWindow.qml</c>). Every entry is a shortcut the
/// app actually binds.
/// </summary>
public partial class ShortcutsWindow : ToolWindow
{
    public ShortcutsWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = this;
        FlickScrolling.Attach(this.FindControl<ScrollViewer>("Flick")!);
    }

    public IReadOnlyList<ShortcutGroup> Groups { get; } =
    [
        new("FILE",
        [
            new("Ctrl+T", "New explorer tab"),
            new("Ctrl+W", "Close current tab"),
            new("Ctrl+O", "Open the configured game"),
            new("Ctrl+Shift+O", "Open a standalone RPF"),
            new("Alt+F4", "Exit"),
        ], IsFirst: true),
        new("GO",
        [
            new("Alt+Left", "Back"),
            new("Alt+Right", "Forward"),
            new("Backspace", "Up one level"),
            new("Enter", "Open the selected entry"),
        ]),
        new("EDIT",
        [
            new("Ctrl+A", "Select all visible entries"),
            new("Ctrl+C", "Copy name"),
            new("Ctrl+Shift+C", "Copy path"),
            new("Delete", "Delete selected files"),
            new("F3 · Ctrl+F", "Search this folder"),
            new("Esc", "Clear the search"),
        ]),
    ];
}
