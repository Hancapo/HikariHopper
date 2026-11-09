using System.Numerics;
using WebGpuSharp;
using Buffer = WebGpuSharp.Buffer;
using Texture = WebGpuSharp.Texture;

namespace HikariHopper.Rendering;

// The reflection maps drawables reflect (reflection_map.wgsl), dual paraboloids with a full mip chain each, as GTA's
// is. The sky map is GTA's sky at the viewer's time of day as the game draws it into its reflection map, with the
// stand-in ground below the horizon, redrawn when the time changes. The scene map is what drawables reflect: like the
// game's, drawn each frame from the camera, the sky map with the drawables over it, which reflect only the sky map
// themselves.
internal sealed unsafe partial class ViewportRenderer
{
    internal const uint ReflectionWidth = 512, ReflectionHeight = 256;
    private const int SkyBytes = 10 * 16, ReflectionViewBytes = 32;

    /// <summary>A reflection map, its mips one by one, and each mip's view for drawing the next from.</summary>
    private sealed record ReflectionMap(Texture Texture, TextureView View, TextureView[] Mips, BindGroup[] Sources);

    private ReflectionMap? skyMap, sceneMap;
    private Texture? reflectionDepth;
    private TextureView? reflectionDepthView;
    private Sampler? reflectionSampler;
    private Buffer? skyBuffer;
    private BindGroup? skyGroup;
    private BindGroupLayout? reflectionViewLayout;
    private readonly Buffer?[] reflectionViewBuffers = new Buffer?[2];
    private readonly BindGroup?[] reflectionViewGroups = new BindGroup?[2];
    private RenderPipeline? reflectionPipeline, downsamplePipeline;

    /// <summary>The reflection map's mip count, which the surfaces' mip curve is in terms of, as the game's is.</summary>
    internal static uint ReflectionMips => (uint)BitOperations.Log2(ReflectionWidth) + 1;

