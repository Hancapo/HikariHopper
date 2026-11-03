using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>
/// A card shown in the main window over its blurred content (see <see cref="ModalOverlay"/>): the
/// chassis Settings, About and the keyboard reference wear instead of windows of their own.
/// </summary>
/// <remarks>
/// A header carries <see cref="Heading"/> and a close mark; the content sits in the body, inset by
/// <see cref="BodyMargin"/>; a footer carries <see cref="FooterText"/> and a Close button. Esc, the
/// close mark and Close all dismiss it. The sheet keeps its state between showings, so it reopens as
/// it was left. Give it a Width and Height: it keeps them, shrinking only to fit a small window.
/// </remarks>
public class ModalSheet : ContentControl
{
    public static readonly StyledProperty<string> HeadingProperty =
        AvaloniaProperty.Register<ModalSheet, string>(nameof(Heading), "");

    public static readonly StyledProperty<string> FooterTextProperty =
        AvaloniaProperty.Register<ModalSheet, string>(nameof(FooterText), "");

    public static readonly StyledProperty<Thickness> BodyMarginProperty =
        AvaloniaProperty.Register<ModalSheet, Thickness>(nameof(BodyMargin), new Thickness(18));

    private ModalOverlay? overlay;

    public string Heading
    {
        get => GetValue(HeadingProperty);
        set => SetValue(HeadingProperty, value);
    }

    public string FooterText
    {
        get => GetValue(FooterTextProperty);
        set => SetValue(FooterTextProperty, value);
    }

    public Thickness BodyMargin
    {
        get => GetValue(BodyMarginProperty);
        set => SetValue(BodyMarginProperty, value);
    }

    /// <summary>Whether the sheet is showing.</summary>
    public bool IsOpen => overlay?.IsShown == true;

    /// <summary>Raised when the sheet is dismissed.</summary>
    public event EventHandler? Closed;

    protected override Type StyleKeyOverride => typeof(ModalSheet);

    /// <summary>Shows the sheet over the window <paramref name="anchor"/> is in; nothing if it is already up.</summary>
    public void Present(Control anchor)
    {
        if (IsOpen)
            return;
        overlay = ModalOverlay.Show(anchor, this);
        if (overlay is null)
            return;
        overlay.Closed += (_, _) =>
        {
            overlay = null;
            Closed?.Invoke(this, EventArgs.Empty);
        };
        OnPresented();
    }

    public void Close() => overlay?.Close();

    /// <summary>Called each time the sheet is shown, to refresh what it displays.</summary>
    protected virtual void OnPresented()
    {
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (e.NameScope.Find<ChromeToolButton>("PART_CloseButton") is { } close)
            close.Click += (_, _) => Close();
        if (e.NameScope.Find<ChromeToolButton>("PART_CloseMark") is { } mark)
            mark.Click += (_, _) => Close();
    }
}
