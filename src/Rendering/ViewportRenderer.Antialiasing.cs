using WebGpuSharp;
using Buffer = WebGpuSharp.Buffer;

namespace HikariHopper.Rendering;

// Conservative morphological anti-aliasing (CMAA2, cmaa2.wgsl) over the finished frame. The frame is drawn to a texture
// of its own; CMAA2 finds its edges and writes the pixels it blends to an overlay; and the frame goes to the window with
// the overlay over it (present.wgsl).
internal sealed unsafe partial class ViewportRenderer
{
    /// <summary>The frame's format: every pass draws to it in linear light, stored as sRGB.</summary>
    internal const TextureFormat FrameFormat = TextureFormat.RGBA8UnormSrgb;

    // The overlay of blended pixels: linear colour, alpha one where CMAA2 wrote.
    private const TextureFormat BlendedFormat = TextureFormat.RGBA16Float;
    private const uint ControlBytes = 16 * sizeof(uint);
    private const uint IndirectBytes = 3 * sizeof(uint);
    private const uint EdgesKernel = 14 * 2;

    private Texture? frameTexture, blendedTexture, edgesTexture;
    private TextureView? frameView, blendedView, edgesView;
    private Buffer? cmaaCandidates, cmaaHeads, cmaaControl, cmaaItems, cmaaLocations, cmaaIndirect;
    private BindGroupLayout? cmaaEdgesLayout, cmaaArgsLayout, cmaaProcessLayout, cmaaApplyLayout, presentLayout;
    private BindGroup? cmaaEdgesGroup, cmaaArgsGroup, cmaaProcessGroup, cmaaApplyGroup, presentGroup;
    private ComputePipeline? cmaaEdgesPipeline, cmaaArgsCandidatesPipeline, cmaaProcessPipeline, cmaaArgsApplyPipeline, cmaaApplyPipeline;
    private RenderPipeline? presentPipeline;

    private static BindGroupLayoutEntry StorageBuffer(uint binding) =>
        new() { Binding = binding, Visibility = ShaderStage.Compute, Buffer = new BufferBindingLayout { Type = BufferBindingType.Storage } };

    private static BindGroupLayoutEntry FloatTexture(uint binding, ShaderStage stage) =>
        new() { Binding = binding, Visibility = stage, Texture = new TextureBindingLayout { SampleType = TextureSampleType.UnfilterableFloat, ViewDimension = TextureViewDimension.D2 } };

