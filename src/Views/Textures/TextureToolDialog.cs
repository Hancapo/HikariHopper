using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>
/// The chassis of every texture tool dialog (port of <c>TextureToolDialog.qml</c>): 520px wide,
/// <c>SheetHeaderHeight + BodyHeight + DialogFooterHeight</c> tall, in the sheets' card: a raised header with
/// the spaced <see cref="Heading"/> and a close mark, the body, and a footer with Cancel and the apply button, green unless it removes something.
/// </summary>
/// <remarks>
/// Apply is enabled while <see cref="ApplyEnabled"/> holds and no operation runs; clicking it
/// calls <see cref="ApplyAction"/> and accepts the dialog when that returns true. Cancel rejects.
/// Subclasses keep this look through <see cref="StyleKeyOverride"/>.
/// </remarks>
public class TextureToolDialog : TextureModalDialog
{
    public const double DialogWidth = 520;
    public const double FooterHeight = Ui.Theme.DialogFooterHeight;

    public static readonly StyledProperty<string> HeadingProperty =
        AvaloniaProperty.Register<TextureToolDialog, string>(nameof(Heading), "");

    public static readonly StyledProperty<string> ApplyLabelProperty =
        AvaloniaProperty.Register<TextureToolDialog, string>(nameof(ApplyLabel), "Apply");

    public static readonly StyledProperty<bool> ApplyEnabledProperty =
        AvaloniaProperty.Register<TextureToolDialog, bool>(nameof(ApplyEnabled), true);

    /// <summary>The apply button removes something: it wears the error colour, as the explorer's Delete does.</summary>
    public static readonly StyledProperty<bool> DestructiveProperty =
        AvaloniaProperty.Register<TextureToolDialog, bool>(nameof(Destructive));

    public static readonly StyledProperty<double> BodyHeightProperty =
        AvaloniaProperty.Register<TextureToolDialog, double>(nameof(BodyHeight), 280);

    private ChromeToolButton? applyButton;
    // Whether the body is as tall as its content (FitBody), measured once the dialog is in a window.
    private bool fitsBody;

    public TextureToolDialog()
    {
        Width = DialogWidth;
        UpdateHeight();
    }

    public string Heading
    {
        get => GetValue(HeadingProperty);
        set => SetValue(HeadingProperty, value);
    }

    public string ApplyLabel
    {
        get => GetValue(ApplyLabelProperty);
        set => SetValue(ApplyLabelProperty, value);
    }

    public bool ApplyEnabled
    {
        get => GetValue(ApplyEnabledProperty);
        set => SetValue(ApplyEnabledProperty, value);
    }

    public bool Destructive
    {
        get => GetValue(DestructiveProperty);
        set => SetValue(DestructiveProperty, value);
    }

    public double BodyHeight
    {
        get => GetValue(BodyHeightProperty);
        set => SetValue(BodyHeightProperty, value);
    }

    /// <summary>Runs the dialog's command; true accepts and closes the dialog.</summary>
    public Func<bool>? ApplyAction { get; set; }

    /// <summary>The apply button, once templated: green, or the error colour when <see cref="Destructive"/>.</summary>
    public ChromeToolButton? ApplyButton => applyButton;

    protected override Type StyleKeyOverride => typeof(TextureToolDialog);

    /// <summary>QML apply button <c>onClicked</c>.</summary>
    public void Apply()
    {
        if (applyButton is { IsEffectivelyEnabled: false })
            return;
        if (ApplyAction?.Invoke() == true)
            Accept();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (e.NameScope.Find<ChromeToolButton>("PART_CancelButton") is { } cancel)
            cancel.Click += (_, _) => Reject();
        if (e.NameScope.Find<ChromeToolButton>("PART_CloseMark") is { } close)
            close.Click += (_, _) => Reject();
        applyButton = e.NameScope.Find<ChromeToolButton>("PART_ApplyButton");
        if (applyButton is not null)
            applyButton.Click += (_, _) => Apply();
        UpdateApplyStyle();
        UpdateApplyEnabled();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BodyHeightProperty)
            UpdateHeight();
        else if (change.Property == ApplyEnabledProperty)
            UpdateApplyEnabled();
        else if (change.Property == DestructiveProperty)
            UpdateApplyStyle();
    }

    protected override void OnViewModelChanged(string? property)
    {
        base.OnViewModelChanged(property);
        if (property is null or nameof(ViewModels.TextureViewerViewModel.OperationBusy))
            UpdateApplyEnabled();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (fitsBody)
            FitBody();
    }

    private void UpdateHeight() => Height = Ui.Theme.SheetHeaderHeight + BodyHeight + FooterHeight;

    /// <summary>
    /// Makes the body as tall as what is in it, at the dialog's width: for a dialog whose content comes and goes
    /// (a warning under its outcome), in place of a height written by hand.
    /// </summary>
    protected void FitBody()
    {
        fitsBody = true;
        // Not before it is in a window: measuring it there would style it without the app's styles, for good.
        if (Content is not Control body || VisualRoot is null)
            return;
        // Measured again even at the same width: what changed is inside it. The card's own border is a pixel on each side.
        body.InvalidateMeasure();
        body.Measure(new Size(DialogWidth - 2, double.PositiveInfinity));
        BodyHeight = Math.Ceiling(body.DesiredSize.Height);
    }

    private void UpdateApplyStyle()
    {
        if (applyButton is null)
            return;
        applyButton.Primary = !Destructive;
        applyButton.Destructive = Destructive;
    }

    private void UpdateApplyEnabled()
    {
        if (applyButton is not null)
            applyButton.IsEnabled = ApplyEnabled && ViewModel is not { OperationBusy: true };
    }
}
