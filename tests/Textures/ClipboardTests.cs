using CommonFury.Textures;
using FiveFury;
using FiveFury.Textures;
using HikariHopper.Textures;
using HikariHopper.ViewModels;
using static HikariHopper.Tests.Textures.TextureTestKit;

namespace HikariHopper.Tests.Textures;

/// <summary>Ports of test_texture_paste.py and test_texture_clipboard.py (view-model side), on a process-local clipboard.</summary>
public sealed class ClipboardTests
{
    private static TextureSet Original() => new(new[] { "paint", "keep" }.Select(name => Bgra(name, 4, 4, [0, 0, 255, 255])), GameTarget.Legacy);

    private static async Task<(TextureViewerViewModel Viewer, FakeClipboard Clipboard, Texture[] Before, int[] Selection)> PasteSetup(
        string scenario)
    {
        var clipboard = new FakeClipboard();
        await clipboard.SetTextAsync("not an image");
        bool empty = scenario is "add" or "opaque" or "snapshot";
        TextureViewerViewModel viewer = Opened("textures.ytd", empty ? EmptyYtd(GameTarget.Enhanced) : Bytes(Original()), clipboard: clipboard);
        Assert.False(viewer.CanPasteImage);
        Assert.False(await viewer.RequestPasteImage());
        if (scenario == "multiple")
            viewer.SelectAllTextures();
        Texture[] before = [.. viewer.Dictionary!.Textures];
        int[] selection = [.. viewer.SelectedRows];
        int changes = 0;
        viewer.PropertyChanged += (_, args) => changes += args.PropertyName == nameof(viewer.CanPasteImage) ? 1 : 0;
        await clipboard.SetImageAsync(Solid(50, 19, 220, 40, 70, scenario == "opaque" ? (byte)255 : (byte)100));
        Assert.True(changes > 0); // Clipboard changes refresh CanPasteImage.
        Assert.True(viewer.CanPasteImage);
        int requests = 0;
        viewer.PasteImageRequested += (_, _) => requests++;
        Assert.True(await viewer.RequestPasteImage());
        Assert.Equal(1, requests);
        Assert.True(viewer.PastePending);
        Assert.False(viewer.CanPasteImage);
        Assert.Equal(before, viewer.Dictionary.Textures);
        return (viewer, clipboard, before, selection);
    }

    [AvaloniaTheory]
    [InlineData("add")]
    [InlineData("opaque")]
    [InlineData("snapshot")]
    [InlineData("multiple")]
    public async Task PasteAddsANewTexture(string scenario)
    {
        var (viewer, clipboard, before, selection) = await PasteSetup(scenario);
        Assert.False(viewer.PasteCanReplace);
        Assert.Equal("", viewer.PasteTargetName);
        if (scenario == "snapshot")
            await clipboard.SetTextAsync("changed after dialog opened");
        List<string> statuses = StatusHistory(viewer);
        Assert.True(viewer.PasteImage(false, " custom_name "));
        Assert.Equal("Pasting custom_name…", viewer.Status);
        Settle(viewer);
        Assert.Equal([.. before.Select(texture => texture.Name!), "custom_name"], Names(viewer));
        Assert.Contains("Pasted custom_name", statuses);
        Assert.Equal(scenario == "multiple" ? GameTarget.Legacy : GameTarget.Enhanced, viewer.Dictionary!.Target);
        Texture texture = viewer.Dictionary.Textures[^1];
        Assert.Equal((64, 16, 3), (texture.Width, texture.Height, texture.MipCount));
        Assert.Equal(scenario == "opaque" ? TextureFormat.BC1Unorm : TextureFormat.BC3Unorm, texture.Format);
        byte[] pixel = TextureProcessing.Decode(texture).Pixels[..4].ToArray();
        Assert.True(pixel[0] > 190 && pixel[2] < 100);
        if (scenario == "opaque")
            Assert.Equal(255, pixel[3]);
        else
            Assert.InRange(pixel[3], 80, 120);
        AssertUndoRestores(viewer, before, selection);
    }