    private void CreateAntialiasing()
    {
        cmaaControl = device.CreateBuffer(new BufferDescriptor { Size = ControlBytes, Usage = BufferUsage.Storage });
        cmaaIndirect = device.CreateBuffer(new BufferDescriptor { Size = IndirectBytes, Usage = BufferUsage.Storage | BufferUsage.Indirect });
        cmaaEdgesLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
        {
            Entries =
            [
                FloatTexture(0, ShaderStage.Compute),
                new BindGroupLayoutEntry
                {
                    Binding = 1,
                    Visibility = ShaderStage.Compute,
                    StorageTexture = new StorageTextureBindingLayout { Access = StorageTextureAccess.WriteOnly, Format = TextureFormat.R32Uint },
                },
                StorageBuffer(3),
                StorageBuffer(4),
                StorageBuffer(5),
            ],
        });
        cmaaArgsLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
        {
            Entries = [StorageBuffer(3), StorageBuffer(5), StorageBuffer(7), StorageBuffer(8)],
        });
        cmaaProcessLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
        {
            Entries =
            [
                FloatTexture(0, ShaderStage.Compute),
                new BindGroupLayoutEntry
                {
                    Binding = 2,
                    Visibility = ShaderStage.Compute,
                    Texture = new TextureBindingLayout { SampleType = TextureSampleType.Uint, ViewDimension = TextureViewDimension.D2 },
                },
                StorageBuffer(3),
                StorageBuffer(4),
                StorageBuffer(5),
                StorageBuffer(6),
                StorageBuffer(7),
            ],
        });
        cmaaApplyLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
        {
            Entries =
            [
                StorageBuffer(4),
                StorageBuffer(5),
                StorageBuffer(6),
                StorageBuffer(7),
                new BindGroupLayoutEntry
                {
                    Binding = 9,
                    Visibility = ShaderStage.Compute,
                    StorageTexture = new StorageTextureBindingLayout { Access = StorageTextureAccess.WriteOnly, Format = BlendedFormat },
                },
            ],
        });
        presentLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
        {
            Entries = [FloatTexture(0, ShaderStage.Fragment), FloatTexture(1, ShaderStage.Fragment)],
        });

        Lazy<ShaderModule> cmaa = new(() => device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor { Code = Read("cmaa2.wgsl") }));
        ComputePipeline Compute(string entry, BindGroupLayout layout) => gpu.Compute($"cmaa2/{entry}", () => device.CreateComputePipelineSync(new ComputePipelineDescriptor
        {
            Layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [layout] }),
            Compute = new ComputeState { Module = cmaa.Value, EntryPoint = entry },
        }));
        cmaaEdgesPipeline = Compute("cs_edges", cmaaEdgesLayout);
        cmaaArgsCandidatesPipeline = Compute("cs_args_candidates", cmaaArgsLayout);
        cmaaProcessPipeline = Compute("cs_process_candidates", cmaaProcessLayout);
        cmaaArgsApplyPipeline = Compute("cs_args_apply", cmaaArgsLayout);
        cmaaApplyPipeline = Compute("cs_apply", cmaaApplyLayout);

        presentPipeline = gpu.Pipeline($"present/{presentFormat}", () =>
        {
            ShaderModule shader = device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor { Code = Read("present.wgsl") });
            return device.CreateRenderPipelineSync(new RenderPipelineDescriptor
            {
                Layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [presentLayout] }),
                Vertex = new VertexState { Module = shader, EntryPoint = "vs_present" },
                Fragment = new FragmentState
                {
                    Module = shader,
                    EntryPoint = "fs_present",
                    Targets = new[] { new ColorTargetState { Format = presentFormat } },
                },
                Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleList },
                Multisample = new MultisampleState { Count = 1, Mask = uint.MaxValue },
            });
        });
    }

    /// <summary>
    /// The frame and CMAA2's targets and working buffers, sized to the window: room for a candidate per four pixels
    /// and a blended colour per two, CMAA2's own allowance, which only extreme scenes run out of (they then lose some
    /// anti-aliasing, nothing more).
    /// </summary>
    private void CreateAntialiasingTargets()
    {
        frameTexture?.Destroy();
        blendedTexture?.Destroy();
        edgesTexture?.Destroy();
        foreach (Buffer? buffer in new[] { cmaaCandidates, cmaaHeads, cmaaItems, cmaaLocations })
            buffer?.Destroy();
        uint w = (uint)width, h = (uint)height;
        frameTexture = device.CreateTexture(new TextureDescriptor
        {
            Usage = TextureUsage.RenderAttachment | TextureUsage.TextureBinding,
            Dimension = TextureDimension.D2,
            Size = new Extent3D(w, h, 1),
            Format = FrameFormat,
            MipLevelCount = 1,
            SampleCount = 1,
        });
        frameView = frameTexture.CreateView();
        blendedTexture = device.CreateTexture(new TextureDescriptor
        {
            Usage = TextureUsage.RenderAttachment | TextureUsage.TextureBinding | TextureUsage.StorageBinding,
            Dimension = TextureDimension.D2,
            Size = new Extent3D(w, h, 1),
            Format = BlendedFormat,
            MipLevelCount = 1,
            SampleCount = 1,
        });
        blendedView = blendedTexture.CreateView();
        edgesTexture = device.CreateTexture(new TextureDescriptor
        {
            Usage = TextureUsage.StorageBinding | TextureUsage.TextureBinding,
            Dimension = TextureDimension.D2,
            Size = new Extent3D((w + 1) / 2, h, 1),
            Format = TextureFormat.R32Uint,
            MipLevelCount = 1,
            SampleCount = 1,
        });
        edgesView = edgesTexture.CreateView();
        ulong pixels = (ulong)w * h;
        Buffer Storage(ulong count, ulong stride) => device.CreateBuffer(new BufferDescriptor { Size = Math.Max(1, count) * stride, Usage = BufferUsage.Storage });
        cmaaCandidates = Storage(pixels / 4, sizeof(uint));
        cmaaHeads = Storage((ulong)((w + 1) / 2) * ((h + 1) / 2), sizeof(uint));
        cmaaItems = Storage(pixels / 2, 2 * sizeof(uint));
        cmaaLocations = Storage((pixels + 3) / 6, sizeof(uint));

        BindGroupEntry Whole(uint binding, Buffer buffer) => new() { Binding = binding, Buffer = buffer, Size = buffer.GetSize() };
        cmaaEdgesGroup = device.CreateBindGroup(new BindGroupDescriptor
        {
            Layout = cmaaEdgesLayout!,
            Entries =
            [
                new BindGroupEntry { Binding = 0, TextureView = frameView },
                new BindGroupEntry { Binding = 1, TextureView = edgesView },
                Whole(3, cmaaCandidates),
                Whole(4, cmaaHeads),
                Whole(5, cmaaControl!),
            ],
        });
        cmaaArgsGroup = device.CreateBindGroup(new BindGroupDescriptor
        {
            Layout = cmaaArgsLayout!,
            Entries = [Whole(3, cmaaCandidates), Whole(5, cmaaControl!), Whole(7, cmaaLocations), Whole(8, cmaaIndirect!)],
        });
        cmaaProcessGroup = device.CreateBindGroup(new BindGroupDescriptor
        {
            Layout = cmaaProcessLayout!,
            Entries =
            [
                new BindGroupEntry { Binding = 0, TextureView = frameView },
                new BindGroupEntry { Binding = 2, TextureView = edgesView },
                Whole(3, cmaaCandidates),
                Whole(4, cmaaHeads),
                Whole(5, cmaaControl!),
                Whole(6, cmaaItems),
                Whole(7, cmaaLocations),
            ],
        });
        cmaaApplyGroup = device.CreateBindGroup(new BindGroupDescriptor
        {
            Layout = cmaaApplyLayout!,
            Entries =
            [
                Whole(4, cmaaHeads),
                Whole(5, cmaaControl!),
                Whole(6, cmaaItems),
                Whole(7, cmaaLocations),
                new BindGroupEntry { Binding = 9, TextureView = blendedView },
            ],
        });
        presentGroup = device.CreateBindGroup(new BindGroupDescriptor
        {
            Layout = presentLayout!,
            Entries = [new BindGroupEntry { Binding = 0, TextureView = frameView }, new BindGroupEntry { Binding = 1, TextureView = blendedView }],
        });
    }

    /// <summary>Whether the frame is anti-aliased (CMAA2); without, it goes to the window as drawn.</summary>
    public bool Antialiasing { get; set; } = true;

    /// <summary>CMAA2 over the frame just drawn: its blended pixels to the overlay, cleared first.</summary>
    private void Antialias(CommandEncoder encoder)
    {
        RenderPassEncoder clear = encoder.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachments = [new RenderPassColorAttachment { View = blendedView!, LoadOp = LoadOp.Clear, StoreOp = StoreOp.Store, ClearValue = new Color(0, 0, 0, 0) }],
        });
        clear.End();
        if (!Antialiasing)
            return;
        ComputePassEncoder pass = encoder.BeginComputePass();
        pass.SetPipeline(cmaaEdgesPipeline!);
        pass.SetBindGroup(0, cmaaEdgesGroup!);
        pass.DispatchWorkgroups(((uint)width + EdgesKernel - 1) / EdgesKernel, ((uint)height + EdgesKernel - 1) / EdgesKernel, 1);
        pass.SetPipeline(cmaaArgsCandidatesPipeline!);
        pass.SetBindGroup(0, cmaaArgsGroup!);
        pass.DispatchWorkgroups(1, 1, 1);
        pass.SetPipeline(cmaaProcessPipeline!);
        pass.SetBindGroup(0, cmaaProcessGroup!);
        pass.DispatchWorkgroupsIndirect(cmaaIndirect!, 0);
        pass.SetPipeline(cmaaArgsApplyPipeline!);
        pass.SetBindGroup(0, cmaaArgsGroup!);
        pass.DispatchWorkgroups(1, 1, 1);
        pass.SetPipeline(cmaaApplyPipeline!);
        pass.SetBindGroup(0, cmaaApplyGroup!);
        pass.DispatchWorkgroupsIndirect(cmaaIndirect!, 0);
        pass.End();
    }

    /// <summary>The frame, with its anti-aliased pixels over it, to the window.</summary>
    private void Present(CommandEncoder encoder, TextureView window)
    {
        RenderPassEncoder pass = encoder.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachments = [new RenderPassColorAttachment { View = window, LoadOp = LoadOp.Clear, StoreOp = StoreOp.Store, ClearValue = new Color(0, 0, 0, 1) }],
        });
        pass.SetPipeline(presentPipeline!);
        pass.SetBindGroup(0, presentGroup!);
        pass.Draw(3);
        pass.End();
    }

    private void DisposeAntialiasing()
    {
        frameTexture?.Destroy();
        blendedTexture?.Destroy();
        edgesTexture?.Destroy();
        foreach (Buffer? buffer in new[] { cmaaCandidates, cmaaHeads, cmaaItems, cmaaLocations, cmaaControl, cmaaIndirect })
            buffer?.Destroy();
    }
}
