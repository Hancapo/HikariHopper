using System.Text.RegularExpressions;
using HikariHopper.Rendering;

namespace HikariHopper.Tests;

/// <summary>
/// Red Dead Redemption 2's cut-outs drawn with the game's sub-sampled alpha (Rdr2/cutout.wgsl), put together by its
/// single-pass blend (Rdr2/subsample_alpha.wgsl).
/// </summary>
public sealed partial class Rdr2CutoutTests
{
    // A tree from the installed game: its leaves are cut out with the trees' programs.
    private static (string Name, byte[] Data) Tree() =>
        Rdr2ModelTests.Find(".ydr", (name, data) => name.StartsWith("p_tree_", StringComparison.OrdinalIgnoreCase)
            && RedFury.Ydr.Ydr.Read(data).Drawable.Materials.Any(m => m.ShaderName.StartsWith("trees", StringComparison.OrdinalIgnoreCase) && m.RenderBucket == 3),
            20_000_000, ["hd_0.rpf"])
        ?? throw new InvalidOperationException("hd_0.rpf holds no tree with cut-out leaves");

    [GeneratedRegex(@"const (RDR2_\w+): u32 = (\d+)u;")]
    private static partial Regex Flag();

    [Fact]
    public void The_shaders_read_the_flags_the_material_sets()
    {
        Dictionary<string, uint> flags = Flag().Matches(ViewportRenderer.Read("Rdr2/cutout.wgsl"))
            .ToDictionary(m => m.Groups[1].Value, m => uint.Parse(m.Groups[2].Value));
        Assert.Equal(Rdr2Material.SubSampledFlag, flags["RDR2_SUB_SAMPLED"]);
        Assert.Equal(Rdr2Material.TreeCutoutFlag, flags["RDR2_TREE_CUTOUT"]);
        // Clear of the flags the material already packs into the same word (bits 0 to 7).
        Assert.Equal(0u, (Rdr2Material.SubSampledFlag | Rdr2Material.TreeCutoutFlag) & 0xFF);
    }

    [Fact]
    public void A_trees_cut_out_leaves_are_sub_sampled_with_the_trees_ramp_and_its_solid_bark_is_not()
    {
        (string name, byte[] data) = Tree();
        DrawableScene scene = ModelContent.Read(name, data).Surfaces!;
        uint Flags(SurfaceMaterial material) =>
            System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(material.Rdr2!.Uniforms.AsSpan())[19];
        var leaves = scene.Materials.Where(m => m.Rdr2 is { AlphaMode: 1 } && m.Shader.StartsWith("trees", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.NotEmpty(leaves);
        Assert.All(leaves, leaf =>
        {
            Assert.True(leaf.Rdr2!.SubSampled);
            Assert.Equal(Rdr2Material.SubSampledFlag | Rdr2Material.TreeCutoutFlag,
                Flags(leaf) & (Rdr2Material.SubSampledFlag | Rdr2Material.TreeCutoutFlag));
        });
        Assert.All(scene.Materials.Where(m => m.Rdr2 is { AlphaMode: not 1 }), solid => Assert.False(solid.Rdr2!.SubSampled));
    }

    [Fact]
    public void A_tree_is_put_together_in_the_viewer_without_a_GPU_error()
    {
        (string name, byte[] data) = Tree();
        DrawableScene scene = ModelContent.Read(name, data).Surfaces!;
        GpuDevice gpu = TestGpu.Device();
        const int Size = 96;
        lock (gpu.Gate)
        {
            var renderer = new ViewportRenderer(gpu);
            renderer.Resize(Size, Size);
            if (!renderer.Rdr2Supported)
            {
                renderer.Dispose();
                Assert.Skip("This GPU binds fewer textures than RDR2's material reads");
            }
            renderer.Show(scene);
            renderer.Frame(scene.Box);
            renderer.SetTime(13);
            WebGpuSharp.Texture target = gpu.Device.CreateTexture(new WebGpuSharp.TextureDescriptor
            {
                Usage = WebGpuSharp.TextureUsage.RenderAttachment | WebGpuSharp.TextureUsage.CopySrc,
                Dimension = WebGpuSharp.TextureDimension.D2,
                Size = new WebGpuSharp.Extent3D(Size, Size, 1),
                Format = renderer.PresentFormat,
                MipLevelCount = 1,
                SampleCount = 1,
            });
            gpu.Device.PushErrorScope(WebGpuSharp.ErrorFilter.Validation);
            WebGpuSharp.TextureView view = target.CreateView();
            for (int i = 0; i < 3; i++)
                renderer.RenderTo(view);
            var (type, message) = gpu.Device.PopErrorScopeSync(5_000_000_000);
            Assert.True(type == WebGpuSharp.ErrorType.NoError, message);
            target.Destroy();
            renderer.Dispose();
        }
    }
}
