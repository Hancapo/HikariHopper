using System.Numerics;
using WebGpuSharp;
using Buffer = WebGpuSharp.Buffer;
using Texture = WebGpuSharp.Texture;

namespace HikariHopper.Rendering;

// The reflection map drawables reflect (reflection_map.wgsl): GTA's sky at the viewer's time of day as the game draws
// it into its reflection map, as a dual paraboloid with a full mip chain, redrawn when the time changes.
internal sealed unsafe partial class ViewportRenderer
{
    internal const uint ReflectionWidth = 512, ReflectionHeight = 256;
    private const int SkyBytes = 10 * 16;

    private Texture? reflectionMap;
    private TextureView? reflectionView;
    private TextureView[] reflectionMips = [];
    private BindGroup[] reflectionSources = [];
    private Sampler? reflectionSampler;
    private Buffer? skyBuffer;
    private BindGroup? skyGroup;
    private RenderPipeline? reflectionPipeline, downsamplePipeline;

    /// <summary>The reflection map's mip count, which the surfaces' mip curve is in terms of, as the game's is.</summary>
    internal static uint ReflectionMips => (uint)BitOperations.Log2(ReflectionWidth) + 1;

    private void CreateReflections()
    {
        reflectionMap = device.CreateTexture(new TextureDescriptor
        {
            Usage = TextureUsage.RenderAttachment | TextureUsage.TextureBinding,
            Dimension = TextureDimension.D2,
            Size = new Extent3D(ReflectionWidth, ReflectionHeight, 1),
            Format = HdrFormat,
            MipLevelCount = ReflectionMips,
            SampleCount = 1,
        });
        reflectionView = reflectionMap.CreateView();
        reflectionMips = [.. Enumerable.Range(0, (int)ReflectionMips).Select(level => reflectionMap.CreateView(new TextureViewDescriptor
        {
            Format = HdrFormat,
            Dimension = TextureViewDimension.D2,
            BaseMipLevel = (uint)level,
            MipLevelCount = 1,
            BaseArrayLayer = 0,
            ArrayLayerCount = 1,
            Aspect = TextureAspect.All,
        }))];
        reflectionSampler = device.CreateSampler(new SamplerDescriptor
        {
            AddressModeU = AddressMode.ClampToEdge,
            AddressModeV = AddressMode.ClampToEdge,
            AddressModeW = AddressMode.ClampToEdge,
            MagFilter = FilterMode.Linear,
            MinFilter = FilterMode.Linear,
            MipmapFilter = MipmapFilterMode.Linear,
            LodMinClamp = 0,
            LodMaxClamp = ReflectionMips,
            MaxAnisotropy = 1,
        })!;

        skyBuffer = device.CreateBuffer(new BufferDescriptor { Size = SkyBytes, Usage = BufferUsage.Uniform | BufferUsage.CopyDst });
        BindGroupLayout skyLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
        {
            Entries =
            [
                new BindGroupLayoutEntry
                {
                    Binding = 0,
                    Visibility = ShaderStage.Fragment,
                    Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = SkyBytes },
                },
            ],
        });
        skyGroup = device.CreateBindGroup(new BindGroupDescriptor
        {
            Layout = skyLayout,
            Entries = [new BindGroupEntry { Binding = 0, Buffer = skyBuffer, Size = SkyBytes }],
        });
        BindGroupLayout sourceLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
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
            ],
        });
        // Mip n is drawn from mip n - 1.
        reflectionSources = [.. reflectionMips.Take(reflectionMips.Length - 1).Select(view => device.CreateBindGroup(new BindGroupDescriptor
        {
            Layout = sourceLayout,
            Entries =
            [
                new BindGroupEntry { Binding = 0, TextureView = view },
                new BindGroupEntry { Binding = 1, Sampler = reflectionSampler },
            ],
        }))];

        ShaderModule shader = device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor { Code = Read("sky_system.wgsl") + "\n" + Read("reflection_map.wgsl") });
        RenderPipeline Pipeline(string entry, BindGroupLayout[] layouts) => device.CreateRenderPipelineSync(new RenderPipelineDescriptor
        {
            Layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = layouts }),
            Vertex = new VertexState { Module = shader, EntryPoint = "vs_reflection_map" },
            Fragment = new FragmentState
            {
                Module = shader,
                EntryPoint = entry,
                Targets = new[] { new ColorTargetState { Format = HdrFormat, WriteMask = ColorWriteMask.All } },
            },
        });
        reflectionPipeline = Pipeline("fs_reflection_map", [skyLayout]);
        downsamplePipeline = Pipeline("fs_reflection_downsample", [skyLayout, sourceLayout]);
    }

    /// <summary>
    /// Draws the reflection map for this time of day: GTA's sky as its reflection pass draws it, the viewer's ground
    /// below the horizon, then each mip from the one above. Also sets the sky the frame is measured against.
    /// </summary>
    private void DrawReflections(float hours)
    {
        WriteSky(frameSkyBuffer!, SkySystem.At(hours), hours);
        WriteSky(skyBuffer!, SkySystem.At(hours, reflection: true), hours);
        CommandEncoder encoder = device.CreateCommandEncoder();
        for (int level = 0; level < reflectionMips.Length; level++)
        {
            RenderPassEncoder pass = encoder.BeginRenderPass(new RenderPassDescriptor
            {
                ColorAttachments = new[]
                {
                    new RenderPassColorAttachment { View = reflectionMips[level], LoadOp = LoadOp.Clear, StoreOp = StoreOp.Store, ClearValue = new Color(0, 0, 0, 1) },
                },
            });
            pass.SetPipeline(level == 0 ? reflectionPipeline! : downsamplePipeline!);
            pass.SetBindGroup(0, skyGroup!);
            if (level > 0)
                pass.SetBindGroup(1, reflectionSources[level - 1]);
            pass.Draw(3);
            pass.End();
        }
        queue.Submit(encoder.Finish());
        ThrowIfFailed();
    }

    /// <summary>
    /// GTA's sky constants for sky_system.wgsl, with where the sun is and the ground the viewer puts below the
    /// horizon, where the game has its world: mid-grey open ground under the timecycle's light.
    /// </summary>
    private void WriteSky(Buffer target, SkySystem sky, float hours)
    {
        Span<Vector4> values = stackalloc Vector4[SkyBytes / 16];
        values[0] = new Vector4(sky.AzimuthEast, sky.AzimuthTransitionPosition);
        values[1] = new Vector4(sky.AzimuthWest, sky.HdrIntensity);
        values[2] = new Vector4(sky.AzimuthTransition, 0);
        values[3] = new Vector4(sky.Zenith, 0);
        values[4] = new Vector4(sky.ZenithTransition, 0);
        values[5] = sky.ZenithConstants;
        values[6] = new Vector4(sky.SunColourHdr, 0);
        values[7] = sky.SunConstants;
        values[8] = new Vector4(DayCycle.SunDirection(hours), 0);
        values[9] = new Vector4(Timecycle.GroundRadiance(cycle, key.Direction), 0);
        queue.WriteBuffer(target, 0, (ReadOnlySpan<Vector4>)values);
    }
}
