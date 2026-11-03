using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace HikariHopper.Views;

/// <summary>One QML <c>Shortcut { sequence; enabled; onActivated }</c>.</summary>
public sealed record WindowShortcut(KeyGesture Gesture, Func<bool> Enabled, Action Activated)
{
    public WindowShortcut(string gesture, Func<bool> enabled, Action activated)
        : this(KeyGesture.Parse(gesture), enabled, activated)
    {
    }

    public WindowShortcut(string gesture, Action activated)
        : this(KeyGesture.Parse(gesture), () => true, activated)
    {
    }
}

/// <summary>
/// QML <c>Shortcut</c>s with the default <c>Qt.WindowShortcut</c> context, registered while their owner
/// is in a window: they see a key press before the focused control does, are blocked while a modal
/// overlay is open (Qt Quick Controls' shortcut matcher), and give way when a focused text field
/// claims the key in its <c>ShortcutOverride</c> (copy/paste/undo, Backspace, Delete, plain typing…).
/// A disabled shortcut lets the key through to the focused control.
/// </summary>
public static class WindowShortcuts
{
    public static void Register(Control owner, params WindowShortcut[] shortcuts)
    {
        TopLevel? host = null;
        void OnKeyDown(object? sender, KeyEventArgs e) => Dispatch(host, shortcuts, e);
        owner.AttachedToVisualTree += (_, _) =>
        {
            host = TopLevel.GetTopLevel(owner);
            host?.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        };
        owner.DetachedFromVisualTree += (_, _) =>
        {
            host?.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
            host = null;
        };
    }

    private static void Dispatch(TopLevel? host, WindowShortcut[] shortcuts, KeyEventArgs e)
    {
        if (host is null || e.Handled || ModalOverlay.IsOpen(host))
            return;
        if (host.FocusManager?.GetFocusedElement() is TextBox { IsReadOnly: false } && TextFieldOverrides(e))
            return;
        foreach (WindowShortcut shortcut in shortcuts)
        {
            if (!shortcut.Gesture.Matches(e) || !shortcut.Enabled())
                continue;
            shortcut.Activated();
            e.Handled = true;
            return;
        }
    }

    /// <summary>The keys QQuickTextInput accepts in its ShortcutOverride event.</summary>
    internal static bool TextFieldOverrides(KeyEventArgs e)
    {
        KeyModifiers modifiers = e.KeyModifiers;
        bool control = modifiers == KeyModifiers.Control;
        bool controlShift = modifiers == (KeyModifiers.Control | KeyModifiers.Shift);
        // QKeySequence::Copy, Paste, Cut, Undo, Redo, word/document moves and selections, SelectAll.
        if (control && e.Key is Key.C or Key.V or Key.X or Key.Z or Key.Y or Key.A or Key.Insert
                or Key.Left or Key.Right or Key.Home or Key.End)
            return true;
        if (controlShift && e.Key is Key.Z or Key.Left or Key.Right or Key.Home or Key.End)
            return true;
        if (modifiers == KeyModifiers.Shift && e.Key is Key.Insert or Key.Delete)
            return true;
        if (modifiers is not (KeyModifiers.None or KeyModifiers.Shift))
            return false;
        // Printable keys (below Qt::Key_Escape) plus the editing keys.
        return e.Key is Key.Delete or Key.Home or Key.End or Key.Back or Key.Left or Key.Right or Key.Space
            || e.KeySymbol is { Length: > 0 } symbol && !char.IsControl(symbol[0]);
    }
}
