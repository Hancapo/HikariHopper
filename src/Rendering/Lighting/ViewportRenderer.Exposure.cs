using System.Numerics;
using HikariHopper.Gpu;
using Buffer = HikariHopper.Gpu.Buffer;

namespace HikariHopper.Rendering;

// The exposure, as GTA V's post-process works it out (Post/exposure.wgsl): a world's frame measured as it is, the viewer's
// fixed view of the world at the hour shown; the target from the mean, and the exposure eased towards it over the
// frames, as PS_CalculateExposure does. Laid out over the frame's size: per 16 by 16 tile, then their sum and the
// exposure, which stays on the GPU from frame to frame.
internal sealed unsafe partial class ViewportRenderer
{
    private const int MeasuredBytes = 16;
    private const uint LuminanceTile = 16;
    private const int ExposureSettingsBytes = 128;

    // RDR2's meter (Post/exposure.wgsl's Rdr2Meter): its histogram's 256 bins, then its least, most and mean, and its pixels.
    private const int Rdr2MeterBytes = 256 * 4 + 16;

    /// <summary>How many frames' times the exposure's easing averages, as the game's post-process does.</summary>
    internal const int AdaptationFrames = 30;

    /// <summary>
    /// A camera moving this far in one frame has cut, and the exposure takes its target at once, as the game's does at a
    /// camera cut.
    /// </summary>
    internal const float ExposureCutDistance = 250;

    private Buffer? measured, partialSums, frameSkyBuffer, exposureSettings, rdr2Meter, noRdr2Cube;
    // The RDR2 sky light the exposure group reads its cube from (none without the sky: noRdr2Cube's).
    private Buffer? exposureCube;
    private readonly float[] frameTimes = new float[AdaptationFrames];
    private int frameTimeCount, frameTimeNext;
    private bool exposureReset = true, exposureWasWorld;
    private Vector3? lastExposureEye;

    /// <summary>
    /// The time since the last frame, in seconds, which the exposure eases over; none (the default, and a test's) takes
    /// the target exposure at once on that frame.
    /// </summary>
    public float FrameSeconds { get; set; }

    /// <summary>Takes the target exposure at once on the next frame, as the game does at a camera cut.</summary>
    public void ResetExposure() => exposureReset = true;
    private BindGroupLayout? exposureLayout;
    private BindGroup? exposureGroup;
    private ComputePipeline? luminancePipeline, meanPipeline, rdr2HistogramPipeline, rdr2ExposurePipeline;

