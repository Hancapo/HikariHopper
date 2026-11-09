using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace HikariHopper.Views;

/// <summary>
/// The <c>imageFrame</c> item of <see cref="TextureViewport"/>: the stretched preview image and its
/// 1px <c>border</c> outline at <see cref="Frame"/>, a rectangle in this control's coordinates
/// that is usually fractional.
/// </summary>
/// <remarks>
/// Qt draws both with unantialiased quads, so pixels are covered when their centres fall inside,
/// and the GPU samples the texture at those centres: trilinearly (Qt <c>smooth</c> + <c>mipmap</c>)
/// when <see cref="Filtered"/>, nearest-neighbour otherwise — bilinear blending even at 100% when
/// the image sits on a half pixel. The image is drawn through Skia with the same sampling.
/// </remarks>
public class TextureImageFrame : Control
{
    public static readonly StyledProperty<Bitmap?> SourceProperty =
        AvaloniaProperty.Register<TextureImageFrame, Bitmap?>(nameof(Source));

    public static readonly StyledProperty<Rect> FrameProperty =
        AvaloniaProperty.Register<TextureImageFrame, Rect>(nameof(Frame));

    public static readonly StyledProperty<bool> FilteredProperty =
        AvaloniaProperty.Register<TextureImageFrame, bool>(nameof(Filtered), true);

    /// <summary>Whether the frame is shown at all (QML <c>visible: selectedIndex &gt;= 0</c>).</summary>
    public static readonly StyledProperty<bool> FrameVisibleProperty =
        AvaloniaProperty.Register<TextureImageFrame, bool>(nameof(FrameVisible));

    // Skia rounds a non-antialiased edge at exactly .5 up where Qt's rasteriser includes the pixel;
    // nudging the destination by a negligible amount makes both cover the same pixels.
    private const double EdgeNudge = 1e-4;

    private Bitmap? imageSource;
    private SKImage? image;

    static TextureImageFrame()
    {
        AffectsRender<TextureImageFrame>(SourceProperty, FrameProperty, FilteredProperty, FrameVisibleProperty);
    }

    public Bitmap? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public Rect Frame
    {
        get => GetValue(FrameProperty);
        set => SetValue(FrameProperty, value);
    }

    public bool Filtered
    {
        get => GetValue(FilteredProperty);
        set => SetValue(FilteredProperty, value);
    }

    public bool FrameVisible
    {
        get => GetValue(FrameVisibleProperty);
        set => SetValue(FrameVisibleProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (!FrameVisible)
            return;
        Rect frame = Frame;
        if (Image() is { } skImage)
        {
            Rect snapped = QtRaster.SnapRect(frame.X, frame.Y, frame.Width, frame.Height);
            var destination = new Rect(frame.X - EdgeNudge, frame.Y - EdgeNudge, frame.Width, frame.Height);
            context.Custom(new ImageOperation(new Rect(Bounds.Size), skImage, snapped, destination, Filtered));
        }
        QtRaster.FillBordered(context, null, Ui.Theme.Border, frame.X, frame.Y, frame.Width, frame.Height);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ReleaseImage();
    }

    /// <summary>A Skia copy of <see cref="Source"/>, in its own pixel format, made once per bitmap.</summary>
    private SKImage? Image()
    {
        Bitmap? source = Source;
        if (ReferenceEquals(source, imageSource))
            return image;
        ReleaseImage();
        imageSource = source;
        if (source is null)
            return null;
        // A bitmap already released (the last texture's, kept up while the next decodes) shows nothing.
        try
        {
            PixelSize size = source.PixelSize;
            if (size.Width <= 0 || size.Height <= 0)
                return null;
            SKColorType colours = source.Format == Avalonia.Platform.PixelFormat.Rgba8888 ? SKColorType.Rgba8888 : SKColorType.Bgra8888;
            SKAlphaType alpha = source.AlphaFormat switch
            {
                Avalonia.Platform.AlphaFormat.Unpremul => SKAlphaType.Unpremul,
                Avalonia.Platform.AlphaFormat.Opaque => SKAlphaType.Opaque,
                _ => SKAlphaType.Premul,
            };
            var info = new SKImageInfo(size.Width, size.Height, colours, alpha);
            using var pixels = new SKBitmap(info);
            source.CopyPixels(new PixelRect(size), pixels.GetPixels(), pixels.ByteCount, pixels.RowBytes);
            pixels.SetImmutable();
            image = SKImage.FromBitmap(pixels);
        }
        catch (ObjectDisposedException)
        {
            image = null;
        }
        return image;
    }

    private void ReleaseImage()
    {
        // A draw operation may still hold the image on the render thread; it is collected with it.
        image = null;
        imageSource = null;
    }

    private sealed class ImageOperation(Rect bounds, SKImage image, Rect clip, Rect destination, bool filtered) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;

        public bool HitTest(Point p) => false;

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose()
        {
        }

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature)
                return;
            using ISkiaSharpApiLease lease = feature.Lease();
            SKCanvas canvas = lease.SkCanvas;
            canvas.Save();
            canvas.ClipRect(clip.ToSKRect(), SKClipOperation.Intersect, false);
            SKSamplingOptions sampling = filtered
                ? new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
                : new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);
            // A shader maps the image onto the destination exactly; drawImageRect may snap a near-identity
            // scale to whole pixels and skip the half-pixel blending the GPU does.
            var dest = destination.ToSKRect();
            SKMatrix local = SKMatrix.CreateScaleTranslation(dest.Width / image.Width, dest.Height / image.Height, dest.Left, dest.Top);
            using SKShader shader = image.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, sampling, local);
            using var paint = new SKPaint { IsAntialias = false, Shader = shader };
            canvas.DrawRect(dest, paint);
            canvas.Restore();
        }
    }
}
