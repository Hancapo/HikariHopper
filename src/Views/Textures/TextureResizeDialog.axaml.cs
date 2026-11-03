using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using HikariHopper.ViewModels;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>
/// Resizes the selection (port of <c>TextureResizeDialog.qml</c>). With <see cref="PowerOfTwo"/>
/// each texture is rounded to powers of two (nearest, down or up) instead of taking typed
/// dimensions.
/// </summary>
/// <remarks>
/// The width and height fields accept integers from 1 to <see cref="TextureViewerViewModel.MaximumDimension"/>;
/// with the aspect lock on, finishing one field recomputes the other from the current texture.
/// Apply is enabled for a valid size (and, rounding, only when a texture is not a power of two).
/// </remarks>
public partial class TextureResizeDialog : TextureToolDialog
{
    public static readonly StyledProperty<bool> PowerOfTwoProperty =
        AvaloniaProperty.Register<TextureResizeDialog, bool>(nameof(PowerOfTwo));

    private readonly QtIntValidator widthValidator;
    private readonly QtIntValidator heightValidator;

    public TextureResizeDialog()
    {
        InitializeComponent();
        RoundingCombo.ItemsSource = new FlatComboOption[]
        {
            new("Nearest", "nearest"),
            new("Round down", "down"),
            new("Round up", "up"),
        };
        RoundingCombo.SelectedIndex = 0;
        widthValidator = new QtIntValidator(WidthField, 1, 0x7FFF);
        heightValidator = new QtIntValidator(HeightField, 1, 0x7FFF);
        WidthField.EditingFinished += (_, _) =>
        {
            if (widthValidator.IsAcceptable && LockAspect.IsChecked == true && ViewModel is { SelectedWidth: > 0 } vm)
                HeightField.Text = AspectSize(WidthField.Text, vm.SelectedHeight, vm.SelectedWidth);
        };
        HeightField.EditingFinished += (_, _) =>
        {
            if (heightValidator.IsAcceptable && LockAspect.IsChecked == true && ViewModel is { SelectedHeight: > 0 } vm)
                WidthField.Text = AspectSize(HeightField.Text, vm.SelectedWidth, vm.SelectedHeight);
        };
        foreach (Control control in new Control[] { WidthField, HeightField, RoundingCombo, MipSizeCombo, MipCheck })
            control.PropertyChanged += (_, e) =>
            {
                if (e.Property == TextBox.TextProperty || e.Property == FlatComboBox.CurrentValueProperty
                    || e.Property == ToggleButton.IsCheckedProperty)
                    Refresh();
            };
        ApplyAction = Run;
        UpdateMode();
    }

    public bool PowerOfTwo
    {
        get => GetValue(PowerOfTwoProperty);
        set => SetValue(PowerOfTwoProperty, value);
    }

    private string RoundingMode => RoundingCombo.CurrentValue as string ?? "nearest";

    /// <summary>QML <c>roundedSize</c>: the current texture rounded with the chosen mode.</summary>
    private TextureSize? RoundedSize =>
        PowerOfTwo && ViewModel is { SelectedWidth: > 0, SelectedHeight: > 0 } vm ? vm.PowerOfTwoSize(RoundingMode) : null;

    public int TargetWidth => PowerOfTwo ? RoundedSize?.Width ?? 0 : QtIntValidator.NumberOf(WidthField.Text ?? "");

    public int TargetHeight => PowerOfTwo ? RoundedSize?.Height ?? 0 : QtIntValidator.NumberOf(HeightField.Text ?? "");

    public bool ValidSize
    {
        get
        {
            if (ViewModel is not { } vm)
                return false;
            int width = TargetWidth, height = TargetHeight;
            return width > 0 && height > 0 && width <= vm.MaximumDimension && height <= vm.MaximumDimension
                && (PowerOfTwo
                    ? vm.SelectedCount > 0 && vm.ValidPowerOfTwoSelection(RoundingMode)
                    : widthValidator.IsAcceptable && heightValidator.IsAcceptable);
        }
    }

