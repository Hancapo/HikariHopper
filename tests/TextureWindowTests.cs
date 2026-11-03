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
        // Its header names the document, its state and its kind.
        Assert.Equal("test.ytd — Texture Dictionary", editor.Title);
        Assert.Equal(editor.Title, editor.TitleText.Text);
        Assert.True(harness.ViewModel.RenameSelected("renamed"));
        harness.Settle();
        Assert.Equal("test.ytd * — Texture Dictionary", editor.Title);
        harness.ViewModel.SetSourceForTesting("model.ydr", "");
        Assert.Equal("model.ydr * — Embedded Textures", editor.Title);
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
        Assert.Equal(Ui.Theme.HeaderHeight + 126 + 58, dialog.Bounds.Height);
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
    public void UndoShortcutRevertsARename()
    {
        TextureWindowHarness harness = Opened();
        string name = harness.ViewModel.SelectedName;
        Assert.True(harness.ViewModel.RenameSelected("renamed"));
        harness.Settle();
        harness.Key(Key.Z, RawInputModifiers.Control);
        harness.Settle();
        Assert.Equal(name, harness.ViewModel.SelectedName);
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
    public void RailRowsHaveTheQmlMetrics()
    {
        TextureWindowHarness harness = Opened(Solid("a", 4, 4), Solid("b", 4, 4));
        Control row = harness.Window.Rail.SelectionList.ContainerFromIndex(0)!;
        Assert.Equal(RowHeight, row.Bounds.Height);
        Assert.Equal(340, row.Bounds.Width);
        Border thumbnail = row.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("thumbnail"));
        Assert.Equal(new Rect(7, 6, 50, 50), thumbnail.Bounds);
        Assert.False(harness.Window.Rail.Scrollable);
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
        Assert.Equal(new Size(520, Ui.Theme.HeaderHeight + 292 + 58), dialog.Bounds.Size);
        Assert.Equal(Math.Floor((1250 - 520) / 2.0 + 0.5), dialog.Bounds.X);
        Assert.Equal(Math.Floor((738 - dialog.Bounds.Height) / 2 + 0.5), dialog.Bounds.Y);
        Assert.Equal("CHANGE TEXTURE FORMAT", dialog.Heading);
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

        // Five wells: channels, the grid toggle, the Smooth / Pixels switch, fit / actual size, zoom.
        Assert.Equal(5, toolbar.GetVisualDescendants().OfType<Border>().Count(border => border.Classes.Contains("well")));

        // The channel letters carry their own colour; the shown channel's segment is raised in full ink.
        Assert.True(toolbar.RgbaButton.Sunken);
        Assert.Equal(Ui.Theme.Text, toolbar.RgbaButton.Foreground);
        Assert.Equal(Avalonia.Media.Color.Parse("#E8837E"), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(toolbar.RedButton.Foreground).Color);
        harness.ViewModel.SetChannel("r");
        harness.Settle();
        Assert.True(toolbar.RedButton.Sunken);
        Assert.Equal(1, Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(toolbar.RedButton.Foreground).Opacity);

        // Smooth / Pixels is one switch in a well of its own, apart from the grid toggle: a click flips
        // it and its thumb slides to the side in use.
        Assert.False(toolbar.FilterSwitch.IsVisualAncestorOf(toolbar.CheckerboardButton));
        Assert.True(preview.FilteredPreview);
        Assert.True(toolbar.SmoothSelected);
        Assert.Equal(0, toolbar.FilterThumbOffset, 3);
        Point onSwitch = toolbar.FilterSwitch.TranslatePoint(new Point(8, 12), harness.Window)!.Value;
        harness.Click(onSwitch.X, onSwitch.Y);
        harness.Settle();
        Assert.False(preview.FilteredPreview);
        Assert.False(toolbar.SmoothSelected);
        ExplorerStateRenders.Pump(250);
        Assert.Equal(toolbar.FilterThumb.Width, toolbar.FilterThumbOffset, 3);
        toolbar.FilterSwitch.Focus();
        harness.Key(Key.Space);
        Assert.True(preview.FilteredPreview);

        // The zoom readout opens the presets, the current zoom checked; picking one zooms exactly there.
        toolbar.OpenZoomPresets();
        harness.Settle();
        RetroMenu menu = toolbar.PresetsMenu!;
        Assert.True(menu.IsOpen);
        RetroMenuItem[] items = [.. menu.Items.OfType<RetroMenuItem>()];
        Assert.Equal(["Fit", "25%", "50%", "100%", "200%", "400%", "800%"], items.Select(item => item.Text));
        Assert.True(items[0].IsChecked);
        items[4].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        harness.Settle();
        Assert.Equal(2, preview.DisplayScale, 6);
        Assert.False(preview.FitMode);
        Assert.Equal("200%", toolbar.ZoomButton.Text);
        menu.Close();
        harness.Settle();
        toolbar.OpenZoomPresets();
        harness.Settle();
        Assert.True(items[4].IsChecked);
        Assert.False(items[0].IsChecked);
        items[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        harness.Settle();
        Assert.True(preview.FitMode);
    }

    [AvaloniaFact]
    public void MenusOpenUnderTheirTitlesAndStayInsideTheWindow()
    {
        TextureWindowHarness harness = Opened();
        harness.Click(22, 42);
        Assert.True(harness.Window.MenuRow.FileMenu.IsOpen);
        Assert.True(harness.Window.MenuRow.FileButton.MenuOpen);
        Size size = RetroMenuPlacement.MenuSize(harness.Window.Rail.ContextMenuPopup);
        Assert.Equal(new Size(Ui.Theme.MenuWidth, 8 + 12 * Ui.Theme.MenuItemHeight + 2 * Ui.Theme.MenuSeparatorHeight), size);
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

    [AvaloniaFact]
    public void ToolbarAndBarsHaveTheQmlHeights()
    {
        TextureWindowHarness harness = Opened();
        Assert.Equal(Ui.Theme.TextureToolbarHeight, harness.Window.Preview.Toolbar.Bounds.Height);
        Assert.Equal(Ui.Theme.TextureMipBarHeight, harness.Window.Preview.MipBar.Bounds.Height);
        Assert.Equal(Ui.Theme.StatusHeight, harness.Window.StatusBar.Bounds.Height);
        Assert.Equal(Ui.Theme.MenuHeight, harness.Window.MenuRow.Bounds.Height);
        Assert.Equal(Ui.Theme.TextureRailWidth, harness.Window.Rail.Bounds.Width);
        Assert.Equal("3 textures", harness.Window.StatusBar.GetVisualDescendants().OfType<TextBlock>().First().Text);
    }
}