    [AvaloniaFact]
    public async Task PasteReplacesTheSelectedTexture()
    {
        var (viewer, _, before, selection) = await PasteSetup("replace");
        before[viewer.SelectedIndex].Usage = TextureUsage.Diffuse;
        Assert.True(viewer.PasteCanReplace);
        int target = viewer.SelectedIndex;
        Assert.Equal(before[target].Name, viewer.PasteTargetName);
        Assert.True(viewer.PasteImage(true, "ignored"));
        Settle(viewer);
        IReadOnlyList<Texture> after = viewer.Dictionary!.Textures;
        Assert.Equal(2, after.Count);
        Assert.Equal(before[target].Name, after[target].Name);
        Assert.Equal((64, 16, TextureFormat.BC3Unorm, 3), (after[target].Width, after[target].Height, after[target].Format, after[target].MipCount));
        Assert.Same(before[1 - target], after[1 - target]);
        Assert.Equal((before[target].Usage, before[target].UsageFlags), (after[target].Usage, after[target].UsageFlags));
        AssertUndoRestores(viewer, before, selection);
    }

    [AvaloniaFact]
    public async Task PasteValidatesNewNames()
    {
        var (viewer, _, before, _) = await PasteSetup("validation");
        Assert.Equal("Enter a texture name", viewer.PasteNameError("   "));
        Assert.Equal("A texture with this name already exists", viewer.PasteNameError("PAINT"));
        Assert.Equal("Use up to 255 characters without control characters", viewer.PasteNameError("bad\0name"));
        Assert.Equal("Use up to 255 characters without control characters", viewer.PasteNameError(new string('a', 256)));
        foreach (string value in new[] { "", "   ", "PAINT", "bad\0name", new string('a', 256) })
        {
            Assert.NotEqual("", viewer.PasteNameError(value));
            Assert.False(viewer.PasteImage(false, value));
            Assert.True(viewer.PastePending);
            Assert.Equal(before, viewer.Dictionary!.Textures);
        }
        viewer.CancelPasteImage();
        Assert.False(viewer.PastePending);
        Assert.False(viewer.Modified);
        Assert.False(viewer.CanUndo);
    }

    [AvaloniaFact]
    public async Task StalePasteIsDiscarded()
    {
        var (viewer, _, _, _) = await PasteSetup("stale");
        viewer.CloseDocument();
        Settle(viewer);
        Assert.False(viewer.PasteImage(true, "ignored"));
        Assert.False(viewer.HasDocument);
        Assert.False(viewer.PastePending);
    }

    [AvaloniaFact]
    public async Task FailedPasteKeepsTheDocument()
    {
        var (viewer, _, before, _) = await PasteSetup("failure");
        viewer.TextureFromClipboardImage = (_, _) => throw new ArgumentException("simulated paste failure");
        Assert.True(viewer.PasteImage(true, ""));
        Settle(viewer);
        Assert.Equal(before, viewer.Dictionary!.Textures);
        Assert.False(viewer.Modified);
        Assert.Equal("Paste failed: simulated paste failure", viewer.Status);
        Assert.False(viewer.OperationBusy);
        Assert.False(viewer.CanUndo);
    }

    private static void AssertUndoRestores(TextureViewerViewModel viewer, Texture[] before, int[] selection)
    {
        Assert.True(viewer.Modified);
        Assert.Single(viewer.UndoStack);
        Assert.Equal("Paste image", viewer.UndoLabel);
        Assert.NotEmpty(YtdSaving.Serialize(viewer.Dictionary!));
        Assert.True(viewer.Undo());
        Settle(viewer);
        Assert.Equal(before, viewer.Dictionary!.Textures);
        Assert.Equal(selection, viewer.SelectedRows);
        Assert.False(viewer.Modified);
    }

    private static async Task<(TextureViewerViewModel, FakeClipboard)> CopySetup()
    {
        var clipboard = new FakeClipboard();
        await clipboard.SetTextAsync("untouched");
        TextureViewerViewModel probe = Viewer(clipboard);
        Assert.False(probe.CopySelectedImage());
        byte[] data = [.. Repeat([10, 20, 30, 64], 32), .. Repeat([50, 60, 70, 255], 8)];
        var textures = new[] { "paint", "detail" }.Select(name => new Texture(name, 8, 4, TextureFormat.B8G8R8A8Unorm, 2, data));
        TextureViewerViewModel viewer = Opened("textures.ytd", Bytes(new TextureSet(textures, GameTarget.Legacy)), clipboard: clipboard);
        Assert.True(viewer.CanEditSingle);
        return (viewer, clipboard);
    }