    protected override void OnOpened()
    {
        base.OnOpened();
        WidthField.Text = (ViewModel?.SelectedWidth ?? 0).ToString();
        HeightField.Text = (ViewModel?.SelectedHeight ?? 0).ToString();
        if (PowerOfTwo)
            RoundingCombo.Focus();
        else
        {
            WidthField.Focus();
            WidthField.SelectAll();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PowerOfTwoProperty)
            UpdateMode();
    }

    protected override void OnViewModelChanged(string? property)
    {
        base.OnViewModelChanged(property);
        if (widthValidator is null)
            return;
        int maximum = ViewModel?.MaximumDimension ?? 0x7FFF;
        widthValidator.Top = maximum;
        heightValidator.Top = maximum;
        Refresh();
    }

    private static string AspectSize(string? text, int numerator, int denominator) =>
        Math.Max(1, QtRaster.JsRound((double)QtIntValidator.NumberOf(text ?? "") * numerator / denominator)).ToString("0");

    private bool Run()
    {
        if (ViewModel is not { } vm)
            return false;
        string filter = (string)FilterCombo.CurrentValue!;
        int minimum = (int)MipSizeCombo.CurrentValue!;
        bool mipmaps = MipCheck.IsChecked == true;
        return PowerOfTwo
            ? vm.ResizeSelectionToPowerOfTwo(RoundingMode, filter, minimum, mipmaps)
            : vm.ResizeSelected(TargetWidth, TargetHeight, filter, minimum, mipmaps);
    }

    private void UpdateMode()
    {
        bool rounding = PowerOfTwo;
        Heading = rounding ? "RESIZE TO POWER OF 2" : "RESIZE TEXTURE";
        ResolutionLabel.IsVisible = ResolutionRow.IsVisible = !rounding;
        LockSpacer.IsVisible = LockAspect.IsVisible = !rounding;
        RoundingLabel.IsVisible = RoundingCombo.IsVisible = rounding;
        RoundingNote.IsVisible = rounding;
        Refresh();
    }

    private void Refresh()
    {
        if (ResultText is null || widthValidator is null)
            return;
        bool mipmaps = MipCheck.IsChecked == true;
        SmallestLabel.IsVisible = MipSizeCombo.IsVisible = mipmaps;
        UpdateRows();
        if (ViewModel is not { } vm)
            return;
        bool valid = ValidSize;
        ApplyEnabled = valid && (!PowerOfTwo || vm.SelectedNeedsPowerOfTwo);
        int width = TargetWidth, height = TargetHeight;
        int minimum = MipSizeCombo.CurrentValue is int value ? value : 0;
        if (!valid && PowerOfTwo)
            ResultText.Text = $"Result exceeds the maximum dimension ({vm.MaximumDimension}). Choose Round down.";
        else if (vm.SelectedCount > 1)
            ResultText.Text = PowerOfTwo
                ? $"Round each texture independently · {RoundingCombo.DisplayText}"
                : $"All selected textures → {width} × {height}";
        else
        {
            string mipCount = mipmaps && valid ? vm.EstimatedMipCount(width, height, minimum).ToString() : "1";
            string last = mipmaps && valid ? vm.EstimatedSmallestMipDimensions(width, height, minimum) : "none";
            ResultText.Text = $"{vm.SelectedWidth} × {vm.SelectedHeight}  →  {(width != 0 ? width : "—")} × "
                + $"{(height != 0 ? height : "—")}  ·  {mipCount} mips  ·  last {last}";
        }
    }

    /// <summary>Collapses the spacing rows next to hidden rows, as a GridLayout skips hidden items.</summary>
    private void UpdateRows()
    {
        RowDefinitions rows = OptionsGrid.RowDefinitions;
        var visible = new bool[rows.Count];
        foreach (Control child in OptionsGrid.Children)
        {
            if (child.IsVisible)
                visible[Grid.GetRow(child)] = true;
        }
        bool previous = false;
        for (int row = 0; row < rows.Count; row += 2)
        {
            if (row > 0)
                rows[row - 1].Height = new GridLength(previous && visible[row] ? 8 : 0);
            if (visible[row])
                previous = true;
        }
    }
}
