using System.Numerics;
using WebGpuSharp;
using Buffer = WebGpuSharp.Buffer;

namespace HikariHopper.Rendering;

// Screen-space ambient occlusion over a world's solid surfaces, both games', as RedFury's viewer works it out
// (ambient_occlusion.wgsl): its ground-truth occlusion from the depth and the normals the surfaces leave, its denoise,
// then the ambient it takes taken back out of the HDR target, before the decals, the fog and the blended surfaces.
// The surfaces write what it needs in the same pass as their colour (HdrTargets' last two targets), so the world's
// drawn once.
internal sealed unsafe partial class ViewportRenderer
{
    // A solid surface's ambient and baked occlusion, and its normal and albedo's luminance.
    private const TextureFormat OccludableFormat = TextureFormat.RGBA16Float;
    private const TextureFormat OcclusionFormat = TextureFormat.R8Unorm;
    private const int AmbientOcclusionSettingsBytes = 16;

    /// <summary>The occlusion's radius in metres, as RedFury's world camera gives it.</summary>
    internal const float AmbientOcclusionRadius = 1.5f;

    /// <summary>The power the visibility is raised to, as RedFury's.</summary>
    internal const float AmbientOcclusionPower = 1.5f;

    /// <summary>Whether a world's solid surfaces are darkened by their screen-space ambient occlusion.</summary>
    public bool AmbientOcclusion { get; set; } = true;

    private WebGpuSharp.Texture? occludableAmbient, occludableNormal, rawOcclusion, occlusion;
    private TextureView? occludableAmbientView, occludableNormalView, rawOcclusionView, occlusionView;
    private BindGroupLayout? occlusionLayout;
    private BindGroup? gtaoGroup, denoiseGroup, occlusionApplyGroup;
    private RenderPipeline? gtaoPipeline, denoisePipeline, occlusionApplyPipeline;
    private Buffer? occlusionSettings;

