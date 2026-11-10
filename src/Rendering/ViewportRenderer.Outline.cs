using WebGpuSharp;

namespace HikariHopper.Rendering;

// The outline round the geometries picked in the viewer (outline.wgsl): their shape drawn into a mask the frame's size,
// all of it, then a pass over the frame that draws the outline just outside the mask and a faint wash inside it.
internal sealed unsafe partial class ViewportRenderer
{
    private const TextureFormat MaskFormat = TextureFormat.R8Unorm;

    private RenderPipeline? maskPipeline;
    private RenderPipeline? outlinePipeline;
    private BindGroupLayout? outlineLayout;
    private Texture? maskTexture;
    private TextureView? maskView;
    private BindGroup? outlineGroup;
    // Which of the drawable's parts are picked, by the same index as its parts.
    private bool[] partsPicked = [];

    /// <summary>Outlines these of the drawable's parts, by index; none outlines none.</summary>
    public void Pick(IEnumerable<int> parts)
    {
        partsPicked = new bool[surfaceParts.Length];
        foreach (int part in parts)
            if (part >= 0 && part < partsPicked.Length)
                partsPicked[part] = true;
    }

    /// <summary>The parts outlined, by index.</summary>
    internal IReadOnlyList<int> Picked => [.. Enumerable.Range(0, partsPicked.Length).Where(i => partsPicked[i])];

    private void CreateOutline()
    {
        outlineLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
        {
            Entries =
            [
                new BindGroupLayoutEntry
                {
                    Binding = 0,
                    Visibility = ShaderStage.Fragment,
                    Texture = new TextureBindingLayout { SampleType = TextureSampleType.Float, ViewDimension = TextureViewDimension.D2 },
                },
            ],
        });
        Lazy<ShaderModule> shader = new(() => device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor { Code = Shader("outline.wgsl") }));
        maskPipeline = gpu.Pipeline($"outline-mask/{viewFormat}", () => device.CreateRenderPipelineSync(new RenderPipelineDescriptor
        {
            Layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [sceneLayout] }),
            Vertex = new VertexState
            {
                Module = shader.Value,
                EntryPoint = "vs_mask",
                Buffers = new[]
                {
                    new VertexBufferLayout
                    {
                        ArrayStride = SurfaceVertex.Size,
                        Attributes = new[] { new VertexAttribute { ShaderLocation = 0, Offset = 0, Format = VertexFormat.Float32x3 } },
                    },
                },
            },
            Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleList, CullMode = CullMode.None },
            Fragment = new FragmentState
            {
                Module = shader.Value,
                EntryPoint = "fs_mask",
                Targets = new[] { new ColorTargetState { Format = MaskFormat } },
            },
        }));
        outlinePipeline = gpu.Pipeline($"outline/{viewFormat}", () => device.CreateRenderPipelineSync(new RenderPipelineDescriptor
        {
            Layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [sceneLayout, outlineLayout] }),
            Vertex = new VertexState { Module = shader.Value, EntryPoint = "vs_outline" },
            Fragment = new FragmentState
            {
                Module = shader.Value,
                EntryPoint = "fs_outline",
                Targets = new[]
                {
                    new ColorTargetState
                    {
                        Format = viewFormat,
                        Blend = new BlendState
                        {
                            Color = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.SrcAlpha, DstFactor = BlendFactor.OneMinusSrcAlpha },
                            Alpha = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.Zero, DstFactor = BlendFactor.One },
                        },
                    },
                },
            },
        }));
    }

    // The mask, the frame's size, made again as the window resizes.
    private void CreateOutlineTargets()
    {
        maskTexture?.Destroy();
        maskTexture = device.CreateTexture(new TextureDescriptor
        {
            Usage = TextureUsage.RenderAttachment | TextureUsage.TextureBinding,
            Dimension = TextureDimension.D2,
            Size = new Extent3D((uint)width, (uint)height, 1),
            Format = MaskFormat,
            MipLevelCount = 1,
            SampleCount = 1,
        });
        maskView = maskTexture.CreateView();
        outlineGroup = device.CreateBindGroup(new BindGroupDescriptor
        {
            Layout = outlineLayout!,
            Entries = [new BindGroupEntry { Binding = 0, TextureView = maskView }],
        });
    }

    /// <summary>Outlines the picked parts that are drawn over the frame, once the drawable is drawn and tone-mapped.</summary>
    private void DrawOutline(CommandEncoder encoder)
    {
        if (surfaceVertices is null || surfaceIndices is null || maskView is null || outlineGroup is null
            || partsPicked.Length != surfaceParts.Length || !partsPicked.Where((picked, i) => picked && partsShown[i]).Any())
            return;
        RenderPassEncoder masked = encoder.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachments = [new RenderPassColorAttachment { View = maskView, LoadOp = LoadOp.Clear, StoreOp = StoreOp.Store, ClearValue = new Color(0, 0, 0, 0) }],
        });
        masked.SetPipeline(maskPipeline!);
        masked.SetBindGroup(0, sceneGroup);
        masked.SetVertexBuffer(0, surfaceVertices);
        masked.SetIndexBuffer(surfaceIndices, IndexFormat.Uint32);
        for (int i = 0; i < surfaceParts.Length; i++)
            if (partsPicked[i] && partsShown[i])
                masked.DrawIndexed(surfaceParts[i].Part.IndexCount, 1, surfaceParts[i].Part.FirstIndex, surfaceParts[i].Part.BaseVertex, 0);
        masked.End();
        RenderPassEncoder outlined = encoder.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachments = [new RenderPassColorAttachment { View = frameView!, LoadOp = LoadOp.Load, StoreOp = StoreOp.Store }],
        });
        outlined.SetPipeline(outlinePipeline!);
        outlined.SetBindGroup(0, sceneGroup);
        outlined.SetBindGroup(1, outlineGroup);
        outlined.Draw(3);
        outlined.End();
    }

    private void DestroyOutline()
    {
        maskTexture?.Destroy();
        maskTexture = null;
        maskView = null;
        outlineGroup = null;
    }
}
