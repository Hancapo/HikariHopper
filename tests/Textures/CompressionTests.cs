using CommonFury.Textures;
using FiveFury;
using FiveFury.Textures;
using HikariHopper.Textures;
using HikariHopper.ViewModels;
using static HikariHopper.Tests.Textures.TextureTestKit;

namespace HikariHopper.Tests.Textures;

/// <summary>Port of test_texture_compression_optimization.py (view-model side).</summary>
public sealed class CompressionTests
{
    /// <summary>16×8 with three authored mips: red, green, blue. BC2 is built from synthetic DXT3 blocks.</summary>
    internal static Texture MakeTexture(TextureFormat format, string name = "paint", byte alpha = 255, byte? lastAlpha = null)
    {
        var chunks = new List<byte>();
        (int Width, int Height, byte R, byte G, byte B)[] levels = [(16, 8, 255, 0, 0), (8, 4, 0, 255, 0), (4, 2, 0, 0, 255)];
        for (int index = 0; index < levels.Length; index++)
        {
            var (width, height, red, green, blue) = levels[index];
            byte levelAlpha = index == 2 && lastAlpha is { } last ? last : alpha;
            if (format == TextureFormat.BC2Unorm)
            {
                // Explicit 4-bit alpha and a solid RGB565 color.
                byte[] endpoint = red == 255 ? [0x00, 0xF8] : green == 255 ? [0xE0, 0x07] : [0x1F, 0x00];
                byte[] block = [.. Enumerable.Repeat((byte)(levelAlpha / 17 * 17), 8), .. endpoint, .. endpoint, 0, 0, 0, 0];
                int blocks = (width + 3) / 4 * ((height + 3) / 4);
                chunks.AddRange(Repeat(block, blocks));
                continue;
            }
            RgbaImage image = Solid(width, height, red, green, blue, levelAlpha);
            chunks.AddRange(TextureProcessing.Encode(image, new TextureEncodingOptions
            {
                Format = format, Quality = 1, GenerateMipmaps = false,
            }).Data.ToArray());
        }
        return new Texture(name, 16, 8, format, 3, chunks.ToArray());
    }

    [AvaloniaTheory]
    [InlineData(GameTarget.Legacy, TextureFormat.BC2Unorm)]
    [InlineData(GameTarget.Legacy, TextureFormat.BC3Unorm)]
    [InlineData(GameTarget.Enhanced, TextureFormat.BC2Unorm)]
    [InlineData(GameTarget.Enhanced, TextureFormat.BC3Unorm)]
    public Task OptimizationPreservesAuthoredMipsAndUndo(GameTarget target, TextureFormat format) => Headless.Run(() =>
    {
        Texture source = MakeTexture(format);
        source.Usage = TextureUsage.Diffuse;
        source.UsageFlags = 3;
        TextureViewerViewModel viewer = Loaded(new TextureSet([source], target));
        Assert.True(viewer.OptimizeSelectedCompression());
        Settle(viewer);
        Texture result = viewer.Dictionary!.Textures[0];
        Assert.Equal(TextureFormat.BC1Unorm, result.Format);
        Assert.Equal((16, 8, 3), (result.Width, result.Height, result.MipCount));
        Assert.Equal((source.Usage, source.UsageFlags), (result.Usage, result.UsageFlags));
        Assert.Equal(source.Data.Length / 2, result.Data.Length);
        (int, int, byte[])[] expected = [(16, 8, [255, 0, 0]), (8, 4, [0, 255, 0]), (4, 2, [0, 0, 255])];
        for (int mip = 0; mip < 3; mip++)
        {
            RgbaImage decoded = TextureProcessing.Decode(result, mip);
            Assert.Equal((expected[mip].Item1, expected[mip].Item2), (decoded.Width, decoded.Height));
            Assert.Equal(expected[mip].Item3, decoded.Pixels[..3].ToArray()); // Stored lower mips, not a regenerated chain.
        }
        Assert.Equal(target, viewer.Dictionary.Target);
        Assert.True(viewer.Modified);
        Assert.True(viewer.Undo());
        Settle(viewer);
        Assert.Same(source, viewer.Dictionary.Textures[0]);
        Assert.False(viewer.Modified);
    });

