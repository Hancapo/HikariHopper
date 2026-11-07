using WebGpuSharp;
using Buffer = WebGpuSharp.Buffer;

namespace HikariHopper.Rendering;

// The frame's mean luminance (exposure.wgsl), measured each frame after the drawables are drawn, which the tone map
// turns into GTA's exposure: per 16 by 16 tile, then their sum over the frame.
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
        measured = device.CreateBuffer(new BufferDescriptor { Size = MeasuredBytes, Usage = BufferUsage.Storage });
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
        ShaderModule shader = device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor
        {
            Code = Read("scene.wgsl") + "\n" + Read("sky_system.wgsl") + "\n" + Read("exposure.wgsl"),
        });
        PipelineLayout layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [sceneLayout, exposureLayout] });
        luminancePipeline = device.CreateComputePipelineSync(new ComputePipelineDescriptor
        {
            Layout = layout,
            Compute = new ComputeState { Module = shader, EntryPoint = "cs_luminance" },
        });
        meanPipeline = device.CreateComputePipelineSync(new ComputePipelineDescriptor
        {
            Layout = layout,
            Compute = new ComputeState { Module = shader, EntryPoint = "cs_luminance_mean" },
        });
    }

    private (uint X, uint Y) LuminanceTiles => (((uint)width + LuminanceTile - 1) / LuminanceTile, ((uint)height + LuminanceTile - 1) / LuminanceTile);

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

    /// <summary>Measures the frame just drawn to the HDR target, for the tone map that follows.</summary>
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
