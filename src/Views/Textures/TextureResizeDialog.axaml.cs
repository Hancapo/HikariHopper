using Avalonia;
using Avalonia.Controls;
using HikariHopper.Textures;
using HikariHopper.ViewModels;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>
/// Resizes the selection: to a size typed in pixels, the same for every texture, or by a percentage, which scales
/// each from its own size; and, with <see cref="Snap"/>, to the nearest power of two.
/// </summary>
/// <remarks>
/// The fields take integers. With the aspect lock on, the other field follows the one typed in as it is typed: in
/// pixels by the current texture's proportions, in percent by taking the same number. The quick sizes set a share of
/// the original, and Power of two stays on until it is pressed again. The outcome says what the selection is now and
/// what it would be, and what about the result is worth a second look; Apply is enabled for a size every texture can
/// take and that changes at least one of them.
/// </remarks>
public partial class TextureResizeDialog : TextureToolDialog
{
    /// <summary>Opens with the result rounded to a power of two: the "To power of 2" command.</summary>
    public static readonly StyledProperty<bool> PowerOfTwoProperty =
        AvaloniaProperty.Register<TextureResizeDialog, bool>(nameof(PowerOfTwo));

    /// <summary>The most a percentage can be: 64 times the size.</summary>
    private const int MaximumPercent = 6400;

    private readonly QtIntValidator widthValidator;
    private readonly QtIntValidator heightValidator;
    // While the dialog itself writes a field: not something typed, so the other field doesn't follow it.
    private bool syncing;

