namespace HikariHopper.Gpu;

// What shaders bind and draw with: made on Direct3D 12 in a later step (shaders compiled at build, root signatures, the
// shader-visible descriptor heap). Until then nothing makes these.

public sealed class Sampler : GpuObject
{
    private Sampler()
    {
    }

    private protected override void Release()
    {
    }
}

public sealed class BindGroup : GpuObject
{
    private BindGroup()
    {
    }

    private protected override void Release()
    {
    }
}

public sealed class BindGroupLayout : GpuObject
{
    private BindGroupLayout()
    {
    }

    private protected override void Release()
    {
    }
}

public sealed class PipelineLayout : GpuObject
{
    private PipelineLayout()
    {
    }

    private protected override void Release()
    {
    }
}

public sealed class ShaderModule : GpuObject
{
    private ShaderModule()
    {
    }

    private protected override void Release()
    {
    }
}

public sealed class RenderPipeline : GpuObject
{
    private RenderPipeline()
    {
    }

    private protected override void Release()
    {
    }

    public BindGroupLayout GetBindGroupLayout(uint index) => throw D3D12Pending.Bindings();
}

public sealed class ComputePipeline : GpuObject
{
    private ComputePipeline()
    {
    }

    private protected override void Release()
    {
    }

    public BindGroupLayout GetBindGroupLayout(uint index) => throw D3D12Pending.Bindings();
}

public sealed class RenderBundle : GpuObject
{
    private RenderBundle()
    {
    }

    private protected override void Release()
    {
    }
}
