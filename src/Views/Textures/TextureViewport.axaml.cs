using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using HikariHopper.ViewModels;

namespace HikariHopper.Views;

/// <summary>
/// The zoomable, pannable texture surface (port of <c>TextureViewport.qml</c>).
/// </summary>
/// <remarks>
/// In fit mode the texture fills the viewport less 48px; any zoom switches to a manual scale
/// clamped to 5%–1600%. The wheel zooms by 1.25 per notch around the pointer, dragging pans (with
/// a closed-hand cursor) and gives the viewport focus, and the arrow keys pan by 48px while it is
/// focused. A new current texture fits again.
/// </remarks>
public partial class TextureViewport : UserControl
{
    public const int KeyboardPanStep = 48;
    // Qt startDragDistance (QStyleHints), which DragHandler compares per axis.
    private const double DragThreshold = 10;

    public static readonly DirectProperty<TextureViewport, bool> FitModeProperty =
        AvaloniaProperty.RegisterDirect<TextureViewport, bool>(nameof(FitMode), o => o.FitMode);

    public static readonly DirectProperty<TextureViewport, double> DisplayScaleProperty =
        AvaloniaProperty.RegisterDirect<TextureViewport, double>(nameof(DisplayScale), o => o.DisplayScale);

    public static readonly DirectProperty<TextureViewport, string> ZoomPercentProperty =
        AvaloniaProperty.RegisterDirect<TextureViewport, string>(nameof(ZoomPercent), o => o.ZoomPercent);

    public static readonly StyledProperty<bool> CheckerboardVisibleProperty =
        AvaloniaProperty.Register<TextureViewport, bool>(nameof(CheckerboardVisible), true);

    public static readonly StyledProperty<bool> FilteredPreviewProperty =
        AvaloniaProperty.Register<TextureViewport, bool>(nameof(FilteredPreview), true);

    private ITexturePreviewSource? viewModel;
    private bool fitMode = true;
    private bool publishedFitMode = true;
    private double manualScale = 1;
    private double panX, panY;
    private double displayScale = 1;
    private string zoomPercent = "100%";
    private int selectedIndexMirror = -1;
    private Point? pressPoint;
    private Point lastDragPoint;
    private bool dragActive;
    // While the next texture decodes, the last one stays up a moment rather than flashing "Decoding texture…",
    // which shows only when the decode takes longer.
    private const int PlaceholderDelay = 250;
    private Avalonia.Threading.DispatcherTimer? placeholderTimer;
    private bool placeholderDue;

