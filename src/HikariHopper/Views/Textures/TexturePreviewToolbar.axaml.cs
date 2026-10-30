using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using HikariHopper.ViewModels;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>
/// The toolbar above the texture preview (port of <c>TexturePreviewToolbar.qml</c>). Channel
/// segments call <see cref="TextureViewerViewModel.SetChannel"/>; the other buttons raise the
/// QML signals for <see cref="TexturePreview"/> to act on. A selected segment or active toggle is
/// sunken, every other button raised.
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

    private readonly (ChromeToolButton Button, string Value)[] channels;
    private TextureViewerViewModel? viewModel;

    public TexturePreviewToolbar()
    {
        InitializeComponent();
        channels = [(RgbaButton, "rgba"), (RedButton, "r"), (GreenButton, "g"), (BlueButton, "b"), (AlphaButton, "a")];
        foreach ((ChromeToolButton button, string value) in channels)
            button.Click += (_, _) => viewModel?.SetChannel(value);
        CheckerboardButton.Click += (_, _) => CheckerboardToggled?.Invoke(this, EventArgs.Empty);
        FilteredButton.Click += (_, _) => FilteredPreviewToggled?.Invoke(this, EventArgs.Empty);
        FitButton.Click += (_, _) => FitRequested?.Invoke(this, EventArgs.Empty);
        ActualSizeButton.Click += (_, _) => ActualSizeRequested?.Invoke(this, EventArgs.Empty);
        ZoomOutButton.Click += (_, _) => ZoomOutRequested?.Invoke(this, EventArgs.Empty);
        ZoomInButton.Click += (_, _) => ZoomInRequested?.Invoke(this, EventArgs.Empty);
        RefreshToggles();
        RefreshChannels();
    }

    public event EventHandler? FitRequested;

    public event EventHandler? ActualSizeRequested;

    public event EventHandler? ZoomInRequested;

    public event EventHandler? ZoomOutRequested;

    public event EventHandler? CheckerboardToggled;

    public event EventHandler? FilteredPreviewToggled;

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
    }

    private void RefreshToggles()
    {
        if (CheckerboardButton is null)
            return;
        SetSunken(CheckerboardButton, CheckerboardVisible);
        SetSunken(FilteredButton, FilteredPreview);
        SetSunken(FitButton, FitMode);
        ZoomText.Text = ZoomPercent;
    }

    private static void SetSunken(ChromeToolButton button, bool sunken)
    {
        button.Sunken = sunken;
        button.Raised = !sunken;
    }
}
