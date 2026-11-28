using System.Numerics;
using System.Runtime.InteropServices;
using WebGpuSharp;
using Buffer = WebGpuSharp.Buffer;

namespace HikariHopper.Rendering;

// GTA V's distant LOD lights over its world (Gta5/distant_lights.wgsl), drawn as the game draws them: after the world's
// blended surfaces and before the coronas, added into the HDR target, tested against the scene's depth. Only with a
// GTA V sky shown, once the world has given them with its coronas (ShowWorldCoronas); only the lights of the maps the
// game has streamed in where the camera is, and only between the hours they fade in and out over.
internal sealed unsafe partial class ViewportRenderer
{
    private Buffer? distantLights, distantUniforms;
    private WebGpuSharp.Texture[] distantTextures = [];
    private TextureView? distantView, waterNoiseView;
    private BindGroupLayout? distantLayout;
    private BindGroup? distantGroup;
    private Sampler? distantSampler, distantNoiseSampler;
    private RenderPipeline? distantPipeline;
    private GtaDistantLights? distantData;
    private readonly List<(int First, int Count)> distantRuns = [];
    private readonly Vector4[] distantValues = new Vector4[DistantFrameBytes / 16];

    // Gta5/distant_lights.wgsl's DistantFrame.
    private const int DistantFrameBytes = 6 * 16;

    /// <summary>The distant LOD lights drawn in the last frame; for tests.</summary>
    internal int WorldDistantLightsDrawn { get; private set; }

    // The distant lights the coronas' data brings, up once: their lights and the two textures they're drawn with.
    private void ShowDistantLights(GtaCoronaData data)
    {
        ClearDistantLights();
        if (data.Distant is not { Lights.Length: > 0 } lights)
            return;
        var owned = new List<WebGpuSharp.Texture>();
        TextureView Own(FiveFury.Textures.Texture? source, TextureView fallback)
        {
            if (source is null || CreateTexture(source) is not var (texture, block))
                return fallback;
            for (int level = 0; level < source.MipCount; level++)
                WriteLevel(source, texture, block, level);
            owned.Add(texture);
            return texture.CreateView();
        }
        // Without its texture a sprite is nothing; without the noise, steady.
        distantView = Own(data.DistantLight, clear!);
        waterNoiseView = Own(data.WaterNoise, white!);
        distantTextures = [.. owned];
        distantLights = device.CreateBuffer(new BufferDescriptor
        {
            Size = (ulong)lights.Lights.Length * GtaDistantLights.Light.Bytes,
            Usage = BufferUsage.Storage | BufferUsage.CopyDst,
        });
        queue.WriteBuffer(distantLights, 0, MemoryMarshal.AsBytes(lights.Lights.AsSpan()));
        distantData = lights;
    }

    // Their GPU memory goes now, not whenever the handles are collected.
    private void ClearDistantLights()
    {
        foreach (WebGpuSharp.Texture texture in distantTextures)
            texture.Destroy();
        distantTextures = [];
        distantView = waterNoiseView = null;
        distantLights?.Destroy();
        distantLights = null;
        distantGroup = null;
        distantData = null;
        WorldDistantLightsDrawn = 0;
    }