    private void CreateExposure()
    {
        measured = device.CreateBuffer(new BufferDescriptor { Size = MeasuredBytes, Usage = BufferUsage.Storage | BufferUsage.CopySrc });
        exposureSettings = device.CreateBuffer(new BufferDescriptor { Size = ExposureSettingsBytes, Usage = BufferUsage.Uniform | BufferUsage.CopyDst });
        frameSkyBuffer = device.CreateBuffer(new BufferDescriptor { Size = SkyBytes, Usage = BufferUsage.Uniform | BufferUsage.CopyDst });
        rdr2Meter = device.CreateBuffer(new BufferDescriptor { Size = Rdr2MeterBytes, Usage = BufferUsage.Storage });
        noRdr2Cube = device.CreateBuffer(new BufferDescriptor { Size = Rdr2SkyLightBytes, Usage = BufferUsage.Storage });
        exposureLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
        {
            Entries =
            [
                new BindGroupLayoutEntry
                {
                    Binding = 0,
                    Visibility = ShaderStage.Compute,
                    Texture = new TextureBindingLayout { SampleType = TextureSampleType.UnfilterableFloat, ViewDimension = TextureViewDimension.D2 },
                },
                new BindGroupLayoutEntry { Binding = 1, Visibility = ShaderStage.Compute, Buffer = new BufferBindingLayout { Type = BufferBindingType.Storage } },
                new BindGroupLayoutEntry
                {
                    Binding = 2,
                    Visibility = ShaderStage.Compute,
                    Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = SkyBytes },
                },
                new BindGroupLayoutEntry
                {
                    Binding = 3,
                    Visibility = ShaderStage.Compute,
                    Buffer = new BufferBindingLayout { Type = BufferBindingType.Storage, MinBindingSize = MeasuredBytes },
                },
                new BindGroupLayoutEntry
                {
                    Binding = 4,
                    Visibility = ShaderStage.Compute,
                    Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = ExposureSettingsBytes },
                },
                new BindGroupLayoutEntry
                {
                    Binding = 5,
                    Visibility = ShaderStage.Compute,
                    Buffer = new BufferBindingLayout { Type = BufferBindingType.Storage, MinBindingSize = Rdr2MeterBytes },
                },
                new BindGroupLayoutEntry
                {
                    Binding = 6,
                    Visibility = ShaderStage.Compute,
                    Buffer = new BufferBindingLayout { Type = BufferBindingType.ReadOnlyStorage, MinBindingSize = Rdr2SkyLightBytes },
                },
            ],
        });
        Lazy<ShaderModule> shader = new(() => device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor
        {
            Code = Read("Scene/scene.wgsl") + "\n" + Read("Gta5/sky_system.wgsl") + "\n" + Read("Post/exposure.wgsl"),
        }));
        PipelineLayout layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [sceneLayout, exposureLayout] });
        luminancePipeline = gpu.Compute($"luminance/{viewFormat}", () => device.CreateComputePipelineSync(new ComputePipelineDescriptor
        {
            Layout = layout,
            Compute = new ComputeState { Module = shader.Value, EntryPoint = "cs_luminance" },
        }));
        meanPipeline = gpu.Compute($"luminance-mean/{viewFormat}", () => device.CreateComputePipelineSync(new ComputePipelineDescriptor
        {
            Layout = layout,
            Compute = new ComputeState { Module = shader.Value, EntryPoint = "cs_luminance_mean" },
        }));
        rdr2HistogramPipeline = gpu.Compute($"rdr2-histogram/{viewFormat}", () => device.CreateComputePipelineSync(new ComputePipelineDescriptor
        {
            Layout = layout,
            Compute = new ComputeState { Module = shader.Value, EntryPoint = "cs_rdr2_histogram" },
        }));
        rdr2ExposurePipeline = gpu.Compute($"rdr2-exposure/{viewFormat}", () => device.CreateComputePipelineSync(new ComputePipelineDescriptor
        {
            Layout = layout,
            Compute = new ComputeState { Module = shader.Value, EntryPoint = "cs_rdr2_exposure" },
        }));
    }

    private (uint X, uint Y) LuminanceTiles => (((uint)hdrWidth + LuminanceTile - 1) / LuminanceTile, ((uint)hdrHeight + LuminanceTile - 1) / LuminanceTile);

    /// <summary>
    /// The measurement's view of the HDR target, with room for a sum, a least and a most per tile of it, and the RDR2 sky's
    /// light for its cube.
    /// </summary>
    private void CreateExposureGroup()
    {
        partialSums?.Destroy();
        var (x, y) = LuminanceTiles;
        partialSums = device.CreateBuffer(new BufferDescriptor { Size = Math.Max(1, x * y) * 3 * sizeof(float), Usage = BufferUsage.Storage });
        exposureCube = rdr2Sky?.Light ?? noRdr2Cube!;
        exposureGroup = device.CreateBindGroup(new BindGroupDescriptor
        {
            Layout = exposureLayout!,
            Entries =
            [
                new BindGroupEntry { Binding = 0, TextureView = hdrView! },
                new BindGroupEntry { Binding = 1, Buffer = partialSums, Size = partialSums.GetSize() },
                new BindGroupEntry { Binding = 2, Buffer = frameSkyBuffer!, Size = SkyBytes },
                new BindGroupEntry { Binding = 3, Buffer = measured!, Size = MeasuredBytes },
                new BindGroupEntry { Binding = 4, Buffer = exposureSettings!, Size = ExposureSettingsBytes },
                new BindGroupEntry { Binding = 5, Buffer = rdr2Meter!, Size = Rdr2MeterBytes },
                new BindGroupEntry { Binding = 6, Buffer = exposureCube, Size = Rdr2SkyLightBytes },
            ],
        });
    }

    /// <summary>The mean luminance last measured, which the exposure is worked out from: read back, for tests.</summary>
    internal float MeasuredMean()
    {
        Buffer readback = device.CreateBuffer(new BufferDescriptor { Size = MeasuredBytes, Usage = BufferUsage.CopyDst | BufferUsage.MapRead });
        CommandEncoder encoder = device.CreateCommandEncoder();
        encoder.CopyBufferToBuffer(measured!, 0, readback, 0, MeasuredBytes);
        queue.Submit(encoder.Finish());
        readback.MapSync(MapMode.Read);
        float mean = readback.GetConstMappedRange<float[], float>(0, 1, span => span.ToArray())[0];
        readback.Unmap();
        readback.Destroy();
        return mean;
    }

    /// <summary>
    /// The mean luminance (RDR2's metered middle), the exposure and its target last worked out, and RDR2's cube's
    /// luminance, read back, for tests.
    /// </summary>
    internal (float Mean, float Exposure, float Target, float Cube) MeasuredExposure()
    {
        Buffer readback = device.CreateBuffer(new BufferDescriptor { Size = MeasuredBytes, Usage = BufferUsage.CopyDst | BufferUsage.MapRead });
        CommandEncoder encoder = device.CreateCommandEncoder();
        encoder.CopyBufferToBuffer(measured!, 0, readback, 0, MeasuredBytes);
        queue.Submit(encoder.Finish());
        readback.MapSync(MapMode.Read);
        float[] values = readback.GetConstMappedRange<float[], float>(0, 4, span => span.ToArray());
        readback.Unmap();
        readback.Destroy();
        return (values[0], values[1], values[2], values[3]);
    }

    // The frame time the exposure eases over: the mean of the last frames', as the game averages them; none for a frame
    // given no time.
    private float AverageFrameSeconds()
    {
        float seconds = FrameSeconds;
        FrameSeconds = 0;
        if (seconds <= 0)
            return 0;
        frameTimes[frameTimeNext] = seconds;
        frameTimeNext = (frameTimeNext + 1) % AdaptationFrames;
        frameTimeCount = Math.Min(frameTimeCount + 1, AdaptationFrames);
        float sum = 0;
        for (int i = 0; i < frameTimeCount; i++)
            sum += frameTimes[i];
        return sum / frameTimeCount;
    }

    // What Post/exposure.wgsl works the exposure out with this frame: a world's frame measured as it is and eased towards, the
    // viewer's reference taken at once; the curve and the tweak (the timecycle's, the viewer's, and the push towards the
    // sun the lens flare sees), the range, and how fast it eases.
    private bool WriteExposureSettings()
    {
        // A world with its sky, GTA V's or RDR2's, the latter with its own exposure.
        Rdr2Exposure? rdr2 = Rdr2WorldExposure;
        bool world = worldShown && (HasWorldSky || rdr2 is not null);
        float dt = AverageFrameSeconds();
        Vector3 eye = Camera.Eye;
        bool cut = lastExposureEye is { } last && Vector3.Distance(last, eye) > ExposureCutDistance;
        lastExposureEye = eye;
        // The first frame of a world takes its target at once, as the game's first frame does.
        bool reset = exposureReset || cut || !world || !exposureWasWorld;
        exposureWasWorld = world;
        exposureReset = false;
        Span<Vector4> values = stackalloc Vector4[8];
        values[0] = new Vector4(world ? 1 : 0, reset ? 1 : 0, dt, rdr2 is null ? 0 : 1);
        if (rdr2 is not null)
        {
            values[1] = rdr2.Curve;
            values[2] = new Vector4(rdr2.Min, rdr2.Max, 0, 0);
            values[3] = new Vector4(rdr2.StepMult, rdr2.MaxStep, rdr2.MinStep, rdr2.Threshold);
            values[4] = rdr2.BlendCurve;
            values[5] = new Vector4(rdr2.BlendWeight, rdr2.Tweak + Exposure, rdr2.CubemapAmount, 0);
            values[6] = rdr2.SpotMeter;
            values[7] = new Vector4(rdr2.Lowlights, rdr2.Highlights, rdr2.AbsoluteMin, 0);
        }
        else
        {
            values[1] = new Vector4(toneMap.ExposureScale, toneMap.ExposurePower, toneMap.ExposureOffset, cycle.Exposure + Exposure + SunExposurePush(reset));
            values[2] = new Vector4(cycle.ExposureMin, cycle.ExposureMax, 0, 0);
            values[3] = new Vector4(toneMap.AdaptationStepMult, toneMap.AdaptationMaxStep, toneMap.AdaptationMinStep, 0);
        }
        queue.WriteBuffer(exposureSettings!, 0, (ReadOnlySpan<Vector4>)values);
        return rdr2 is not null;
    }

    // The game's push of the exposure when the camera looks at the sun: Adaptation.sun.exposure.tweak, by how much of the
    // sun the lens flare sees, and by the directional light's intensity over 64; none on a reset, as the game's.
    private float SunExposurePush(bool reset) =>
        reset ? 0 : toneMap.SunExposureTweak * WorldSunVisibility * Math.Clamp(cycle.DirectionalIntensity / 64, 0, 1);

    /// <summary>Measures the frame (or the viewer's reference) and works out the exposure, for the passes that follow.</summary>
    private void MeasureFrame(CommandEncoder encoder)
    {
        bool rdr2 = WriteExposureSettings();
        if (!ReferenceEquals(exposureCube, rdr2Sky?.Light ?? noRdr2Cube))
            CreateExposureGroup();
        var (x, y) = LuminanceTiles;
        ComputePassEncoder pass = encoder.BeginComputePass();
        pass.SetBindGroup(0, sceneGroup);
        pass.SetBindGroup(1, exposureGroup!);
        pass.SetPipeline(luminancePipeline!);
        pass.DispatchWorkgroups(x, y, 1);
        pass.SetPipeline(meanPipeline!);
        pass.DispatchWorkgroups(1, 1, 1);
        // RDR2's: its histogram over the spot meter, then the exposure from its middle.
        if (rdr2)
        {
            pass.SetPipeline(rdr2HistogramPipeline!);
            pass.DispatchWorkgroups(x, y, 1);
            pass.SetPipeline(rdr2ExposurePipeline!);
            pass.DispatchWorkgroups(1, 1, 1);
        }
        pass.End();
    }
}