    private void CreateReflections()
    {
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
        ReflectionMap Map(TextureUsage usage)
        {
            Texture texture = device.CreateTexture(new TextureDescriptor
            {
                Usage = TextureUsage.RenderAttachment | TextureUsage.TextureBinding | usage,
                Dimension = TextureDimension.D2,
                Size = new Extent3D(ReflectionWidth, ReflectionHeight, 1),
                Format = HdrFormat,
                MipLevelCount = ReflectionMips,
                SampleCount = 1,
            });
            TextureView[] mips = [.. Enumerable.Range(0, (int)ReflectionMips).Select(level => texture.CreateView(new TextureViewDescriptor
            {
                Format = HdrFormat,
                Dimension = TextureViewDimension.D2,
                BaseMipLevel = (uint)level,
                MipLevelCount = 1,
                BaseArrayLayer = 0,
                ArrayLayerCount = 1,
                Aspect = TextureAspect.All,
            }))];
            // Mip n is drawn from mip n - 1.
            BindGroup[] sources = [.. mips.Take(mips.Length - 1).Select(view => device.CreateBindGroup(new BindGroupDescriptor
            {
                Layout = sourceLayout,
                Entries =
                [
                    new BindGroupEntry { Binding = 0, TextureView = view },
                    new BindGroupEntry { Binding = 1, Sampler = reflectionSampler },
                ],
            }))];
            return new(texture, texture.CreateView(), mips, sources);
        }
        skyMap = Map(TextureUsage.CopySrc);
        sceneMap = Map(TextureUsage.CopyDst);
        reflectionDepth = device.CreateTexture(new TextureDescriptor
        {
            Usage = TextureUsage.RenderAttachment,
            Dimension = TextureDimension.D2,
            Size = new Extent3D(ReflectionWidth, ReflectionHeight, 1),
            Format = DepthFormat,
            MipLevelCount = 1,
            SampleCount = 1,
        });
        reflectionDepthView = reflectionDepth.CreateView();

        // The hemisphere being drawn into the scene map, upper then lower.
        reflectionViewLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
        {
            Entries =
            [
                new BindGroupLayoutEntry
                {
                    Binding = 0,
                    Visibility = ShaderStage.Vertex,
                    Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = ReflectionViewBytes },
                },
            ],
        });
        for (int side = 0; side < 2; side++)
        {
            reflectionViewBuffers[side] = device.CreateBuffer(new BufferDescriptor { Size = ReflectionViewBytes, Usage = BufferUsage.Uniform | BufferUsage.CopyDst });
            reflectionViewGroups[side] = device.CreateBindGroup(new BindGroupDescriptor
            {
                Layout = reflectionViewLayout,
                Entries = [new BindGroupEntry { Binding = 0, Buffer = reflectionViewBuffers[side]!, Size = ReflectionViewBytes }],
            });
        }

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

        Lazy<ShaderModule> shader = new(() => device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor { Code = Read("sky_system.wgsl") + "\n" + Read("reflection_map.wgsl") }));
        RenderPipeline Pipeline(string entry, BindGroupLayout[] layouts) => gpu.Pipeline($"reflection-map/{entry}/{viewFormat}", () => device.CreateRenderPipelineSync(new RenderPipelineDescriptor
        {
            Layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = layouts }),
            Vertex = new VertexState { Module = shader.Value, EntryPoint = "vs_reflection_map" },
            Fragment = new FragmentState
            {
                Module = shader.Value,
                EntryPoint = entry,
                Targets = new[] { new ColorTargetState { Format = HdrFormat, WriteMask = ColorWriteMask.All } },
            },
        }));
        reflectionPipeline = Pipeline("fs_reflection_map", [skyLayout]);
        downsamplePipeline = Pipeline("fs_reflection_downsample", [skyLayout, sourceLayout]);
    }

    /// <summary>
    /// Draws the sky map for this time of day: GTA's sky as its reflection pass draws it, the viewer's ground below the
    /// horizon, then each mip from the one above. Also sets the sky the frame is measured against.
    /// </summary>
    private void DrawReflections(float hours)
    {
        WriteSky(frameSkyBuffer!, SkySystem.At(hours), hours);
        WriteSky(skyBuffer!, SkySystem.At(hours, reflection: true), hours);
        CommandEncoder encoder = device.CreateCommandEncoder();
        RenderPassEncoder pass = encoder.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachments = [new RenderPassColorAttachment { View = skyMap!.Mips[0], LoadOp = LoadOp.Clear, StoreOp = StoreOp.Store, ClearValue = new Color(0, 0, 0, 1) }],
        });
        pass.SetPipeline(reflectionPipeline!);
        pass.SetBindGroup(0, skyGroup!);
        pass.Draw(3);
        pass.End();
        DrawMips(encoder, skyMap);
        queue.Submit(encoder.Finish());
        ThrowIfFailed();
    }

    /// <summary>
    /// Draws the scene map for this frame, as GTA draws its reflection map: from the camera, the sky map with the
    /// drawables drawn over it into each hemisphere's half (lit with the sky map), then its mips.
    /// </summary>
    private void DrawSceneReflections(CommandEncoder encoder)
    {
        Vector3 eye = Camera.Eye - origin;
        // Far enough for all of the drawables and the ground they stand on.
        float far = Math.Max(Camera.Radius, 1) * 200;
        for (int side = 0; side < 2; side++)
        {
            Span<Vector4> view = [new Vector4(eye, far), new Vector4(side == 0 ? 1 : -1, 0, 0, 0)];
            queue.WriteBuffer(reflectionViewBuffers[side]!, 0, (ReadOnlySpan<Vector4>)view);
        }
        var whole = new Extent3D(ReflectionWidth, ReflectionHeight, 1);
        encoder.CopyTextureToTexture(
            new TexelCopyTextureInfo { Texture = skyMap!.Texture, MipLevel = 0, Aspect = TextureAspect.All },
            new TexelCopyTextureInfo { Texture = sceneMap!.Texture, MipLevel = 0, Aspect = TextureAspect.All },
            whole);
        RenderPassEncoder pass = encoder.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachments = [new RenderPassColorAttachment { View = sceneMap.Mips[0], LoadOp = LoadOp.Load, StoreOp = StoreOp.Store }],
            DepthStencilAttachment = new RenderPassDepthStencilAttachment
            {
                View = reflectionDepthView!,
                DepthLoadOp = LoadOp.Clear,
                DepthStoreOp = StoreOp.Discard,
                DepthClearValue = 1,
            },
        });
        pass.SetBindGroup(0, sceneGroup);
        pass.SetBindGroup(2, skyLightGroup!);
        // The upper hemisphere on the right half, the lower on the left.
        uint half = ReflectionWidth / 2;
        for (int side = 0; side < 2; side++)
        {
            pass.SetBindGroup(3, reflectionViewGroups[side]!);
            pass.SetViewport(side == 0 ? half : 0, 0, half, ReflectionHeight, 0, 1);
            DrawSurfaces(pass, reflected: true);
        }
        pass.End();
        DrawMips(encoder, sceneMap);
    }

    /// <summary>Each mip of a reflection map from the one above it.</summary>
    private void DrawMips(CommandEncoder encoder, ReflectionMap map)
    {
        for (int level = 1; level < map.Mips.Length; level++)
        {
            RenderPassEncoder pass = encoder.BeginRenderPass(new RenderPassDescriptor
            {
                ColorAttachments = [new RenderPassColorAttachment { View = map.Mips[level], LoadOp = LoadOp.Clear, StoreOp = StoreOp.Store, ClearValue = new Color(0, 0, 0, 1) }],
            });
            pass.SetPipeline(downsamplePipeline!);
            pass.SetBindGroup(0, skyGroup!);
            pass.SetBindGroup(1, map.Sources[level - 1]);
            pass.Draw(3);
            pass.End();
        }
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
        values[8] = new Vector4(DayCycle.SunDirection(hours, Sky), 0);
        values[9] = new Vector4(Timecycle.GroundRadiance(cycle, key.Direction), 0);
        queue.WriteBuffer(target, 0, (ReadOnlySpan<Vector4>)values);
    }
}