    [AvaloniaTheory]
    [InlineData(TextureFormat.BC1Unorm, 255, null, TextureFormat.BC1Unorm)]
    [InlineData(TextureFormat.BC3Unorm, 100, null, TextureFormat.BC3Unorm)]
    [InlineData(TextureFormat.BC3Unorm, 255, 100, TextureFormat.BC3Unorm)]
    [InlineData(TextureFormat.BC2Unorm, 255, 100, TextureFormat.BC3Unorm)]
    public Task ChecksAlphaInAllMipsAndLeavesCorrectFormatsUntouched(TextureFormat format, int alpha, int? lastAlpha, TextureFormat expected) => Headless.Run(() =>
    {
        Texture source = MakeTexture(format, alpha: (byte)alpha, lastAlpha: (byte?)lastAlpha);
        TextureViewerViewModel viewer = Loaded(new TextureSet([source], GameTarget.Legacy));
        Assert.True(viewer.OptimizeSelectedCompression());
        Settle(viewer);
        Texture result = viewer.Dictionary!.Textures[0];
        Assert.Equal(expected, result.Format);
        if (format == expected)
        {
            Assert.Same(source, result);
            Assert.False(viewer.Modified);
            Assert.False(viewer.CanUndo);
            Assert.Equal("No changes were needed", viewer.Status);
        }
        if (lastAlpha is not null)
            Assert.True(TextureProcessing.Decode(result, 2).Pixels[3] < 255);
    });

    [AvaloniaFact]
    public Task BatchSkipsNonconventionalFormatsAndPreservesSelection() => Headless.Run(() =>
    {
        TextureFormat[] formats = [TextureFormat.BC3Unorm, TextureFormat.B8G8R8A8Unorm, TextureFormat.BC5Unorm,
            TextureFormat.BC4Unorm, TextureFormat.BC7Unorm];
        Texture[] sources = [.. formats.Select((format, index) => MakeTexture(format, $"tex_{index}"))];
        TextureViewerViewModel viewer = Loaded(new TextureSet(sources, GameTarget.Enhanced));
        viewer.SelectAllTextures();
        Assert.True(viewer.CanOptimizeSelection);
        Assert.True(viewer.OptimizeSelectedCompression());
        Settle(viewer);
        Assert.Equal(TextureFormat.BC1Unorm, viewer.Dictionary!.Textures[0].Format);
        for (int index = 1; index < 5; index++)
            Assert.Same(sources[index], viewer.Dictionary.Textures[index]);
        Assert.Equal([0, 1, 2, 3, 4], viewer.SelectedRows);
        Assert.Single(viewer.UndoStack);
        Assert.Equal("Optimize compression", viewer.UndoLabel);
        Assert.True(viewer.Undo());
        Settle(viewer);
        viewer.SelectTexture(1);
        Assert.False(viewer.CanOptimizeCompression);
        Assert.False(viewer.OptimizeSelectedCompression());
        Assert.False(viewer.Modified);
        Assert.False(viewer.CanUndo);
    });

    [AvaloniaFact]
    public Task OptimizationErrorKeepsEntireBatchUnchanged() => Headless.Run(() =>
    {
        Texture[] sources = [MakeTexture(TextureFormat.BC3Unorm, "good"), MakeTexture(TextureFormat.BC3Unorm, "bad")];
        TextureViewerViewModel viewer = Loaded(new TextureSet(sources, GameTarget.Legacy));
        viewer.SelectAllTextures();
        viewer.OptimizeCompression = texture => texture.Name == "bad"
            ? throw new ArgumentException("simulated optimization failure") : TextureCompression.Optimize(texture);
        Assert.True(viewer.OptimizeSelectedCompression());
        Assert.True(viewer.OperationBusy);
        Assert.False(viewer.CanOptimizeSelection);
        Settle(viewer);
        Assert.Equal(sources, viewer.Dictionary!.Textures);
        Assert.False(viewer.Modified);
        Assert.False(viewer.CanUndo);
        Assert.False(viewer.OperationBusy);
        Assert.Contains("simulated optimization failure", viewer.Status);
        Assert.StartsWith("Optimizing compression for failed: bad: ", viewer.Status);
    });
}
