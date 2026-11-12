using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
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
/// it was left. It is sized with the window: its <see cref="WindowShare"/> of it, within
/// <see cref="MinSize"/> and <see cref="MaxSize"/>, and never closer than 24px to its edges; one that
/// <see cref="FitsContent"/> is no taller than what it holds. It can't be moved; it stays centred.
/// </remarks>
public class ModalSheet : ContentControl, IModalContent
{
    public static readonly StyledProperty<string> HeadingProperty =
        AvaloniaProperty.Register<ModalSheet, string>(nameof(Heading), "");

    public static readonly StyledProperty<string> FooterTextProperty =
        AvaloniaProperty.Register<ModalSheet, string>(nameof(FooterText), "");

    /// <summary>The footer's own content, in place of <see cref="FooterText"/> and Close; for sheets with actions.</summary>
    public static readonly StyledProperty<Control?> FooterProperty =
        AvaloniaProperty.Register<ModalSheet, Control?>(nameof(Footer));

    public static readonly StyledProperty<Thickness> BodyMarginProperty =
        AvaloniaProperty.Register<ModalSheet, Thickness>(nameof(BodyMargin), new Thickness(18));

    /// <summary>The share of the window the sheet takes, across and down.</summary>
    public static readonly StyledProperty<Size> WindowShareProperty =
        AvaloniaProperty.Register<ModalSheet, Size>(nameof(WindowShare), new Size(0.6, 0.7));

    /// <summary>The least it shrinks to with the window, unless the window itself is smaller.</summary>
    public static readonly StyledProperty<Size> MinSizeProperty =
        AvaloniaProperty.Register<ModalSheet, Size>(nameof(MinSize), new Size(420, 320));

    /// <summary>The most it grows to with the window.</summary>
    public static readonly StyledProperty<Size> MaxSizeProperty =
        AvaloniaProperty.Register<ModalSheet, Size>(nameof(MaxSize), new Size(1000, 760));

    /// <summary>
    /// The sheet is only as tall as what it holds, up to its share of the window, where it scrolls: for one whose
    /// content is short or long by what it shows (a file's properties), so a short one isn't mostly empty.
    /// </summary>
    public static readonly StyledProperty<bool> FitsContentProperty =
        AvaloniaProperty.Register<ModalSheet, bool>(nameof(FitsContent));

    private ModalOverlay? overlay;

    /// <summary>How tall the sheet is with all of its content showing, as last measured; 0 before it is.</summary>
    internal double FittedHeight { get; set; }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // A sheet that changes size while it is up (the update sheet, from its check to a new version) is laid out again.
        if (change.Property == WindowShareProperty || change.Property == MinSizeProperty || change.Property == MaxSizeProperty
            || change.Property == FitsContentProperty)
            (this.GetVisualParent() as Avalonia.Layout.Layoutable)?.InvalidateMeasure();
    }

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

    public Control? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    public Thickness BodyMargin
    {
        get => GetValue(BodyMarginProperty);
        set => SetValue(BodyMarginProperty, value);
    }

    public Size WindowShare
    {
        get => GetValue(WindowShareProperty);
        set => SetValue(WindowShareProperty, value);
    }

    public Size MinSize
    {
        get => GetValue(MinSizeProperty);
        set => SetValue(MinSizeProperty, value);
    }

    public Size MaxSize
    {
        get => GetValue(MaxSizeProperty);
        set => SetValue(MaxSizeProperty, value);
    }

    public bool FitsContent
    {
        get => GetValue(FitsContentProperty);
        set => SetValue(FitsContentProperty, value);
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

    void IModalContent.RequestClose() => Close();

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
