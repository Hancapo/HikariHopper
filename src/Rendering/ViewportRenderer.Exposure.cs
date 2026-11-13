using WebGpuSharp;
using Buffer = WebGpuSharp.Buffer;

namespace HikariHopper.Rendering;

// The mean luminance the tone map turns into GTA's exposure (exposure.wgsl): of a fixed view of the world at the hour
// shown, all round the horizon, not of the frame, so where the camera is doesn't change it. Measured each frame, laid
// out over the frame's size: per 16 by 16 tile, then their sum.
internal sealed unsafe partial class ViewportRenderer
{
    private const int MeasuredBytes = 16;
    private const uint LuminanceTile = 16;

    private Buffer? measured, partialSums, frameSkyBuffer;
    private BindGroupLayout? exposureLayout;
    private BindGroup? exposureGroup;
    private ComputePipeline? luminancePipeline, meanPipeline;

    private void CreateExposure()
    {
        measured = device.CreateBuffer(new BufferDescriptor { Size = MeasuredBytes, Usage = BufferUsage.Storage | BufferUsage.CopySrc });
        frameSkyBuffer = device.CreateBuffer(new BufferDescriptor { Size = SkyBytes, Usage = BufferUsage.Uniform | BufferUsage.CopyDst });
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
            ],
        });
        Lazy<ShaderModule> shader = new(() => device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor
        {
            Code = Read("scene.wgsl") + "\n" + Read("sky_system.wgsl") + "\n" + Read("exposure.wgsl"),
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
    }

    private (uint X, uint Y) LuminanceTiles => (((uint)hdrWidth + LuminanceTile - 1) / LuminanceTile, ((uint)hdrHeight + LuminanceTile - 1) / LuminanceTile);

    /// <summary>The measurement's view of the HDR target, with room for a sum per tile of it.</summary>
    private void CreateExposureGroup()
    {
        partialSums?.Destroy();
        var (x, y) = LuminanceTiles;
        partialSums = device.CreateBuffer(new BufferDescriptor { Size = Math.Max(1, x * y) * sizeof(float), Usage = BufferUsage.Storage });
        exposureGroup = device.CreateBindGroup(new BindGroupDescriptor
        {
            Layout = exposureLayout!,
            Entries =
            [
                new BindGroupEntry { Binding = 0, TextureView = hdrView! },
                new BindGroupEntry { Binding = 1, Buffer = partialSums, Size = partialSums.GetSize() },
                new BindGroupEntry { Binding = 2, Buffer = frameSkyBuffer!, Size = SkyBytes },
                new BindGroupEntry { Binding = 3, Buffer = measured!, Size = MeasuredBytes },
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

    /// <summary>Measures the world's light at the hour shown, for the tone map that follows.</summary>
    private void MeasureFrame(CommandEncoder encoder)
    {
        var (x, y) = LuminanceTiles;
        ComputePassEncoder pass = encoder.BeginComputePass();
        pass.SetBindGroup(0, sceneGroup);
        pass.SetBindGroup(1, exposureGroup!);
        pass.SetPipeline(luminancePipeline!);
        pass.DispatchWorkgroups(x, y, 1);
        pass.SetPipeline(meanPipeline!);
        pass.DispatchWorkgroups(1, 1, 1);
        pass.End();
    }
}