    /// <summary>
    /// The distant lights' pipeline. With <paramref name="prepare"/>, it only starts compiling in the background, for when
    /// a world's distant lights are drawn.
    /// </summary>
    internal void CreateDistantLightsPipeline(bool prepare = false)
    {
        if (distantPipeline is not null)
            return;
        BindGroupLayoutEntry Map(uint binding) => new()
        {
            Binding = binding,
            Visibility = ShaderStage.Fragment,
            Texture = new TextureBindingLayout { SampleType = TextureSampleType.Float, ViewDimension = TextureViewDimension.D2 },
        };
        BindGroupLayoutEntry Filtering(uint binding) => new()
        {
            Binding = binding,
            Visibility = ShaderStage.Fragment,
            Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering },
        };
        distantLayout ??= device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
        {
            Entries =
            [
                new BindGroupLayoutEntry
                {
                    Binding = 0,
                    Visibility = ShaderStage.Vertex,
                    Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = GtaSky.Bytes },
                },
                new BindGroupLayoutEntry
                {
                    Binding = 1,
                    Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
                    Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = DistantFrameBytes },
                },
                new BindGroupLayoutEntry
                {
                    Binding = 2,
                    Visibility = ShaderStage.Vertex,
                    Buffer = new BufferBindingLayout { Type = BufferBindingType.ReadOnlyStorage, MinBindingSize = GtaDistantLights.Light.Bytes },
                },
                Map(3),
                Map(4),
                Filtering(5),
                Filtering(6),
            ],
        });
        Lazy<ShaderModule> shader = new(() => device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor
        {
            Code = Read("Scene/scene.wgsl") + "\n" + Read("Gta5/sky_system.wgsl") + "\n" + Read("Gta5/fog.wgsl") + "\n" + Read("Gta5/sky.wgsl") + "\n"
                + Read("Gta5/distant_lights.wgsl"),
        }));
        PipelineLayout layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [sceneLayout, distantLayout] });
        // Added (ONE + ONE), over whatever's there; the HDR target's alpha, the sky's cover, is left as it is.
        var add = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.One, DstFactor = BlendFactor.One };
        distantPipeline = Compile("gta-distant-lights", () => new RenderPipelineDescriptor
        {
            Layout = layout,
            Vertex = new VertexState { Module = shader.Value, EntryPoint = "vs_distant_light" },
            Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleStrip, CullMode = CullMode.None },
            Fragment = new FragmentState
            {
                Module = shader.Value,
                EntryPoint = "fs_distant_light",
                Targets = new[] { new ColorTargetState { Format = HdrFormat, Blend = new BlendState { Color = add, Alpha = add }, WriteMask = ColorWriteMask.Red | ColorWriteMask.Green | ColorWriteMask.Blue } },
            },
            // Tested against the scene (less-equal, the depth reversed), writing none.
            DepthStencil = new DepthStencilState { Format = DepthFormat, DepthWriteEnabled = OptionalBool.False, DepthCompare = CompareFunction.GreaterEqual },
        }, prepare);
        if (prepare)
        {
            distantPipeline = null;
            return;
        }
        distantUniforms ??= device.CreateBuffer(new BufferDescriptor { Size = DistantFrameBytes, Usage = BufferUsage.Uniform | BufferUsage.CopyDst });
        Sampler Linear(AddressMode address) => device.CreateSampler(new SamplerDescriptor
        {
            AddressModeU = address,
            AddressModeV = address,
            AddressModeW = address,
            MagFilter = FilterMode.Linear,
            MinFilter = FilterMode.Linear,
            MipmapFilter = MipmapFilterMode.Linear,
            LodMinClamp = 0,
            LodMaxClamp = 32,
            MaxAnisotropy = 1,
        });
        // The sprite's texture clamped, the noise wrapping.
        distantSampler ??= Linear(AddressMode.ClampToEdge);
        distantNoiseSampler ??= Linear(AddressMode.Repeat);
    }

    /// <summary>
    /// The distant lights' frame (Gta5/distant_lights.wgsl's DistantFrame), from visualsettings.dat's distantlights.*
    /// settings and the timecycle, for a camera at <paramref name="eye"/> at <paramref name="hours"/>.
    /// </summary>
    internal static void WriteDistantFrame(Span<Vector4> values, GtaCoronaVisuals visuals, float hours, Vector3 eye, Vector3 right, Vector3 up,
        Vector3 forward, float tanHalfHorizontal, double milliseconds, TimecycleSample sample)
    {
        // Doubled once the camera is sizeDist above sizeDistStart (by the file's values, anywhere above the sea).
        float grow = 1 + Math.Clamp((eye.Z - visuals.DistantLightSizeDistStart) / MathF.Max(visuals.DistantLightSizeDist, 1e-6f), 0, 1);
        float diameter = visuals.DistantLightSize * 2 * grow;
        float upK = tanHalfHorizontal / (1024 / MathF.Max(visuals.DistantLightSizeMin, 1e-6f) / MathF.Max(visuals.DistantLightSizeUpscale, 1e-6f) * diameter);
        float hdr = visuals.DistantLightHdrIntensity * sample.GetOr("sprite_brightness", 1);
        float twinkle = visuals.DistantLightTwinkleAmount * sample.GetOr("sprite_distant_light_twinkle", 1);
        float time = (float)(milliseconds * visuals.DistantLightTwinkleSpeed / 256 % 4096);
        float others = MathF.Round(GtaDistantLights.HourFade(hours, visuals.DistantLightHours.X, visuals.DistantLightHours.Y) * 255);
        float street = MathF.Round(GtaDistantLights.HourFade(hours, visuals.DistantStreetLightHours.X, visuals.DistantStreetLightHours.Y) * 255);
        values[0] = new Vector4(right, diameter);
        values[1] = new Vector4(up, upK);
        values[2] = new Vector4(forward, hdr);
        values[3] = new Vector4(eye, time);
        values[4] = new Vector4(0, visuals.LodLightCoronaRange.Y, visuals.LodLightCoronaRange.Z, visuals.DistantLightFlicker);
        values[5] = new Vector4(others, street, twinkle, 0);
    }

    /// <summary>The distant lights, added over the world; nothing without a GTA V sky, by day, or with none streamed in.</summary>
    private void DrawWorldDistantLights(CommandEncoder encoder)
    {
        WorldDistantLightsDrawn = 0;
        if (!worldShown || worldSky is null || coronaData is not { } data || distantData is not { } lights || distantLights is null
            || hdrDepthView is null)
            return;
        GtaCoronaVisuals visuals = data.Visuals;
        if (GtaDistantLights.HourFade(Hours, visuals.DistantLightHours.X, visuals.DistantLightHours.Y) <= 0
            && GtaDistantLights.HourFade(Hours, visuals.DistantStreetLightHours.X, visuals.DistantStreetLightHours.Y) <= 0)
            return;
        Vector3 eye = Camera.Eye;
        lights.Streamed(eye, distantRuns);
        if (distantRuns.Count == 0)
            return;
        CreateDistantLightsPipeline();
        distantGroup ??= device.CreateBindGroup(new BindGroupDescriptor
        {
            Layout = distantLayout!,
            Entries =
            [
                new BindGroupEntry { Binding = 0, Buffer = skyUniforms!, Size = GtaSky.Bytes },
                new BindGroupEntry { Binding = 1, Buffer = distantUniforms!, Size = DistantFrameBytes },
                new BindGroupEntry { Binding = 2, Buffer = distantLights, Size = (ulong)lights.Lights.Length * GtaDistantLights.Light.Bytes },
                new BindGroupEntry { Binding = 3, TextureView = distantView! },
                new BindGroupEntry { Binding = 4, TextureView = waterNoiseView! },
                new BindGroupEntry { Binding = 5, Sampler = distantSampler! },
                new BindGroupEntry { Binding = 6, Sampler = distantNoiseSampler! },
            ],
        });
        WriteDistantFrame(distantValues, visuals, Hours, eye, Camera.Right, Camera.Up, Camera.Forward, OrbitCamera.TanHalf * Aspect,
            waterClock.Elapsed.TotalMilliseconds, timecycle);
        queue.WriteBuffer(distantUniforms!, 0, (ReadOnlySpan<Vector4>)distantValues);
        RenderPassEncoder pass = encoder.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachments = [new RenderPassColorAttachment { View = hdrView!, LoadOp = LoadOp.Load, StoreOp = StoreOp.Store }],
            DepthStencilAttachment = new RenderPassDepthStencilAttachment { View = hdrDepthView, DepthLoadOp = LoadOp.Load, DepthStoreOp = StoreOp.Store },
        });
        pass.SetPipeline(distantPipeline!);
        pass.SetBindGroup(0, sceneGroup);
        pass.SetBindGroup(1, distantGroup);
        foreach (var (first, count) in distantRuns)
        {
            pass.Draw(4, (uint)count, 0, (uint)first);
            WorldDistantLightsDrawn += count;
        }
        pass.End();
    }
}
