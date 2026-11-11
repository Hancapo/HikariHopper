using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using FiveFury;
using CommonFury.Textures;
using FiveFury.Textures;
using HikariHopper.Textures;
using Avalonia.VisualTree;
using HikariHopper.Tests.Textures;
using HikariHopper.Views;
using HikariHopper.Views.Controls;

namespace HikariHopper.Tests;

/// <summary>Behaviour of the texture editor (once TextureDictionaryWindow.qml) and its parts.</summary>
public sealed class TextureWindowTests
{
    // Window layout: title bar 30 + menu row 26; rail 340 + 1px handle; toolbar 38 above the viewport.
    private const double RailTop = 56, RowHeight = 62, ViewportLeft = 341, ViewportTop = 94;

    private static TextureWindowHarness Opened(params Texture[] textures)
    {
        var harness = new TextureWindowHarness();
        TextureSet set = new(textures.Length > 0 ? textures : [TextureTestKit.Red("one"), TextureTestKit.Red("two"), TextureTestKit.Red("three")],
            GameTarget.Legacy);
        return harness.Open("test.ytd", TextureTestKit.Bytes(set), "");
    }

    private static Texture Solid(string name, int width, int height) => TextureTestKit.Bgra(name, width, height, [40, 80, 160, 255]);

    private static Point Row(int index) => new(150, RailTop + index * RowHeight + 20);

    [AvaloniaFact]
    public void TheEditorFillsItsWindowAsAModalAndNamesItsDocument()
    {
        TextureWindowHarness harness = Opened();
        TextureEditor editor = harness.Window;
        Assert.True(editor.IsOpen);
        // A modal over the blurred window, everything but a 24px margin.
        Assert.Same(harness.Host, TopLevel.GetTopLevel(editor));
        Assert.Equal(new Size(TextureWindowHarness.EditorWidth, TextureWindowHarness.EditorHeight), editor.Bounds.Size);
        Assert.Equal(ModalOverlay.BlurRadius, Assert.IsAssignableFrom<IBlurEffect>(harness.Backdrop.Effect).Radius, 1);
        // Its header names the document and its state, beside the icon the explorer gives the file.
        Assert.Equal("test.ytd", editor.Title);
        Assert.Equal(editor.Title, editor.TitleText.Text);
        Assert.Equal(("book-image", "asset"), editor.TitleGlyph.Resolved);
        Assert.True(harness.ViewModel.RenameSelected("renamed"));
        harness.Settle();
        Assert.Equal("test.ytd *", editor.Title);
        harness.ViewModel.SetSourceForTesting("model.ydr", "");
        Assert.Equal("model.ydr *", editor.Title);
        Assert.Equal(("box", "asset"), editor.TitleGlyph.Resolved);
    }

    [AvaloniaFact]
    public void TheCloseMarkAndEscapeCloseTheEditorThroughTheViewModel()
    {
        TextureWindowHarness harness = Opened();
        harness.Window.CloseMark.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        harness.Settle();
        Assert.False(harness.Window.IsOpen);
        Assert.False(harness.ViewModel.HasDocument);

        // Esc asks too, so a modified document still gets its question.
        harness.Open("again.ytd", TextureTestKit.Bytes(new TextureSet([TextureTestKit.Red("again")], GameTarget.Legacy)));
        Assert.True(harness.ViewModel.RenameSelected("renamed"));
        harness.Key(Key.Escape);
        harness.Settle();
        Assert.True(harness.Window.IsOpen);
        Assert.True(Assert.IsType<TextureUnsavedChangesDialog>(harness.Window.UnsavedDialog).IsOpen);
    }

    [AvaloniaFact]
    public void ClosingAnUnmodifiedDocumentHidesTheWindow()
    {
        TextureWindowHarness harness = Opened();
        harness.Key(Key.W, RawInputModifiers.Control);
        harness.Settle();
        Assert.False(harness.Window.IsOpen);
        Assert.True(harness.Window.CloseApproved);
        Assert.False(harness.ViewModel.HasDocument);
    }

    [AvaloniaFact]
    public void ClosingAModifiedDocumentAsksAndCancelKeepsIt()
    {
        TextureWindowHarness harness = Opened();
        Assert.True(harness.ViewModel.RenameSelected("renamed"));
        harness.Settle();
        harness.Window.Close();
        harness.Settle();
        TextureUnsavedChangesDialog dialog = Assert.IsType<TextureUnsavedChangesDialog>(harness.Window.UnsavedDialog);
        Assert.True(dialog.IsOpen);
        Assert.Equal(470, dialog.Bounds.Width);
        // In the sheets' card: their header and footer.
        Assert.Equal(Ui.Theme.SheetHeaderHeight + 126 + Ui.Theme.DialogFooterHeight, dialog.Bounds.Height);
        harness.Key(Key.Escape);
        harness.Settle();
        Assert.False(dialog.IsOpen);
        Assert.True(harness.Window.IsOpen);
        Assert.Null(harness.ViewModel.PendingChange);
    }

