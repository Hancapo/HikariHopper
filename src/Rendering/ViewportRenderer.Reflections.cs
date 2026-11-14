using System.Numerics;
using WebGpuSharp;
using Buffer = WebGpuSharp.Buffer;
using Texture = WebGpuSharp.Texture;

namespace HikariHopper.Rendering;

// The reflection maps drawables reflect (reflection_map.wgsl), dual paraboloids with a full mip chain each, as GTA's
// is. The sky map is GTA's sky at the viewer's time of day as the game draws it into its reflection map, with the
// stand-in ground below the horizon, redrawn when the time changes. The scene map is what drawables reflect: like the
// game's, drawn each frame from the camera, the sky map with the backdrop's floor below its horizon and the drawables
// over it, which reflect only the sky map themselves.
internal sealed unsafe partial class ViewportRenderer
{
    private uint reflectionWidth = Core.GraphicsSettings.DefaultReflectionWidth;
    private BindGroupLayout? reflectionSourceLayout;

    /// <summary>
    /// The reflection maps' width (Core.GraphicsSettings.ReflectionWidths), each hemisphere half of it, the height half
    /// of it; a change makes the maps again at that size and draws the sky into them again.
    /// </summary>
    public uint ReflectionWidth
    {
        get => reflectionWidth;
        set
        {
            if (value == reflectionWidth || value < 4 || !BitOperations.IsPow2(value))
                return;
            reflectionWidth = value;
            if (skyMap is null)
                return;
            CreateReflectionMaps();
            CreateLightGroups();
            DrawReflections(Hours);
        }
    }

    internal uint ReflectionHeight => reflectionWidth / 2;

    /// <summary>The sky map, with every mip; for tests.</summary>
    internal Texture? SkyReflectionMap => skyMap?.Texture;
    private const int SkyBytes = 10 * 16, ReflectionViewBytes = 32, FloorBytes = 5 * 16;

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
    private Buffer? floorBuffer;
    private BindGroup? floorGroup;
    private RenderPipeline? reflectionPipeline, downsamplePipeline, floorPipeline;

    /// <summary>The reflection map's mip count, which the surfaces' mip curve is in terms of, as the game's is.</summary>
    internal uint ReflectionMips => MipsFor(reflectionWidth);

    /// <summary>A reflection map's mip count at this width: down to a texel.</summary>
    internal static uint MipsFor(uint width) => (uint)BitOperations.Log2(width) + 1;

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
            LodMaxClamp = 32,
            MaxAnisotropy = 1,
        })!;
        BindGroupLayout sourceLayout = reflectionSourceLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
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
        CreateReflectionMaps();

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

        // The backdrop's floor, blended over the scene map's sky below the horizon, before its drawables.
        BindGroupLayout floorLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
        {
            Entries =
            [
                new BindGroupLayoutEntry
                {
                    Binding = 0,
                    Visibility = ShaderStage.Fragment,
                    Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = FloorBytes },
                },
            ],
        });
        floorBuffer = device.CreateBuffer(new BufferDescriptor { Size = FloorBytes, Usage = BufferUsage.Uniform | BufferUsage.CopyDst });
        floorGroup = device.CreateBindGroup(new BindGroupDescriptor
        {
            Layout = floorLayout,
            Entries = [new BindGroupEntry { Binding = 0, Buffer = floorBuffer, Size = FloorBytes }],
        });
        floorPipeline = gpu.Pipeline($"reflection-map/fs_reflection_floor/{viewFormat}", () => device.CreateRenderPipelineSync(new RenderPipelineDescriptor
        {
            Layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [skyLayout, floorLayout] }),
            Vertex = new VertexState { Module = shader.Value, EntryPoint = "vs_reflection_map" },
            Fragment = new FragmentState
            {
                Module = shader.Value,
                EntryPoint = "fs_reflection_floor",
                Targets = new[]
                {
                    new ColorTargetState
                    {
                        Format = HdrFormat,
                        Blend = new BlendState
                        {
                            Color = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.SrcAlpha, DstFactor = BlendFactor.OneMinusSrcAlpha },
                            Alpha = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.Zero, DstFactor = BlendFactor.One },
                        },
                        WriteMask = ColorWriteMask.All,
                    },
                },
            },
            DepthStencil = new DepthStencilState { Format = DepthFormat, DepthWriteEnabled = OptionalBool.False, DepthCompare = CompareFunction.Always },
        }));
    }

    // The sky and scene maps at ReflectionWidth, each with every mip and what draws each mip from the one above, and the
    // depth the scene map's drawables are drawn with.
    private void CreateReflectionMaps()
    {
        skyMap?.Texture.Destroy();
        sceneMap?.Texture.Destroy();
        reflectionDepth?.Destroy();
        uint mips = ReflectionMips;
        ReflectionMap Map(TextureUsage usage)
        {
            Texture texture = device.CreateTexture(new TextureDescriptor
            {
                Usage = TextureUsage.RenderAttachment | TextureUsage.TextureBinding | usage,
                Dimension = TextureDimension.D2,
                Size = new Extent3D(reflectionWidth, ReflectionHeight, 1),
                Format = HdrFormat,
                MipLevelCount = mips,
                SampleCount = 1,
            });
            TextureView[] levels = [.. Enumerable.Range(0, (int)mips).Select(level => texture.CreateView(new TextureViewDescriptor
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
            BindGroup[] sources = [.. levels.Take(levels.Length - 1).Select(view => device.CreateBindGroup(new BindGroupDescriptor
            {
                Layout = reflectionSourceLayout!,
                Entries =
                [
                    new BindGroupEntry { Binding = 0, TextureView = view },
                    new BindGroupEntry { Binding = 1, Sampler = reflectionSampler! },
                ],
            }))];
            return new(texture, texture.CreateView(), levels, sources);
        }
        skyMap = Map(TextureUsage.CopySrc);
        sceneMap = Map(TextureUsage.CopyDst);
        reflectionDepth = device.CreateTexture(new TextureDescriptor
        {
            Usage = TextureUsage.RenderAttachment,
            Dimension = TextureDimension.D2,
            Size = new Extent3D(reflectionWidth, ReflectionHeight, 1),
            Format = DepthFormat,
            MipLevelCount = 1,
            SampleCount = 1,
        });
        reflectionDepthView = reflectionDepth.CreateView();
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
    /// backdrop's floor below its horizon and the drawables drawn over it into each hemisphere's half (lit with the sky
    /// map), then its mips.
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
        // The floor as the backdrop draws it, lit as a grey card is by the timecycle's light.
        Vector4[] palette = ViewportPalette.Linear();
        float light = Vector3.Dot(Timecycle.GroundRadiance(cycle, key.Direction), new Vector3(.2126f, .7152f, .0722f)) / .18f;
        Span<Vector4> floor = [new Vector4(Camera.Eye, Ground), new Vector4(GridSpacing(Camera.Radius), light, 0, 0), palette[2], palette[3], palette[4]];
        queue.WriteBuffer(floorBuffer!, 0, (ReadOnlySpan<Vector4>)floor);
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
        pass.SetPipeline(floorPipeline!);
        pass.SetBindGroup(0, skyGroup!);
        pass.SetBindGroup(1, floorGroup!);
        pass.Draw(3);
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
