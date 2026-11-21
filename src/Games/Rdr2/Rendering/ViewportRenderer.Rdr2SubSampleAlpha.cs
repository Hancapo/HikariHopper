using WebGpuSharp;

namespace HikariHopper.Rendering;

// Red Dead Redemption 2's sub-sampled alpha (Rdr2/cutout.wgsl): its cut-outs drop half the pixels they partly cover and
// mark the others with their coverage; once the solid surfaces are drawn, before what's blended over them, the HDR pass
// ends while a copy of the target is blended back into it by its depth (Rdr2/subsample_alpha.wgsl), as the game's
// PS_SubSampleAlphaSinglePass does, and goes on. The copy goes to the water's copy of the scene, which is made after.
internal sealed partial class ViewportRenderer
{
    private BindGroupLayout? subSampleAlphaLayout;
    private RenderPipeline? subSampleAlphaPipeline;
    private BindGroup? subSampleAlphaGroup;
    private TextureView? subSampleAlphaSource, subSampleAlphaDepth;

    private RenderPipeline SubSampleAlphaPipeline()
    {
        if (subSampleAlphaPipeline is not null)
            return subSampleAlphaPipeline;
        subSampleAlphaLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
        {
            Entries =
            [
                new BindGroupLayoutEntry
                {
                    Binding = 0,
                    Visibility = ShaderStage.Fragment,
                    Texture = new TextureBindingLayout { SampleType = TextureSampleType.UnfilterableFloat, ViewDimension = TextureViewDimension.D2 },
                },
                new BindGroupLayoutEntry
                {
                    Binding = 1,
                    Visibility = ShaderStage.Fragment,
                    Texture = new TextureBindingLayout { SampleType = TextureSampleType.Depth, ViewDimension = TextureViewDimension.D2 },
                },
            ],
        });
        BindGroupLayout layout = subSampleAlphaLayout;
        subSampleAlphaPipeline = gpu.Pipeline("rdr2-subsample-alpha", () =>
        {
            ShaderModule module = device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor { Code = Read("Rdr2/subsample_alpha.wgsl") });
            return device.CreateRenderPipelineSync(new RenderPipelineDescriptor
            {
                Layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [layout] }),
                Vertex = new VertexState { Module = module, EntryPoint = "vs_sub_sample_alpha" },
                Fragment = new FragmentState
                {
                    Module = module,
                    EntryPoint = "fs_sub_sample_alpha",
                    Targets = [new ColorTargetState { Format = HdrFormat, WriteMask = ColorWriteMask.All }],
                },
            });
        });
        return subSampleAlphaPipeline;
    }

    /// <summary>
    /// Ends the HDR pass, blends the sub-sampled cut-outs into what's behind them, and begins it again where it was, its
    /// scene and light bound.
    /// </summary>
    private RenderPassEncoder CombineSubSampledAlpha(RenderPassEncoder pass, CommandEncoder encoder)
    {
        pass.End();
        RenderPipeline pipeline = SubSampleAlphaPipeline();
        if (!ReferenceEquals(subSampleAlphaSource, waterBackgroundView) || !ReferenceEquals(subSampleAlphaDepth, hdrDepthView))
        {
            subSampleAlphaGroup = device.CreateBindGroup(new BindGroupDescriptor
            {
                Layout = subSampleAlphaLayout!,
                Entries =
                [
                    new BindGroupEntry { Binding = 0, TextureView = waterBackgroundView! },
                    new BindGroupEntry { Binding = 1, TextureView = hdrDepthView! },
                ],
            });
            subSampleAlphaSource = waterBackgroundView;
            subSampleAlphaDepth = hdrDepthView;
        }
        encoder.CopyTextureToTexture(new TexelCopyTextureInfo { Texture = hdr! }, new TexelCopyTextureInfo { Texture = waterBackground! },
            new Extent3D((uint)hdrWidth, (uint)hdrHeight, 1));
        RenderPassEncoder combine = encoder.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachments = [new RenderPassColorAttachment { View = hdrView!, LoadOp = LoadOp.Load, StoreOp = StoreOp.Store }],
        });
        combine.SetPipeline(pipeline);
        combine.SetBindGroup(0, subSampleAlphaGroup!);
        combine.Draw(3);
        combine.End();
        RenderPassEncoder resumed = encoder.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachments = HdrAttachments(clear: false),
            DepthStencilAttachment = new RenderPassDepthStencilAttachment
            {
                View = hdrDepthView!,
                DepthLoadOp = LoadOp.Load,
                DepthStoreOp = StoreOp.Store,
            },
        });
        resumed.SetBindGroup(0, sceneGroup);
        resumed.SetBindGroup(2, lightGroup!);
        return resumed;
    }

    // Whether the model's parts in view hold an RDR2 cut-out drawn sub-sampled.
    private bool SurfacesSubSampled()
    {
        for (int i = 0; i < surfaceParts.Length; i++)
            if (surfaceParts[i].Kind == PartKind.Rdr2 && surfaceParts[i].Mode == AlphaMode.Mask && partsShown[i] && surfaceParts[i].Part.Shown
                && partRdr2[i]?.SubSampled == true)
                return true;
        return false;
    }

    // Whether the world's assets in view hold an RDR2 cut-out drawn sub-sampled.
    private bool WorldSubSampled()
    {
        foreach (WorldGpuAsset asset in listedAssets)
            if (asset.SubSampled)
                return true;
        return false;
    }
}
