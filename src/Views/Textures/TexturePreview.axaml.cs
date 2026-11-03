using Avalonia;
using Avalonia.Controls;

namespace HikariHopper.Views;

/// <summary>
/// The texture preview pane (port of <c>TexturePreview.qml</c>): <see cref="TexturePreviewToolbar"/>,
/// <see cref="TextureViewport"/> and <see cref="TextureMipBar"/>. The toolbar's zoom buttons step by
/// 1.25 and 0.8 around the viewport centre. The F and 1 shortcuts are routed here by
/// <see cref="TextureEditor"/>.
/// </summary>
public partial class TexturePreview : UserControl
{
    public static readonly StyledProperty<bool> CheckerboardVisibleProperty =
        AvaloniaProperty.Register<TexturePreview, bool>(nameof(CheckerboardVisible), true);

    public static readonly StyledProperty<bool> FilteredPreviewProperty =
        AvaloniaProperty.Register<TexturePreview, bool>(nameof(FilteredPreview), true);

    public TexturePreview()
    {
        InitializeComponent();
        Toolbar.FitRequested += (_, _) => FitTexture();
        Toolbar.ActualSizeRequested += (_, _) => ShowActualSize();
        Toolbar.ZoomInRequested += (_, _) => ZoomBy(1.25);
        Toolbar.ZoomOutRequested += (_, _) => ZoomBy(0.8);
        Toolbar.CheckerboardToggled += (_, _) => CheckerboardVisible = !CheckerboardVisible;
        Toolbar.FilteredPreviewToggled += (_, _) => FilteredPreview = !FilteredPreview;
        Viewport.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextureViewport.FitModeProperty)
                Toolbar.FitMode = Viewport.FitMode;
            else if (e.Property == TextureViewport.ZoomPercentProperty)
                Toolbar.ZoomPercent = Viewport.ZoomPercent;
        };
        Toolbar.FitMode = Viewport.FitMode;
        Toolbar.ZoomPercent = Viewport.ZoomPercent;
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

    public bool FitMode => Viewport.FitMode;

    public double DisplayScale => Viewport.DisplayScale;

    public string ZoomPercent => Viewport.ZoomPercent;

    public void FitTexture() => Viewport.FitTexture();

    public void ShowActualSize() => Viewport.ShowActualSize();

    public void ZoomBy(double factor) => Viewport.ZoomBy(factor);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CheckerboardVisibleProperty)
        {
            Toolbar.CheckerboardVisible = CheckerboardVisible;
            Viewport.CheckerboardVisible = CheckerboardVisible;
        }
        else if (change.Property == FilteredPreviewProperty)
        {
            Toolbar.FilteredPreview = FilteredPreview;
            Viewport.FilteredPreview = FilteredPreview;
        }
    }
}
