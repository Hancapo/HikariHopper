using System.Diagnostics;
using System.Numerics;
using WebGpuSharp;
using Buffer = WebGpuSharp.Buffer;
using GameTexture = FiveFury.Textures.Texture;

namespace HikariHopper.Rendering;

// GTA V's lens flare over its world (gta_lens_flare.wgsl), drawn as the game draws it each frame, after the world and
// before the post-process: the sun's visibility counted over a disc where the sky shows, and the flare's elements
// added to the HDR frame, faded by the visibility counted a frame or two before. Only with a world's sky
// (ShowWorldSky) and once its flare is given (ShowWorldLensFlare).
internal sealed unsafe partial class ViewportRenderer
{
    // The counts in flight: the game's buffered occlusion queries (as many as it keeps on PC, for one GPU).
    private const int FlareReadbacks = 4;

    // A count's readback: the pixels counted and the mean luminance the exposure came from, the frame it was drawn.
    private const uint FlareReadbackBytes = 8;

    // The disc's fan: 22 triangles.
    private const uint SunVisibilityCorners = 22 * 3;

    // A ring's strip, drawn as a list.
    private const uint FlareRingCorners = (GtaLensFlareState.RingVertices - 2) * 3;

    private const int FlareSpriteBytes = GtaLensFlareState.SpriteVectors * 16;

    /// <summary>The flare's GPU side: its state, its textures, a group for each texture, and its sprites.</summary>
    private sealed record WorldLensFlareResources(GtaLensFlare Flare, GtaLensFlareState State, WebGpuSharp.Texture[] Textures, BindGroup[] Groups,
        Buffer Sprites, Vector4[] Values, (int First, int Count)[] Layout);

    private enum FlareReadback { Free, Copied, Mapping }

    private WorldLensFlareResources? worldLensFlare;
    private Buffer? flareVisibilityUniforms, flarePixels;
    private readonly Buffer?[] flareReadbacks = new Buffer?[FlareReadbacks];
    private readonly FlareReadback[] flareReadbackStates = new FlareReadback[FlareReadbacks];
    private readonly long[] flareReadbackFrames = new long[FlareReadbacks];
    // The pixels the whole sun disc would have covered on each count's frame, which the count is measured against.
    private readonly float[] flareReadbackExpected = new float[FlareReadbacks];
    // Each readback's map, as its callback says it ended (nought while under way, one mapped, two failed): WebGpuSharp
    // lets go of the buffer before it calls back, while Dawn may say it's mapped sooner, so this is what's polled.
    private readonly int[] flareReadbackEnds = new int[FlareReadbacks];
    private Action<MapAsyncStatus, ReadOnlySpan<byte>>[]? flareReadbackCallbacks;
    // The frame; the first frame whose counts the flare shown goes by (those drawn before it was shown are the last
    // flare's, another world's); and the frame of the count it goes by.
    private long flareFrame, flareFirstFrame, flareResultFrame = -1;
    private uint flarePixelsCounted;
    private float flarePixelsExpected;
    private float flareMeanLuminance;
    private BindGroupLayout? flareVisibilityLayout, flareSpriteLayout;
    private BindGroup? flareVisibilityGroup;
    private TextureView? flareVisibilityDepth;
    private RenderPipeline? flareVisibility, flareQuads, flareRings;
    private Sampler? flareWrap;
    private readonly Stopwatch flareClock = Stopwatch.StartNew();
    private double flareLastSeconds;
    private int flareSpritesDrawn;

    /// <summary>Whether a world's lens flare is shown over it.</summary>
    public bool HasWorldLensFlare => worldLensFlare is not null;

    /// <summary>How much of the sun the flare last found showing, nought to one; for tests.</summary>
    internal float WorldSunVisibility => worldLensFlare?.State.SunVisibility ?? 0;

    /// <summary>The flare's sprites the last frame drew; for tests.</summary>
    internal int WorldLensFlareSprites => flareSpritesDrawn;