    [AvaloniaFact]
    public void DontSaveDiscardsAndHides()
    {
        TextureWindowHarness harness = Opened();
        Assert.True(harness.ViewModel.RenameSelected("renamed"));
        harness.Window.Close();
        harness.Settle();
        harness.Window.UnsavedDialog!.FindControl<ChromeToolButton>("DiscardButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        harness.Settle();
        Assert.False(harness.Window.IsOpen);
        Assert.False(harness.ViewModel.HasDocument);
    }

    [AvaloniaFact]
    public void ShortcutsOpenDialogsAndAreBlockedWhileOneIsOpen()
    {
        TextureWindowHarness harness = Opened();
        harness.Key(Key.F2);
        Assert.IsType<TextureRenameDialog>(harness.Window.ToolDialog);
        harness.Key(Key.Delete);
        Assert.IsType<TextureRenameDialog>(harness.Window.ToolDialog);
        harness.Key(Key.Escape);
        harness.Settle();
        Assert.Null(harness.Window.ToolDialog);
        harness.Key(Key.Delete);
        Assert.IsType<TextureRemoveDialog>(harness.Window.ToolDialog);
    }

    [AvaloniaFact]
    public void ShortcutsFollowTheirEnabledConditions()
    {
        TextureWindowHarness harness = Opened();
        harness.ViewModel.SelectAllTextures();
        harness.Settle();
        Assert.False(harness.Window.RunShortcut(Key.F2, KeyModifiers.None));
        Assert.True(harness.Window.RunShortcut(Key.Delete, KeyModifiers.None));
        harness.Window.ToolDialog!.Close();
        harness.Settle();
        Assert.False(harness.Window.RunShortcut(Key.Z, KeyModifiers.Control));
        Assert.False(harness.Window.RunShortcut(Key.V, KeyModifiers.Control));
        Assert.False(harness.Window.RunShortcut(Key.F, KeyModifiers.Shift));
        Assert.True(harness.Window.RunShortcut(Key.F, KeyModifiers.None));
        Assert.True(harness.Window.RunShortcut(Key.D1, KeyModifiers.None));
        Assert.False(harness.Viewport.FitMode);
    }

    [AvaloniaFact]
    public void RailClicksSelectWithControlAndShift()
    {
        TextureWindowHarness harness = Opened(Solid("a", 4, 4), Solid("b", 4, 4), Solid("c", 4, 4), Solid("d", 4, 4));
        harness.Click(Row(1).X, Row(1).Y);
        Assert.Equal([1], TextureTestKit.Selected(harness.ViewModel));
        harness.Click(Row(3).X, Row(3).Y, modifiers: RawInputModifiers.Control);
        Assert.Equal([1, 3], TextureTestKit.Selected(harness.ViewModel));
        harness.Click(Row(0).X, Row(0).Y, modifiers: RawInputModifiers.Shift);
        Assert.Equal([0, 1, 2, 3], TextureTestKit.Selected(harness.ViewModel).Order());
        Assert.True(harness.Window.Rail.SelectionList.IsKeyboardFocusWithin);
    }

    [AvaloniaFact]
    public void RailKeysMoveAndExtendTheSelection()
    {
        TextureWindowHarness harness = Opened(Solid("a", 4, 4), Solid("b", 4, 4), Solid("c", 4, 4), Solid("d", 4, 4));
        harness.Click(Row(0).X, Row(0).Y);
        harness.Key(Key.Down);
        Assert.Equal(1, harness.ViewModel.SelectedIndex);
        harness.Key(Key.Down, RawInputModifiers.Shift);
        Assert.Equal([1, 2], TextureTestKit.Selected(harness.ViewModel).Order());
        harness.Key(Key.End);
        Assert.Equal(3, harness.ViewModel.SelectedIndex);
        harness.Key(Key.Down);
        Assert.Equal(3, harness.ViewModel.SelectedIndex);
        harness.Key(Key.Home);
        Assert.Equal(0, harness.ViewModel.SelectedIndex);
        harness.Key(Key.A, RawInputModifiers.Control);
        Assert.Equal(4, harness.ViewModel.SelectedCount);
    }

    [AvaloniaFact]
    public void RightClickingTheShownTextureKeepsItsPreview()
    {
        Texture mipped = new("mips", 64, 64, TextureFormat.B8G8R8A8Unorm, 3,
            TextureTestKit.Repeat([1, 2, 3, 255], 64 * 64 + 32 * 32 + 16 * 16));
        TextureWindowHarness harness = Opened(mipped, Solid("flat", 4, 4));
        harness.Settle();
        int row = harness.ViewModel.TexturesModel.ToList().FindIndex(item => item.Name == "mips");
        harness.ViewModel.SelectTexture(row);
        harness.Settle();
        harness.ViewModel.SetMipLevel(1);
        harness.Settle();
        Avalonia.Media.Imaging.Bitmap? shown = harness.ViewModel.PreviewImage;
        Assert.NotNull(shown);
        Assert.Equal(1, harness.ViewModel.MipLevel);

        // The press alone, then the release that opens the menu: neither may drop or redecode the preview.
        harness.MouseMove(Row(row));
        harness.MouseDown(Row(row), MouseButton.Right);
        Assert.Same(shown, harness.ViewModel.PreviewImage);
        Assert.False(harness.ViewModel.PreviewLoading);
        Assert.Equal(1, harness.ViewModel.MipLevel);
        harness.MouseUp(Row(row), MouseButton.Right);
        harness.Settle();
        Assert.True(harness.Window.Rail.ContextMenuPopup.IsOpen);
        Assert.Same(shown, harness.ViewModel.PreviewImage);
        Assert.Equal(1, harness.ViewModel.MipLevel);
        harness.Key(Key.Escape);
        harness.Settle();

        // An edit that replaces the texture in that row is a different texture: it reloads, back at the first mip.
        Assert.True(harness.ViewModel.ResizeSelected(64, 64, "point", 4, true));
        harness.Settle();
        Assert.NotSame(shown, harness.ViewModel.PreviewImage);
        Assert.NotNull(harness.ViewModel.PreviewImage);
        Assert.Equal(0, harness.ViewModel.MipLevel);
        // Right-clicking the replacement keeps it, as it does any texture on screen.
        Avalonia.Media.Imaging.Bitmap? replaced = harness.ViewModel.PreviewImage;
        harness.Click(Row(row).X, Row(row).Y, MouseButton.Right);
        harness.Settle();
        Assert.Same(replaced, harness.ViewModel.PreviewImage);
    }

    [AvaloniaFact]
    public void TheLastTextureStaysUpWhileTheNextDecodesThenTheMessageShows()
    {
        TextureWindowHarness harness = Opened(Solid("first", 64, 64), Solid("second", 32, 32));
        Avalonia.Media.Imaging.Bitmap? first = harness.Viewport.ImageFrame.Source;
        Assert.NotNull(first);
        using var release = new ManualResetEventSlim();
        Func<Texture, string, int, RgbaImage> decode = harness.ViewModel.DecodeImage;
        harness.ViewModel.DecodeImage = (texture, mode, mip) =>
        {
            Assert.True(release.Wait(10_000));
            return decode(texture, mode, mip);
        };
        try
        {
            // At once: the last texture, its frame as it was, and no "Decoding texture…" flash.
            Rect frame = harness.Viewport.ImageFrame.Frame;
            harness.ViewModel.SelectTexture(1);
            TextureWindowHarness.Pump();
            Assert.True(harness.ViewModel.PreviewLoading);
            Assert.Same(first, harness.Viewport.ImageFrame.Source);
            Assert.Equal(frame, harness.Viewport.ImageFrame.Frame);
            Assert.False(harness.Viewport.MessageText.IsVisible);
            // A decode that takes longer shows the message.
            TestWait.Pump(350);
            Assert.True(harness.Viewport.MessageText.IsVisible);
            Assert.Equal("Decoding texture…", harness.Viewport.MessageText.Text);
            Assert.Null(harness.Viewport.ImageFrame.Source);
        }
        finally
        {
            release.Set();
        }
        harness.Settle();
        Assert.False(harness.ViewModel.PreviewLoading);
        Assert.Same(harness.ViewModel.PreviewImage, harness.Viewport.ImageFrame.Source);
        Assert.False(harness.Viewport.MessageText.IsVisible);
    }

    [AvaloniaFact]
    public void RightClickKeepsTheSelectionAndOpensTheContextMenu()
    {
        TextureWindowHarness harness = Opened(Solid("a", 4, 4), Solid("b", 4, 4), Solid("c", 4, 4));
        harness.ViewModel.SelectAllTextures();
        harness.Settle();
        harness.Click(Row(2).X, Row(2).Y, MouseButton.Right);
        Assert.Equal(3, harness.ViewModel.SelectedCount);
        Assert.Equal(2, harness.ViewModel.SelectedIndex);
        Assert.True(harness.Window.Rail.ContextMenuPopup.IsOpen);
        Assert.True(harness.Window.PopupOpen);
        // A menu blocks the window shortcuts, as a Qt Menu does.
        harness.Key(Key.Delete);
        Assert.Null(harness.Window.ToolDialog);
    }

    [AvaloniaFact]
    public void MenuKeyAndShiftF10OpenTheContextMenuOnTheCurrentRow()
    {
        TextureWindowHarness harness = Opened(Solid("a", 4, 4), Solid("b", 4, 4));
        harness.Click(Row(1).X, Row(1).Y);
        harness.Key(Key.Apps);
        Assert.True(harness.Window.Rail.ContextMenuPopup.IsOpen);
        harness.Window.Rail.ContextMenuPopup.Close();
        harness.Settle();
        harness.Window.Rail.SelectionList.Focus();
        harness.Key(Key.F10, RawInputModifiers.Shift);
        Assert.True(harness.Window.Rail.ContextMenuPopup.IsOpen);
    }

    [AvaloniaFact]
    public void FitScaleLeaves24PixelsOnEachSide()
    {
        TextureWindowHarness harness = Opened(Solid("wide", 256, 64));
        TextureViewport viewport = harness.Viewport;
        Assert.Equal(new Size(909, 588), viewport.Bounds.Size);
        double expected = Math.Min((909 - 48) / 256.0, (588 - 48) / 64.0);
        Assert.True(viewport.FitMode);
        Assert.Equal(expected, viewport.DisplayScale, 9);
        Assert.Equal($"{Math.Floor(expected * 100 + 0.5)}%", viewport.ZoomPercent);
        Rect frame = viewport.ImageFrameRect;
        Assert.Equal((909 - 256 * expected) / 2, frame.X, 9);
    }

    [AvaloniaFact]
    public void ZoomKeepsTheImagePointUnderTheFocusAndClamps()
    {
        TextureWindowHarness harness = Opened(Solid("square", 100, 100));
        TextureViewport viewport = harness.Viewport;
        viewport.ShowActualSize();
        Rect before = viewport.ImageFrameRect;
        var focus = new Point(500, 300);
        double imageX = (focus.X - before.X) / 1.0, imageY = (focus.Y - before.Y) / 1.0;
        viewport.ZoomAt(2, focus.X, focus.Y);
        Rect after = viewport.ImageFrameRect;
        Assert.Equal(2, viewport.DisplayScale, 9);
        Assert.Equal(focus.X, after.X + imageX * 2, 6);
        Assert.Equal(focus.Y, after.Y + imageY * 2, 6);
        viewport.ZoomBy(1000);
        Assert.Equal(16, viewport.DisplayScale);
        viewport.ZoomBy(0.00001);
        Assert.Equal(0.05, viewport.DisplayScale);
        Assert.Equal("5%", viewport.ZoomPercent);
        viewport.FitTexture();
        Assert.True(viewport.FitMode);
        Assert.Equal(0, viewport.PanX);
    }

    [AvaloniaFact]
    public void WheelZoomsBy125PerNotchAroundThePointer()
    {
        TextureWindowHarness harness = Opened(Solid("square", 100, 100));
        harness.Viewport.ShowActualSize();
        harness.MouseWheel(new Point(ViewportLeft + 300, ViewportTop + 200), new Vector(0, 1));
        harness.Settle();
        Assert.Equal(1.25, harness.Viewport.DisplayScale, 9);
        harness.MouseWheel(new Point(ViewportLeft + 300, ViewportTop + 200), new Vector(0, -2));
        harness.Settle();
        Assert.Equal(1.25 / 1.5625, harness.Viewport.DisplayScale, 9);
    }

    [AvaloniaFact]
    public void DraggingPansPastTheThresholdAndFocusesForArrowKeys()
    {
        TextureWindowHarness harness = Opened(Solid("square", 100, 100));
        TextureViewport viewport = harness.Viewport;
        viewport.ShowActualSize();
        var start = new Point(ViewportLeft + 400, ViewportTop + 300);
        TextureEditor window = harness.Window;
        harness.MouseMove(start);
        harness.MouseDown(start, MouseButton.Left);
        harness.MouseMove(start + new Point(8, 6), RawInputModifiers.LeftMouseButton);
        Assert.Equal(0, viewport.PanX);
        Assert.False(viewport.IsFocused);
        harness.MouseMove(start + new Point(30, 20), RawInputModifiers.LeftMouseButton);
        Assert.Equal(30, viewport.PanX);
        Assert.Equal(20, viewport.PanY);
        Assert.True(viewport.IsFocused);
        harness.MouseUp(start + new Point(30, 20), MouseButton.Left);
        harness.Key(Key.Left);
        harness.Key(Key.Up);
        Assert.Equal(30 - TextureViewport.KeyboardPanStep, viewport.PanX);
        Assert.Equal(20 - TextureViewport.KeyboardPanStep, viewport.PanY);
    }

    [AvaloniaFact]
    public void ANewCurrentTextureFitsAgain()
    {
        TextureWindowHarness harness = Opened(Solid("a", 64, 64), Solid("b", 32, 32));
        harness.Viewport.ZoomBy(2);
        Assert.False(harness.Viewport.FitMode);
        harness.ViewModel.SelectTexture(1);
        harness.Settle();
        Assert.True(harness.Viewport.FitMode);
    }

    [AvaloniaFact]
    public void MipSliderStepsWithArrowKeys()
    {
        Texture mipped = new("mips", 64, 64, TextureFormat.B8G8R8A8Unorm, 3,
            TextureTestKit.Repeat([1, 2, 3, 255], 64 * 64 + 32 * 32 + 16 * 16));
        TextureWindowHarness harness = Opened(mipped);
        TextureMipSlider slider = harness.Window.Preview.MipBar.Slider;
        Assert.True(slider.IsEnabled);
        Assert.Equal(2, slider.Maximum);
        slider.Focus();
        harness.Key(Key.Right);
        harness.Settle();
        Assert.Equal(1, harness.ViewModel.MipLevel);
        Assert.Equal("MIP 1 / 2", harness.Window.Preview.MipBar.MipText.Text);
        Assert.Equal("32 × 32", harness.Window.Preview.MipBar.SizeText.Text);
    }

    [AvaloniaFact]
    public void MipSliderJumpsToAPressOffTheHandle()
    {
        Texture mipped = new("mips", 64, 64, TextureFormat.B8G8R8A8Unorm, 3,
            TextureTestKit.Repeat([1, 2, 3, 255], 64 * 64 + 32 * 32 + 16 * 16));
        TextureWindowHarness harness = Opened(mipped);
        TextureMipSlider slider = harness.Window.Preview.MipBar.Slider;
        // Near the right end, 3px from the top edge: empty space, well away from the 3px track and the handle.
        Point right = slider.TranslatePoint(new Point(slider.Bounds.Width - 8, 3), harness.Window)!.Value;
        harness.MouseMove(right);
        harness.MouseDown(right, MouseButton.Left);
        harness.Settle();
        // The value moves on the press itself, not on release.
        Assert.Equal(2, harness.ViewModel.MipLevel);
        harness.MouseUp(right, MouseButton.Left);
        harness.Settle();
        Assert.Equal(2, harness.ViewModel.MipLevel);

        Point left = slider.TranslatePoint(new Point(8, slider.Bounds.Height - 3), harness.Window)!.Value;
        harness.Click(left.X, left.Y);
        harness.Settle();
        Assert.Equal(0, harness.ViewModel.MipLevel);
    }

    [AvaloniaFact]
    public void MovingBetweenMipsIsInstant()
    {
        Texture mipped = new("mips", 64, 64, TextureFormat.B8G8R8A8Unorm, 3,
            TextureTestKit.Repeat([1, 2, 3, 255], 64 * 64 + 32 * 32 + 16 * 16));
        TextureWindowHarness harness = Opened(mipped);
        // Once the first preview is up, the other mips are decoded in the background.
        harness.Settle();
        foreach (int level in new[] { 2, 1, 0, 2 })
        {
            harness.ViewModel.SetMipLevel(level);
            // No settling: the decoded mip is on screen in the same call, with nothing loading.
            Assert.False(harness.ViewModel.PreviewLoading);
            Assert.Equal(64 >> level, harness.ViewModel.PreviewImage!.PixelSize.Width);
        }
    }

    [AvaloniaFact]
    public void ToolDialogsAreCentredWithTheQmlSize()
    {
        TextureWindowHarness harness = Opened();
        TextureToolDialog dialog = harness.Window.OpenToolDialog(TextureTool.Format);
        harness.Settle();
        Assert.Equal(new Size(520, Ui.Theme.SheetHeaderHeight + 292 + Ui.Theme.DialogFooterHeight), dialog.Bounds.Size);
        Assert.Equal(Math.Floor((1250 - 520) / 2.0 + 0.5), dialog.Bounds.X);
        Assert.Equal(Math.Floor((738 - dialog.Bounds.Height) / 2 + 0.5), dialog.Bounds.Y);
        Assert.Equal("CHANGE TEXTURE FORMAT", dialog.Heading);
        // Its header's close mark cancels it, as the sheets' does.
        ChromeToolButton close = Assert.IsType<ChromeToolButton>(dialog.GetVisualDescendants().OfType<Control>().Single(control => control.Name == "PART_CloseMark"));
        Assert.False(close.IsTabStop);
        close.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        harness.Settle();
        Assert.False(dialog.IsOpen);
    }

    [AvaloniaFact]
    public void RenameDialogValidatesAndRenames()
    {
        TextureWindowHarness harness = Opened();
        var dialog = (TextureRenameDialog)harness.Window.OpenToolDialog(TextureTool.Rename);
        harness.Settle();
        string name = harness.ViewModel.SelectedName;
        Assert.True(dialog.NameField.IsFocused);
        Assert.Equal(name, dialog.NameField.Text);
        Assert.Equal(name.Length, dialog.NameField.SelectionEnd - dialog.NameField.SelectionStart);
        dialog.NameField.Text = "   ";
        Assert.False(dialog.ApplyEnabled);
        dialog.NameField.Text = "fresh";
        Assert.True(dialog.ApplyEnabled);
        dialog.Apply();
        harness.Settle();
        Assert.False(dialog.IsOpen);
        Assert.Equal("fresh", harness.ViewModel.SelectedName);
    }

    [AvaloniaFact]
    public void TabCyclesInsideADialogFooterFirst()
    {
        TextureWindowHarness harness = Opened();
        var dialog = (TextureRenameDialog)harness.Window.OpenToolDialog(TextureTool.Rename);
        harness.Settle();
        harness.Key(Key.Tab);
        Assert.Equal("PART_CancelButton", (harness.Host.FocusManager!.GetFocusedElement() as Control)?.Name);
        harness.Key(Key.Tab);
        Assert.Equal("PART_ApplyButton", (harness.Host.FocusManager!.GetFocusedElement() as Control)?.Name);
        harness.Key(Key.Tab);
        Assert.True(dialog.NameField.IsFocused);

        // A dialog that focuses nothing itself still holds the focus, so Esc closes it.
        dialog.Close();
        harness.Settle();
        TextureToolDialog mipmaps = harness.Window.OpenToolDialog(TextureTool.Mipmaps);
        harness.Settle();
        Assert.True(mipmaps.IsFocused);
        harness.Key(Key.Tab);
        Assert.Equal("PART_CancelButton", (harness.Host.FocusManager!.GetFocusedElement() as Control)?.Name);
        harness.Key(Key.Escape);
        harness.Settle();
        Assert.False(mipmaps.IsOpen);
    }

    [AvaloniaFact]
    public void ResizeFieldsRejectWhatQtIntValidatorRejects()
    {
        Assert.Equal(QtIntValidator.State.Acceptable, QtIntValidator.Validate("512", 1, 32767));
        Assert.Equal(QtIntValidator.State.Intermediate, QtIntValidator.Validate("", 1, 32767));
        Assert.Equal(QtIntValidator.State.Intermediate, QtIntValidator.Validate("0", 1, 32767));
        Assert.Equal(QtIntValidator.State.Intermediate, QtIntValidator.Validate("99999", 1, 32767));
        Assert.Equal(QtIntValidator.State.Invalid, QtIntValidator.Validate("999999", 1, 32767));
        Assert.Equal(QtIntValidator.State.Invalid, QtIntValidator.Validate("-1", 1, 32767));
        Assert.Equal(QtIntValidator.State.Invalid, QtIntValidator.Validate("12a", 1, 32767));
        Assert.Equal(QtIntValidator.State.Acceptable, QtIntValidator.Validate("+12", 1, 32767));
    }

    [AvaloniaFact]
    public void CtrlZUndoesAResizeAtOnceEvenWhenWhatHadTheKeyboardIsGone()
    {
        TextureWindowHarness harness = Opened(Solid("wide", 256, 64));
        // The keyboard on something the dialog outlives (a menu's item, a row its work rebuilds): gone when it closes.
        var gone = new Avalonia.Controls.Button { Focusable = true };
        harness.Backdrop.Children.Add(gone);
        harness.Settle();
        gone.Focus();
        var dialog = (TextureResizeDialog)harness.Window.OpenToolDialog(TextureTool.Resize);
        harness.Settle();
        harness.Backdrop.Children.Remove(gone);
        dialog.WidthField.Text = "128";
        dialog.Apply();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (harness.ViewModel.OperationBusy && clock.ElapsedMilliseconds < 10_000)
            harness.Settle();
        harness.Settle();
        Assert.Equal(128, harness.ViewModel.SelectedWidth);

        // The keyboard is back in the editor, so Ctrl+Z undoes the resize without a click first.
        Assert.True(harness.Window.IsKeyboardFocusWithin, $"focused: {harness.Host.FocusManager?.GetFocusedElement()}");
        harness.Key(Key.Z, RawInputModifiers.Control);
        harness.Settle();
        Assert.Equal(256, harness.ViewModel.SelectedWidth);
    }

    [AvaloniaFact]
    public void CtrlZUndoesAResizeAppliedFromTheRightClickMenuWithNothingElseTouched()
    {
        TextureWindowHarness harness = Opened(Solid("wide", 256, 64));
        void ClickOn(Avalonia.Controls.Control control)
        {
            Point at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), harness.Host)!.Value;
            harness.Host.MouseMove(at);
            harness.Host.MouseDown(at, MouseButton.Left);
            harness.Host.MouseUp(at, MouseButton.Left);
            TextureWindowHarness.Pump();
        }

