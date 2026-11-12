using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using HikariHopper.Core;
using HikariHopper.Views;

namespace HikariHopper.Tests;

/// <summary>Selecting and scrolling in the text viewer, through the pointer and the wheel.</summary>
[Collection(ExplorerCollection.Name)]
public sealed class TextViewerInputTests
{
    private static (MainWindow Window, TextViewer Viewer) Open(int lines = 400)
    {
        var text = new StringBuilder();
        for (int line = 0; line < lines; line++)
            text.Append($"line {line:000} alpha beta gamma\r\n");
        var window = new MainWindow { Width = 1000, Height = 640 };
        window.Show();
        var viewer = new TextViewer(TextDocument.Load("notes.txt", Encoding.UTF8.GetBytes(text.ToString()))) { Anchor = window.ExplorerScreenHost };
        viewer.Present();
        TestWait.Settle();
        return (window, viewer);
    }

    /// <summary>Where a line and column are drawn, in the window.</summary>
    private static Point At(MainWindow window, TextViewer viewer, int line, int column)
    {
        TextLinesView view = viewer.Lines;
        double charWidth = TextViewerInk.CharWidth(view.FontSize);
        var point = new Point(view.GutterWidth + 10 + (column + 0.25) * charWidth, 10 + (line + 0.5) * view.LineHeight);
        return view.TranslatePoint(point, window)!.Value;
    }

    [AvaloniaFact]
    public void TheHeaderNamesTheFileBesideTheExplorersIconForIt()
    {
        (MainWindow window, TextViewer viewer) = Open(10);
        Assert.Equal("notes.txt", viewer.TitleText.Text);
        Assert.Equal(("file-text", "data"), viewer.TitleGlyph.Resolved);
        window.Close();
        var meta = new TextViewer(TextDocument.Load("vehicles.meta", Encoding.UTF8.GetBytes("<Item />")));
        Assert.Equal("vehicles.meta", meta.TitleText.Text);
        // FiveFury knows this one: the vehicles' definitions, as its row in the explorer shows.
        Assert.Equal(("car-front", "data"), meta.TitleGlyph.Resolved);
    }

    [AvaloniaFact]
    public void DraggingPastTheBottomKeepsScrollingAndSelecting()
    {
        (MainWindow window, TextViewer viewer) = Open();
        ScrollViewer scroller = viewer.TextScroller;
        window.MouseDown(At(window, viewer, 2, 0), MouseButton.Left);
        Point below = scroller.TranslatePoint(new Point(200, scroller.Viewport.Height + 60), window)!.Value;
        window.MouseMove(below, RawInputModifiers.LeftMouseButton);
        int caretThen = viewer.Lines.Caret.Line;
        // Held still past the edge, it goes on scrolling on its own.
        TestWait.Until(() => viewer.Lines.Caret.Line > caretThen + 5, "the drag to scroll on past the edge");
        Assert.True(scroller.Offset.Y > 0);
        Assert.True(viewer.Lines.Caret.Line > caretThen + 5, $"{viewer.Lines.Caret.Line} vs {caretThen}");
        Assert.Equal(new TextPosition(2, 0), viewer.Lines.Selection.Start);
        window.MouseUp(below, MouseButton.Left);
        double stopped = scroller.Offset.Y;
        TestWait.Pump(200);
        Assert.Equal(stopped, scroller.Offset.Y);
        Assert.False(viewer.Lines.IsSelecting);
        window.Close();
    }

    [AvaloniaFact]
    public void DoubleAndTripleClicksDragByWordsAndLines()
    {
        (MainWindow window, TextViewer viewer) = Open();
        // "line 003 alpha beta gamma": a double-click on "alpha" picks it; dragging on to "gamma" picks whole words.
        Point alpha = At(window, viewer, 3, 10);
        window.MouseDown(alpha, MouseButton.Left);
        window.MouseUp(alpha, MouseButton.Left);
        window.MouseDown(alpha, MouseButton.Left);
        Assert.Equal((new TextPosition(3, 9), new TextPosition(3, 14)), viewer.Lines.Selection);
        window.MouseMove(At(window, viewer, 3, 22), RawInputModifiers.LeftMouseButton);
        Assert.Equal((new TextPosition(3, 9), new TextPosition(3, 25)), viewer.Lines.Selection);
        window.MouseUp(At(window, viewer, 3, 22), MouseButton.Left);

        // A click on a line number picks the line; a drag down the numbers picks more.
        TextLinesView view = viewer.Lines;
        Point number = view.TranslatePoint(new Point(10, 10 + 5.5 * view.LineHeight), window)!.Value;
        TestWait.Settle();
        window.MouseDown(number, MouseButton.Left);
        Assert.Equal((new TextPosition(5, 0), new TextPosition(6, 0)), viewer.Lines.Selection);
        window.MouseMove(number + new Point(0, 2 * view.LineHeight), RawInputModifiers.LeftMouseButton);
        Assert.Equal((new TextPosition(5, 0), new TextPosition(8, 0)), viewer.Lines.Selection);
        window.MouseUp(number, MouseButton.Left);
        // The count is worked out, not built: three lines of 25 characters and their breaks.
        Assert.Equal(3 * 26, viewer.Lines.SelectionLength);
        Assert.Contains("(78 selected)", viewer.CaretText.Text);
        window.Close();
    }

