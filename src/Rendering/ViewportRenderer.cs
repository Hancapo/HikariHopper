using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using CommonFury.Numerics;
using WebGpuSharp;
using WebGpuSharp.FFI;
using Buffer = WebGpuSharp.Buffer;

namespace HikariHopper.Rendering;

/// <summary>
/// Draws the 3D viewport into a window's swap chain with WebGPU (Dawn, on Direct3D 12): the backdrop, a sky and the
/// ground grid, and in front of it a mesh of triangles or a drawable's textured surfaces, seen through the
/// <see cref="Camera"/>.
/// </summary>
/// <remarks>
/// Single-threaded: create, draw, resize and dispose it on one thread, while its window is alive. It draws with the
/// app's <see cref="GpuDevice"/>, whose pipelines it compiles once for every viewport.
/// </remarks>
internal sealed unsafe partial class ViewportRenderer : IDisposable
{
    private const int SceneBytes = 288;
    private const TextureFormat DepthFormat = TextureFormat.Depth32Float;

    private readonly GpuDevice gpu;
    // The device's errors, until this viewport is gone (the device holds it weakly).
    private readonly Action<string> failed;
    private readonly Surface? surface;
    private readonly Adapter adapter;
    private readonly Device device;
    private readonly Queue queue;
    private Buffer sceneBuffer= null!;
    private BindGroupLayout sceneLayout= null!;
    private BindGroup sceneGroup= null!;
    private RenderPipeline backdrop= null!;
    private RenderPipeline meshes= null!;
    private RenderPipeline lines= null!;
    private RenderPipeline overlayLines = null!;
    private RenderPipeline overlayMeshes = null!;
    private Buffer? solidBuffer;
    private uint solidCount;

    /// <summary>How much of a drawable's solid collision covers it: enough to read, the model still seen through.</summary>
    private const double SolidOpacity = .75;
    private Buffer? overlayBuffer;
    private uint overlayCount;
    // The swap chain's format, and the sRGB view of it that frames are shown through; frames are drawn to a texture of
    // their own first (ViewportRenderer.Antialiasing.cs), in the frame format every pass's pipeline is made for.
    private readonly TextureFormat surfaceFormat;
    private readonly TextureFormat presentFormat;
    private readonly TextureFormat viewFormat = FrameFormat;
    private readonly float[] scene = new float[SceneBytes / sizeof(float)];
    private Texture? depth;
    private TextureView? depthView;
    private Buffer? vertexBuffer;
    private uint vertexCount;
    private Buffer? segmentBuffer;
    private uint segmentCount;
    private Vector3 origin;
    private int width;
    private int height;
    private bool disposed;
    private string? error;

    public ViewportRenderer(nint window, nint module, int width, int height) : this(GpuDevice.Shared, window, module, width, height)
    {
    }

    /// <summary>A viewport with no window: it only compiles the pipelines, for <see cref="GpuDevice.WarmUp"/>.</summary>
    internal ViewportRenderer(GpuDevice gpu) : this(gpu, 0, 0, 1, 1)
    {
    }

