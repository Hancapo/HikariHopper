using CommonFury.Textures;
using FiveFury.Textures;
using HikariHopper.Textures;
using static HikariHopper.Tests.Textures.TextureTestKit;

namespace HikariHopper.Tests.Textures;

/// <summary>Ports of the pure checks in test_texture_viewer.py, test_power_of_two_resize.py,
/// test_texture_compression_optimization.py and test_texture_image_drop.py.</summary>
public sealed class TextureProcessingTests
{
    [Fact]
    public void DecodesChannels()
    {
        Texture texture = Red();
        Assert.Equal([255, 0, 0, 255], TextureOperations.DecodeImage(texture).Pixels[..4].ToArray());
        Assert.Equal([255, 255, 255, 255], TextureOperations.DecodeImage(texture, "r").Pixels[..4].ToArray());
        Assert.Equal([0, 0, 0, 255], TextureOperations.DecodeImage(texture, "g").Pixels[..4].ToArray());
        Assert.Equal([255, 255, 255, 255], TextureOperations.DecodeImage(texture, "a").Pixels[..4].ToArray());
        Assert.Throws<ArgumentException>(() => TextureOperations.DecodeImage(texture, "rgb"));
    }

    [Fact]
    public void DecodesRequestedMipLevel()
    {
        var texture = new Texture("mipped", 2, 2, TextureFormat.B8G8R8A8Unorm, 2,
            [.. Repeat([0, 0, 255, 255], 4), 0, 255, 0, 255]);
        RgbaImage mip = TextureOperations.DecodeImage(texture, mip: 1);
        Assert.Equal((1, 1), (mip.Width, mip.Height));
        Assert.Equal([0, 255, 0, 255], mip.Pixels.ToArray());
        Assert.Throws<ArgumentException>(() => TextureOperations.DecodeImage(texture, mip: 2));
    }

    [Fact]
    public void MipChainStopsWhenShortEdgeReachesMinimum()
    {
        Assert.Equal(10, TextureDimensions.MipCountForDimensions(2048, 2048, 4));
        Assert.Equal(8, TextureDimensions.MipCountForDimensions(1024, 512, 4));
        Assert.Equal(7, TextureDimensions.MipCountForDimensions(256, 512, 4));
        Assert.Equal((4, 8), TextureDimensions.MipDimensionsForMinimum(256, 512, 4)[^1]);
        Assert.Equal((8, 4), TextureDimensions.MipDimensionsForMinimum(512, 256, 4)[^1]);
        Assert.Equal(1, TextureDimensions.MipCountForDimensions(4, 4, 4));
        Assert.Equal((6, 12), TextureDimensions.MipDimensionsForMinimum(100, 200, 4)[^1]);
        (int, int)[] expected = [(4, 4), (2, 2), (1, 1)];
        Assert.Equal(expected, TextureDimensions.MipDimensionsForMinimum(4, 4, 1));
        Assert.Equal(expected, TextureDimensions.MipDimensionsForMinimum(4, 4, 0));
        Assert.Equal([(1, 1)], TextureDimensions.MipDimensionsForMinimum(1, 1, 1));
    }

    [Fact]
    public void StopSizeProducesTheRequestedShortEdge()
    {
        Texture texture = TextureProcessing.Encode(Solid(16, 32, 0, 0, 0, 0), new TextureEncodingOptions
        {
            Format = TextureFormat.BC1Unorm,
            MinimumMipDimension = TextureDimensions.MipStopSize(16, 32, 4),
        });
        Assert.Equal(3, texture.MipCount);
    }

    [Fact]
    public void ResizeReturnsAValidTextureWithTheSourceMetadata()
    {
        Texture source = Red("paint");
        source.Usage = TextureUsage.Diffuse;
        Texture result = TextureOperations.Resize(source, 4, 4, ImageFilter.Mitchell, 1, generateMipmaps: true);
        Assert.Equal("paint", result.Name);
        Assert.Equal((4, 4, 3), (result.Width, result.Height, result.MipCount));
        Assert.Equal(source.Usage, result.Usage);
        Assert.True(result.Validate().IsValid);
    }

    [Theory]
    [InlineData("nearest", 256, 512)]
    [InlineData("down", 256, 256)]
    [InlineData("up", 512, 512)]
    public void PowerOfTwoRoundingModes(string mode, int width, int height)
    {
        Assert.Equal((width, height), TextureDimensions.PowerOfTwoDimensions(300, 500, mode));
        Assert.Equal((1, 512), TextureDimensions.PowerOfTwoDimensions(1, 512, mode));
    }

    [Theory]
    [InlineData(0, 4, "nearest")]
    [InlineData(4, -1, "up")]
    [InlineData(65536, 4, "down")]
    [InlineData(3, 5, "unknown")]
    public void PowerOfTwoRejectsInvalidDimensionsAndModes(int width, int height, string mode) =>
        Assert.Throws<ArgumentException>(() => TextureDimensions.PowerOfTwoDimensions(width, height, mode));