        // Right-click the texture, Resize, To a size…, then Apply as it opens.
        harness.Click(Row(0).X, Row(0).Y, MouseButton.Right);
        harness.Settle();
        TextureContextMenu menu = harness.Window.Rail.ContextMenuPopup;
        Assert.True(menu.IsOpen);
        ClickOn(menu.ResizeMenu);
        harness.Settle();
        ClickOn(menu.ResizeItem);
        harness.Settle();
        var dialog = Assert.IsType<TextureResizeDialog>(harness.Window.ToolDialog);
        dialog.WidthField.Text = "128";
        ClickOn(dialog.ApplyButton!);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (harness.ViewModel.OperationBusy && clock.ElapsedMilliseconds < 10_000)
            harness.Settle();
        harness.Settle();
        Assert.Equal(128, harness.ViewModel.SelectedWidth);

        // Nothing else touched: Ctrl+Z undoes it.
        Assert.True(harness.Window.IsKeyboardFocusWithin, $"focused: {harness.Host.FocusManager?.GetFocusedElement()}");
        harness.Key(Key.Z, RawInputModifiers.Control);
        harness.Settle();
        Assert.Equal(256, harness.ViewModel.SelectedWidth);
    }

    [AvaloniaFact]
    public void ResizeDialogLocksTheAspectAndEstimatesTheResult()
    {
        TextureWindowHarness harness = Opened(Solid("wide", 256, 64));
        var dialog = (TextureResizeDialog)harness.Window.OpenToolDialog(TextureTool.Resize);
        harness.Settle();
        Assert.Equal("RESIZE TEXTURE", dialog.Heading);
        Assert.True(dialog.WidthField.IsFocused);
        dialog.WidthField.Text = "128";
        harness.Key(Key.Enter);
        Assert.Equal("32", dialog.HeightField.Text);
        Assert.Equal($"256 × 64  →  128 × 32  ·  {harness.ViewModel.EstimatedMipCount(128, 32, 4)} mips  ·  last "
            + harness.ViewModel.EstimatedSmallestMipDimensions(128, 32, 4), dialog.ResultText.Text);
        Assert.True(dialog.ApplyEnabled);
        // Typing a letter is refused, as the IntValidator refuses it.
        dialog.WidthField.Text = "12x";
        Assert.Equal("128", dialog.WidthField.Text);
        dialog.WidthField.Text = "";
        Assert.False(dialog.ApplyEnabled);
        Assert.Equal("256 × 64  →  — × 32  ·  1 mips  ·  last none", dialog.ResultText.Text);
        dialog.MipCheck.IsChecked = false;
        Assert.False(dialog.MipSizeCombo.IsVisible);
    }

    [AvaloniaFact]
    public void PowerOfTwoDialogNeedsANonPowerOfTwoTexture()
    {
        TextureWindowHarness harness = Opened(Solid("odd", 100, 60), Solid("even", 64, 64));
        int odd = harness.ViewModel.TexturesModel.ToList().FindIndex(item => item.Name == "odd");
        harness.ViewModel.SelectTexture(odd);
        harness.Settle();
        var dialog = (TextureResizeDialog)harness.Window.OpenToolDialog(TextureTool.PowerOfTwo);
        harness.Settle();
        Assert.Equal("RESIZE TO POWER OF 2", dialog.Heading);
        Assert.False(dialog.WidthField.IsEffectivelyVisible);
        Assert.True(dialog.RoundingCombo.IsFocused, $"focused: {harness.Host.FocusManager?.GetFocusedElement()}");
        Assert.True(dialog.ApplyEnabled);
        Assert.Equal($"100 × 60  →  128 × 64  ·  {harness.ViewModel.EstimatedMipCount(128, 64, 4)} mips  ·  last "
            + harness.ViewModel.EstimatedSmallestMipDimensions(128, 64, 4), dialog.ResultText.Text);
        dialog.Close();
        harness.ViewModel.SelectTexture(1 - odd);
        harness.Settle();
        dialog = (TextureResizeDialog)harness.Window.OpenToolDialog(TextureTool.PowerOfTwo);
        harness.Settle();
        Assert.False(dialog.ApplyEnabled);
    }

    [AvaloniaFact]
    public void PasteDialogValidatesTheNewName()
    {
        TextureWindowHarness harness = Opened();
        harness.ViewModel.SelectAllTextures();
        harness.Clipboard.SetImageAsync(TextureTestKit.Solid(8, 4, 1, 2, 3, 255)).Wait();
        harness.Key(Key.V, RawInputModifiers.Control);
        harness.Settle();
        TexturePasteDialog dialog = Assert.IsType<TexturePasteDialog>(harness.Window.PasteDialog);
        Assert.False(dialog.ReplaceMode);
        Assert.Equal("pasted_texture", dialog.NameField.Text);
        Assert.True(dialog.ApplyEnabled);
        dialog.NameField.Text = "TWO";
        Assert.False(dialog.ApplyEnabled);
        Assert.True(dialog.ErrorText.IsVisible);
        Assert.True(dialog.NameField.Invalid);
        harness.Key(Key.Escape);
        harness.Settle();
        Assert.False(dialog.IsOpen);
        Assert.False(harness.ViewModel.PastePending);
    }

    [AvaloniaFact]
    public void PasteOverASingleSelectionOffersReplace()
    {
        TextureWindowHarness harness = Opened();
        harness.Clipboard.SetImageAsync(TextureTestKit.Solid(8, 4, 1, 2, 3, 255)).Wait();
        harness.ViewModel.RequestPasteImage().Wait();
        harness.Settle();
        TexturePasteDialog dialog = harness.Window.PasteDialog!;
        Assert.True(dialog.ReplaceMode);
        Assert.Equal(harness.ViewModel.SelectedName, dialog.NameField.Text);
        Assert.False(dialog.NameField.IsEnabled);
        Assert.True(dialog.ModeCombo.IsFocused);
        dialog.ModeCombo.SelectedIndex = 1;
        Assert.Equal("pasted_texture", dialog.NameField.Text);
    }

    [AvaloniaFact]
    public void DroppingImagesWithClashingNamesAsksFirst()
    {
        using var folder = new TempFolder();
        string clash = TextureTestKit.ImageFile(folder.File("two.png"));
        TextureWindowHarness harness = Opened();
        var point = new Point(ViewportLeft + 300, ViewportTop + 200);
        harness.DragEnter(point.X, point.Y, clash);
        Assert.True(harness.Window.DropArea.OverlayVisible);
        harness.Drop(point.X, point.Y, clash);
        harness.Settle();
        Assert.False(harness.Window.DropArea.OverlayVisible);
        TextureImportDialog dialog = Assert.IsType<TextureImportDialog>(harness.Window.ImportDialog);
        Assert.Contains("Import 1 files? 1 matching names", dialog.MessageText.Text);
        harness.Key(Key.Escape);
        harness.Settle();
        Assert.False(harness.ViewModel.ImportConfirmationPending);
        Assert.Null(harness.Window.ImportDialog);
    }

    [AvaloniaFact]
    public void ImagesThatArentOnDiskAreCopiedInAndImported()
    {
        using var folder = new TempFolder();
        byte[] png = File.ReadAllBytes(TextureTestKit.ImageFile(folder.File("source.png")));
        TextureWindowHarness harness = Opened();
        var point = new Point(ViewportLeft + 300, ViewportTop + 200);
        harness.DragEnter(point.X, point.Y, MemoryStorageFile.Create("from_browser.png", png));
        Assert.True(harness.Window.DropArea.OverlayVisible);
        harness.Drop(point.X, point.Y, MemoryStorageFile.Create("from_browser.png", png));
        harness.Settle();
        Assert.Contains("from_browser", harness.ViewModel.Dictionary!.Names);
        Assert.Equal((8, 4), (harness.ViewModel.SelectedWidth, harness.ViewModel.SelectedHeight));

        // Something that isn't an image is still refused.
        harness.DragEnter(point.X, point.Y, MemoryStorageFile.Create("notes.txt", [1, 2, 3]));
        Assert.False(harness.Window.DropArea.OverlayVisible);
    }

    [AvaloniaFact]
    public void TheFileMenuAndCtrlIImportImages()
    {
        using var folder = new TempFolder();
        TextureWindowHarness harness = Opened();
        var dialogs = (FakeDialogs)harness.ViewModel.Dialogs;
        RetroMenuItem import = harness.Window.MenuRow.FileMenu.Items.OfType<RetroMenuItem>().Single(item => item.Text == "Import images…");
        Assert.Equal("Ctrl+I", import.ShortcutText);
        Assert.True(import.IsEnabled);
        dialogs.OpenFiles.Add(TextureTestKit.ImageFile(folder.File("from_menu.png")));
        import.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        harness.Settle();
        Assert.Contains("from_menu", harness.ViewModel.Dictionary!.Names);

        dialogs.OpenFiles.Clear();
        dialogs.OpenFiles.Add(TextureTestKit.ImageFile(folder.File("from_keys.png")));
        harness.Key(Key.I, RawInputModifiers.Control);
        harness.Settle();
        Assert.Contains("from_keys", harness.ViewModel.Dictionary!.Names);
        Assert.Equal(["Import images", "Import images"], dialogs.Titles);
    }

    [AvaloniaFact]
    public void DropAreaRefusesNonImagesAndOpenDialogs()
    {
        using var folder = new TempFolder();
        File.WriteAllText(folder.File("notes.txt"), "x");
        TextureWindowHarness harness = Opened();
        harness.DragEnter(700, 300, folder.File("notes.txt"));
        Assert.False(harness.Window.DropArea.OverlayVisible);
        harness.Window.OpenToolDialog(TextureTool.Mipmaps);
        Assert.False(harness.Window.DropArea.DropEnabled);
    }

    [AvaloniaFact]
    public void ThePreviewToolbarGroupsItsControlsAndOffersZoomPresets()
    {
        TextureWindowHarness harness = Opened(Solid("square", 100, 100));
        TexturePreviewToolbar toolbar = harness.Window.Preview.Toolbar;
        TexturePreview preview = harness.Window.Preview;

        // Two wells (the grid toggle, zoom) and three switches: the channel, Smooth / Pixels, and Fit / 1:1.
        Assert.Equal(2, toolbar.GetVisualDescendants().OfType<Border>().Count(border => border.Classes.Contains("well")));
        Assert.Equal(3, toolbar.GetVisualDescendants().OfType<SegmentedSwitch>().Count());

        // The channel: the raised segment under the one shown, each letter in its own colour, full strength
        // when it is shown; a click on another channel shows it.
        Assert.Equal(0, toolbar.ChannelSwitch.SelectedIndex);
        Assert.Equal(Ui.Theme.Text, toolbar.ChannelSwitch.InkOf(0));
        Assert.Equal(Avalonia.Media.Color.Parse("#B8E8837E"), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(toolbar.ChannelSwitch.InkOf(1)).Color);
        double middle = toolbar.ChannelSwitch.Bounds.Height / 2;
        Point onRed = toolbar.ChannelSwitch.TranslatePoint(new Point(toolbar.ChannelSwitch.SideStart(1) + 8, middle), harness.Window)!.Value;
        harness.Click(onRed.X, onRed.Y);
        harness.Settle();
        Assert.Equal("r", harness.ViewModel.Channel);
        Assert.Equal(1, toolbar.ChannelSwitch.SelectedIndex);
        Assert.Equal(Avalonia.Media.Color.Parse("#E8837E"), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(toolbar.ChannelSwitch.InkOf(1)).Color);
        harness.ViewModel.SetChannel("a");
        harness.Settle();
        Assert.Equal(4, toolbar.ChannelSwitch.SelectedIndex);

        // Smooth / Pixels: a click on Pixels moves the raised segment there; a click on the side in use
        // changes nothing.
        Assert.True(preview.FilteredPreview);
        Assert.True(toolbar.SmoothSelected);
        Assert.Equal(0, toolbar.FilterSwitch.SegmentOffset, 3);
        Point onSmooth = toolbar.FilterSwitch.TranslatePoint(new Point(8, middle), harness.Window)!.Value;
        Point onPixels = toolbar.FilterSwitch.TranslatePoint(new Point(toolbar.FilterSwitch.Bounds.Width - 8, middle), harness.Window)!.Value;
        harness.Click(onSmooth.X, onSmooth.Y);
        harness.Settle();
        Assert.True(preview.FilteredPreview);
        harness.Click(onPixels.X, onPixels.Y);
        harness.Settle();
        Assert.False(preview.FilteredPreview);
        Assert.False(toolbar.SmoothSelected);
        TestWait.Pump(250);
        Assert.Equal(toolbar.FilterSwitch.Sides[0].Width, toolbar.FilterSwitch.SegmentOffset, 3);
        toolbar.FilterSwitch.Focus();
        harness.Key(Key.Space);
        Assert.True(preview.FilteredPreview);
        harness.Key(Key.Right);
        Assert.False(preview.FilteredPreview);
        harness.Key(Key.Left);
        Assert.True(preview.FilteredPreview);

        // Fit / 1:1: on Fit while the texture fits, on 1:1 at actual size, and on neither at any other zoom.
        Assert.True(preview.FitMode);
        Assert.Equal(0, toolbar.FitSwitch.SelectedIndex);
        Point onActual = toolbar.FitSwitch.TranslatePoint(new Point(toolbar.FitSwitch.Bounds.Width - 8, middle), harness.Window)!.Value;
        harness.Click(onActual.X, onActual.Y);
        harness.Settle();
        Assert.False(preview.FitMode);
        Assert.Equal(1, preview.DisplayScale, 6);
        Assert.Equal(1, toolbar.FitSwitch.SelectedIndex);
        // A zoom that only rounds to 100% is not actual size.
        preview.Viewport.ZoomBy(0.997);
        harness.Settle();
        Assert.Equal("100%", toolbar.ZoomButton.Text);
        Assert.Equal(-1, toolbar.FitSwitch.SelectedIndex);
        preview.Viewport.ZoomBy(2);
        harness.Settle();
        Assert.Equal(-1, toolbar.FitSwitch.SelectedIndex);
        toolbar.FitSwitch.Focus();
        harness.Key(Key.Space);
        harness.Settle();
        Assert.True(preview.FitMode);

        // The zoom readout opens the presets, centred under it: zooms only, the current one checked, none
        // while the texture fits at some other zoom. Picking one zooms exactly there.
        toolbar.OpenZoomPresets();
        harness.Settle();
        RetroMenu menu = toolbar.PresetsMenu!;
        Assert.True(menu.IsOpen);
        RetroMenuItem[] items = [.. menu.Items.OfType<RetroMenuItem>()];
        Assert.Equal(["25%", "50%", "100%", "200%", "400%", "800%"], items.Select(item => item.Text));
        Assert.Empty(menu.Items.OfType<RetroMenuSeparator>());
        Assert.DoesNotContain(items, item => item.IsChecked);
        RetroMenuFrame frame = menu.GetVisualDescendants().OfType<RetroMenuFrame>().First();
        PixelPoint frameCentre = frame.PointToScreen(new Point(frame.Bounds.Width / 2, 0));
        PixelPoint readoutCentre = toolbar.ZoomButton.PointToScreen(new Point(toolbar.ZoomButton.Bounds.Width / 2, toolbar.ZoomButton.Bounds.Height));
        Assert.InRange(frameCentre.X - readoutCentre.X, -1, 1);
        Assert.InRange(frameCentre.Y - readoutCentre.Y, 0, 8);
        items[3].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        harness.Settle();
        Assert.Equal(2, preview.DisplayScale, 6);
        Assert.False(preview.FitMode);
        Assert.Equal("200%", toolbar.ZoomButton.Text);
        menu.Close();
        harness.Settle();
        toolbar.OpenZoomPresets();
        harness.Settle();
        Assert.True(items[3].IsChecked);
        Assert.Single(items, item => item.IsChecked);
        menu.Close();
    }

    [AvaloniaFact]
    public void MenusOpenUnderTheirTitlesAndStayInsideTheWindow()
    {
        TextureWindowHarness harness = Opened();
        harness.Click(22, 42);
        Assert.True(harness.Window.MenuRow.FileMenu.IsOpen);
        Assert.True(harness.Window.MenuRow.FileButton.MenuOpen);
        // Pointing at the other title opens its menu instead, and back again.
        MenuBarButton edit = harness.Window.MenuRow.EditButton;
        harness.MouseMove(edit.TranslatePoint(new Point(edit.Bounds.Width / 2, edit.Bounds.Height / 2), harness.Window)!.Value);
        harness.Settle();
        Assert.True(harness.Window.MenuRow.EditMenu.IsOpen);
        Assert.False(harness.Window.MenuRow.FileMenu.IsOpen);
        Assert.True(edit.MenuOpen);
        Assert.False(harness.Window.MenuRow.FileButton.MenuOpen);
        MenuBarButton file = harness.Window.MenuRow.FileButton;
        harness.MouseMove(file.TranslatePoint(new Point(file.Bounds.Width / 2, file.Bounds.Height / 2), harness.Window)!.Value);
        harness.Settle();
        Assert.True(harness.Window.MenuRow.FileMenu.IsOpen);
        Assert.False(harness.Window.MenuRow.EditMenu.IsOpen);
        harness.Window.MenuRow.CloseMenus();
        Size size = RetroMenuPlacement.MenuSize(harness.Window.Rail.ContextMenuPopup);
        Assert.Equal(new Size(Ui.Theme.MenuWidth, 8 + 9 * Ui.Theme.MenuItemHeight + 2 * Ui.Theme.MenuSeparatorHeight), size);
    }

    [AvaloniaFact]
    public void TheTextureMenuGroupsItsActionsWithResizeAndFormatOpeningTheirOptions()
    {
        TextureWindowHarness harness = Opened(Solid("square", 100, 100));
        TextureContextMenu menu = harness.Window.Rail.ContextMenuPopup;
        string[] Labels(ItemsControl items) => [.. items.Items.Select(item => item is RetroMenuItem entry ? entry.Text : "—")];

        // The clipboard and replacing, then editing, then the texture's own actions; every row has an icon.
        Assert.Equal(["Copy image", "Paste…", "Replace from image…", "—", "Resize", "Recalculate mipmaps…", "Format", "—", "Extract DDS…", "Rename…", "Remove"],
            Labels(menu));
        Assert.All(menu.Items.OfType<RetroMenuItem>(), item => Assert.NotEqual("", item.IconName));
        Assert.Equal(["To a size…", "To power of 2…"], Labels(menu.ResizeMenu));
        Assert.Equal(["Change format…", "Optimize compression"], Labels(menu.FormatMenu));
        Assert.DoesNotContain(Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(menu).OfType<RetroMenuItem>(), item => item.Text.Contains("alpha", StringComparison.OrdinalIgnoreCase));

        // The options inside the submenus still open their dialogs.
        var asked = new List<string>();
        menu.ResizeRequested += (_, _) => asked.Add("resize");
        menu.PowerOfTwoRequested += (_, _) => asked.Add("pow2");
        menu.FormatRequested += (_, _) => asked.Add("format");
        foreach (RetroMenuItem item in new[] { menu.ResizeItem, menu.PowerOfTwoItem, menu.FormatItem })
            item.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(["resize", "pow2", "format"], asked);
    }

    [AvaloniaFact]
    public void QtCentringAndRasterRules()
    {
        // anchors.centerIn: an odd width takes the larger half.
        Assert.Equal(549, QtCenterPanel.Offset(1250, 151));
        Assert.Equal(550, QtCenterPanel.Offset(1250, 150));
        // An unantialiased edge at .5 starts on that pixel.
        Assert.Equal(new Rect(8, 8, 3, 3), QtRaster.SnapRect(8.5, 8.5, 3, 3));
        Assert.Equal(3, QtRaster.JsRound(2.5));
    }

}
