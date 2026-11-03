using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>
/// The chassis of every texture tool dialog (port of <c>TextureToolDialog.qml</c>): 520px wide,
/// <c>headerHeight + BodyHeight + 58</c> tall, a raised header with the spaced
/// <see cref="Heading"/>, the body, and a footer with Cancel and the green apply button.
/// </summary>
/// <remarks>
/// Apply is enabled while <see cref="ApplyEnabled"/> holds and no operation runs; clicking it
/// calls <see cref="ApplyAction"/> and accepts the dialog when that returns true. Cancel rejects.
/// Subclasses keep this look through <see cref="StyleKeyOverride"/>.
/// </remarks>
public class TextureToolDialog : TextureModalDialog
{
    public const double DialogWidth = 520;
    public const double FooterHeight = 58;

    public static readonly StyledProperty<string> HeadingProperty =
        AvaloniaProperty.Register<TextureToolDialog, string>(nameof(Heading), "");

    public static readonly StyledProperty<string> ApplyLabelProperty =
        AvaloniaProperty.Register<TextureToolDialog, string>(nameof(ApplyLabel), "Apply");

    public static readonly StyledProperty<bool> ApplyEnabledProperty =
        AvaloniaProperty.Register<TextureToolDialog, bool>(nameof(ApplyEnabled), true);

    public static readonly StyledProperty<double> BodyHeightProperty =
        AvaloniaProperty.Register<TextureToolDialog, double>(nameof(BodyHeight), 280);

    private ChromeToolButton? applyButton;

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

    public double BodyHeight
    {
        get => GetValue(BodyHeightProperty);
        set => SetValue(BodyHeightProperty, value);
    }

    /// <summary>Runs the dialog's command; true accepts and closes the dialog.</summary>
    public Func<bool>? ApplyAction { get; set; }

    /// <summary>The green apply button, once templated.</summary>
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
        applyButton = e.NameScope.Find<ChromeToolButton>("PART_ApplyButton");
        if (applyButton is not null)
            applyButton.Click += (_, _) => Apply();
        UpdateApplyEnabled();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BodyHeightProperty)
            UpdateHeight();
        else if (change.Property == ApplyEnabledProperty)
            UpdateApplyEnabled();
    }

    protected override void OnViewModelChanged(string? property)
    {
        base.OnViewModelChanged(property);
        if (property is null or nameof(ViewModels.TextureViewerViewModel.OperationBusy))
            UpdateApplyEnabled();
    }

    private void UpdateHeight() => Height = Ui.Theme.HeaderHeight + BodyHeight + FooterHeight;

    private void UpdateApplyEnabled()
    {
        if (applyButton is not null)
            applyButton.IsEnabled = ApplyEnabled && ViewModel is not { OperationBusy: true };
    }
}