    [AvaloniaFact]
    public void AMinifiedFilesOneHugeLineDrawsAsQuicklyAsAShortOne()
    {
        // Two million characters on one line, as a minified JSON file has.
        var text = new StringBuilder("[");
        while (text.Length < 2_000_000)
            text.Append("{\"key\":\"value\",\"n\":12345},");
        text.Append("{}]");
        var window = new MainWindow { Width = 1000, Height = 640 };
        window.Show();
        // From opening on: loading, the first frame, then frames across the line.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var viewer = new TextViewer(TextDocument.Load("min.json", Encoding.UTF8.GetBytes(text.ToString()))) { Anchor = window.ExplorerScreenHost };
        viewer.Present();
        TestWait.Pump(50);
        using (var first = window.CaptureRenderedFrame()!)
        {
        }
        for (int frame = 0; frame < 5; frame++)
        {
            viewer.TextScroller.Offset = new Vector(frame * 400_000, 0);
            TestWait.Pump(20);
            using var shot = window.CaptureRenderedFrame()!;
        }
        // Only the columns on screen are drawn: whole lines would take seconds a frame.
        Assert.True(clock.ElapsedMilliseconds < 3000, $"{clock.ElapsedMilliseconds} ms to open and draw six frames");
        viewer.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void AClickAnywhereInTheTextLands()
    {
        (MainWindow window, TextViewer viewer) = Open();
        TextLinesView view = viewer.Lines;
        // Between two letters, in the space after "line", and far past the end of a line: all of it is the text.
        foreach ((Point point, TextPosition expected) in new[]
        {
            (At(window, viewer, 4, 4), new TextPosition(4, 4)),
            (view.TranslatePoint(new Point(view.GutterWidth + 600, 10 + 6.5 * view.LineHeight), window)!.Value, new TextPosition(6, 25)),
        })
        {
            Assert.Same(view, window.InputHitTest(point));
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            TestWait.Settle();
            Assert.True(view.IsFocused);
            Assert.Equal(expected, view.Caret);
        }
        window.Close();
    }

    private static byte[] Frame(MainWindow window)
    {
        using var frame = window.CaptureRenderedFrame()!;
        using var buffer = frame.Lock();
        var pixels = new byte[buffer.RowBytes * buffer.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address, pixels, 0, pixels.Length);
        return pixels;
    }

    [AvaloniaFact]
    public void AScrollMovesTheLastDrawingAndRedrawsOnlyNearItsEdge()
    {
        (MainWindow window, TextViewer viewer) = Open(2000);
        TextLinesView view = viewer.Lines;
        ScrollViewer scroller = viewer.TextScroller;
        // Focus elsewhere, so the caret's blink can't draw the view again between two frames.
        viewer.FindField.Focus();
        TestWait.Pump(50);
        Frame(window);

        // A few lines down: what was drawn moves, nothing is drawn again, and it looks as a fresh drawing would.
        int renders = view.Renders;
        scroller.Offset = new Vector(0, 3 * view.LineHeight + 7);
        byte[] moved = Frame(window);
        Assert.Equal(renders, view.Renders);
        view.InvalidateVisual();
        byte[] fresh = Frame(window);
        Assert.Equal(renders + 1, view.Renders);
        Assert.Equal(fresh, moved);

        // Most of a screen further, near the edge of what was drawn: a new drawing.
        scroller.Offset = new Vector(0, scroller.Offset.Y + scroller.Viewport.Height * 0.8);
        Frame(window);
        Assert.Equal(renders + 2, view.Renders);

        // Sideways, the gutter has to follow, so it always draws again.
        view.Document = TextDocument.Load("wide.txt", Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(new string('w', 400) + "\r\n", 50))));
        TestWait.Pump(100);
        Frame(window);
        renders = view.Renders;
        scroller.Offset = new Vector(40, scroller.Offset.Y);
        Frame(window);
        Assert.Equal(renders + 1, view.Renders);
        window.Close();
    }

    [AvaloniaFact]
    public void TheWheelGlidesAndAClickBackInTheTextDoesNotJump()
    {
        (MainWindow window, TextViewer viewer) = Open();
        ScrollViewer scroller = viewer.TextScroller;
        Point middle = At(window, viewer, 8, 4);
        window.MouseWheel(middle, new Vector(0, -1));
        // It eases out: a notch's whole distance once the glide has settled.
        TestWait.Until(() => Math.Abs(scroller.Offset.Y - SmoothScrolling.NotchDistance) < 1, "the glide to cover a notch");
        TestWait.Pump(50);
        double scrolled = scroller.Offset.Y;
        Assert.Equal(SmoothScrolling.NotchDistance, scrolled, 1);

        // Focus goes to the find field and back to the text by a click: the view stays where it is.
        viewer.FindField.Focus();
        TestWait.Pump(50);
        window.MouseDown(middle, MouseButton.Left);
        window.MouseUp(middle, MouseButton.Left);
        TestWait.Pump(100);
        Assert.True(viewer.Lines.IsFocused);
        Assert.Equal(scrolled, scroller.Offset.Y, 1);
        window.Close();
    }
}
