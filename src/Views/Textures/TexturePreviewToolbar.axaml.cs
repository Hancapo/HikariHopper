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

    /// <summary>The zooms the readout offers, as display scales (1 is actual size).</summary>
    public static IReadOnlyList<double> ZoomPresets { get; } = [0.25, 0.5, 1, 2, 4, 8];

    // Each channel's letter in its own colour; full strength when it is the one shown.
    private static readonly (string Value, Color Tint)[] ChannelTints =
    [
        ("r", Color.Parse("#E8837E")),
        ("g", Color.Parse("#98D684")),
        ("b", Color.Parse("#7FAEEE")),
        ("a", Color.Parse("#C6C8DA")),
    ];

    private readonly (ChromeToolButton Button, string Value)[] channels;
    private TextureViewerViewModel? viewModel;
    private RetroMenu? presets;

    public TexturePreviewToolbar()
    {
        InitializeComponent();
        channels = [(RgbaButton, "rgba"), (RedButton, "r"), (GreenButton, "g"), (BlueButton, "b"), (AlphaButton, "a")];
        foreach ((ChromeToolButton button, string value) in channels)
            button.Click += (_, _) => viewModel?.SetChannel(value);
        CheckerboardButton.Click += (_, _) => CheckerboardToggled?.Invoke(this, EventArgs.Empty);
        // Smooth and Pixels are the two sides of one setting: pressing the other one switches it.
        SmoothButton.Click += (_, _) =>
        {
            if (!FilteredPreview)
                FilteredPreviewToggled?.Invoke(this, EventArgs.Empty);
        };
        PixelsButton.Click += (_, _) =>
        {
            if (FilteredPreview)
                FilteredPreviewToggled?.Invoke(this, EventArgs.Empty);
        };
        FitButton.Click += (_, _) => FitRequested?.Invoke(this, EventArgs.Empty);
        ActualSizeButton.Click += (_, _) => ActualSizeRequested?.Invoke(this, EventArgs.Empty);
        ZoomOutButton.Click += (_, _) => ZoomOutRequested?.Invoke(this, EventArgs.Empty);
        ZoomInButton.Click += (_, _) => ZoomInRequested?.Invoke(this, EventArgs.Empty);
        ZoomButton.Click += (_, _) => OpenZoomPresets();

        ThemedToolTip.Set(RgbaButton, "All channels");
        ThemedToolTip.Set(RedButton, "Red channel");
        ThemedToolTip.Set(GreenButton, "Green channel");
        ThemedToolTip.Set(BlueButton, "Blue channel");
        ThemedToolTip.Set(AlphaButton, "Alpha channel");
        ThemedToolTip.Set(CheckerboardButton, "Transparency grid");
        ThemedToolTip.Set(SmoothButton, "Smooth");
        ThemedToolTip.Set(PixelsButton, "Pixels");
        ThemedToolTip.Set(FitButton, "Fit to the view  (F)");
        ThemedToolTip.Set(ActualSizeButton, "Actual size  (1)");
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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FitModeProperty || change.Property == CheckerboardVisibleProperty
            || change.Property == FilteredPreviewProperty || change.Property == ZoomPercentProperty)
            RefreshToggles();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (viewModel is not null)
            viewModel.PropertyChanged -= ViewModelChanged;
        viewModel = DataContext as TextureViewerViewModel;
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
        // OnDataContextChanged can run during InitializeComponent, before the segments are known.
        if (channels is null)
            return;
        string channel = viewModel?.Channel ?? "";
        foreach ((ChromeToolButton button, string value) in channels)
            SetSunken(button, channel == value);
        foreach ((string value, Color tint) in ChannelTints)
        {
            ChromeToolButton button = channels.First(entry => entry.Value == value).Button;
            button.Foreground = new SolidColorBrush(tint, channel == value ? 1 : 0.72);
        }
    }

    private void RefreshToggles()
    {
        if (CheckerboardButton is null)
            return;
        SetSunken(CheckerboardButton, CheckerboardVisible);
        SetSunken(SmoothButton, FilteredPreview);
        SetSunken(PixelsButton, !FilteredPreview);
        SetSunken(FitButton, FitMode);
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
            item.IsChecked = item.Tag is double scale ? !FitMode && PresetLabel(scale) == ZoomPercent : FitMode;
        if (TopLevel.GetTopLevel(this) is not { } top || ZoomButton.TranslatePoint(new Point(0, ZoomButton.Bounds.Height + 4), top) is not { } at)
            return;
        presets.Open(top, at.X, at.Y);
    }

    private RetroMenu BuildPresets()
    {
        var menu = new RetroMenu { Width = 128 + 2 * Ui.Theme.MenuPadding };
        var fit = new RetroMenuItem { Text = "Fit", ShortcutText = "F", Checkable = true, MinWidth = 0 };
        fit.Triggered += (_, _) => FitRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(fit);
        menu.Items.Add(new RetroMenuSeparator());
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