    private void CreateAmbientOcclusion()
    {
        occlusionSettings = device.CreateBuffer(new BufferDescriptor { Size = AmbientOcclusionSettingsBytes, Usage = BufferUsage.Uniform | BufferUsage.CopyDst });
        BindGroupLayoutEntry Texture(uint binding, TextureSampleType type) => new()
        {
            Binding = binding,
            Visibility = ShaderStage.Fragment,
            Texture = new TextureBindingLayout { SampleType = type, ViewDimension = TextureViewDimension.D2 },
        };
        occlusionLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
        {
            Entries =
            [
                Texture(0, TextureSampleType.Depth),
                Texture(1, TextureSampleType.UnfilterableFloat),
                Texture(2, TextureSampleType.UnfilterableFloat),
                Texture(3, TextureSampleType.UnfilterableFloat),
                new BindGroupLayoutEntry
                {
                    Binding = 4,
                    Visibility = ShaderStage.Fragment,
                    Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = AmbientOcclusionSettingsBytes },
                },
            ],
        });
        Lazy<ShaderModule> shader = new(() => device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor
        {
            Code = Read("scene.wgsl") + "\n" + Read("ambient_occlusion.wgsl"),
        }));
        PipelineLayout layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [sceneLayout, occlusionLayout] });
        RenderPipeline Pass(string entry, ColorTargetState target) => gpu.Pipeline($"ambient-occlusion/{entry}", () => device.CreateRenderPipelineSync(new RenderPipelineDescriptor
        {
            Layout = layout,
            Vertex = new VertexState { Module = shader.Value, EntryPoint = "vs_ambient_occlusion" },
            Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleList, CullMode = CullMode.None },
            Fragment = new FragmentState { Module = shader.Value, EntryPoint = entry, Targets = [target] },
        }));
        gtaoPipeline = Pass("fs_gtao", new ColorTargetState { Format = OcclusionFormat, WriteMask = ColorWriteMask.All });
        denoisePipeline = Pass("fs_ao_denoise", new ColorTargetState { Format = OcclusionFormat, WriteMask = ColorWriteMask.All });
        // The HDR target less what the occlusion takes, by its coverage; its coverage kept.
        occlusionApplyPipeline = Pass("fs_ao_apply", new ColorTargetState
        {
            Format = HdrFormat,
            Blend = new BlendState
            {
                Color = new BlendComponent { Operation = BlendOperation.ReverseSubtract, SrcFactor = BlendFactor.DstAlpha, DstFactor = BlendFactor.One },
                Alpha = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.Zero, DstFactor = BlendFactor.One },
            },
            WriteMask = ColorWriteMask.Red | ColorWriteMask.Green | ColorWriteMask.Blue,
        });
    }

    /// <summary>The targets at the HDR target's size, and the groups that read them.</summary>
    private void CreateAmbientOcclusionTargets()
    {
        DestroyAmbientOcclusionTargets();
        WebGpuSharp.Texture Target(TextureFormat format) => device.CreateTexture(new TextureDescriptor
        {
            Usage = TextureUsage.RenderAttachment | TextureUsage.TextureBinding,
            Dimension = TextureDimension.D2,
            Size = new Extent3D((uint)hdrWidth, (uint)hdrHeight, 1),
            Format = format,
            MipLevelCount = 1,
            SampleCount = 1,
        })!;
        occludableAmbient = Target(OccludableFormat);
        occludableNormal = Target(OccludableFormat);
        rawOcclusion = Target(OcclusionFormat);
        occlusion = Target(OcclusionFormat);
        occludableAmbientView = occludableAmbient.CreateView();
        occludableNormalView = occludableNormal.CreateView();
        rawOcclusionView = rawOcclusion.CreateView();
        occlusionView = occlusion.CreateView();
        BindGroup Group(TextureView input) => device.CreateBindGroup(new BindGroupDescriptor
        {
            Layout = occlusionLayout!,
            Entries =
            [
                new BindGroupEntry { Binding = 0, TextureView = hdrDepthView! },
                new BindGroupEntry { Binding = 1, TextureView = occludableNormalView },
                new BindGroupEntry { Binding = 2, TextureView = occludableAmbientView },
                new BindGroupEntry { Binding = 3, TextureView = input },
                new BindGroupEntry { Binding = 4, Buffer = occlusionSettings!, Size = AmbientOcclusionSettingsBytes },
            ],
        });
        // The occlusion pass reads no input; it's given the denoised target, which it doesn't read.
        gtaoGroup = Group(occlusionView);
        denoiseGroup = Group(rawOcclusionView);
        occlusionApplyGroup = Group(occlusionView);
    }

    private void DestroyAmbientOcclusionTargets()
    {
        occludableAmbient?.Destroy();
        occludableNormal?.Destroy();
        rawOcclusion?.Destroy();
        occlusion?.Destroy();
        occludableAmbient = occludableNormal = rawOcclusion = occlusion = null;
    }

    /// <summary>
    /// The ambient occlusion over what the world's solid surfaces drew: the pass ends, the occlusion is worked out and
    /// taken out of the HDR target, and a pass goes on, as the sub-sampled cut-outs' combine does.
    /// </summary>
    private RenderPassEncoder ApplyAmbientOcclusion(RenderPassEncoder pass, CommandEncoder encoder)
    {
        pass.End();
        queue.WriteBuffer(occlusionSettings!, 0, (ReadOnlySpan<Vector4>)[new Vector4(AmbientOcclusionRadius, AmbientOcclusionPower,
            MathF.Max(32, hdrHeight * .15f), Camera.Near)]);
        void Fullscreen(TextureView target, RenderPipeline pipeline, BindGroup group, bool load)
        {
            RenderPassEncoder full = encoder.BeginRenderPass(new RenderPassDescriptor
            {
                ColorAttachments = [new RenderPassColorAttachment
                {
                    View = target,
                    LoadOp = load ? LoadOp.Load : LoadOp.Clear,
                    StoreOp = StoreOp.Store,
                    ClearValue = new Color(1, 1, 1, 1),
                }],
            });
            full.SetPipeline(pipeline);
            full.SetBindGroup(0, sceneGroup);
            full.SetBindGroup(1, group);
            full.Draw(3);
            full.End();
        }
        Fullscreen(rawOcclusionView!, gtaoPipeline!, gtaoGroup!, load: false);
        Fullscreen(occlusionView!, denoisePipeline!, denoiseGroup!, load: false);
        Fullscreen(hdrView!, occlusionApplyPipeline!, occlusionApplyGroup!, load: true);
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
}
