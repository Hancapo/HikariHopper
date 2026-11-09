using WebGpuSharp;
using Texture = WebGpuSharp.Texture;

namespace HikariHopper.Rendering;

// GTA V Enhanced's bloom (bloom.wgsl), so what glows (emissive shaders, lights) spills light around it as in the game:
// after the frame is measured, its bright parts to a half-sized target (the game's Bloom Buffer Unfiltered), filtered
// into the first of a chain of levels (Bloom Buffer, half the window, to 1/256), down the chain and back up it, for the
// tone map to add the first to the drawables.
internal sealed unsafe partial class ViewportRenderer
{
    /// <summary>The most levels the bloom's chain has, from half the window to 1/256 of it: the game's eight.</summary>
    internal const int BloomLevels = 8;

    private BindGroupLayout? bloomLayout;
    private Sampler? bloomSampler;
    private RenderPipeline? bloomBrightPipeline, bloomKarisPipeline, bloomDownsamplePipeline, bloomUpsamplePipeline;
    private Texture? bloom, bloomUnfiltered;
    private TextureView? bloomUnfilteredView;
    private TextureView[] bloomViews = [];
    // What each pass reads: the HDR target for the bright pass, its result for the first level, then each level for
    // the pass after it.
    private BindGroup? bloomBrightGroup, bloomUnfilteredGroup;
    private BindGroup[] bloomGroups = [];

