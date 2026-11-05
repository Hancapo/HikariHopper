using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using HikariHopper.ViewModels;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>
/// The toolbar above the texture preview: segmented wells for the channel, for how the preview is
/// drawn and for its size. Channel segments call <see cref="TextureViewerViewModel.SetChannel"/>; the
/// other buttons raise events for <see cref="TexturePreview"/> to act on. The selected segment of a
/// well sits raised in it. The zoom readout opens a list of preset zooms.
/// </summary>
public partial class TexturePreviewToolbar : UserControl
{
    public static readonly StyledProperty<bool> FitModeProperty =
        AvaloniaProperty.Register<TexturePreviewToolbar, bool>(nameof(FitMode), true);

    public static readonly StyledProperty<bool> CheckerboardVisibleProperty =
        AvaloniaProperty.Register<TexturePreviewToolbar, bool>(nameof(CheckerboardVisible), true);

    public static readonly StyledProperty<bool> FilteredPreviewProperty =
        AvaloniaProperty.Register<TexturePreviewToolbar, bool>(nameof(FilteredPreview), true);

    public static readonly StyledProperty<string> ZoomPercentProperty =
        AvaloniaProperty.Register<TexturePreviewToolbar, string>(nameof(ZoomPercent), "");

    public static readonly StyledProperty<double> DisplayScaleProperty =
        AvaloniaProperty.Register<TexturePreviewToolbar, double>(nameof(DisplayScale), 1);

    /// <summary>The zooms the readout offers, as display scales (1 is actual size).</summary>
    public static IReadOnlyList<double> ZoomPresets { get; } = [0.25, 0.5, 1, 2, 4, 8];

    /// <summary>The channels, in the order of the channel switch's sides.</summary>
    internal static string[] ChannelOrder { get; } = ["rgba", "r", "g", "b", "a"];

    private ITexturePreviewSource? viewModel;
    private RetroMenu? presets;

    public TexturePreviewToolbar()
    {
        InitializeComponent();
        ChannelSwitch.SideRequested += (_, index) => viewModel?.SetChannel(ChannelOrder[index]);
        CheckerboardButton.Click += (_, _) => CheckerboardToggled?.Invoke(this, EventArgs.Empty);
        // Smooth is the first side, Pixels the second.
        FilterSwitch.SideRequested += (_, side) =>
        {
            if ((side == 1) == FilteredPreview)
                FlipFilter();
        };
        // Fit is the first side, actual size the second; at any other zoom neither is in use.
        FitSwitch.SideRequested += (_, side) => (side == 1 ? ActualSizeRequested : FitRequested)?.Invoke(this, EventArgs.Empty);
        ZoomOutButton.Click += (_, _) => ZoomOutRequested?.Invoke(this, EventArgs.Empty);
        ZoomInButton.Click += (_, _) => ZoomInRequested?.Invoke(this, EventArgs.Empty);
        ZoomButton.Click += (_, _) => OpenZoomPresets();

        ThemedToolTip.Set(CheckerboardButton, "Transparency grid");
        ThemedToolTip.Set(ZoomOutButton, "Zoom out");
        ThemedToolTip.Set(ZoomInButton, "Zoom in");
        ThemedToolTip.Set(ZoomButton, "Zoom presets");
        RefreshToggles();
        RefreshChannels();
    }

    public event EventHandler? FitRequested;

    public event EventHandler? ActualSizeRequested;

    public event EventHandler? ZoomInRequested;

    public event EventHandler? ZoomOutRequested;

    public event EventHandler? CheckerboardToggled;

    public event EventHandler? FilteredPreviewToggled;

    /// <summary>A preset zoom was picked: its display scale (1 is actual size).</summary>
    public event EventHandler<double>? ZoomPresetRequested;

    /// <summary>Whether the switch shows its Smooth side; Pixels otherwise.</summary>
    internal bool SmoothSelected => FilterSwitch.SelectedIndex == 0;

    /// <summary>Flips the Smooth / Pixels switch, as a click on it does.</summary>
    internal void FlipFilter() => FilteredPreviewToggled?.Invoke(this, EventArgs.Empty);

