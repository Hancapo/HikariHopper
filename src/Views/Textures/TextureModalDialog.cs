using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HikariHopper.ViewModels;

namespace HikariHopper.Views;

/// <summary>
/// A modal in-window dialog, as a QML <c>Dialog { modal: true; parent: Overlay.overlay;
/// closePolicy: Popup.CloseOnEscape }</c>: shown centred in a <see cref="TextureDialogLayer"/>
/// over a <c>marqueeFill</c> dimmer that swallows the pointer, holding keyboard focus (Tab cycles
/// inside), closed with Esc as a rejection. Closing gives focus back to what had it before.
/// </summary>
/// <remarks>
/// <see cref="Accept"/>/<see cref="Reject"/> raise <see cref="Accepted"/>/<see cref="Rejected"/> and
/// close; <see cref="Close"/> closes silently. <see cref="Opened"/> runs once the dialog is in the
/// layer (QML <c>onOpened</c>), <see cref="Closed"/> after it left (QML <c>onClosed</c>).
/// The <see cref="StyledElement.DataContext"/> is the <see cref="TextureViewerViewModel"/>; subclasses
/// override <see cref="OnViewModelChanged"/> to follow it.
/// </remarks>
public class TextureModalDialog : ContentControl
{
    private TextureDialogLayer? layer;
    private IInputElement? focusBefore;
    private TextureViewerViewModel? viewModel;

    static TextureModalDialog()
    {
        FocusableProperty.OverrideDefaultValue<TextureModalDialog>(true);
        FocusAdornerProperty.OverrideDefaultValue<TextureModalDialog>(null);
    }

    public TextureModalDialog()
    {
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
    }

    public event EventHandler? Opened;

    public event EventHandler? Closed;

    public event EventHandler? Accepted;

    public event EventHandler? Rejected;

    /// <summary>QML <c>visible</c>/<c>opened</c>.</summary>
    public bool IsOpen => layer is not null;

    public TextureViewerViewModel? ViewModel => viewModel;

    /// <summary>A plain content host; <see cref="TextureToolDialog"/> brings its own chrome.</summary>
    protected override Type StyleKeyOverride => typeof(ContentControl);

    /// <summary>Shows the dialog in <paramref name="host"/> and moves focus into it.</summary>
    public void Open(TextureDialogLayer host)
    {
        if (layer is not null)
            return;
        layer = host;
        focusBefore = TopLevel.GetTopLevel(host)?.FocusManager?.GetFocusedElement();
        host.Add(this);
        // Template and lay out the content now so onOpened can focus a field inside it.
        host.UpdateLayout();
        Focus();
        OnOpened();
        Opened?.Invoke(this, EventArgs.Empty);
    }

    public void Close()
    {
        if (layer is not { } host)
            return;
        layer = null;
        // Whether the keyboard is the dialog's to give back: inside it, or nowhere, as when the Apply button that had
        // it was disabled while its work ran.
        bool hadFocus = IsKeyboardFocusWithin || TopLevel.GetTopLevel(host)?.FocusManager?.GetFocusedElement() is null;
        host.Remove(this);
        // The keyboard goes back where it was; when that is gone (a menu's item, its popup closed, or a row the
        // dialog's work rebuilt), to the nearest focusable control around the dialog, the editor, so its shortcuts
        // (Ctrl+Z after a resize) work at once rather than after a click.
        if (hadFocus)
        {
            bool restored = focusBefore is InputElement { IsEffectivelyVisible: true } previous && TopLevel.GetTopLevel(previous) is not null
                && previous.Focus();
            if (!restored)
                host.GetVisualAncestors().OfType<InputElement>().FirstOrDefault(element => element.Focusable && element.IsEffectivelyVisible)?.Focus();
        }
        focusBefore = null;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    public void Accept()
    {
        Accepted?.Invoke(this, EventArgs.Empty);
        Close();
    }

    public void Reject()
    {
        Rejected?.Invoke(this, EventArgs.Empty);
        Close();
    }

    /// <summary>Called when the dialog is shown, before <see cref="Opened"/>.</summary>
    protected virtual void OnOpened()
    {
    }

    /// <summary>Called when the view model or any of its properties changes (<paramref name="property"/> null for a new view model).</summary>
    protected virtual void OnViewModelChanged(string? property)
    {
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (viewModel is not null)
            viewModel.PropertyChanged -= ViewModelPropertyChanged;
        viewModel = DataContext as TextureViewerViewModel;
        if (viewModel is not null)
            viewModel.PropertyChanged += ViewModelPropertyChanged;
        OnViewModelChanged(null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape && IsOpen)
        {
            e.Handled = true;
            Reject();
        }
        else if (!e.Handled && e.Key == Key.Tab && IsOpen && (e.KeyModifiers & ~KeyModifiers.Shift) == KeyModifiers.None)
        {
            // Tab stays inside a modal popup, as Qt's popup focus scope keeps it.
            e.Handled = MoveFocus(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
        }
    }

    /// <summary>The controls Tab visits, in visual order (the dialog itself is not one).</summary>
    public IReadOnlyList<InputElement> TabStops() =>
        [.. this.GetVisualDescendants().OfType<InputElement>().Where(element =>
            element.Focusable && element.IsTabStop && element.IsEffectivelyVisible && element.IsEffectivelyEnabled
            && !element.GetVisualAncestors().OfType<InputElement>().TakeWhile(ancestor => ancestor != this)
                .Any(ancestor => ancestor.Focusable && ancestor is TextBox or ComboBox))];

    private bool MoveFocus(int step)
    {
        IReadOnlyList<InputElement> stops = TabStops();
        if (stops.Count == 0)
            return false;
        IInputElement? focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        int index = focused is InputElement current ? IndexOf(stops, current) : -1;
        int next = index < 0 ? (step > 0 ? 0 : stops.Count - 1) : (index + step + stops.Count) % stops.Count;
        return stops[next].Focus(NavigationMethod.Tab, step < 0 ? KeyModifiers.Shift : KeyModifiers.None);
    }

    private static int IndexOf(IReadOnlyList<InputElement> stops, InputElement element)
    {
        for (int i = 0; i < stops.Count; i++)
        {
            if (stops[i] == element || element.GetVisualAncestors().Contains(stops[i]))
                return i;
        }
        return -1;
    }

    private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) => OnViewModelChanged(e.PropertyName);

    /// <summary>Runs <paramref name="action"/> after the current event, like QML <c>Qt.callLater</c>.</summary>
    protected static void CallLater(Action action) => Dispatcher.UIThread.Post(action);
}
