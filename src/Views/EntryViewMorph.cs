using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Transformation;
using Avalonia.Threading;

namespace HikariHopper.Views;

/// <summary>A list of entries whose items on show can be found by their place in the model.</summary>
internal interface IEntryItemsView
{
    /// <summary>
    /// The items laid out now: each one's model index, its container, its icon, and the details that come in once
    /// it has landed (a row's type and size, a cell's name and size).
    /// </summary>
    IEnumerable<(int Index, Control Item, Control Glyph, IReadOnlyList<Control> Details)> ShownItems();

    /// <summary>What the items scroll in.</summary>
    ScrollViewer Viewer { get; }

    /// <summary>The column header above the items.</summary>
    Control Header { get; }

    /// <summary>Scrolls so the item at <paramref name="index"/> is the first one on show.</summary>
    void ShowFirst(int index);
}

/// <summary>
/// The list and the grid trading places as one view morphing into the other: each item on show glides
/// from where it was to where it is now, its icon growing into the grid's big one (or settling into the
/// list's small one), while the old view fades out beneath. Items that weren't on show before come in
/// after them, one after another.
/// </summary>
internal static class EntryViewMorph
{
    /// <summary>How slow the morph runs: 1 as it is meant to, more to watch it (tests), 0 not at all.</summary>
    internal static double Pace { get; set; } = 1;

    /// <summary>How long an item takes to reach its new place.</summary>
    private static TimeSpan Duration => TimeSpan.FromMilliseconds(340 * Pace);

    private static TimeSpan FadeOut => TimeSpan.FromMilliseconds(160 * Pace);
    private static TimeSpan FadeIn => TimeSpan.FromMilliseconds(220 * Pace);
    private static TimeSpan Stagger => TimeSpan.FromMilliseconds(14 * Pace);
    private static TimeSpan MaxStagger => TimeSpan.FromMilliseconds(180 * Pace);
    private static TimeSpan DetailsDelay => TimeSpan.FromMilliseconds(200 * Pace);
    private static TimeSpan DetailsIn => TimeSpan.FromMilliseconds(160 * Pace);

    /// <summary>
    /// Replaces <paramref name="host"/>'s view with <paramref name="next"/> (through <paramref name="swap"/>) and
    /// morphs the old one into it, inside <paramref name="area"/>.
    /// </summary>
    public static void Run(Panel area, ContentControl host, Control next, Action swap)
    {
        Control? previous = host.Content as Control;
        // At once when Windows' animation effects are off.
        if (Pace <= 0 || Ui.Motion.Reduced || previous is not IEntryItemsView old || previous.Bounds.Width < 1 || previous.Bounds.Height < 1)
        {
            swap();
            return;
        }
        // Where each item's icon was, which one was first on show, and a picture of the old view to fade out
        // beneath the new one.
        var was = new Dictionary<int, Rect>();
        int first = int.MaxValue;
        foreach (var (index, item, glyph, _) in old.ShownItems())
        {
            if (glyph.TransformToVisual(area) is { } transform)
                was[index] = new Rect(glyph.Bounds.Size).TransformToAABB(transform);
            // The first item at least half in view.
            if (item.TransformToVisual(old.Viewer) is { } inViewer
                && new Rect(item.Bounds.Size).TransformToAABB(inViewer) is var rect
                && rect.Bottom - rect.Height / 2 > 0 && rect.Top < old.Viewer.Viewport.Height)
                first = Math.Min(first, index);
        }
        Image? picture = Picture(previous, host, area);
        swap();
        if (picture is not null)
        {
            area.Children.Insert(area.Children.IndexOf(host), picture);
            picture.Transitions = [new DoubleTransition { Property = Visual.OpacityProperty, Duration = FadeOut, Easing = new CubicEaseOut() }];
            Dispatcher.UIThread.Post(() => picture.Opacity = 0, DispatcherPriority.Render);
            DispatcherTimer.RunOnce(() =>
            {
                area.Children.Remove(picture);
                (picture.Source as IDisposable)?.Dispose();
                picture.Source = null;
            }, FadeOut + TimeSpan.FromMilliseconds(40));
        }
        // The headers cross-fade: the old one goes with the picture, the new one comes in over it.
        if (next is IEntryItemsView arriving)
        {
            Control header = arriving.Header;
            header.Opacity = 0;
            header.Transitions = [new DoubleTransition { Property = Visual.OpacityProperty, Duration = FadeOut, Easing = new CubicEaseOut() }];
            Dispatcher.UIThread.Post(() => header.Opacity = 1, DispatcherPriority.Render);
            DispatcherTimer.RunOnce(() => header.Transitions = null, FadeOut + TimeSpan.FromMilliseconds(40));
        }
        void OnLaidOut(object? sender, EventArgs e)
        {
            next.LayoutUpdated -= OnLaidOut;
            if (next is not IEntryItemsView view)
                return;
            // The new view keeps your place: what was first on show is first again, laid out there before it is
            // drawn (the layout pass is over, so it can run again now).
            if (first is > 0 and < int.MaxValue)
            {
                view.ShowFirst(first);
                next.UpdateLayout();
            }
            Morph(area, view, was);
        }
        next.LayoutUpdated += OnLaidOut;
    }