    private void CreateLensFlarePipelines()
    {
        if (flareVisibility is not null)
            return;
        CreateSkyLayouts();
        flareVisibilityLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
        {
            Entries =
            [
                new BindGroupLayoutEntry
                {
                    Binding = 0,
                    Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
                    Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = GtaSky.Bytes },
                },
                new BindGroupLayoutEntry
                {
                    Binding = 8,
                    Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
                    Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = 16 },
                },
                new BindGroupLayoutEntry
                {
                    Binding = 9,
                    Visibility = ShaderStage.Fragment,
                    Texture = new TextureBindingLayout { SampleType = TextureSampleType.Depth, ViewDimension = TextureViewDimension.D2 },
                },
                new BindGroupLayoutEntry
                {
                    Binding = 10,
                    Visibility = ShaderStage.Fragment,
                    Buffer = new BufferBindingLayout { Type = BufferBindingType.Storage, MinBindingSize = 16 },
                },
            ],
        });
        flareSpriteLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
        {
            Entries =
            [
                new BindGroupLayoutEntry
                {
                    Binding = 11,
                    Visibility = ShaderStage.Vertex,
                    Buffer = new BufferBindingLayout { Type = BufferBindingType.ReadOnlyStorage, MinBindingSize = FlareSpriteBytes },
                },
                new BindGroupLayoutEntry { Binding = 12, Visibility = ShaderStage.Fragment, Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering } },
                new BindGroupLayoutEntry
                {
                    Binding = 13,
                    Visibility = ShaderStage.Fragment,
                    Texture = new TextureBindingLayout { SampleType = TextureSampleType.Float, ViewDimension = TextureViewDimension.D2 },
                },
            ],
        });
        Lazy<ShaderModule> shader = new(() => device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor
        {
            Code = Read("scene.wgsl") + "\n" + Read("sky_system.wgsl") + "\n" + Read("gta_fog.wgsl") + "\n" + Read("gta_sky.wgsl") + "\n" + Read("gta_lens_flare.wgsl"),
        }));
        RenderPipelineDescriptor Describe(BindGroupLayout layout, string vertex, string fragment, BlendState? blend, ColorWriteMask mask) => new()
        {
            Layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [sceneLayout, layout] }),
            Vertex = new VertexState { Module = shader.Value, EntryPoint = vertex },
            Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleList, CullMode = CullMode.None },
            Fragment = new FragmentState
            {
                Module = shader.Value,
                EntryPoint = fragment,
                Targets = new[] { new ColorTargetState { Format = HdrFormat, Blend = blend, WriteMask = mask } },
            },
        };
        // The flare is added to the frame (BS_Add), its colour only: the frame's alpha is the tone map's.
        var add = new BlendState
        {
            Color = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.One, DstFactor = BlendFactor.One },
            Alpha = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.Zero, DstFactor = BlendFactor.One },
        };
        const ColorWriteMask Colour = ColorWriteMask.Red | ColorWriteMask.Green | ColorWriteMask.Blue;
        flareVisibility = gpu.Pipeline("gta-flare/visibility",
            () => device.CreateRenderPipelineSync(Describe(flareVisibilityLayout, "vs_gta_sun_visibility", "fs_gta_sun_visibility", null, ColorWriteMask.None)));
        flareQuads = gpu.Pipeline("gta-flare/quads", () => device.CreateRenderPipelineSync(Describe(flareSpriteLayout, "vs_gta_flare_quad", "fs_gta_flare", add, Colour)));
        flareRings = gpu.Pipeline("gta-flare/rings", () => device.CreateRenderPipelineSync(Describe(flareSpriteLayout, "vs_gta_flare_ring", "fs_gta_flare", add, Colour)));
    }

    /// <summary>Shows this lens flare over the world, in place of any before; null shows none.</summary>
    public void ShowWorldLensFlare(GtaLensFlare? flare)
    {
        ClearWorldLensFlare();
        // Counts still being read back are the flare's before.
        flareFirstFrame = flareFrame + 1;
        flareResultFrame = -1;
        if (flare is null)
            return;
        CreateLensFlarePipelines();
        // im's DiffuseSampler: repeating, linear between texels and mips.
        flareWrap ??= device.CreateSampler(new SamplerDescriptor
        {
            AddressModeU = AddressMode.Repeat,
            AddressModeV = AddressMode.Repeat,
            AddressModeW = AddressMode.Repeat,
            MagFilter = FilterMode.Linear,
            MinFilter = FilterMode.Linear,
            MipmapFilter = MipmapFilterMode.Linear,
            LodMinClamp = 0,
            LodMaxClamp = 32,
            MaxAnisotropy = 1,
        });
        flareVisibilityUniforms ??= device.CreateBuffer(new BufferDescriptor { Size = 16, Usage = BufferUsage.Uniform | BufferUsage.CopyDst });
        flarePixels ??= device.CreateBuffer(new BufferDescriptor { Size = 16, Usage = BufferUsage.Storage | BufferUsage.CopySrc | BufferUsage.CopyDst });
        for (int i = 0; i < FlareReadbacks; i++)
            flareReadbacks[i] ??= device.CreateBuffer(new BufferDescriptor { Size = FlareReadbackBytes, Usage = BufferUsage.CopyDst | BufferUsage.MapRead });
        var state = new GtaLensFlareState(flare);
        (int First, int Count)[] layout = state.Layout();
        int sprites = Math.Max(1, layout.Sum(room => room.Count));
        Buffer buffer = device.CreateBuffer(new BufferDescriptor { Size = (ulong)(sprites * FlareSpriteBytes), Usage = BufferUsage.Storage | BufferUsage.CopyDst });
        var owned = new List<WebGpuSharp.Texture>();
        var groups = new BindGroup[GtaLensFlare.TextureCount];
        for (int i = 0; i < groups.Length; i++)
        {
            TextureView view = clear!;
            if (flare.Textures[i] is GameTexture source && CreateTexture(source) is var (texture, block))
            {
                for (int level = 0; level < source.MipCount; level++)
                    WriteLevel(source, texture, block, level);
                owned.Add(texture);
                view = texture.CreateView();
            }
            groups[i] = device.CreateBindGroup(new BindGroupDescriptor
            {
                Layout = flareSpriteLayout!,
                Entries =
                [
                    new BindGroupEntry { Binding = 11, Buffer = buffer, Size = buffer.GetSize() },
                    new BindGroupEntry { Binding = 12, Sampler = flareWrap },
                    new BindGroupEntry { Binding = 13, TextureView = view },
                ],
            });
        }
        worldLensFlare = new WorldLensFlareResources(flare, state, [.. owned], groups, buffer, new Vector4[sprites * GtaLensFlareState.SpriteVectors], layout);
    }

    // The flare's GPU memory goes now, not whenever the handles are collected.
    private void ClearWorldLensFlare()
    {
        if (worldLensFlare is not { } flare)
            return;
        worldLensFlare = null;
        flare.Sprites.Destroy();
        foreach (WebGpuSharp.Texture texture in flare.Textures)
            texture.Destroy();
    }

    // The counts read back since the last frame: the newest one drawn since the flare was shown is the one it goes by.
    private void ReadLensFlareCounts()
    {
        for (int i = 0; i < FlareReadbacks; i++)
        {
            if (flareReadbackStates[i] != FlareReadback.Mapping || flareReadbacks[i] is not { } readback)
                continue;
            int end = Volatile.Read(ref flareReadbackEnds[i]);
            if (end == 0)
                continue;
            if (end == 1)
            {
                (uint pixels, float mean) = readback.GetConstMappedRange<(uint, float), uint>(0, 2,
                    span => (span[0], BitConverter.UInt32BitsToSingle(span[1])));
                readback.Unmap();
                long frame = flareReadbackFrames[i];
                if (frame >= flareFirstFrame && frame > flareResultFrame)
                    (flareResultFrame, flarePixelsCounted, flarePixelsExpected, flareMeanLuminance) = (frame, pixels, flareReadbackExpected[i], mean);
            }
            flareReadbackStates[i] = FlareReadback.Free;
        }
    }

    /// <summary>Asks for this frame's count once the frame is submitted; it's read on a later frame, never waited for.</summary>
    private void MapLensFlareCount()
    {
        flareReadbackCallbacks ??= [.. Enumerable.Range(0, FlareReadbacks).Select(slot => (Action<MapAsyncStatus, ReadOnlySpan<byte>>)((status, _) =>
            Volatile.Write(ref flareReadbackEnds[slot], status == MapAsyncStatus.Success ? 1 : 2)))];
        for (int i = 0; i < FlareReadbacks; i++)
            if (flareReadbackStates[i] == FlareReadback.Copied)
            {
                flareReadbackStates[i] = FlareReadback.Mapping;
                Volatile.Write(ref flareReadbackEnds[i], 0);
                flareReadbacks[i]!.Map(MapMode.Read, flareReadbackCallbacks[i]);
            }
    }

    // The exposure in stops the tone map took from this mean luminance (light.wgsl's exposure_stops).
    private float ExposureStops(float mean) => Math.Clamp(
        toneMap.ExposureScale * MathF.Pow(MathF.Max(mean, 1e-6f), toneMap.ExposurePower) + toneMap.ExposureOffset + cycle.Exposure + Exposure,
        cycle.ExposureMin, cycle.ExposureMax);

    /// <summary>
    /// After the world's HDR pass, with its sky: the sun's visibility counted for a later frame, then the flare as the
    /// last count says, over the frame.
    /// </summary>
    private void DrawWorldLensFlare(CommandEncoder encoder)
    {
        flareSpritesDrawn = 0;
        if (worldLensFlare is not { } flare || skyUniforms is null)
            return;
        flareFrame++;
        ReadLensFlareCounts();
        if (flareVisibilityGroup is null || flareVisibilityDepth != hdrDepthView)
        {
            flareVisibilityDepth = hdrDepthView;
            flareVisibilityGroup = device.CreateBindGroup(new BindGroupDescriptor
            {
                Layout = flareVisibilityLayout!,
                Entries =
                [
                    new BindGroupEntry { Binding = 0, Buffer = skyUniforms, Size = GtaSky.Bytes },
                    new BindGroupEntry { Binding = 8, Buffer = flareVisibilityUniforms!, Size = 16 },
                    new BindGroupEntry { Binding = 9, TextureView = hdrDepthView! },
                    new BindGroupEntry { Binding = 10, Buffer = flarePixels!, Size = 16 },
                ],
            });
        }

        // The sun as the game registers it: where the sky put it on the dome, its disc's radius, as the sky has them.
        GtaLensFlareState state = flare.State;
        TimecycleSample tc = Timecycle.Sample(Hours);
        Vector3 sun = skyValues[12].AsVector3();
        float radius = state.VisibilityRadius(skyValues[6].W);
        float aspect = (float)width / Math.Max(height, 1);
        var view = new GtaLensFlareView(sceneViewProjection, origin, Camera.Eye, Camera.Forward, OrbitCamera.TanHalf * aspect, aspect, hdrWidth);
        // The count goes by the disc as it was on its own frame (the target's size may have changed since).
        float expected = GtaLensFlareState.ExpectedPixels(sun, radius, view);
        if (flareResultFrame >= 0)
            state.UpdateVisibility(flarePixelsCounted, flarePixelsExpected, tc.Get("Lensflare_visibility", 1));
        queue.WriteBuffer(flareVisibilityUniforms!, 0, (ReadOnlySpan<Vector4>)[new Vector4(radius, flare.Flare.Settings.SunFogFactor, 0, 0)]);
        encoder.ClearBuffer(flarePixels!, 0, 16);

        RenderPassEncoder pass = encoder.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachments = [new RenderPassColorAttachment { View = hdrView!, LoadOp = LoadOp.Load, StoreOp = StoreOp.Store }],
        });
        pass.SetBindGroup(0, sceneGroup);
        pass.SetBindGroup(1, flareVisibilityGroup);
        pass.SetPipeline(flareVisibility!);
        pass.Draw(SunVisibilityCorners);

        double seconds = flareClock.Elapsed.TotalSeconds;
        float step = (float)(seconds - flareLastSeconds);
        flareLastSeconds = seconds;
        Vector2? onScreen = GtaLensFlareState.ScreenPosition(sun, view);
        if (state.Advance(onScreen, seconds * 1000) && onScreen is { } place)
        {
            float exposure = flareResultFrame >= 0 ? GtaLensFlareState.LinearExposure(ExposureStops(flareMeanLuminance), cycle.ExposureMin, cycle.ExposureMax) : 0;
            int[] written = state.Build(flare.Values, place, tc.Colour("sky_sun_col"), aspect, exposure, step, seconds * 1000);
            queue.WriteBuffer(flare.Sprites, 0, (ReadOnlySpan<Vector4>)flare.Values);
            for (int texture = 0; texture < written.Length; texture++)
            {
                if (written[texture] == 0)
                    continue;
                bool rings = texture == GtaLensFlare.Chromatic;
                pass.SetBindGroup(1, flare.Groups[texture]);
                pass.SetPipeline(rings ? flareRings! : flareQuads!);
                pass.Draw(rings ? FlareRingCorners : 6, (uint)written[texture], 0, (uint)flare.Layout[texture].First);
                flareSpritesDrawn += written[texture];
            }
        }
        pass.End();

        // The count and the mean luminance the exposure came from, to be read back once they're done.
        int slot = Array.IndexOf(flareReadbackStates, FlareReadback.Free);
        if (slot >= 0)
        {
            encoder.CopyBufferToBuffer(flarePixels!, 0, flareReadbacks[slot]!, 0, 4);
            encoder.CopyBufferToBuffer(measured!, 0, flareReadbacks[slot]!, 4, 4);
            flareReadbackStates[slot] = FlareReadback.Copied;
            flareReadbackFrames[slot] = flareFrame;
            flareReadbackExpected[slot] = expected;
        }
    }
}