    public TextureResizeDialog()
    {
        InitializeComponent();
        UnitSwitch.Sides.Add(new SwitchSide { Text = "px", Width = 34, Label = "Pixels" });
        UnitSwitch.Sides.Add(new SwitchSide { Text = "%", Width = 30, Label = "Percent" });
        UnitSwitch.SelectedIndex = 0;
        UnitSwitch.SideRequested += (_, side) => SetUnit(side == 1 ? ResizeUnit.Percent : ResizeUnit.Pixels);
        widthValidator = new QtIntValidator(WidthField, 1, 0x7FFF);
        heightValidator = new QtIntValidator(HeightField, 1, 0x7FFF);
        WidthField.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
                Typed(WidthField);
        };
        HeightField.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
                Typed(HeightField);
        };
        LockButton.Click += (_, _) => SetLocked(!Locked);
        QuarterButton.Click += (_, _) => SetScale(25);
        HalfButton.Click += (_, _) => SetScale(50);
        DoubleButton.Click += (_, _) => SetScale(200);
        SnapButton.Click += (_, _) => SetSnap(!Snap);
        foreach (FlatComboBox combo in new FlatComboBox[] { FilterCombo, MipCombo })
            combo.PropertyChanged += (_, e) =>
            {
                if (e.Property == FlatComboBox.CurrentValueProperty)
                    Refresh();
            };
        foreach (ChromeToolButton quick in new[] { QuarterButton, HalfButton, DoubleButton, SnapButton })
        {
            quick.Height = 22;
            quick.CornerRadius = Ui.Theme.InnerCorner;
        }
        ThemedToolTip.Set(LockButton, "Lock aspect ratio");
        ThemedToolTip.Set(SnapButton, "Round the result to the nearest power of two");
        ApplyAction = Run;
        RefreshToggles();
    }

    public bool PowerOfTwo
    {
        get => GetValue(PowerOfTwoProperty);
        set => SetValue(PowerOfTwoProperty, value);
    }

    /// <summary>Whether the fields are pixels or percent.</summary>
    internal ResizeUnit Unit { get; private set; }

    /// <summary>Whether the other field follows the one typed in.</summary>
    internal bool Locked { get; private set; } = true;

    /// <summary>Whether the result is rounded to the nearest power of two.</summary>
    internal bool Snap { get; private set; }

    /// <summary>What the fields ask for.</summary>
    internal ResizePlan Plan => new(Unit, QtIntValidator.NumberOf(WidthField.Text ?? ""), QtIntValidator.NumberOf(HeightField.Text ?? ""), Snap);

    /// <summary>The size the current texture would come out at; 0 × 0 while the fields don't say.</summary>
    public int TargetWidth => ViewModel is { SelectedWidth: > 0 } vm ? Plan.Target(vm.SelectedWidth, vm.SelectedHeight).Width : 0;

    public int TargetHeight => ViewModel is { SelectedHeight: > 0 } vm ? Plan.Target(vm.SelectedWidth, vm.SelectedHeight).Height : 0;

    /// <summary>Whether every selected texture can take the size the fields give it.</summary>
    public bool ValidSize
    {
        get
        {
            if (ViewModel is not { } vm || !widthValidator.IsAcceptable || !heightValidator.IsAcceptable)
                return false;
            ResizePlan plan = Plan;
            IReadOnlyList<TextureFacts> facts = vm.SelectionFacts;
            return facts.Count > 0 && facts.All(each =>
            {
                (int width, int height) = plan.Target(each.Width, each.Height);
                return width > 0 && height > 0 && width <= vm.MaximumDimension && height <= vm.MaximumDimension;
            });
        }
    }

    protected override void OnOpened()
    {
        base.OnOpened();
        bool several = ViewModel is { SelectedCount: > 1 };
        Heading = several ? $"RESIZE {ViewModel!.SelectedCount} TEXTURES" : "RESIZE TEXTURE";
        Snap = PowerOfTwo;
        // Several textures each keep their shape in percent; one is typed in pixels.
        Unit = several ? ResizeUnit.Percent : ResizeUnit.Pixels;
        UnitSwitch.SelectedIndex = Unit == ResizeUnit.Percent ? 1 : 0;
        UpdateLimits();
        Write(Unit == ResizeUnit.Percent ? 100 : ViewModel?.SelectedWidth ?? 0, Unit == ResizeUnit.Percent ? 100 : ViewModel?.SelectedHeight ?? 0);
        RefreshToggles();
        Refresh();
        WidthField.Focus();
        WidthField.SelectAll();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PowerOfTwoProperty && UnitSwitch is not null)
            SetSnap(PowerOfTwo);
    }

    protected override void OnViewModelChanged(string? property)
    {
        base.OnViewModelChanged(property);
        if (widthValidator is null)
            return;
        UpdateLimits();
        Refresh();
    }

    /// <summary>Turns the aspect lock on or off; turned on, the height takes what the width gives it.</summary>
    internal void SetLocked(bool locked)
    {
        Locked = locked;
        RefreshToggles();
        if (locked)
            Follow(WidthField);
        Refresh();
    }

    /// <summary>Rounds the result to the nearest power of two, or stops.</summary>
    internal void SetSnap(bool snap)
    {
        Snap = snap;
        RefreshToggles();
        Refresh();
    }

    /// <summary>
    /// Reads the fields as pixels or as percent, keeping the size reached: 512 of 1024 becomes 50, and back.
    /// </summary>
    internal void SetUnit(ResizeUnit unit)
    {
        if (unit == Unit)
            return;
        int width = QtIntValidator.NumberOf(WidthField.Text ?? ""), height = QtIntValidator.NumberOf(HeightField.Text ?? "");
        (int ownWidth, int ownHeight) = (ViewModel?.SelectedWidth ?? 0, ViewModel?.SelectedHeight ?? 0);
        Unit = unit;
        UnitSwitch.SelectedIndex = unit == ResizeUnit.Percent ? 1 : 0;
        UpdateLimits();
        if (width > 0 && height > 0 && ownWidth > 0 && ownHeight > 0)
        {
            if (unit == ResizeUnit.Percent)
            {
                int share = ResizePlan.PercentOf(width, ownWidth);
                Write(share, Locked ? share : ResizePlan.PercentOf(height, ownHeight));
            }
            else
                Write(ResizePlan.Scaled(ownWidth, width), ResizePlan.Scaled(ownHeight, height));
        }
        Refresh();
    }

    /// <summary>
    /// Sets the size to a share of the original, as a quick size does. Several textures typed in pixels switch to
    /// percent, where each keeps its shape.
    /// </summary>
    internal void SetScale(int percent)
    {
        if (ViewModel is not { } vm)
            return;
        if (Unit == ResizeUnit.Pixels && vm.SelectedCount > 1)
        {
            Unit = ResizeUnit.Percent;
            UnitSwitch.SelectedIndex = 1;
            UpdateLimits();
        }
        if (Unit == ResizeUnit.Percent)
            Write(percent, percent);
        else
            Write(ResizePlan.Scaled(vm.SelectedWidth, percent), ResizePlan.Scaled(vm.SelectedHeight, percent));
        Refresh();
    }

    private void UpdateLimits()
    {
        int most = Unit == ResizeUnit.Percent ? MaximumPercent : ViewModel?.MaximumDimension ?? 0x7FFF;
        widthValidator.Top = most;
        heightValidator.Top = most;
    }

    // The dialog's own writing of both fields, which neither follows.
    private void Write(int width, int height)
    {
        syncing = true;
        try
        {
            WidthField.Text = width.ToString(System.Globalization.CultureInfo.InvariantCulture);
            HeightField.Text = height.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        finally
        {
            syncing = false;
        }
    }

    // A field's text changed by anything but the dialog: with the lock on, the other follows at once.
    private void Typed(FlatTextField field)
    {
        if (syncing || widthValidator is null || heightValidator is null)
            return;
        if (Locked)
            Follow(field);
        Refresh();
    }

    // Gives the other field what this one's value makes it: the same percentage, or in pixels the current texture's
    // proportions. A value that isn't a size yet (empty, or out of range) leaves the other as it is.
    private void Follow(FlatTextField field)
    {
        bool fromWidth = ReferenceEquals(field, WidthField);
        if (!(fromWidth ? widthValidator : heightValidator).IsAcceptable || ViewModel is not { SelectedWidth: > 0, SelectedHeight: > 0 } vm)
            return;
        FlatTextField other = fromWidth ? HeightField : WidthField;
        string value = Unit == ResizeUnit.Percent
            ? field.Text ?? ""
            : fromWidth ? AspectSize(field.Text, vm.SelectedHeight, vm.SelectedWidth) : AspectSize(field.Text, vm.SelectedWidth, vm.SelectedHeight);
        if (other.Text == value)
            return;
        syncing = true;
        try
        {
            other.Text = value;
        }
        finally
        {
            syncing = false;
        }
    }

    private static string AspectSize(string? text, int numerator, int denominator) =>
        Math.Max(1, QtRaster.JsRound((double)QtIntValidator.NumberOf(text ?? "") * numerator / denominator)).ToString("0");

    private bool Run()
    {
        TextureFilterCombo.Remember(FilterCombo.CurrentValue as string);
        return ViewModel is { } vm && vm.ResizeSelection(Plan, (string)FilterCombo.CurrentValue!, MipCombo.Minimum, MipCombo.Generate);
    }

    // On, a toggle sits raised in its well in full ink.
    private void RefreshToggles()
    {
        LockButton.Sunken = Locked;
        // A button of its own, not in a well: on, it is raised.
        SnapButton.Raised = Snap;

    }

    private void Refresh()
    {
        if (Outcome is null || widthValidator is null || ViewModel is not { } vm)
            return;
        bool valid = ValidSize;
        ResizePlan plan = Plan;
        IReadOnlyList<TextureFacts> facts = vm.SelectionFacts;
        // Nothing to apply while every texture already has the size asked for.
        ApplyEnabled = valid && facts.Any(each => !plan.Keeps(each.Width, each.Height));
        (int Width, int Height)[] targets = [.. facts.Select(each => plan.Target(each.Width, each.Height))];
        int minimum = MipCombo.Minimum;
        bool mipmaps = MipCombo.Generate;
        long now = facts.Sum(each => each.DataSize), after = 0;
        for (int index = 0; index < facts.Count && after >= 0; index++)
        {
            TextureFacts each = facts[index];
            // A texture left at its size keeps its bytes; the others are counted from their new size and chain.
            long bytes = !valid ? -1
                : plan.Keeps(each.Width, each.Height) ? each.DataSize
                : ResizePlan.EstimatedSize(targets[index].Width, targets[index].Height, each.Format,
                    mipmaps ? vm.EstimatedMipCount(targets[index].Width, targets[index].Height, minimum) : 1);
            after = bytes < 0 ? -1 : after + bytes;
        }
        Outcome.Show(TextureDialogSummary.Sizes(facts.Select(each => (each.Width, each.Height))), now,
            valid ? TextureDialogSummary.Sizes(targets) : "—", after, valid ? Warnings(vm, facts, targets) : []);
        FitBody();
    }

    /// <summary>
    /// What about the sizes asked for is worth a second look: for one texture in its own numbers, for several by how
    /// many. Only of the textures that would change.
    /// </summary>
    private static IReadOnlyList<string> Warnings(TextureViewerViewModel vm, IReadOnlyList<TextureFacts> facts, (int Width, int Height)[] targets)
    {
        var lines = new List<string>();
        int odd = 0, unblocked = 0, larger = 0;
        for (int index = 0; index < facts.Count; index++)
        {
            (int width, int height) = targets[index];
            // A texture left at its size isn't touched: there is nothing about its result to point at.
            if ((width, height) == (facts[index].Width, facts[index].Height))
                continue;
            if (!TextureDimensions.IsPowerOfTwo(width, height))
                odd++;
            if (ResizePlan.InBlocks(facts[index].Format) && (width % 4 != 0 || height % 4 != 0))
                unblocked++;
            if (width > facts[index].Width || height > facts[index].Height)
                larger++;
        }
        bool one = facts.Count == 1;
        string size = one ? $"{targets[0].Width} × {targets[0].Height}" : "";
        if (odd > 0)
            lines.Add(one ? $"{size} is not a power of two." : $"{odd} of {facts.Count} will not be a power of two.");
        if (unblocked > 0)
            lines.Add(one
                ? $"{vm.SelectedFormat} is stored in 4 × 4 blocks, and {size} is not a multiple of 4."
                : $"{unblocked} of {facts.Count} are stored in 4 × 4 blocks and will not be a multiple of 4.");
        if (larger > 0)
            lines.Add(one ? "Larger than the original: no detail is added." : $"{larger} of {facts.Count} get larger: no detail is added.");
        return lines;
    }
}
