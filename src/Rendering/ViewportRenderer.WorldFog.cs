using System.Numerics;
using WebGpuSharp;
using Buffer = WebGpuSharp.Buffer;

namespace HikariHopper.Rendering;

// GTA V's fog over its world (gta_fog.wgsl), as the game fogs it: once the world's solid surfaces and decals are drawn,
// a pass over the frame blends the fog along each pixel's ray over it (gta_world_fog.wgsl, the game's depth effects
// pass), then the water and the blended surfaces are drawn, each fogging itself. The fog's constants are the sky's
// (GtaSky.Fog), given to the world's shaders through the light groups (binding 9); with no world's sky, none.
internal sealed unsafe partial class ViewportRenderer
{
    private Buffer? worldFogBuffer;
    private BindGroupLayout? worldFogLayout;
    private BindGroup? worldFogGroup;
    private TextureView? worldFogDepth;
    private RenderPipeline? worldFogPipeline;
    private readonly Vector4[] noFog = new Vector4[GtaSky.FogVectors];

    /// <summary>Whether the last frame drew the world's fog; for tests.</summary>
    internal bool WorldFogged { get; private set; }

    // The fog's constants the light groups bind: none until a world's sky is shown.
    private void CreateWorldFogBuffer()
    {
        worldFogBuffer ??= device.CreateBuffer(new BufferDescriptor { Size = GtaSky.FogBytes, Usage = BufferUsage.Uniform | BufferUsage.CopyDst });
        ClearWorldFog();
    }

    private void ClearWorldFog()
    {
        if (worldFogBuffer is null)
            return;
        GtaSky.NoFog(noFog);
        queue.WriteBuffer(worldFogBuffer, 0, (ReadOnlySpan<Vector4>)noFog);
    }

    // The frame's fog: the sky's fog constants, just written.
    private void WriteWorldFog(ReadOnlySpan<Vector4> skyValues) =>
        queue.WriteBuffer(worldFogBuffer!, 0, skyValues.Slice(GtaSky.FogStart, GtaSky.FogVectors));

    /// <summary>
    /// The fog pass's pipeline: over the frame, blended by the fog's amount into colour only, reading the depth it's
    /// attached to. With <paramref name="prepare"/>, it only starts compiling in the background.
    /// </summary>
    internal void CreateWorldFogPipeline(bool prepare = false)
    {
        if (worldFogPipeline is not null)
            return;
        worldFogLayout ??= device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
        {
            Entries =
            [
                new BindGroupLayoutEntry
                {
                    Binding = 0,
                    Visibility = ShaderStage.Fragment,
                    Texture = new TextureBindingLayout { SampleType = TextureSampleType.Depth, ViewDimension = TextureViewDimension.D2 },
                },
            ],
        });
        RenderPipeline pipeline = Compile("gta-world-fog", () =>
        {
            ShaderModule module = device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor
            {
                Code = LitShader("gta_fog.wgsl") + "\n" + Read("gta_world_fog.wgsl"),
            });
            return new RenderPipelineDescriptor
            {
                Layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [sceneLayout, worldFogLayout, lightLayout!] }),
                Vertex = new VertexState { Module = module, EntryPoint = "vs_gta_world_fog" },
                Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleList, CullMode = CullMode.None },
                Fragment = new FragmentState
                {
                    Module = module,
                    EntryPoint = "fs_gta_world_fog",
                    Targets = new[]
                    {
                        // As the game's depthFXBlendState: by the fog's amount, colour alone.
                        new ColorTargetState
                        {
                            Format = HdrFormat,
                            Blend = new BlendState
                            {
                                Color = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.SrcAlpha, DstFactor = BlendFactor.OneMinusSrcAlpha },
                                Alpha = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.Zero, DstFactor = BlendFactor.One },
                            },
                            WriteMask = ColorWriteMask.Red | ColorWriteMask.Green | ColorWriteMask.Blue,
                        },
                    },
                },
                DepthStencil = new DepthStencilState { Format = DepthFormat, DepthWriteEnabled = OptionalBool.False, DepthCompare = CompareFunction.Always },
            };
        }, prepare);
        if (!prepare)
            worldFogPipeline = pipeline;
    }

    /// <summary>The fog over the world drawn so far, first in the pass that reads the world's depth.</summary>
    private void DrawWorldFog(RenderPassEncoder pass)
    {
        CreateWorldFogPipeline();
        // Made again only when the depth is (a resize).
        if (!ReferenceEquals(worldFogDepth, hdrDepthView))
        {
            worldFogDepth = hdrDepthView;
            worldFogGroup = device.CreateBindGroup(new BindGroupDescriptor
            {
                Layout = worldFogLayout!,
                Entries = [new BindGroupEntry { Binding = 0, TextureView = hdrDepthView! }],
            });
        }
        pass.SetPipeline(worldFogPipeline!);
        pass.SetBindGroup(0, sceneGroup);
        pass.SetBindGroup(1, worldFogGroup!);
        pass.SetBindGroup(2, lightGroup!);
        pass.Draw(3);
    }
}