    /// <summary>The old view as it looked, placed where it was.</summary>
    private static Image? Picture(Control view, Control host, Control area)
    {
        double scaling = TopLevel.GetTopLevel(view)?.RenderScaling ?? 1;
        if (host.TranslatePoint(default, area) is not { } at)
            return null;
        var bitmap = new RenderTargetBitmap(
            new PixelSize((int)Math.Ceiling(view.Bounds.Width * scaling), (int)Math.Ceiling(view.Bounds.Height * scaling)),
            new Vector(96 * scaling, 96 * scaling));
        bitmap.Render(view);
        return new Image
        {
            Source = bitmap,
            Stretch = Stretch.Fill,
            Width = view.Bounds.Width,
            Height = view.Bounds.Height,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Margin = new Thickness(at.X, at.Y, 0, 0),
            IsHitTestVisible = false,
        };
    }

    private static void Morph(Panel area, IEntryItemsView view, Dictionary<int, Rect> was)
    {
        Rect shown = new(area.Bounds.Size);
        var moving = new List<Control>();
        var landing = new List<Control>();
        int arriving = 0;
        foreach (var (index, item, glyph, details) in view.ShownItems().OrderBy(entry => entry.Index))
        {
            if (item.TransformToVisual(area) is not { } itemTransform || glyph.TransformToVisual(item) is not { } glyphTransform)
                continue;
            Rect itemRect = new Rect(item.Bounds.Size).TransformToAABB(itemTransform);
            if (!itemRect.Intersects(shown))
                continue;
            Rect icon = new Rect(glyph.Bounds.Size).TransformToAABB(glyphTransform);
            Point centre = icon.Center;
            // The item grows or shrinks about its icon, so the icon is what travels.
            item.RenderTransformOrigin = new RelativePoint(
                item.Bounds.Width > 0 ? centre.X / item.Bounds.Width : 0.5,
                item.Bounds.Height > 0 ? centre.Y / item.Bounds.Height : 0.5, RelativeUnit.Relative);
            TimeSpan delay = TimeSpan.Zero;
            if (was.TryGetValue(index, out Rect from))
            {
                Point now = icon.Translate(itemRect.Position).Center;
                double scale = icon.Width > 0 ? Math.Clamp(from.Width / icon.Width, 0.3, 1) : 1;
                item.RenderTransform = Place(from.Center.X - now.X, from.Center.Y - now.Y, scale);
                item.Opacity = 0.35;
                // Only the icon (and a row's name) travels: the rest comes in as the item lands, so the text of
                // items crossing one another doesn't pile up on the way.
                foreach (Control detail in details)
                {
                    detail.Opacity = 0;
                    detail.Transitions =
                    [
                        new DoubleTransition { Property = Visual.OpacityProperty, Duration = DetailsIn, Delay = DetailsDelay, Easing = new CubicEaseOut() },
                    ];
                    landing.Add(detail);
                }
            }
            else
            {
                delay = TimeSpan.FromTicks(Math.Min(MaxStagger.Ticks, Stagger.Ticks * arriving++));
                item.RenderTransform = Place(0, 10, 0.96);
                item.Opacity = 0;
            }
            item.Transitions =
            [
                new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = Duration, Delay = delay, Easing = new Settle() },
                new DoubleTransition { Property = Visual.OpacityProperty, Duration = FadeIn, Delay = delay, Easing = new CubicEaseOut() },
            ];
            moving.Add(item);
        }
        if (moving.Count == 0)
            return;
        // In the next frame, so the transitions run from where the items start.
        Dispatcher.UIThread.Post(() =>
        {
            foreach (Control item in moving)
            {
                item.RenderTransform = Place(0, 0, 1);
                item.Opacity = 1;
            }
            foreach (Control detail in landing)
                detail.Opacity = 1;
        }, DispatcherPriority.Render);
        // Once there, the items are as they were before: containers are reused as the view scrolls.
        DispatcherTimer.RunOnce(() =>
        {
            foreach (Control item in moving)
            {
                item.Transitions = null;
                item.RenderTransform = null;
                item.Opacity = 1;
            }
            foreach (Control detail in landing)
            {
                detail.Transitions = null;
                detail.Opacity = 1;
            }
        }, Duration + MaxStagger + TimeSpan.FromMilliseconds(60));
    }

    /// <summary>
    /// An ease-out that runs a little past the end and settles back (a back ease, overshooting by under 2% of the
    /// way), so the items come to rest instead of stopping.
    /// </summary>
    private sealed class Settle : Easing
    {
        private const double Overshoot = 0.7;

        public override double Ease(double progress)
        {
            double back = progress - 1;
            return 1 + (Overshoot + 1) * back * back * back + Overshoot * back * back;
        }
    }

    private static ITransform Place(double x, double y, double scale)
    {
        TransformOperations.Builder builder = TransformOperations.CreateBuilder(2);
        builder.AppendScale(scale, scale);
        builder.AppendTranslate(x, y);
        return builder.Build();
    }
}