    [AvaloniaFact]
    public async Task CopiesTheBaseMipWithoutChangingThePreview()
    {
        var (viewer, clipboard) = await CopySetup();
        viewer.SetChannel("r");
        viewer.SetMipLevel(1);
        Settle(viewer);
        string preview = viewer.PreviewUrl;
        var decodedOn = new List<int>();
        viewer.DecodeImage = (texture, mode, mip) =>
        {
            lock (decodedOn)
                decodedOn.Add(Environment.CurrentManagedThreadId);
            return TextureOperations.DecodeImage(texture, mode, mip);
        };
        int uiThread = Environment.CurrentManagedThreadId;
        Assert.True(viewer.CopySelectedImage());
        Assert.True(viewer.OperationBusy);
        Assert.False(viewer.CanEditSingle);
        Settle(viewer);
        Assert.DoesNotContain(uiThread, decodedOn); // Decoding never blocks the UI thread.
        Assert.NotNull(clipboard.Image);
        Assert.Equal((8, 4), (clipboard.Image!.Width, clipboard.Image.Height));
        Assert.Equal([30, 20, 10, 64], clipboard.Image.Pixels[..4].ToArray());
        Assert.Equal(preview, viewer.PreviewUrl);
        Assert.Equal(("r", 1), (viewer.Channel, viewer.MipLevel));
        Assert.Equal($"Copied {viewer.SelectedName} to clipboard", viewer.Status);
        Assert.False(viewer.Modified);
        Assert.False(viewer.CanUndo);
        Assert.False(viewer.OperationBusy);
    }

    [AvaloniaFact]
    public async Task CopyGuards()
    {
        var (viewer, clipboard) = await CopySetup();
        viewer.SelectAllTextures();
        Assert.False(viewer.CanEditSingle);
        Assert.False(viewer.CopySelectedImage());
        viewer.SelectTexture(0);
        viewer.SetExternalWriteBusy(true);
        Assert.False(viewer.CanEditSingle);
        Assert.False(viewer.CopySelectedImage());
        viewer.SetExternalWriteBusy(false);
        viewer.SelectTexture(0, Avalonia.Input.KeyModifiers.Control);
        Assert.Equal(0, viewer.SelectedCount);
        Assert.False(viewer.CopySelectedImage());
        Assert.Equal("untouched", clipboard.Text);
    }

    [AvaloniaFact]
    public async Task CopyFailureReportsTheDecodeError()
    {
        var (viewer, clipboard) = await CopySetup();
        viewer.DecodeImage = (_, _, _) => throw new ArgumentException("simulated image decode failure");
        Assert.True(viewer.CopySelectedImage());
        Settle(viewer);
        Assert.False(viewer.OperationBusy);
        Assert.Equal("untouched", clipboard.Text);
        Assert.Equal("Could not copy image: simulated image decode failure", viewer.Status);
        Assert.False(viewer.Modified);
    }

    [AvaloniaFact]
    public async Task StaleCopyLeavesTheClipboardAlone()
    {
        var (viewer, clipboard) = await CopySetup();
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        viewer.DecodeImage = (texture, mode, mip) =>
        {
            started.Set();
            Assert.True(release.Wait(5000));
            return TextureOperations.DecodeImage(texture, mode, mip);
        };
        Assert.True(viewer.CopySelectedImage());
        Assert.True(started.Wait(5000));
        try
        {
            Assert.False(viewer.CopySelectedImage());
            viewer.CloseDocument();
        }
        finally
        {
            release.Set();
        }
        Settle(viewer);
        Assert.Equal("untouched", clipboard.Text);
        Assert.Equal(0, clipboard.ImageWrites - 0);
        Assert.False(viewer.OperationBusy);
        Assert.False(viewer.HasDocument);
    }
}
