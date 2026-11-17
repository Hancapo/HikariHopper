using System.Diagnostics;
using WebGpuSharp;
using Buffer = WebGpuSharp.Buffer;
using GameTexture = FiveFury.Textures.Texture;

namespace HikariHopper.Rendering;

// Water (RedFury's WebGpuRenderer.Water, copied): the scene behind it, its colour and depth, copied once the world's
// solid surfaces and decals are drawn, which the water then refracts and reflects (rdr2_water.wgsl, gta_water.wgsl,
// through light.wgsl's bindings 7 and 8); and GTA V's water's own material group.
internal sealed unsafe partial class ViewportRenderer
{
    private Texture? waterBackground, waterDepth, waterPlaceholder, waterDepthPlaceholder;
    private TextureView? waterBackgroundView, waterDepthView;
    private BindGroupLayout? gtaWaterLayout;
    private Sampler? waterSampler;
    private readonly Stopwatch waterClock = Stopwatch.StartNew();

    // The water's time: seconds since the view began, within an hour, so it stays precise.
    private double waterTime => waterClock.Elapsed.TotalSeconds % 3600;

    // The copies, the HDR target's size; and the light groups again, which bind them.
    private void CreateWaterTargets()
    {
        waterBackground?.Destroy();
        waterDepth?.Destroy();
        waterBackground = device.CreateTexture(new TextureDescriptor
        {
            // Read back by the sub-sampled alpha's tests, as the frame was before its combine.
            Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst | TextureUsage.CopySrc,
            Dimension = TextureDimension.D2,
            Size = new Extent3D((uint)hdrWidth, (uint)hdrHeight, 1),
            Format = HdrFormat,
            MipLevelCount = 1,
            SampleCount = 1,
        });
        waterDepth = device.CreateTexture(new TextureDescriptor
        {
            Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
            Dimension = TextureDimension.D2,
            Size = new Extent3D((uint)hdrWidth, (uint)hdrHeight, 1),
            Format = DepthFormat,
            MipLevelCount = 1,
            SampleCount = 1,
        });
        waterBackgroundView = waterBackground.CreateView();
        waterDepthView = waterDepth.CreateView();
        if (lightLayout is not null && shadowView is not null && sceneMap is not null && skyMap is not null)
            CreateLightGroups();
    }

    // What the light groups bind before the copies exist: a pixel of each.
    private TextureView WaterBackgroundView()
    {
        if (waterBackgroundView is { } view)
            return view;
        waterPlaceholder ??= device.CreateTexture(new TextureDescriptor
        {
            Usage = TextureUsage.TextureBinding,
            Dimension = TextureDimension.D2,
            Size = new Extent3D(1, 1, 1),
            Format = HdrFormat,
            MipLevelCount = 1,
            SampleCount = 1,
        });
        return waterPlaceholder.CreateView();
    }

    private TextureView WaterDepthView()
    {
        if (waterDepthView is { } view)
            return view;
        waterDepthPlaceholder ??= device.CreateTexture(new TextureDescriptor
        {
            Usage = TextureUsage.TextureBinding,
            Dimension = TextureDimension.D2,
            Size = new Extent3D(1, 1, 1),
            Format = DepthFormat,
            MipLevelCount = 1,
            SampleCount = 1,
        });
        return waterDepthPlaceholder.CreateView();
    }

    // The scene as it is now, behind the water.
    private void CopyWaterBackground(CommandEncoder encoder)
    {
        var size = new Extent3D((uint)hdrWidth, (uint)hdrHeight, 1);
        encoder.CopyTextureToTexture(new TexelCopyTextureInfo { Texture = hdr! }, new TexelCopyTextureInfo { Texture = waterBackground! }, size);
        encoder.CopyTextureToTexture(new TexelCopyTextureInfo { Texture = hdrDepth!, Aspect = TextureAspect.DepthOnly },
            new TexelCopyTextureInfo { Texture = waterDepth!, Aspect = TextureAspect.DepthOnly }, size);
    }

    /// <summary>
    /// GTA V's water's material group (gta_water.wgsl): its values, a repeating sampler, then its fog map, its static
    /// bump, its wave bump, its noise and its foam.
    /// </summary>
    private BindGroupLayout GtaWaterLayout()
    {
        if (gtaWaterLayout is not null)
            return gtaWaterLayout;
        static BindGroupLayoutEntry Map(uint binding) => new()
        {
            Binding = binding,
            Visibility = ShaderStage.Fragment,
            Texture = new TextureBindingLayout { SampleType = TextureSampleType.Float, ViewDimension = TextureViewDimension.D2 },
        };
        gtaWaterLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
        {
            Entries =
            [
                new BindGroupLayoutEntry
                {
                    Binding = 0,
                    Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
                    Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = GtaWater.UniformBytes },
                },
                new BindGroupLayoutEntry { Binding = 1, Visibility = ShaderStage.Fragment, Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering } },
                Map(2), Map(3), Map(4), Map(5), Map(6),
            ],
        });
        return gtaWaterLayout;
    }

    // A GTA water material's group, reading its uniforms from the buffer given.
    private BindGroup GtaWaterGroup(GtaWater water, Buffer uniforms, ulong offset = 0)
    {
        waterSampler ??= device.CreateSampler(new SamplerDescriptor
        {
            AddressModeU = AddressMode.Repeat,
            AddressModeV = AddressMode.Repeat,
            AddressModeW = AddressMode.Repeat,
            MagFilter = FilterMode.Linear,
            MinFilter = FilterMode.Linear,
            MipmapFilter = MipmapFilterMode.Linear,
            LodMinClamp = 0,
            LodMaxClamp = 32,
            MaxAnisotropy = 8,
        });
        return device.CreateBindGroup(new BindGroupDescriptor
        {
            Layout = GtaWaterLayout(),
            Entries =
            [
                new BindGroupEntry { Binding = 0, Buffer = uniforms, Offset = offset, Size = GtaWater.UniformBytes },
                new BindGroupEntry { Binding = 1, Sampler = waterSampler },
                new BindGroupEntry { Binding = 2, TextureView = View(water.Fog) ?? white! },
                new BindGroupEntry { Binding = 3, TextureView = View(water.Bump) ?? flat! },
                new BindGroupEntry { Binding = 4, TextureView = View(water.WaveBump) ?? flat! },
                new BindGroupEntry { Binding = 5, TextureView = View(water.Noise) ?? clear! },
                new BindGroupEntry { Binding = 6, TextureView = View(water.Foam) ?? clear! },
            ],
        });
    }
}