    private ViewportRenderer(GpuDevice gpu, nint window, nint module, int width, int height)
    {
        this.gpu = gpu;
        adapter = gpu.Adapter;
        device = gpu.Device;
        queue = gpu.Queue;
        compressedTextures = gpu.CompressedTextures;
        failed = message => error ??= message;
        gpu.Listen(failed);
        // Made while the background warm-up isn't using the device, which then has the pipelines ready.
        lock (gpu.Gate)
        {
            if (window != 0)
            {
                var source = new SurfaceSourceWindowsHWNDFFI
                {
                    Chain = new ChainedStruct { SType = SType.SurfaceSourceWindowsHWND },
                    Hwnd = (void*)window,
                    Hinstance = (void*)module,
                };
                surface = gpu.Instance.CreateSurface(new SurfaceDescriptor(ref source)) ?? throw new InvalidOperationException("WebGPU can't draw into this window.");
                // Colours are blended in linear light and stored as sRGB. A swap chain that can't be sRGB itself is
                // drawn through an sRGB view of it.
                SurfaceCapabilities capabilities = surface.GetCapabilities(adapter) ?? throw new NotSupportedException("The window's capabilities are unknown.");
                ReadOnlySpan<TextureFormat> formats = capabilities.Formats;
                surfaceFormat = formats.Contains(TextureFormat.BGRA8UnormSrgb) ? TextureFormat.BGRA8UnormSrgb
                    : formats.Contains(TextureFormat.BGRA8Unorm) ? TextureFormat.BGRA8Unorm
                    : formats.Contains(TextureFormat.RGBA8Unorm) ? TextureFormat.RGBA8Unorm
                    : throw new NotSupportedException("The window offers no 8-bit colour format.");
            }
            else
            {
                // A window's usual format, so the pipelines compiled for it are the ones a window needs.
                surfaceFormat = TextureFormat.BGRA8UnormSrgb;
            }
            presentFormat = surfaceFormat switch
            {
                TextureFormat.BGRA8Unorm => TextureFormat.BGRA8UnormSrgb,
                TextureFormat.RGBA8Unorm => TextureFormat.RGBA8UnormSrgb,
                _ => surfaceFormat,
            };
            Create(width, height);
        }
    }