    /// <summary>The preset menu, once it has been opened.</summary>
    internal RetroMenu? PresetsMenu => presets;

    public bool FitMode
    {
        get => GetValue(FitModeProperty);
        set => SetValue(FitModeProperty, value);
    }

    public bool CheckerboardVisible
    {
        get => GetValue(CheckerboardVisibleProperty);
        set => SetValue(CheckerboardVisibleProperty, value);
    }

    public bool FilteredPreview
    {
        get => GetValue(FilteredPreviewProperty);
        set => SetValue(FilteredPreviewProperty, value);
    }

    public string ZoomPercent
    {
        get => GetValue(ZoomPercentProperty);
        set => SetValue(ZoomPercentProperty, value);
    }

    /// <summary>The preview's exact display scale (1 is actual size); the readout rounds it.</summary>
    public double DisplayScale
    {
        get => GetValue(DisplayScaleProperty);
        set => SetValue(DisplayScaleProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FitModeProperty || change.Property == CheckerboardVisibleProperty
            || change.Property == FilteredPreviewProperty || change.Property == ZoomPercentProperty
            || change.Property == DisplayScaleProperty)
            RefreshToggles();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (viewModel is not null)
            viewModel.PropertyChanged -= ViewModelChanged;
        viewModel = DataContext as ITexturePreviewSource;
        if (viewModel is not null)
            viewModel.PropertyChanged += ViewModelChanged;
        RefreshChannels();
    }

    private void ViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TextureViewerViewModel.Channel))
            RefreshChannels();
    }

    private void RefreshChannels()
    {
        // OnDataContextChanged can run during InitializeComponent, before the switch is there.
        if (ChannelSwitch is null)
            return;
        int index = Array.IndexOf(ChannelOrder, viewModel?.Channel ?? "");
        ChannelSwitch.SelectedIndex = index;
    }

    private void RefreshToggles()
    {
        if (CheckerboardButton is null)
            return;
        SetSunken(CheckerboardButton, CheckerboardVisible);
        FilterSwitch.SelectedIndex = FilteredPreview ? 0 : 1;
        // On Fit while the texture fits, on 1:1 at actual size, on neither at any other zoom. Actual size is
        // the exact scale: a zoom that only rounds to 100% is not it.
        FitSwitch.SelectedIndex = FitMode ? 0 : Math.Abs(DisplayScale - 1) < 1e-9 ? 1 : -1;
        ZoomButton.Text = ZoomPercent;
    }

    /// <summary>The selected segment sits raised in its well in full ink; the others are dimmer.</summary>
    private static void SetSunken(ChromeToolButton button, bool sunken)
    {
        button.Sunken = sunken;
        button.Foreground = sunken ? Ui.Theme.Text : Ui.Theme.TextDim;
    }

    /// <summary>Label of a preset: "25%", "100%", "800%".</summary>
    public static string PresetLabel(double scale) => $"{Math.Round(scale * 100)}%";

    /// <summary>Opens the preset zooms under the readout, the current one checked.</summary>
    internal void OpenZoomPresets()
    {
        presets ??= BuildPresets();
        foreach (RetroMenuItem item in presets.Items.OfType<RetroMenuItem>())
            item.IsChecked = item.Tag is double scale && PresetLabel(scale) == ZoomPercent;
        presets.OpenUnder(ZoomButton, 4);
    }

    private RetroMenu BuildPresets()
    {
        // Only zooms: fitting has its own button beside the readout.
        var menu = new RetroMenu { Width = 104 + 2 * Ui.Theme.MenuPadding };
        foreach (double scale in ZoomPresets)
        {
            var item = new RetroMenuItem { Text = PresetLabel(scale), Tag = scale, Checkable = true, MinWidth = 0 };
            if (scale == 1)
                item.ShortcutText = "1";
            item.Triggered += (_, _) => ZoomPresetRequested?.Invoke(this, scale);
            menu.Items.Add(item);
        }
        return menu;
    }
}