    private void CreateBloom()
    {
        bloomSampler = device.CreateSampler(new SamplerDescriptor
        {
            AddressModeU = AddressMode.ClampToEdge,
            AddressModeV = AddressMode.ClampToEdge,
            AddressModeW = AddressMode.ClampToEdge,
            MagFilter = FilterMode.Linear,
            MinFilter = FilterMode.Linear,
            MipmapFilter = MipmapFilterMode.Nearest,
            LodMinClamp = 0,
            LodMaxClamp = 1,
            MaxAnisotropy = 1,
        })!;
        bloomLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
        {
            Entries =
            [
                new BindGroupLayoutEntry
                {
                    Binding = 0,
                    Visibility = ShaderStage.Fragment,
                    Texture = new TextureBindingLayout { SampleType = TextureSampleType.Float, ViewDimension = TextureViewDimension.D2 },
                },
                new BindGroupLayoutEntry { Binding = 1, Visibility = ShaderStage.Fragment, Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering } },
                new BindGroupLayoutEntry
                {
                    Binding = 2,
                    Visibility = ShaderStage.Fragment,
                    Buffer = new BufferBindingLayout { Type = BufferBindingType.ReadOnlyStorage, MinBindingSize = MeasuredBytes },
                },
            ],
        });
        Lazy<ShaderModule> shader = new(() => device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor { Code = LitShader("bloom.wgsl") }));
        PipelineLayout layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [sceneLayout, bloomLayout, lightLayout!] });
        // The upsample blends halfway (its alpha, UpsampleWeight) into the level it's drawn to; the others replace it.
        BlendState upsampleBlend = new()
        {
            Color = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.SrcAlpha, DstFactor = BlendFactor.OneMinusSrcAlpha },
            Alpha = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.Zero, DstFactor = BlendFactor.One },
        };
        RenderPipeline Pipeline(string entry, BlendState? blend) => gpu.Pipeline($"bloom/{entry}/{viewFormat}", () => device.CreateRenderPipelineSync(new RenderPipelineDescriptor
        {
            Layout = layout,
            Vertex = new VertexState { Module = shader.Value, EntryPoint = "vs_bloom" },
            Fragment = new FragmentState
            {
                Module = shader.Value,
                EntryPoint = entry,
                Targets = new[]
                {
                    blend is { } state
                        ? new ColorTargetState { Format = HdrFormat, Blend = state, WriteMask = ColorWriteMask.All }
                        : new ColorTargetState { Format = HdrFormat, WriteMask = ColorWriteMask.All },
                },
            },
        }));
        bloomBrightPipeline = Pipeline("fs_bloom_bright", null);
        bloomKarisPipeline = Pipeline("fs_bloom_downsample_karis", null);
        bloomDownsamplePipeline = Pipeline("fs_bloom_downsample", null);
        bloomUpsamplePipeline = Pipeline("fs_bloom_upsample", upsampleBlend);
    }

    /// <summary>How many levels the bloom's chain has for a window this size: the game's eight, fewer for a small one.</summary>
    internal static int BloomLevelsFor(int width, int height)
    {
        int smallest = Math.Max(1, Math.Min(width, height) / 2);
        return Math.Min(BloomLevels, (int)Math.Log2(smallest) + 1);
    }

    /// <summary>The bloom's chain for the window's size, and what each of its passes reads.</summary>
    private void CreateBloomChain()
    {
        bloom?.Destroy();
        bloomUnfiltered?.Destroy();
        int levels = BloomLevelsFor(width, height);
        Texture Half(int mips) => device.CreateTexture(new TextureDescriptor
        {
            Usage = TextureUsage.RenderAttachment | TextureUsage.TextureBinding,
            Dimension = TextureDimension.D2,
            Size = new Extent3D((uint)Math.Max(1, width / 2), (uint)Math.Max(1, height / 2), 1),
            Format = HdrFormat,
            MipLevelCount = (uint)mips,
            SampleCount = 1,
        });
        bloomUnfiltered = Half(1);
        bloomUnfilteredView = bloomUnfiltered.CreateView();
        bloom = Half(levels);
        bloomViews = new TextureView[levels];
        for (int level = 0; level < levels; level++)
            bloomViews[level] = bloom.CreateView(new TextureViewDescriptor
            {
                Format = HdrFormat,
                Dimension = TextureViewDimension.D2,
                BaseMipLevel = (uint)level,
                MipLevelCount = 1,
                ArrayLayerCount = 1,
                Aspect = TextureAspect.All,
            });
        BindGroup Reading(TextureView view) => device.CreateBindGroup(new BindGroupDescriptor
        {
            Layout = bloomLayout!,
            Entries =
            [
                new BindGroupEntry { Binding = 0, TextureView = view },
                new BindGroupEntry { Binding = 1, Sampler = bloomSampler! },
                new BindGroupEntry { Binding = 2, Buffer = measured!, Size = MeasuredBytes },
            ],
        });
        bloomBrightGroup = Reading(hdrView!);
        bloomUnfilteredGroup = Reading(bloomUnfilteredView);
        bloomGroups = [.. bloomViews.Select(Reading)];
    }

    /// <summary>The bloom of the frame just drawn and measured, for the tone map that follows: its first level.</summary>
    private void DrawBloom(CommandEncoder encoder)
    {
        void Pass(RenderPipeline pipeline, BindGroup source, TextureView target, bool blend)
        {
            RenderPassEncoder pass = encoder.BeginRenderPass(new RenderPassDescriptor
            {
                ColorAttachments =
                [
                    new RenderPassColorAttachment
                    {
                        View = target,
                        LoadOp = blend ? LoadOp.Load : LoadOp.Clear,
                        StoreOp = StoreOp.Store,
                        ClearValue = new Color(0, 0, 0, 0),
                    },
                ],
            });
            pass.SetPipeline(pipeline);
            pass.SetBindGroup(0, sceneGroup);
            pass.SetBindGroup(1, source);
            pass.SetBindGroup(2, lightGroup!);
            pass.Draw(3);
            pass.End();
        }

        // The bright pass; filtered into the first level, the same size, weighted against fireflies; down the chain;
        // back up it.
        Pass(bloomBrightPipeline!, bloomBrightGroup!, bloomUnfilteredView!, false);
        Pass(bloomKarisPipeline!, bloomUnfilteredGroup!, bloomViews[0], false);
        for (int level = 1; level < bloomViews.Length; level++)
            Pass(bloomDownsamplePipeline!, bloomGroups[level - 1], bloomViews[level], false);
        for (int level = bloomViews.Length - 2; level >= 0; level--)
            Pass(bloomUpsamplePipeline!, bloomGroups[level + 1], bloomViews[level], true);
    }
}
