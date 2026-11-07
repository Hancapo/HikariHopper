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
/// ground grid, and a mesh of triangles in front of it, seen through the <see cref="Camera"/>.
/// </summary>
/// <remarks>Single-threaded: create, draw, resize and dispose it on one thread, while its window is alive.</remarks>
internal sealed unsafe class ViewportRenderer : IDisposable
{
    private const int SceneBytes = 288;
    private const TextureFormat DepthFormat = TextureFormat.Depth32Float;

    private readonly Surface surface;
    private readonly Adapter adapter;
    private readonly Device device;
    private readonly Queue queue;
    private readonly Buffer sceneBuffer;
    private readonly BindGroup sceneGroup;
    private readonly RenderPipeline backdrop;
    private readonly RenderPipeline meshes;
    private readonly RenderPipeline lines;
    // The swap chain's format, and the sRGB view of it that frames are drawn through.
    private readonly TextureFormat surfaceFormat;
    private readonly TextureFormat viewFormat;
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

    public ViewportRenderer(nint window, nint module, int width, int height)
    {
        Instance instance = WebGPU.CreateInstance() ?? throw new InvalidOperationException("WebGPU is unavailable.");
        var source = new SurfaceSourceWindowsHWNDFFI
        {
            Chain = new ChainedStruct { SType = SType.SurfaceSourceWindowsHWND },
            Hwnd = (void*)window,
            Hinstance = (void*)module,
        };
        surface = instance.CreateSurface(new SurfaceDescriptor(ref source)) ?? throw new InvalidOperationException("WebGPU can't draw into this window.");
        adapter = instance.RequestAdapterSync(new RequestAdapterOptions
        {
            CompatibleSurface = surface,
            BackendType = BackendType.D3D12,
            PowerPreference = PowerPreference.HighPerformance,
        });
        device = RequestDevice(adapter, message => error ??= message);
        queue = device.GetQueue();

        // Colours are blended in linear light and stored as sRGB. A swap chain that can't be sRGB itself is drawn
        // through an sRGB view of it.
        SurfaceCapabilities capabilities = surface.GetCapabilities(adapter) ?? throw new NotSupportedException("The window's capabilities are unknown.");
        ReadOnlySpan<TextureFormat> formats = capabilities.Formats;
        surfaceFormat = formats.Contains(TextureFormat.BGRA8UnormSrgb) ? TextureFormat.BGRA8UnormSrgb
            : formats.Contains(TextureFormat.BGRA8Unorm) ? TextureFormat.BGRA8Unorm
            : formats.Contains(TextureFormat.RGBA8Unorm) ? TextureFormat.RGBA8Unorm
            : throw new NotSupportedException("The window offers no 8-bit colour format.");
        viewFormat = surfaceFormat switch
        {
            TextureFormat.BGRA8Unorm => TextureFormat.BGRA8UnormSrgb,
            TextureFormat.RGBA8Unorm => TextureFormat.RGBA8UnormSrgb,
            _ => surfaceFormat,
        };

        sceneBuffer = device.CreateBuffer(new BufferDescriptor { Size = SceneBytes, Usage = BufferUsage.Uniform | BufferUsage.CopyDst });
        BindGroupLayout layout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
        {
            Entries =
            [
                new BindGroupLayoutEntry
                {
                    Binding = 0,
                    Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
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
        ShaderModule backdropShader = device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor { Code = Shader("backdrop.wgsl") });
        backdrop = device.CreateRenderPipelineSync(new RenderPipelineDescriptor
        {
            Layout = pipelineLayout,
            Vertex = new VertexState { Module = backdropShader, EntryPoint = "vs_backdrop" },
            Fragment = new FragmentState
            {
                Module = backdropShader,
                EntryPoint = "fs_backdrop",
                Targets = new[] { new ColorTargetState { Format = viewFormat } },
            },
            DepthStencil = new DepthStencilState { Format = DepthFormat, DepthWriteEnabled = OptionalBool.False, DepthCompare = CompareFunction.Always },
        });
        // Meshes: nearer is greater in reversed Z. Both sides drawn, as collisions aren't wound consistently.
        ShaderModule meshShader = device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor { Code = Shader("mesh.wgsl") });
        meshes = device.CreateRenderPipelineSync(new RenderPipelineDescriptor
        {
            Layout = pipelineLayout,
            Vertex = new VertexState
            {
                Module = meshShader,
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
                Module = meshShader,
                EntryPoint = "fs_mesh",
                Targets = new[] { new ColorTargetState { Format = viewFormat } },
            },
            DepthStencil = new DepthStencilState { Format = DepthFormat, DepthWriteEnabled = OptionalBool.True, DepthCompare = CompareFunction.Greater },
        });
        // Lines over the meshes: tested against them, writing no depth. Each segment is one instance of six corners.
        ShaderModule lineShader = device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor { Code = Shader("lines.wgsl") });
        lines = device.CreateRenderPipelineSync(new RenderPipelineDescriptor
        {
            Layout = pipelineLayout,
            Vertex = new VertexState
            {
                Module = lineShader,
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
                Module = lineShader,
                EntryPoint = "fs_line",
                Targets = new[] { new ColorTargetState { Format = viewFormat } },
            },
            DepthStencil = new DepthStencilState { Format = DepthFormat, DepthWriteEnabled = OptionalBool.False, DepthCompare = CompareFunction.GreaterEqual },
        });
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

    /// <summary>Shows these triangles and lines in place of any before; null shows none.</summary>
    public void Show(ViewportMesh? mesh)
    {
        // The GPU memory goes now, not whenever the handles are collected.
        vertexBuffer?.Destroy();
        segmentBuffer?.Destroy();
        vertexBuffer = segmentBuffer = null;
        vertexCount = segmentCount = 0;
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

    private Buffer Upload<T>(ReadOnlySpan<T> items, int size) where T : unmanaged
    {
        Buffer buffer = device.CreateBuffer(new BufferDescriptor
        {
            Size = (ulong)items.Length * (ulong)size,
            Usage = BufferUsage.Vertex | BufferUsage.CopyDst,
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
        surface.Configure(new SurfaceConfiguration
        {
            Device = device,
            Format = surfaceFormat,
            ViewFormats = surfaceFormat == viewFormat ? [] : [viewFormat],
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
    }

    /// <summary>Draws and presents a frame.</summary>
    public void Draw()
    {
        if (disposed || width == 0 || height == 0 || depthView is null)
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
            Format = viewFormat,
            Dimension = TextureViewDimension.D2,
            MipLevelCount = 1,
            ArrayLayerCount = 1,
            Aspect = TextureAspect.All,
        });
        CommandEncoder encoder = device.CreateCommandEncoder();
        RenderPassEncoder pass = encoder.BeginRenderPass(new RenderPassDescriptor
        {
            ColorAttachments =
            [
                new RenderPassColorAttachment
                {
                    View = view,
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
        queue.Submit(encoder.Finish());
        surface.Present();
    }

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

    private static Device RequestDevice(Adapter adapter, Action<string> failed)
    {
        // The descriptor is a ref struct, so it is built where the request is made, off the UI thread's context.
        return Task.Run(() => adapter.RequestDeviceAsync(new DeviceDescriptor
        {
            UncapturedErrorCallback = (type, message) => failed($"WebGPU {type}: {Encoding.UTF8.GetString(message)}"),
            DeviceLostCallback = (reason, message) =>
            {
                if (reason != DeviceLostReason.Destroyed)
                    failed($"WebGPU device lost ({reason}): {Encoding.UTF8.GetString(message)}");
            },
        })).GetAwaiter().GetResult();
    }

    /// <summary>A shader's source: its own file, after scene.wgsl, which every shader shares (WGSL has no includes).</summary>
    private static string Shader(string name) => Read("scene.wgsl") + "\n" + Read(name);

    private static string Read(string name)
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
        vertexBuffer?.Destroy();
        segmentBuffer?.Destroy();
        surface.Unconfigure();
    }
}