    [Fact]
    public void NearestPowerOfTwoRoundsTiesUp()
    {
        Assert.Equal(128, TextureDimensions.NearestPowerOfTwo(96));
        Assert.Equal(1024, TextureDimensions.NearestPowerOfTwo(768));
        Assert.Equal(128, TextureDimensions.NearestPowerOfTwo(100));
        Assert.Equal(4, TextureDimensions.NearestPowerOfTwo(5));
        Assert.Equal(65536, TextureDimensions.NearestPowerOfTwo(65535));
        Assert.Equal(1, TextureDimensions.NearestPowerOfTwo(0));
    }

    [Fact]
    public void DxtOnePunchThroughAlphaIsNotTreatedAsOpaque()
    {
        // Endpoint ordering enables DXT1 punch-through; all sixteen indices select alpha.
        var source = new Texture("paint", 4, 4, TextureFormat.BC1Unorm, 1, Convert.FromHexString("0000ffffffffffff"));
        Texture result = TextureCompression.Optimize(source);
        Assert.Equal(TextureFormat.BC3Unorm, result.Format);
        RgbaImage decoded = TextureProcessing.Decode(result);
        for (int offset = 3; offset < decoded.Pixels.Length; offset += 4)
            Assert.Equal(0, decoded.Pixels[offset]);
    }

    [Fact]
    public void EveryExistingMipIsEncodedAtMaximumQuality()
    {
        Texture source = CompressionTests.MakeTexture(TextureFormat.BC3Unorm);
        Texture result = TextureCompression.Optimize(source);
        Assert.Equal(TextureFormat.BC1Unorm, result.Format);
        for (int mip = 0; mip < source.MipCount; mip++)
        {
            Texture expected = TextureProcessing.Encode(TextureProcessing.Decode(source, mip), new TextureEncodingOptions
            {
                Format = TextureFormat.BC1Unorm, Quality = 1, GenerateMipmaps = false,
            });
            TextureMip level = result.Mips[mip];
            Assert.Equal(expected.Data.ToArray(), result.Data.Slice(level.Offset, level.Length).ToArray());
        }
    }

    [Theory]
    [InlineData(255, TextureFormat.BC1Unorm, ".png")]
    [InlineData(254, TextureFormat.BC3Unorm, ".png")]
    [InlineData(100, TextureFormat.BC3Unorm, ".png")]
    [InlineData(0, TextureFormat.BC3Unorm, ".png")]
    [InlineData(255, TextureFormat.BC1Unorm, ".dds")]
    [InlineData(254, TextureFormat.BC3Unorm, ".dds")]
    [InlineData(0, TextureFormat.BC3Unorm, ".dds")]
    public void ImportSelectsCompressionFromPixelsNotFileChannels(byte alpha, TextureFormat expected, string extension)
    {
        using var folder = new TempFolder();
        byte[] pixels = [.. Repeat([90, 60, 30, 255], 15), 90, 60, 30, alpha];
        string path = folder.File("alpha" + extension);
        WriteImage(path, new RgbaImage(4, 4, pixels));
        Assert.Equal(expected, TextureImport.ReadTextureImage(path).Format);
    }

    [Theory]
    [InlineData(100, 30, "128x32,64x16,32x8,16x4")]
    [InlineData(30, 100, "32x128,16x64,8x32,4x16")]
    [InlineData(32, 32, "32x32,16x16,8x8,4x4")]
    [InlineData(7, 5, "8x4")]
    [InlineData(1, 2, "4x4")]
    public void ImportRoundsToPowerOfTwoAndStopsMipsAtShortEdgeFour(int width, int height, string expected)
    {
        using var folder = new TempFolder();
        foreach (string extension in new[] { ".png", ".dds" })
        {
            string path = folder.File("sized" + extension);
            WriteImage(path, Solid(width, height, 30, 60, 90, 255));
            Texture texture = TextureImport.ReadTextureImage(path);
            Assert.Equal("sized", texture.Name);
            Assert.Equal(expected, string.Join(",", texture.Mips.Select(mip => $"{mip.Width}x{mip.Height}")));
        }
    }

    [Fact]
    public void ImportEncodesAtMaximumQuality()
    {
        using var folder = new TempFolder();
        string path = folder.File("quality.png");
        RgbaImage image = Solid(13, 7, 30, 60, 90, 255);
        WriteImage(path, image);
        Texture result = TextureImport.ReadTextureImage(path);
        RgbaImage resized = image.Resize(16, 8, new ImageResizeOptions(ImageFilter.Mitchell, ImageColorSpace.Linear));
        Texture expected = TextureProcessing.Encode(resized, new TextureEncodingOptions
        {
            Format = TextureFormat.BC1Unorm, Quality = 1, MinimumMipDimension = TextureDimensions.MipStopSize(16, 8, 4),
        }, "quality");
        Assert.Equal(expected.Data.ToArray(), result.Data.ToArray());
    }

    [Fact]
    public void ImageFilterMatchesThePythonSuffixes()
    {
        Assert.Equal("Texture images", TextureImport.ImageFileFilter.Name);
        Assert.Equal(["*.dds", "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.tga", "*.webp"], TextureImport.ImageFileFilter.Patterns);
        Assert.True(TextureImport.IsImagePath(@"C:\a\B.WEBP"));
        Assert.False(TextureImport.IsImagePath(@"C:\a\b.txt"));
    }
}