    private void Create(int width, int height)
    {
        sceneBuffer = device.CreateBuffer(new BufferDescriptor { Size = SceneBytes, Usage = BufferUsage.Uniform | BufferUsage.CopyDst });
        BindGroupLayout layout = sceneLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
        {
            Entries =
            [
                new BindGroupLayoutEntry
                {
                    Binding = 0,
                    Visibility = ShaderStage.Vertex | ShaderStage.Fragment | ShaderStage.Compute,
                    Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = SceneBytes },
                },
            ],
        });
        sceneGroup = device.CreateBindGroup(new BindGroupDescriptor
        {
            Layout = layout,
            Entries = [new BindGroupEntry { Binding = 0, Buffer = sceneBuffer, Size = SceneBytes }],
        });
        PipelineLayout pipelineLayout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [layout] });

        // The backdrop goes first and behind everything: it neither tests nor writes depth.
        Lazy<ShaderModule> backdropShader = new(() => device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor { Code = Shader("backdrop.wgsl") }));
        backdrop = gpu.Pipeline($"backdrop/{viewFormat}", () => device.CreateRenderPipelineSync(new RenderPipelineDescriptor
        {
            Layout = pipelineLayout,
            Vertex = new VertexState { Module = backdropShader.Value, EntryPoint = "vs_backdrop" },
            Fragment = new FragmentState
            {
                Module = backdropShader.Value,
                EntryPoint = "fs_backdrop",
                Targets = new[] { new ColorTargetState { Format = viewFormat } },
            },
            DepthStencil = new DepthStencilState { Format = DepthFormat, DepthWriteEnabled = OptionalBool.False, DepthCompare = CompareFunction.Always },
        }));
        // Meshes: nearer is greater in reversed Z. Both sides drawn, as collisions aren't wound consistently.
        Lazy<ShaderModule> meshShader = new(() => device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor { Code = Shader("mesh.wgsl") }));
        // Over a drawable, its solid collision blends by a constant, the nearest surface only.
        RenderPipelineDescriptor Meshes(bool blended) => new()
        {
            Layout = pipelineLayout,
            Vertex = new VertexState
            {
                Module = meshShader.Value,
                EntryPoint = "vs_mesh",
                Buffers = new[]
                {
                    new VertexBufferLayout
                    {
                        ArrayStride = ViewportVertex.Size,
                        Attributes = new[]
                        {
                            new VertexAttribute { ShaderLocation = 0, Offset = 0, Format = VertexFormat.Float32x3 },
                            new VertexAttribute { ShaderLocation = 1, Offset = 12, Format = VertexFormat.Float32x3 },
                            new VertexAttribute { ShaderLocation = 2, Offset = 24, Format = VertexFormat.Unorm8x4 },
                        },
                    },
                },
            },
            Fragment = new FragmentState
            {
                Module = meshShader.Value,
                EntryPoint = "fs_mesh",
                Targets = new[]
                {
                    blended
                        ? new ColorTargetState
                        {
                            Format = viewFormat,
                            Blend = new BlendState
                            {
                                Color = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.Constant, DstFactor = BlendFactor.OneMinusConstant },
                                Alpha = new BlendComponent { Operation = BlendOperation.Add, SrcFactor = BlendFactor.Zero, DstFactor = BlendFactor.One },
                            },
                        }
                        : new ColorTargetState { Format = viewFormat },
                },
            },
            DepthStencil = new DepthStencilState { Format = DepthFormat, DepthWriteEnabled = OptionalBool.True, DepthCompare = CompareFunction.Greater },
        };
        meshes = gpu.Pipeline($"meshes/{viewFormat}", () => device.CreateRenderPipelineSync(Meshes(blended: false)));
        overlayMeshes = gpu.Pipeline($"overlay-meshes/{viewFormat}", () => device.CreateRenderPipelineSync(Meshes(blended: true)));
        // Lines over the meshes: tested against them, writing no depth. Each segment is one instance of six corners.
        Lazy<ShaderModule> lineShader = new(() => device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor { Code = Shader("lines.wgsl") }));
        RenderPipelineDescriptor Lines(bool depth) => new()
        {
            Layout = pipelineLayout,
            Vertex = new VertexState
            {
                Module = lineShader.Value,
                EntryPoint = "vs_line",
                Buffers = new[]
                {
                    new VertexBufferLayout
                    {
                        ArrayStride = ViewportSegment.Size,
                        StepMode = VertexStepMode.Instance,
                        Attributes = new[]
                        {
                            new VertexAttribute { ShaderLocation = 0, Offset = 0, Format = VertexFormat.Float32x3 },
                            new VertexAttribute { ShaderLocation = 1, Offset = 12, Format = VertexFormat.Float32x3 },
                            new VertexAttribute { ShaderLocation = 2, Offset = 24, Format = VertexFormat.Unorm8x4 },
                            new VertexAttribute { ShaderLocation = 3, Offset = 28, Format = VertexFormat.Float32 },
                        },
                    },
                },
            },
            Fragment = new FragmentState
            {
                Module = lineShader.Value,
                EntryPoint = "fs_line",
                Targets = new[] { new ColorTargetState { Format = viewFormat } },
            },
            DepthStencil = depth ? new DepthStencilState { Format = DepthFormat, DepthWriteEnabled = OptionalBool.False, DepthCompare = CompareFunction.GreaterEqual } : null,
        };
        lines = gpu.Pipeline($"lines/{viewFormat}", () => device.CreateRenderPipelineSync(Lines(depth: true)));
        // A drawable's overlays (its skeleton, bounds and lights): over everything, seen through the model.
        overlayLines = gpu.Pipeline($"overlay-lines/{viewFormat}", () => device.CreateRenderPipelineSync(Lines(depth: false)));
        CreateLighting();
        CreateSurfacePipelines();
        CreateAntialiasing();
        Resize(width, height);
        ThrowIfFailed();
    }

    public OrbitCamera Camera { get; } = new();

    /// <summary>The ground grid's height: the bottom of what is framed.</summary>
    public float Ground { get; private set; }

    /// <summary>The GPU it draws with, as "name (backend)".</summary>
    public string AdapterName
    {
        get
        {
            AdapterInfo info = adapter.GetInfo();
            return $"{info.Device} ({info.BackendType})";
        }
    }

    /// <summary>Points the camera at a box in the world, from a distance that shows it whole, over a grid under it.</summary>
    public void Frame(Aabb3 box)
    {
        // A flat or tiny box still gets room to orbit.
        Camera.Frame(box.Center, Math.Max(box.Size.Length() / 2, .5f));
        Ground = box.Minimum.Z;
    }

    /// <summary>Shows these triangles and lines in place of anything before; null shows none.</summary>
    public void Show(ViewportMesh? mesh)
    {
        // The GPU memory goes now, not whenever the handles are collected.
        vertexBuffer?.Destroy();
        segmentBuffer?.Destroy();
        vertexBuffer = segmentBuffer = null;
        vertexCount = segmentCount = 0;
        ClearSurfaces();
        if (mesh is null)
            return;
        origin = mesh.Origin;
        if (mesh.Vertices.Count > 0)
        {
            ViewportVertex[] vertices = mesh.ToArray();
            vertexBuffer = Upload((ReadOnlySpan<ViewportVertex>)vertices, ViewportVertex.Size);
            vertexCount = (uint)vertices.Length;
        }
        if (mesh.Segments.Count > 0)
        {
            ViewportSegment[] segments = mesh.SegmentArray();
            segmentBuffer = Upload((ReadOnlySpan<ViewportSegment>)segments, ViewportSegment.Size);
            segmentCount = (uint)segments.Length;
        }
    }

    /// <summary>Draws these triangles over a drawable, its collision, relative to its origin, in place of any before; none draws none.</summary>
    public void ShowSolid(ReadOnlySpan<ViewportVertex> triangles)
    {
        solidBuffer?.Destroy();
        solidBuffer = null;
        solidCount = 0;
        if (triangles.Length == 0)
            return;
        solidBuffer = Upload(triangles, ViewportVertex.Size);
        solidCount = (uint)triangles.Length;
    }

    /// <summary>Draws these lines over a drawable, relative to its origin, in place of any before; none draws none.</summary>
    public void ShowOverlays(ReadOnlySpan<ViewportSegment> segments)
    {
        overlayBuffer?.Destroy();
        overlayBuffer = null;
        overlayCount = 0;
        if (segments.Length == 0)
            return;
        overlayBuffer = Upload(segments, ViewportSegment.Size);
        overlayCount = (uint)segments.Length;
    }

    private Buffer Upload<T>(ReadOnlySpan<T> items, int size, BufferUsage usage = BufferUsage.Vertex) where T : unmanaged
    {
        Buffer buffer = device.CreateBuffer(new BufferDescriptor
        {
            // Writes go in multiples of four bytes.
            Size = ((ulong)items.Length * (ulong)size + 3) & ~3UL,
            Usage = usage | BufferUsage.CopyDst,
        });
        queue.WriteBuffer(buffer, 0, items);
        return buffer;
    }

    /// <summary>Reconfigures the swap chain for a window resized to these pixels; an empty window draws nothing.</summary>
    public void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            this.width = this.height = 0;
            return;
        }
        this.width = width;
        this.height = height;
        surface?.Configure(new SurfaceConfiguration
        {
            Device = device,
            Format = surfaceFormat,
            ViewFormats = surfaceFormat == presentFormat ? [] : [presentFormat],
            Width = (uint)width,
            Height = (uint)height,
            PresentMode = PresentMode.Fifo,
        });
        depth?.Destroy();
        depth = device.CreateTexture(new TextureDescriptor
        {
            Usage = TextureUsage.RenderAttachment,
            Dimension = TextureDimension.D2,
            Size = new Extent3D((uint)width, (uint)height),
            Format = DepthFormat,
            MipLevelCount = 1,
            SampleCount = 1,
        });
        depthView = depth.CreateView();
        CreateHdr();
        CreateAntialiasingTargets();
    }

    /// <summary>Draws and presents a frame.</summary>
    public void Draw()
    {
        if (disposed || width == 0 || height == 0 || depthView is null || surface is null)
            return;
        ThrowIfFailed();
        WriteScene();
        SurfaceTexture frame = surface.GetCurrentTexture();
        if (frame.Status is not (SurfaceGetCurrentTextureStatus.SuccessOptimal or SurfaceGetCurrentTextureStatus.SuccessSuboptimal)
            || frame.Texture is not { } texture)
        {
            // Lost or outdated (a resize, a display change): configure again and draw the next frame.
            Resize(width, height);
            return;
        }
        TextureView view = texture.CreateView(new TextureViewDescriptor
        {
            Format = presentFormat,
            Dimension = TextureViewDimension.D2,
            MipLevelCount = 1,
            ArrayLayerCount = 1,
            Aspect = TextureAspect.All,
        });
        Render(view);
        surface.Present();
    }

    /// <summary>
    /// Draws a frame to a target in the window's format (the window's own, or a test's), with the scene as last
    /// written.
    /// </summary>
    internal void Render(TextureView target)
    {
        CommandEncoder encoder = device.CreateCommandEncoder();
        // A drawable's shadow first: the ground under it and the drawable itself both read it.
        bool drawable = surfaceVertices is not null && surfaceParts.Length > 0;
        if (drawable)
        {
            WriteLight(Shadows);
            if (Shadows)
                DrawShadow(encoder);
        }
        RenderPassEncoder pass = encoder.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachments =
            [
                new RenderPassColorAttachment
                {
                    View = frameView!,
                    LoadOp = LoadOp.Clear,
                    StoreOp = StoreOp.Store,
                    ClearValue = new Color(0, 0, 0, 1),
                },
            ],
            // Reversed Z: cleared to zero, infinitely far.
            DepthStencilAttachment = new RenderPassDepthStencilAttachment
            {
                View = depthView,
                DepthLoadOp = LoadOp.Clear,
                DepthStoreOp = StoreOp.Discard,
                DepthClearValue = 0,
            },
        });
        pass.SetBindGroup(0, sceneGroup);
        pass.SetPipeline(backdrop);
        pass.Draw(3);
        if (drawable && Shadows)
            DrawGroundShadow(pass);
        if (vertexBuffer is not null)
        {
            pass.SetPipeline(meshes);
            pass.SetVertexBuffer(0, vertexBuffer);
            pass.Draw(vertexCount);
        }
        if (segmentBuffer is not null)
        {
            pass.SetPipeline(lines);
            pass.SetVertexBuffer(0, segmentBuffer);
            pass.Draw(6, segmentCount);
        }
        pass.End();
        // Drawables in HDR, reflecting the scene as it is from the camera, then tone-mapped over the backdrop.
        if (drawable)
        {
            DrawSceneReflections(encoder);
            RenderPassEncoder lit = encoder.BeginRenderPass(new RenderPassDescriptor
            {
                ColorAttachments =
                [
                    new RenderPassColorAttachment { View = hdrView!, LoadOp = LoadOp.Clear, StoreOp = StoreOp.Store, ClearValue = new Color(0, 0, 0, 0) },
                ],
                DepthStencilAttachment = new RenderPassDepthStencilAttachment
                {
                    View = depthView,
                    DepthLoadOp = LoadOp.Clear,
                    DepthStoreOp = StoreOp.Discard,
                    DepthClearValue = 0,
                },
            });
            lit.SetBindGroup(0, sceneGroup);
            lit.SetBindGroup(2, lightGroup!);
            DrawSurfaces(lit);
            lit.End();
            MeasureFrame(encoder);
            DrawBloom(encoder);
            RenderPassEncoder mapped = encoder.BeginRenderPass(new RenderPassDescriptor
            {
                ColorAttachments = [new RenderPassColorAttachment { View = frameView!, LoadOp = LoadOp.Load, StoreOp = StoreOp.Store }],
            });
            mapped.SetPipeline(tonemapPipeline!);
            mapped.SetBindGroup(0, sceneGroup);
            mapped.SetBindGroup(1, tonemapGroup!);
            mapped.SetBindGroup(2, lightGroup!);
            mapped.Draw(3);
            mapped.End();
            // The solid collision over the model, its nearest surface only, then the lines over everything.
            if (solidBuffer is not null)
            {
                RenderPassEncoder solid = encoder.BeginRenderPass(new RenderPassDescriptor
                {
                    ColorAttachments = [new RenderPassColorAttachment { View = frameView!, LoadOp = LoadOp.Load, StoreOp = StoreOp.Store }],
                    DepthStencilAttachment = new RenderPassDepthStencilAttachment
                    {
                        View = depthView,
                        DepthLoadOp = LoadOp.Clear,
                        DepthStoreOp = StoreOp.Discard,
                        DepthClearValue = 0,
                    },
                });
                solid.SetBindGroup(0, sceneGroup);
                solid.SetPipeline(overlayMeshes);
                solid.SetBlendConstant(new Color(SolidOpacity, SolidOpacity, SolidOpacity, SolidOpacity));
                solid.SetVertexBuffer(0, solidBuffer);
                solid.Draw(solidCount);
                solid.End();
            }
            if (overlayBuffer is not null)
            {
                RenderPassEncoder lined = encoder.BeginRenderPass(new RenderPassDescriptor
                {
                    ColorAttachments = [new RenderPassColorAttachment { View = frameView!, LoadOp = LoadOp.Load, StoreOp = StoreOp.Store }],
                });
                lined.SetBindGroup(0, sceneGroup);
                lined.SetPipeline(overlayLines);
                lined.SetVertexBuffer(0, overlayBuffer);
                lined.Draw(6, overlayCount);
                lined.End();
            }
        }
        // Anti-aliased, then to the window.
        Antialias(encoder);
        Present(encoder, target);
        queue.Submit(encoder.Finish());
    }

    /// <summary>Draws a frame offscreen, to a target in the window's format, for tests.</summary>
    internal void RenderTo(TextureView target)
    {
        ThrowIfFailed();
        WriteScene();
        Render(target);
    }

    /// <summary>The format frames go to the window in: what <see cref="RenderTo"/> needs.</summary>
    internal TextureFormat PresentFormat => presentFormat;

    private void WriteScene()
    {
        float aspect = (float)width / height;
        Span<float> values = scene;
        var matrix = Camera.ViewProjection(aspect, origin);
        MemoryMarshal.Write(MemoryMarshal.AsBytes(values), in matrix);
        Span<Vector4> vectors = MemoryMarshal.Cast<float, Vector4>(values[16..]);
        vectors[0] = new Vector4(Camera.Eye, 0);
        vectors[1] = new Vector4(Camera.Forward, 0);
        vectors[2] = new Vector4(Camera.Right, 0);
        vectors[3] = new Vector4(Camera.Up, 0);
        // The ground under what is framed, its grid a power of ten about a tenth of its size.
        vectors[4] = new Vector4(aspect, OrbitCamera.TanHalf, Ground, GridSpacing(Camera.Radius));
        ViewportPalette.Linear().CopyTo(vectors[5..]);
        vectors[12] = new Vector4(origin, 0);
        vectors[13] = new Vector4(width, height, 0, 0);
        queue.WriteBuffer(sceneBuffer, 0, (ReadOnlySpan<float>)values);
    }

    /// <summary>The minor grid's spacing for something of this radius: the power of ten at or under a tenth of its width.</summary>
    internal static float GridSpacing(float radius) => MathF.Pow(10, MathF.Floor(MathF.Log10(Math.Max(radius / 5, .1f))));

    private void ThrowIfFailed()
    {
        if (error is { } message)
        {
            error = null;
            throw new InvalidOperationException(message);
        }
    }

    /// <summary>A shader's source: its own file, after scene.wgsl, which every shader shares (WGSL has no includes).</summary>
    private static string Shader(string name) => Read("scene.wgsl") + "\n" + Read(name);

    internal static string Read(string name)
    {
        using Stream stream = typeof(ViewportRenderer).Assembly.GetManifestResourceStream("HikariHopper.Shaders." + name)
            ?? throw new FileNotFoundException(name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        // The rest is released with its handles; the swap chain lets go of the window now.
        depth?.Destroy();
        hdr?.Destroy();
        bloom?.Destroy();
        bloomUnfiltered?.Destroy();
        shadowMap?.Destroy();
        vertexBuffer?.Destroy();
        segmentBuffer?.Destroy();
        DisposeAntialiasing();
        ClearSurfaces();
        surface?.Unconfigure();
    }
}
