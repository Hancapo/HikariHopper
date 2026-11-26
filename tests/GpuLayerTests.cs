using System.Runtime.InteropServices;
using HikariHopper.Gpu;
using HikariHopper.Rendering;
using GpuBuffer = HikariHopper.Gpu.Buffer;

namespace HikariHopper.Tests;

/// <summary>
/// The GPU layer itself, whichever backend it's built on: what's written comes back, what's cleared is clear, what's
/// let go of is freed once the GPU is done with it, and a window's swap chain presents.
/// </summary>
public sealed class GpuLayerTests
{
    [Fact]
    public void A_cleared_target_reads_back_its_colour()
    {
        GpuDevice gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            gpu.Device.PushErrorScope(ErrorFilter.Validation);
            Texture target = Target(gpu, 64, 32, TextureFormat.RGBA8Unorm);
            TextureView view = target.CreateView();
            CommandEncoder encoder = gpu.Device.CreateCommandEncoder();
            RenderPassEncoder pass = encoder.BeginRenderPass(new RenderPassDescriptor
            {
                ColorAttachments = [new RenderPassColorAttachment { View = view, LoadOp = LoadOp.Clear, StoreOp = StoreOp.Store, ClearValue = new Color(1, 0.5, 0, 1) }],
            });
            pass.End();
            byte[] pixels = Read(gpu, encoder, target, 64, 32, 4);
            view.Dispose();
            target.Destroy();
            Assert.Equal(NoError, gpu.Device.PopErrorScopeSync());
            for (int i = 0; i < pixels.Length; i += 4)
                Assert.True(pixels[i] == 255 && pixels[i + 1] is 127 or 128 && pixels[i + 2] == 0 && pixels[i + 3] == 255, $"texel {i / 4}: {pixels[i]} {pixels[i + 1]} {pixels[i + 2]} {pixels[i + 3]}");
        }
    }

    [Fact]
    public void Written_buffers_copy_and_read_back_and_new_ones_are_zero()
    {
        GpuDevice gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            gpu.Device.PushErrorScope(ErrorFilter.Validation);
            GpuBuffer source = gpu.Device.CreateBuffer(new BufferDescriptor { Size = 1024, Usage = BufferUsage.CopySrc | BufferUsage.CopyDst | BufferUsage.Uniform });
            GpuBuffer untouched = gpu.Device.CreateBuffer(new BufferDescriptor { Size = 1024, Usage = BufferUsage.CopySrc | BufferUsage.Storage });
            uint[] values = [.. Enumerable.Range(0, 256).Select(i => (uint)(i * 7 + 1))];
            gpu.Queue.WriteBuffer(source, 0, values);
            // Written twice before a submit: the later write wins where they overlap.
            gpu.Queue.WriteBuffer(source, 16, new uint[] { 99, 98 });
            GpuBuffer readback = gpu.Device.CreateBuffer(new BufferDescriptor { Size = 2048, Usage = BufferUsage.CopyDst | BufferUsage.MapRead });
            CommandEncoder encoder = gpu.Device.CreateCommandEncoder();
            encoder.CopyBufferToBuffer(source, 0, readback, 0, 1024);
            encoder.CopyBufferToBuffer(untouched, 0, readback, 1024, 1024);
            gpu.Queue.Submit(encoder.Finish());
            readback.MapSync(MapMode.Read);
            uint[] read = readback.GetConstMappedRange<uint[], uint>(0, 512, span => span.ToArray());
            readback.Unmap();
            foreach (GpuBuffer buffer in new[] { source, untouched, readback })
                buffer.Destroy();
            Assert.Equal(NoError, gpu.Device.PopErrorScopeSync());
            values[4] = 99;
            values[5] = 98;
            Assert.Equal(values, read[..256]);
            Assert.All(read[256..], value => Assert.Equal(0u, value));
        }
    }

    [Fact]
    public void A_buffer_mapped_at_creation_holds_what_was_written_once_unmapped()
    {
        GpuDevice gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            gpu.Device.PushErrorScope(ErrorFilter.Validation);
            GpuBuffer vertices = gpu.Device.CreateBuffer(new BufferDescriptor { Size = 64, Usage = BufferUsage.Vertex | BufferUsage.CopySrc, MappedAtCreation = true });
            Span<float> mapped = vertices.GetMappedRange<float>(0, 16);
            for (int i = 0; i < 16; i++)
                mapped[i] = i * 0.25f;
            vertices.Unmap();
            GpuBuffer readback = gpu.Device.CreateBuffer(new BufferDescriptor { Size = 64, Usage = BufferUsage.CopyDst | BufferUsage.MapRead });
            CommandEncoder encoder = gpu.Device.CreateCommandEncoder();
            encoder.CopyBufferToBuffer(vertices, 0, readback, 0, 64);
            gpu.Queue.Submit(encoder.Finish());
            // Mapped without waiting: called back from a later poll.
            MapAsyncStatus? status = null;
            readback.Map(MapMode.Read, (result, _) => status = result);
            for (int i = 0; i < 1000 && status is null; i++)
                gpu.Device.Poll(wait: true);
            Assert.Equal(MapAsyncStatus.Success, status);
            float[] read = readback.GetConstMappedRange<float[], float>(0, 16, span => span.ToArray());
            readback.Unmap();
            vertices.Destroy();
            readback.Destroy();
            Assert.Equal(NoError, gpu.Device.PopErrorScopeSync());
            Assert.Equal(Enumerable.Range(0, 16).Select(i => i * 0.25f), read);
        }
    }

    [Fact]
    public void Texels_written_with_any_row_pitch_read_back_from_a_mip_and_layer()
    {
        GpuDevice gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            gpu.Device.PushErrorScope(ErrorFilter.Validation);
            Texture texture = gpu.Device.CreateTexture(new TextureDescriptor
            {
                Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst | TextureUsage.CopySrc,
                Size = new Extent3D(20, 12, 3),
                Format = TextureFormat.RGBA8Unorm,
                MipLevelCount = 2,
            });
            // Mip 1 of layer 2 is 10×6; its rows given 44 bytes apart, an offset of 12 before them.
            const uint Width = 10, Height = 6, Pitch = 44, Offset = 12;
            byte[] texels = new byte[Offset + Pitch * Height];
            for (uint y = 0; y < Height; y++)
                for (uint x = 0; x < Width * 4; x++)
                    texels[Offset + y * Pitch + x] = (byte)(y * 40 + x);
            gpu.Queue.WriteTexture(new TexelCopyTextureInfo { Texture = texture, MipLevel = 1, Origin = new Origin3D(0, 0, 2) }, texels,
                new TexelCopyBufferLayout { Offset = Offset, BytesPerRow = Pitch, RowsPerImage = Height }, new Extent3D(Width, Height, 1));
            GpuBuffer readback = gpu.Device.CreateBuffer(new BufferDescriptor { Size = 256 * Height, Usage = BufferUsage.CopyDst | BufferUsage.MapRead });
            CommandEncoder encoder = gpu.Device.CreateCommandEncoder();
            encoder.CopyTextureToBuffer(new TexelCopyTextureInfo { Texture = texture, MipLevel = 1, Origin = new Origin3D(0, 0, 2) },
                new TexelCopyBufferInfo { Buffer = readback, Layout = new TexelCopyBufferLayout { BytesPerRow = 256, RowsPerImage = Height } }, new Extent3D(Width, Height, 1));
            gpu.Queue.Submit(encoder.Finish());
            readback.MapSync(MapMode.Read);
            byte[] read = readback.GetConstMappedRange<byte[], byte>(0, 256 * Height, span => span.ToArray());
            readback.Unmap();
            readback.Destroy();
            texture.Destroy();
            Assert.Equal(NoError, gpu.Device.PopErrorScopeSync());
            for (uint y = 0; y < Height; y++)
                Assert.Equal(texels.AsSpan((int)(Offset + y * Pitch), (int)Width * 4).ToArray(), read.AsSpan((int)(y * 256), (int)Width * 4).ToArray());
        }
    }

    [Fact]
    public void Block_compressed_texels_round_trip()
    {
        GpuDevice gpu = TestGpu.Device();
        if (!gpu.CompressedTextures)
            Assert.Skip("The GPU samples no BC textures.");
        lock (gpu.Gate)
        {
            gpu.Device.PushErrorScope(ErrorFilter.Validation);
            Texture texture = gpu.Device.CreateTexture(new TextureDescriptor
            {
                Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst | TextureUsage.CopySrc,
                Size = new Extent3D(16, 8, 1),
                Format = TextureFormat.BC1RGBAUnorm,
            });
            // 4×2 blocks of 8 bytes, rows tightly packed.
            byte[] blocks = [.. Enumerable.Range(0, 64).Select(i => (byte)(i * 3))];
            gpu.Queue.WriteTexture(new TexelCopyTextureInfo { Texture = texture }, blocks, new TexelCopyBufferLayout { BytesPerRow = 32, RowsPerImage = 2 }, new Extent3D(16, 8, 1));
            GpuBuffer readback = gpu.Device.CreateBuffer(new BufferDescriptor { Size = 512, Usage = BufferUsage.CopyDst | BufferUsage.MapRead });
            CommandEncoder encoder = gpu.Device.CreateCommandEncoder();
            encoder.CopyTextureToBuffer(new TexelCopyTextureInfo { Texture = texture },
                new TexelCopyBufferInfo { Buffer = readback, Layout = new TexelCopyBufferLayout { BytesPerRow = 256, RowsPerImage = 2 } }, new Extent3D(16, 8, 1));
            gpu.Queue.Submit(encoder.Finish());
            readback.MapSync(MapMode.Read);
            byte[] read = readback.GetConstMappedRange<byte[], byte>(0, 512, span => span.ToArray());
            readback.Unmap();
            readback.Destroy();
            texture.Destroy();
            Assert.Equal(NoError, gpu.Device.PopErrorScopeSync());
            Assert.Equal(blocks[..32], read[..32]);
            Assert.Equal(blocks[32..], read[256..288]);
        }
    }

    [Fact]
    public void Depth_cleared_in_a_pass_reads_back()
    {
        GpuDevice gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            gpu.Device.PushErrorScope(ErrorFilter.Validation);
            Texture depth = gpu.Device.CreateTexture(new TextureDescriptor
            {
                Usage = TextureUsage.RenderAttachment | TextureUsage.TextureBinding | TextureUsage.CopySrc,
                Size = new Extent3D(16, 16, 1),
                Format = TextureFormat.Depth32Float,
            });
            TextureView view = depth.CreateView();
            CommandEncoder encoder = gpu.Device.CreateCommandEncoder();
            encoder.BeginRenderPass(new RenderPassDescriptor
            {
                ColorAttachments = [],
                DepthStencilAttachment = new RenderPassDepthStencilAttachment { View = view, DepthLoadOp = LoadOp.Clear, DepthStoreOp = StoreOp.Store, DepthClearValue = 0.75f },
            }).End();
            GpuBuffer readback = gpu.Device.CreateBuffer(new BufferDescriptor { Size = 256 * 16, Usage = BufferUsage.CopyDst | BufferUsage.MapRead });
            encoder.CopyTextureToBuffer(new TexelCopyTextureInfo { Texture = depth, Aspect = TextureAspect.DepthOnly },
                new TexelCopyBufferInfo { Buffer = readback, Layout = new TexelCopyBufferLayout { BytesPerRow = 256, RowsPerImage = 16 } }, new Extent3D(16, 16, 1));
            gpu.Queue.Submit(encoder.Finish());
            readback.MapSync(MapMode.Read);
            float[] read = readback.GetConstMappedRange<float[], float>(0, 64 * 16, span => span.ToArray());
            readback.Unmap();
            readback.Destroy();
            view.Dispose();
            depth.Destroy();
            Assert.Equal(NoError, gpu.Device.PopErrorScopeSync());
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                    Assert.Equal(0.75f, read[y * 64 + x]);
        }
    }

    [Fact]
    public void What_is_let_go_of_while_recorded_is_freed_once_the_GPU_is_done()
    {
        GpuDevice gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            gpu.Device.Poll(wait: true);
            int before = gpu.Instance.LiveObjects("textures");
            Texture target = Target(gpu, 32, 32, TextureFormat.RGBA8Unorm);
            TextureView view = target.CreateView();
            CommandEncoder encoder = gpu.Device.CreateCommandEncoder();
            encoder.BeginRenderPass(new RenderPassDescriptor
            {
                ColorAttachments = [new RenderPassColorAttachment { View = view, LoadOp = LoadOp.Clear, StoreOp = StoreOp.Store, ClearValue = new Color(0, 1, 0, 1) }],
            }).End();
            // Let go of before the commands that draw into it are even submitted.
            view.Dispose();
            target.Dispose();
            gpu.Device.Poll();
            gpu.Queue.Submit(encoder.Finish());
            Assert.True(gpu.Device.Poll(wait: true));
            Assert.Equal(before, gpu.Instance.LiveObjects("textures"));
        }
    }

    [Fact]
    public void Every_listed_shader_module_is_made()
    {
        GpuDevice gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            foreach (string name in ViewportRenderer.ModuleNames)
            {
                gpu.Device.PushErrorScope(ErrorFilter.Validation);
                gpu.Device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor { Label = name, Code = ViewportRenderer.Module(name) }).Dispose();
                Assert.True(gpu.Device.PopErrorScopeSync() == NoError, name);
            }
        }
    }

    [Fact]
    public void Bind_groups_of_every_kind_of_binding_and_their_pipelines_are_made()
    {
        GpuDevice gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            gpu.Device.PushErrorScope(ErrorFilter.Validation);
            const ShaderStage All = ShaderStage.Vertex | ShaderStage.Fragment | ShaderStage.Compute;
            BindGroupLayout layout = gpu.Device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
            {
                Entries =
                [
                    new BindGroupLayoutEntry { Binding = 0, Visibility = All, Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform } },
                    new BindGroupLayoutEntry { Binding = 1, Visibility = All, Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, HasDynamicOffset = true, MinBindingSize = 256 } },
                    new BindGroupLayoutEntry { Binding = 2, Visibility = ShaderStage.Compute, Buffer = new BufferBindingLayout { Type = BufferBindingType.ReadOnlyStorage } },
                    new BindGroupLayoutEntry { Binding = 3, Visibility = ShaderStage.Compute, Buffer = new BufferBindingLayout { Type = BufferBindingType.Storage } },
                    new BindGroupLayoutEntry { Binding = 4, Visibility = All, Texture = new TextureBindingLayout() },
                    new BindGroupLayoutEntry { Binding = 5, Visibility = All, Texture = new TextureBindingLayout { SampleType = TextureSampleType.Depth } },
                    new BindGroupLayoutEntry { Binding = 6, Visibility = ShaderStage.Compute, StorageTexture = new StorageTextureBindingLayout { Format = TextureFormat.RGBA16Float } },
                    new BindGroupLayoutEntry { Binding = 7, Visibility = All, Sampler = new SamplerBindingLayout() },
                    new BindGroupLayoutEntry { Binding = 9, Visibility = All, Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Comparison } },
                    new BindGroupLayoutEntry { Binding = 10, Visibility = All, Texture = new TextureBindingLayout { ViewDimension = TextureViewDimension.Cube } },
                ],
            });
            GpuBuffer uniforms = gpu.Device.CreateBuffer(new BufferDescriptor { Size = 1024, Usage = BufferUsage.Uniform });
            GpuBuffer storage = gpu.Device.CreateBuffer(new BufferDescriptor { Size = 1024, Usage = BufferUsage.Storage });
            Texture colour = gpu.Device.CreateTexture(new TextureDescriptor
            {
                Usage = TextureUsage.TextureBinding | TextureUsage.StorageBinding,
                Size = new Extent3D(8, 8, 1),
                Format = TextureFormat.RGBA16Float,
            });
            Texture depth = gpu.Device.CreateTexture(new TextureDescriptor
            {
                Usage = TextureUsage.TextureBinding | TextureUsage.RenderAttachment,
                Size = new Extent3D(8, 8, 1),
                Format = TextureFormat.Depth32Float,
            });
            Texture cube = gpu.Device.CreateTexture(new TextureDescriptor { Usage = TextureUsage.TextureBinding, Size = new Extent3D(4, 4, 6), Format = TextureFormat.RGBA8Unorm });
            Sampler filtering = gpu.Device.CreateSampler(new SamplerDescriptor { MagFilter = FilterMode.Linear, MinFilter = FilterMode.Linear, AddressModeU = AddressMode.Repeat });
            Sampler comparison = gpu.Device.CreateSampler(new SamplerDescriptor { Compare = CompareFunction.Less });
            TextureView colourView = colour.CreateView(), depthView = depth.CreateView();
            TextureView cubeView = cube.CreateView(new TextureViewDescriptor { Dimension = TextureViewDimension.Cube });
            BindGroup group = gpu.Device.CreateBindGroup(new BindGroupDescriptor
            {
                Layout = layout,
                Entries =
                [
                    new BindGroupEntry { Binding = 0, Buffer = uniforms, Size = 256 },
                    new BindGroupEntry { Binding = 1, Buffer = uniforms, Size = 256 },
                    new BindGroupEntry { Binding = 2, Buffer = storage, Offset = 256, Size = 512 },
                    new BindGroupEntry { Binding = 3, Buffer = storage },
                    new BindGroupEntry { Binding = 4, TextureView = colourView },
                    new BindGroupEntry { Binding = 5, TextureView = depthView },
                    new BindGroupEntry { Binding = 6, TextureView = colourView },
                    new BindGroupEntry { Binding = 7, Sampler = filtering },
                    new BindGroupEntry { Binding = 9, Sampler = comparison },
                    new BindGroupEntry { Binding = 10, TextureView = cubeView },
                ],
            });
            PipelineLayout pipelineLayout = gpu.Device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [layout, layout] });
            // A real one too: the window's present pipeline.
            BindGroupLayout presentLayout = gpu.Device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
            {
                Entries =
                [
                    new BindGroupLayoutEntry { Binding = 0, Visibility = ShaderStage.Fragment, Texture = new TextureBindingLayout() },
                    new BindGroupLayoutEntry { Binding = 1, Visibility = ShaderStage.Fragment, Texture = new TextureBindingLayout() },
                ],
            });
            ShaderModule present = gpu.Device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor { Label = "post/present", Code = ViewportRenderer.Module("post/present") });
            RenderPipeline pipeline = gpu.Device.CreateRenderPipelineSync(new RenderPipelineDescriptor
            {
                Layout = gpu.Device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [presentLayout] }),
                Vertex = new VertexState { Module = present, EntryPoint = "vs_present" },
                Fragment = new FragmentState { Module = present, EntryPoint = "fs_present", Targets = [new ColorTargetState { Format = TextureFormat.BGRA8UnormSrgb }] },
            });
            BindGroup presented = gpu.Device.CreateBindGroup(new BindGroupDescriptor
            {
                Layout = pipeline.GetBindGroupLayout(0),
                Entries = [new BindGroupEntry { Binding = 0, TextureView = colourView }, new BindGroupEntry { Binding = 1, TextureView = colourView }],
            });
            Assert.Equal(NoError, gpu.Device.PopErrorScopeSync());
            foreach (GpuObject made in new GpuObject[] { presented, pipeline, present, presentLayout, pipelineLayout, group, cubeView, depthView, colourView, cube, depth, colour, storage, uniforms, layout })
                made.Dispose();
            Assert.True(gpu.Device.Poll(wait: true));
        }
    }

    [Fact]
    public void A_window_swap_chain_presents_and_resizes()
    {
        GpuDevice gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            const uint Popup = 0x80000000, NoActivate = 0x08000000, ToolWindow = 0x00000080;
            nint module = GetModuleHandle(null);
            nint window = CreateWindowEx(NoActivate | ToolWindow, "STATIC", "swap chain", Popup, -4000, -4000, 320, 240, 0, 0, module, 0);
            try
            {
                gpu.Device.PushErrorScope(ErrorFilter.Validation);
                Surface surface = gpu.Instance.CreateSurface(window, module)!;
                SurfaceCapabilities capabilities = surface.GetCapabilities(gpu.Adapter)!;
                Assert.Contains(TextureFormat.BGRA8Unorm, capabilities.Formats);
                foreach ((uint width, uint height) in new[] { (320u, 240u), (200u, 120u), (640u, 360u) })
                {
                    surface.Configure(new SurfaceConfiguration
                    {
                        Device = gpu.Device,
                        Format = TextureFormat.BGRA8Unorm,
                        ViewFormats = [TextureFormat.BGRA8UnormSrgb],
                        Width = width,
                        Height = height,
                        PresentMode = PresentMode.Fifo,
                    });
                    for (int frame = 0; frame < 4; frame++)
                    {
                        SurfaceTexture current = surface.GetCurrentTexture();
                        Assert.Equal(SurfaceGetCurrentTextureStatus.SuccessOptimal, current.Status);
                        Texture texture = current.Texture!;
                        Assert.Equal((width, height), (texture.GetWidth(), texture.GetHeight()));
                        TextureView view = texture.CreateView(new TextureViewDescriptor { Format = TextureFormat.BGRA8UnormSrgb });
                        CommandEncoder encoder = gpu.Device.CreateCommandEncoder();
                        encoder.BeginRenderPass(new RenderPassDescriptor
                        {
                            ColorAttachments = [new RenderPassColorAttachment { View = view, LoadOp = LoadOp.Clear, StoreOp = StoreOp.Store, ClearValue = new Color(0.2, 0.4, 0.8, 1) }],
                        }).End();
                        gpu.Queue.Submit(encoder.Finish());
                        Assert.True(surface.Present());
                        view.Dispose();
                        texture.Dispose();
                    }
                }
                surface.Unconfigure();
                surface.Dispose();
                Assert.Equal(NoError, gpu.Device.PopErrorScopeSync());
                Assert.True(gpu.Device.Poll(wait: true));
            }
            finally
            {
                DestroyWindow(window);
            }
        }
    }

    private static readonly (ErrorType, string) NoError = (ErrorType.NoError, "");

    private static Texture Target(GpuDevice gpu, uint width, uint height, TextureFormat format) => gpu.Device.CreateTexture(new TextureDescriptor
    {
        Usage = TextureUsage.RenderAttachment | TextureUsage.CopySrc,
        Size = new Extent3D(width, height, 1),
        Format = format,
    });

    private static byte[] Read(GpuDevice gpu, CommandEncoder encoder, Texture texture, uint width, uint height, uint texelBytes)
    {
        uint pitch = (width * texelBytes + 255) / 256 * 256;
        GpuBuffer readback = gpu.Device.CreateBuffer(new BufferDescriptor { Size = pitch * height, Usage = BufferUsage.CopyDst | BufferUsage.MapRead });
        encoder.CopyTextureToBuffer(new TexelCopyTextureInfo { Texture = texture },
            new TexelCopyBufferInfo { Buffer = readback, Layout = new TexelCopyBufferLayout { BytesPerRow = pitch, RowsPerImage = height } }, new Extent3D(width, height, 1));
        gpu.Queue.Submit(encoder.Finish());
        readback.MapSync(MapMode.Read);
        byte[] rows = readback.GetConstMappedRange<byte[], byte>(0, pitch * height, span => span.ToArray());
        readback.Unmap();
        readback.Destroy();
        byte[] texels = new byte[width * height * texelBytes];
        for (uint y = 0; y < height; y++)
            rows.AsSpan((int)(y * pitch), (int)(width * texelBytes)).CopyTo(texels.AsSpan((int)(y * width * texelBytes)));
        return texels;
    }

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowEx(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint window);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? module);
}
