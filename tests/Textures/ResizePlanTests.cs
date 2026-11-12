using CommonFury.Textures;
using HikariHopper.Textures;

namespace HikariHopper.Tests.Textures;

/// <summary>What a resize asks of each texture: pixels, percent, and the rounding to powers of two.</summary>
public sealed class ResizePlanTests
{
    [Fact]
    public void Pixels_give_every_texture_the_same_size()
    {
        var plan = new ResizePlan(ResizeUnit.Pixels, 128, 32);
        Assert.Equal((128, 32), plan.Target(256, 64));
        Assert.Equal((128, 32), plan.Target(100, 60));
        Assert.True(plan.Keeps(128, 32));
        Assert.False(plan.Keeps(256, 64));
    }

    [Fact]
    public void Percent_scales_each_texture_from_its_own_size()
    {
        var half = new ResizePlan(ResizeUnit.Percent, 50, 50);
        Assert.Equal((128, 32), half.Target(256, 64));
        Assert.Equal((50, 30), half.Target(100, 60));
        // Each dimension has its own share, rounded, and never under a pixel.
        Assert.Equal((512, 16), new ResizePlan(ResizeUnit.Percent, 200, 25).Target(256, 64));
        Assert.Equal((1, 1), new ResizePlan(ResizeUnit.Percent, 1, 1).Target(8, 4));
        Assert.Equal((3, 2), new ResizePlan(ResizeUnit.Percent, 50, 50).Target(5, 3));
        // 100% is the size it has.
        Assert.True(new ResizePlan(ResizeUnit.Percent, 100, 100).Keeps(100, 60));
        // No percentage is no size.
        Assert.Equal((0, 0), new ResizePlan(ResizeUnit.Percent, 0, 0).Target(256, 64));
    }

    [Fact]
    public void Power_of_two_rounds_what_the_numbers_give()
    {
        Assert.Equal((128, 64), new ResizePlan(ResizeUnit.Percent, 100, 100, PowerOfTwo: true).Target(100, 60));
        Assert.Equal((64, 32), new ResizePlan(ResizeUnit.Percent, 50, 50, PowerOfTwo: true).Target(100, 60));
        Assert.Equal((256, 64), new ResizePlan(ResizeUnit.Pixels, 250, 63, PowerOfTwo: true).Target(1024, 1024));
        Assert.True(new ResizePlan(ResizeUnit.Percent, 100, 100, PowerOfTwo: true).Keeps(64, 64));
        // Without a size there is nothing to round.
        Assert.Equal((0, 5), new ResizePlan(ResizeUnit.Pixels, 0, 5, PowerOfTwo: true).Target(8, 8));
    }

    [Fact]
    public void A_size_and_its_percentage_turn_into_each_other()
    {
        Assert.Equal(50, ResizePlan.PercentOf(512, 1024));
        Assert.Equal(512, ResizePlan.Scaled(1024, 50));
        Assert.Equal(24, ResizePlan.PercentOf(250, 1024));
        Assert.Equal(1, ResizePlan.PercentOf(1, 4096));
        Assert.Equal(100, ResizePlan.PercentOf(64, 0));
    }

    [Fact]
    public void Memory_is_counted_from_the_size_the_format_and_the_chain()
    {
        // Four bytes a pixel, then a third more for the whole chain; a block format rounds up to its blocks.
        Assert.Equal(256 * 256 * 4, ResizePlan.EstimatedSize(256, 256, TextureFormat.B8G8R8A8Unorm, 1));
        Assert.Equal((256 * 256 + 128 * 128 + 64 * 64) * 4, ResizePlan.EstimatedSize(256, 256, TextureFormat.B8G8R8A8Unorm, 3));
        Assert.Equal(64 * 64 / 16 * 8, ResizePlan.EstimatedSize(64, 64, TextureFormat.BC1Unorm, 1));
        Assert.Equal(16 * 16, ResizePlan.EstimatedSize(62, 62, TextureFormat.BC3Unorm, 1) / 16);
        Assert.Equal(-1, ResizePlan.EstimatedSize(0, 64, TextureFormat.BC1Unorm, 1));
        Assert.True(ResizePlan.InBlocks(TextureFormat.BC3Unorm));
        Assert.False(ResizePlan.InBlocks(TextureFormat.B8G8R8A8Unorm));
    }
}
