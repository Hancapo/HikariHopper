using WebGpuSharp;

namespace HikariHopper.Rendering;

// GTA V's sub-sampled alpha, its PC default without MSAA: its cut-outs are drawn on half of a checkerboard where their
// alpha isn't whole (Gta5/surface.wgsl's sub_sample), and leave that alpha beside the HDR target, as the game leaves it in
// its G-buffer's first target; once the frame's surfaces, coronas and lens flare are drawn, its combine
// (Gta5/subsample_alpha.wgsl) blends each such pixel with its neighbour by it, before the post-process, as the game's does.
// Every pipeline of the HDR passes has that second target; only the solid surfaces' shaders write it.
internal sealed unsafe partial class ViewportRenderer
{
    private const TextureFormat SubSampledFormat = TextureFormat.R8Unorm;
    private Texture? subSampled;
    private TextureView? subSampledView;
    private BindGroupLayout? subSampledLayout;
    private BindGroup? subSampledGroup;
    private RenderPipeline? subSampledCombine;

    /// <summary>
    /// Whether the sub-sampled cut-outs are blended with their neighbours, as the game's combine does; without, they're
    /// left on their checkerboard. For tests and measurements.
    /// </summary>
    internal bool SubSampledCombine { get; set; } = true;

    /// <summary>The HDR passes' targets: the HDR one, and the sub-sampled alpha, written only where it says.</summary>
    private static ColorTargetState[] HdrTargets(ColorTargetState colour, bool subSampled = false, bool occludable = false) =>
    [
        colour,
        new ColorTargetState { Format = SubSampledFormat, WriteMask = subSampled ? ColorWriteMask.All : ColorWriteMask.None },
        // What a world's solid surfaces leave for its ambient occlusion (ViewportRenderer.AmbientOcclusion.cs).
        new ColorTargetState { Format = OccludableFormat, WriteMask = occludable ? ColorWriteMask.All : ColorWriteMask.None },
        new ColorTargetState { Format = OccludableFormat, WriteMask = occludable ? ColorWriteMask.All : ColorWriteMask.None },
    ];

    /// <summary>A drawable's targets: the HDR passes' into the view, the HDR target alone into the reflection map.</summary>
    private static ColorTargetState[] ViewTargets(ColorTargetState colour, bool reflected, bool subSampled = false) =>
        reflected ? [colour] : HdrTargets(colour, subSampled);

    /// <summary>
    /// The HDR passes' attachments: the HDR target, the sub-sampled alpha, and what the world's ambient occlusion takes
    /// out (its ambient with nothing baked into it, its normals with no albedo), cleared as it begins.
    /// </summary>
    private RenderPassColorAttachment[] HdrAttachments(bool clear) =>
    [
        new RenderPassColorAttachment
        {
            View = hdrView!,
            LoadOp = clear ? LoadOp.Clear : LoadOp.Load,
            StoreOp = StoreOp.Store,
            ClearValue = new Color(0, 0, 0, 0),
        },
        new RenderPassColorAttachment
        {
            View = subSampledView!,
            LoadOp = clear ? LoadOp.Clear : LoadOp.Load,
            StoreOp = StoreOp.Store,
            ClearValue = new Color(0, 0, 0, 0),
        },
        new RenderPassColorAttachment
        {
            View = occludableAmbientView!,
            LoadOp = clear ? LoadOp.Clear : LoadOp.Load,
            StoreOp = StoreOp.Store,
            ClearValue = new Color(0, 0, 0, 1),
        },
        new RenderPassColorAttachment
        {
            View = occludableNormalView!,
            LoadOp = clear ? LoadOp.Clear : LoadOp.Load,
            StoreOp = StoreOp.Store,
            ClearValue = new Color(0, 0, 0, 0),
        },
    ];

    private void CreateSubSampledPipeline()
    {
        subSampledLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
        {
            Entries =
            [
                new BindGroupLayoutEntry
                {
                    Binding = 0,
                    Visibility = ShaderStage.Fragment,
                    Texture = new TextureBindingLayout { SampleType = TextureSampleType.Float, ViewDimension = TextureViewDimension.D2 },
                },
                new BindGroupLayoutEntry
                {
                    Binding = 1,
                    Visibility = ShaderStage.Fragment,
                    Texture = new TextureBindingLayout { SampleType = TextureSampleType.Float, ViewDimension = TextureViewDimension.D2 },
                },
            ],
        });
        BindGroupLayout layout = subSampledLayout;
        subSampledCombine = gpu.Pipeline("subsample-alpha", () =>
        {
            ShaderModule shader = device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor { Code = Read("Gta5/subsample_alpha.wgsl") });
            return device.CreateRenderPipelineSync(new RenderPipelineDescriptor
            {
                Layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [layout] }),
                Vertex = new VertexState { Module = shader, EntryPoint = "vs_subsample_alpha" },
                Fragment = new FragmentState
                {
                    Module = shader,
                    EntryPoint = "fs_subsample_alpha",
                    Targets = new[] { new ColorTargetState { Format = HdrFormat, WriteMask = ColorWriteMask.All } },
                },
            });
        });
    }

    // The sub-sampled alpha, the HDR target's size; the combine reads it, and the water's copy of the HDR target, which
    // the frame is copied to again before the combine, as the game's reads its copy of the back buffer.
    private void CreateSubSampledTargets()
    {
        subSampled?.Destroy();
        subSampled = device.CreateTexture(new TextureDescriptor
        {
            // Read back by its tests.
            Usage = TextureUsage.RenderAttachment | TextureUsage.TextureBinding | TextureUsage.CopySrc,
            Dimension = TextureDimension.D2,
            Size = new Extent3D((uint)hdrWidth, (uint)hdrHeight, 1),
            Format = SubSampledFormat,
            MipLevelCount = 1,
            SampleCount = 1,
        });
        subSampledView = subSampled.CreateView();
        subSampledGroup = device.CreateBindGroup(new BindGroupDescriptor
        {
            Layout = subSampledLayout!,
            Entries =
            [
                new BindGroupEntry { Binding = 0, TextureView = waterBackgroundView! },
                new BindGroupEntry { Binding = 1, TextureView = subSampledView },
            ],
        });
    }

    // Whether anything drawn may have left sub-sampled alpha: a drawable's cut-outs, or a GTA V world's.
    private bool MaySubSample(bool drawable)
    {
        if (worldShown && worldSolid[(int)WorldFamily.Gta] is not null)
            return true;
        if (!drawable)
            return false;
        foreach (var part in surfaceParts)
            if (part.Mode == AlphaMode.Mask && part.Kind == PartKind.Surface)
                return true;
        return false;
    }

    /// <summary>The game's sub-sampled alpha combine, over the HDR target, from a copy of it.</summary>
    private void CombineSubSampledAlpha(CommandEncoder encoder, bool drawable)
    {
        if (!SubSampledCombine || subSampledCombine is null || !MaySubSample(drawable))
            return;
        encoder.CopyTextureToTexture(new TexelCopyTextureInfo { Texture = hdr! }, new TexelCopyTextureInfo { Texture = waterBackground! },
            new Extent3D((uint)hdrWidth, (uint)hdrHeight, 1));
        RenderPassEncoder pass = encoder.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachments = [new RenderPassColorAttachment { View = hdrView!, LoadOp = LoadOp.Load, StoreOp = StoreOp.Store }],
        });
        pass.SetPipeline(subSampledCombine);
        pass.SetBindGroup(0, subSampledGroup!);
        pass.Draw(3);
        pass.End();
    }
}