    public TextureViewport()
    {
        InitializeComponent();
        Checkerboard.CellSize = (int)Ui.Theme.TextureCheckerCellSize;
        AddHandler(PointerPressedEvent, PressTunnel, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Refresh();
    }

    public bool FitMode
    {
        get => publishedFitMode;
        private set => SetAndRaise(FitModeProperty, ref publishedFitMode, value);
    }

    public double DisplayScale
    {
        get => displayScale;
        private set => SetAndRaise(DisplayScaleProperty, ref displayScale, value);
    }

    /// <summary><c>"105%"</c>.</summary>
    public string ZoomPercent
    {
        get => zoomPercent;
        private set => SetAndRaise(ZoomPercentProperty, ref zoomPercent, value);
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

    public double ManualScale => manualScale;

    public double PanX => panX;

    public double PanY => panY;

    /// <summary>The scale that fits the current preview into the viewport less 48px on each axis.</summary>
    public double FittedScale
    {
        get
        {
            int width = viewModel?.PreviewWidth ?? 0;
            int height = viewModel?.PreviewHeight ?? 0;
            return width > 0 && height > 0
                ? Math.Min(Math.Max(1, Bounds.Width - 48) / width, Math.Max(1, Bounds.Height - 48) / height)
                : 1;
        }
    }

    /// <summary>The image frame in viewport coordinates (QML <c>imageFrame</c> geometry).</summary>
    public Rect ImageFrameRect
    {
        get
        {
            double width = Math.Max(1, (viewModel?.PreviewWidth ?? 0) * DisplayScale);
            double height = Math.Max(1, (viewModel?.PreviewHeight ?? 0) * DisplayScale);
            return new Rect((Bounds.Width - width) / 2 + panX, (Bounds.Height - height) / 2 + panY, width, height);
        }
    }

    public void FitTexture()
    {
        fitMode = true;
        panX = 0;
        panY = 0;
        Refresh();
    }

    public void ShowActualSize()
    {
        fitMode = false;
        manualScale = 1;
        panX = 0;
        panY = 0;
        Refresh();
    }

    public void ZoomBy(double factor) => ZoomAt(factor, Bounds.Width / 2, Bounds.Height / 2);

    /// <summary>Shows the texture at <paramref name="scale"/> (1 is actual size), around the middle of the view.</summary>
    public void ZoomTo(double scale)
    {
        if (scale > 0)
            ZoomBy(scale / DisplayScale);
    }

    public void ZoomAt(double factor, double focusX, double focusY)
    {
        if (viewModel is null || viewModel.SelectedIndex < 0)
            return;
        double width = Bounds.Width, height = Bounds.Height;
        double currentScale = DisplayScale;
        double currentImageX = (width - viewModel.PreviewWidth * currentScale) / 2 + panX;
        double currentImageY = (height - viewModel.PreviewHeight * currentScale) / 2 + panY;
        double clampedFocusX = Math.Max(0, Math.Min(width, focusX));
        double clampedFocusY = Math.Max(0, Math.Min(height, focusY));
        double imagePointX = (clampedFocusX - currentImageX) / currentScale;
        double imagePointY = (clampedFocusY - currentImageY) / currentScale;

        fitMode = false;
        manualScale = Math.Max(0.05, Math.Min(16, currentScale * factor));
        panX = clampedFocusX - (width - viewModel.PreviewWidth * manualScale) / 2 - imagePointX * manualScale;
        panY = clampedFocusY - (height - viewModel.PreviewHeight * manualScale) / 2 - imagePointY * manualScale;
        Refresh();
    }

    public void PanBy(double xDistance, double yDistance)
    {
        panX += xDistance;
        panY += yDistance;
        Refresh();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (viewModel is not null)
            viewModel.PropertyChanged -= ViewModelChanged;
        viewModel = DataContext as ITexturePreviewSource;
        if (viewModel is not null)
            viewModel.PropertyChanged += ViewModelChanged;
        selectedIndexMirror = viewModel?.SelectedIndex ?? -1;
        Refresh();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty || change.Property == CheckerboardVisibleProperty
            || change.Property == FilteredPreviewProperty)
            Refresh();
    }

    /// <summary>Takes the press before Avalonia's click-to-focus: a QML Item gains focus only once the drag starts.</summary>
    private void PressTunnel(object? sender, PointerPressedEventArgs e)
    {
        if (viewModel is null || viewModel.SelectedIndex < 0 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        pressPoint = e.GetPosition(this);
        lastDragPoint = pressPoint.Value;
        dragActive = false;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (pressPoint is not { } origin)
            return;
        Point point = e.GetPosition(this);
        if (!dragActive)
        {
            if (Math.Abs(point.X - origin.X) <= DragThreshold && Math.Abs(point.Y - origin.Y) <= DragThreshold)
                return;
            // DragHandler's translation runs from the press point, so the image catches up with the pointer.
            dragActive = true;
            lastDragPoint = origin;
            Cursor = TextureCursors.ClosedHand;
            Focus(NavigationMethod.Pointer);
        }
        PanBy(point.X - lastDragPoint.X, point.Y - lastDragPoint.Y);
        lastDragPoint = point;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (pressPoint is null)
            return;
        EndDrag();
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        EndDrag();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        // Qt angleDelta: 120 per notch.
        double delta = e.Delta.Y * 120;
        if (delta == 0)
            return;
        Point point = e.GetPosition(this);
        ZoomAt(Math.Pow(1.25, delta / 120), point.X, point.Y);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
            return;
        switch (e.Key)
        {
            case Key.Left: PanBy(-KeyboardPanStep, 0); break;
            case Key.Right: PanBy(KeyboardPanStep, 0); break;
            case Key.Up: PanBy(0, -KeyboardPanStep); break;
            case Key.Down: PanBy(0, KeyboardPanStep); break;
            default: return;
        }
        e.Handled = true;
    }

    private void EndDrag()
    {
        pressPoint = null;
        if (!dragActive)
            return;
        dragActive = false;
        Cursor = null;
    }

    private void ViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (viewModel is null)
            return;
        if (e.PropertyName == nameof(TextureViewerViewModel.SelectedIndex) && viewModel.SelectedIndex != selectedIndexMirror)
        {
            selectedIndexMirror = viewModel.SelectedIndex;
            FitTexture();
            return;
        }
        if (e.PropertyName is nameof(TextureViewerViewModel.PreviewWidth) or nameof(TextureViewerViewModel.PreviewHeight)
            or nameof(TextureViewerViewModel.PreviewImage) or nameof(TextureViewerViewModel.PreviewLoading)
            or nameof(TextureViewerViewModel.Status) or nameof(TextureViewerViewModel.Error))
            Refresh();
    }

    /// <summary>
    /// Whether the last image stays up, its frame too, while the next decodes: for a moment after the decode starts,
    /// and only when there is one up.
    /// </summary>
    private bool HoldsLastImage()
    {
        if (viewModel is not { PreviewLoading: true, SelectedIndex: >= 0 })
        {
            placeholderTimer?.Stop();
            placeholderDue = false;
            return false;
        }
        if (placeholderDue || ImageFrame.Source is null)
            return false;
        if (placeholderTimer is not { IsEnabled: true })
        {
            placeholderTimer ??= new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PlaceholderDelay) };
            placeholderTimer.Tick -= OnPlaceholderDue;
            placeholderTimer.Tick += OnPlaceholderDue;
            placeholderTimer.Start();
        }
        return true;
    }

    private void OnPlaceholderDue(object? sender, EventArgs e)
    {
        placeholderTimer?.Stop();
        placeholderDue = true;
        Refresh();
    }

    private void Refresh()
    {
        DisplayScale = fitMode ? FittedScale : manualScale;
        FitMode = fitMode;
        ZoomPercent = $"{QtRaster.JsRound(DisplayScale * 100):0}%";
        bool hasSelection = viewModel is { SelectedIndex: >= 0 };
        Focusable = hasSelection;
        Checkerboard.IsVisible = CheckerboardVisible;
        PlainBackground.IsVisible = !CheckerboardVisible;
        ImageFrame.FrameVisible = hasSelection;
        bool holding = HoldsLastImage();
        if (!holding)
        {
            ImageFrame.Frame = ImageFrameRect;
            ImageFrame.Source = viewModel is { HasPreview: true } ? viewModel.PreviewImage : null;
        }
        ImageFrame.Filtered = FilteredPreview;
        MessageText.IsVisible = !holding && (viewModel is null || viewModel.PreviewLoading || viewModel.SelectedIndex < 0);
        MessageText.Text = viewModel is null ? "" : viewModel.PreviewLoading ? "Decoding texture…" : viewModel.Status;
        MessageText.Foreground = viewModel is null || viewModel.Error.Length == 0 ? Ui.Theme.TextFaint : Ui.Theme.Error;
        // The panning cursor shows only while a drag is active.
        if (!hasSelection && dragActive)
            EndDrag();
    }
}
