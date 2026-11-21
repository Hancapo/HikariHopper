using System.Numerics;
using System.Runtime.InteropServices;
using HikariHopper.Rendering;
using WebGpuSharp;
using Buffer = WebGpuSharp.Buffer;

namespace HikariHopper.Tests;

// The combine's pixels (Rdr2/subsample_alpha.wgsl) from fixed surfaces: drawn into an HDR target and its depth as the HDR
// pass draws them, the cut-outs kept and marked by Rdr2/cutout.wgsl's own checkerboard and coverage, then put together
// as ViewportRenderer.CombineSubSampledAlpha does (a copy of the target read, the target loaded and drawn over).
public sealed partial class Rdr2CutoutTests
{
    private const int CombineSize = 32;
    private const TextureFormat Hdr = TextureFormat.RGBA16Float;
    private const TextureFormat Depth = TextureFormat.Depth32Float;

    // A rectangle of one colour at one depth, solid or a standard (not a tree's) sub-sampled cut-out of this coverage.
    private readonly record struct Surface(Vector3 Colour, float Depth, float Coverage = 1, bool Cutout = false,
        int X0 = 0, int Y0 = 0, int X1 = CombineSize, int Y1 = CombineSize);

    private static string TestShader(string name)
    {
        using Stream stream = typeof(Rdr2CutoutTests).Assembly.GetManifestResourceStream("HikariHopper.Tests.Shaders." + name)
            ?? throw new FileNotFoundException(name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // The target's pixels once the surfaces are drawn, in order, over the backdrop, and put together, with the GPU error,
    // if any.
    private static Vector4[] Combine(Vector4 backdrop, Surface[] surfaces, out string? error)
    {
        GpuDevice gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            Device device = gpu.Device;
            device.PushErrorScope(ErrorFilter.Validation);
            Texture Target(TextureFormat format, TextureUsage usage) => device.CreateTexture(new TextureDescriptor
            {
                Usage = usage,
                Dimension = TextureDimension.D2,
                Size = new Extent3D(CombineSize, CombineSize, 1),
                Format = format,
                MipLevelCount = 1,
                SampleCount = 1,
            });
            Texture hdr = Target(Hdr, TextureUsage.RenderAttachment | TextureUsage.CopySrc);
            Texture copy = Target(Hdr, TextureUsage.TextureBinding | TextureUsage.CopyDst);
            Texture depth = Target(Depth, TextureUsage.RenderAttachment | TextureUsage.TextureBinding);
            TextureView hdrView = hdr.CreateView(), depthView = depth.CreateView();

            // The surfaces, depth tested as the HDR pass's (reversed Z, cleared to nought).
            BindGroupLayout probeLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
            {
                Entries =
                [
                    new BindGroupLayoutEntry
                    {
                        Binding = 0,
                        Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
                        Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = 48 },
                    },
                ],
            });
            ShaderModule probe = device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor
            {
                Code = ViewportRenderer.Read("Rdr2/cutout.wgsl") + "\n" + TestShader("rdr2_cutout_probe.wgsl"),
            });
            RenderPipeline probePipeline = device.CreateRenderPipelineSync(new RenderPipelineDescriptor
            {
                Layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [probeLayout] }),
                Vertex = new VertexState { Module = probe, EntryPoint = "vs_probe" },
                Fragment = new FragmentState
                {
                    Module = probe,
                    EntryPoint = "fs_probe",
                    Targets = [new ColorTargetState { Format = Hdr, WriteMask = ColorWriteMask.All }],
                },
                DepthStencil = new DepthStencilState { Format = Depth, DepthWriteEnabled = OptionalBool.True, DepthCompare = CompareFunction.GreaterEqual },
            });
            var buffers = new List<Buffer>();
            CommandEncoder encoder = device.CreateCommandEncoder();
            RenderPassEncoder pass = encoder.BeginRenderPass(new RenderPassDescriptor
            {
                ColorAttachments =
                [
                    new RenderPassColorAttachment
                    {
                        View = hdrView,
                        LoadOp = LoadOp.Clear,
                        StoreOp = StoreOp.Store,
                        ClearValue = new Color(backdrop.X, backdrop.Y, backdrop.Z, backdrop.W),
                    },
                ],
                DepthStencilAttachment = new RenderPassDepthStencilAttachment
                {
                    View = depthView,
                    DepthLoadOp = LoadOp.Clear,
                    DepthStoreOp = StoreOp.Store,
                    DepthClearValue = 0,
                },
            });
            pass.SetPipeline(probePipeline);
            foreach (Surface surface in surfaces)
            {
                Buffer uniforms = device.CreateBuffer(new BufferDescriptor { Size = 48, Usage = BufferUsage.Uniform | BufferUsage.CopyDst });
                buffers.Add(uniforms);
                float[] floats = [surface.Colour.X, surface.Colour.Y, surface.Colour.Z, 1, surface.X0, surface.Y0, surface.X1, surface.Y1,
                    surface.Depth, surface.Coverage, 0, 0];
                uint[] words = MemoryMarshal.Cast<float, uint>(floats).ToArray();
                words[10] = Rdr2Material.SubSampledFlag;
                words[11] = surface.Cutout ? 1u : 0u;
                gpu.Queue.WriteBuffer(uniforms, 0, (ReadOnlySpan<uint>)words);
                pass.SetBindGroup(0, device.CreateBindGroup(new BindGroupDescriptor
                {
                    Layout = probeLayout,
                    Entries = [new BindGroupEntry { Binding = 0, Buffer = uniforms, Size = 48 }],
                }));
                pass.Draw(3);
            }
            pass.End();

            // The combine, bound as the renderer binds it.
            BindGroupLayout combineLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor
            {
                Entries =
                [
                    new BindGroupLayoutEntry
                    {
                        Binding = 0,
                        Visibility = ShaderStage.Fragment,
                        Texture = new TextureBindingLayout { SampleType = TextureSampleType.UnfilterableFloat, ViewDimension = TextureViewDimension.D2 },
                    },
                    new BindGroupLayoutEntry
                    {
                        Binding = 1,
                        Visibility = ShaderStage.Fragment,
                        Texture = new TextureBindingLayout { SampleType = TextureSampleType.Depth, ViewDimension = TextureViewDimension.D2 },
                    },
                ],
            });
            ShaderModule combine = device.CreateShaderModuleWGSL(new ShaderModuleWGSLDescriptor { Code = ViewportRenderer.Read("Rdr2/subsample_alpha.wgsl") });
            RenderPipeline combinePipeline = device.CreateRenderPipelineSync(new RenderPipelineDescriptor
            {
                Layout = device.CreatePipelineLayout(new PipelineLayoutDescriptor { BindGroupLayouts = [combineLayout] }),
                Vertex = new VertexState { Module = combine, EntryPoint = "vs_sub_sample_alpha" },
                Fragment = new FragmentState
                {
                    Module = combine,
                    EntryPoint = "fs_sub_sample_alpha",
                    Targets = [new ColorTargetState { Format = Hdr, WriteMask = ColorWriteMask.All }],
                },
            });
            encoder.CopyTextureToTexture(new TexelCopyTextureInfo { Texture = hdr }, new TexelCopyTextureInfo { Texture = copy },
                new Extent3D(CombineSize, CombineSize, 1));
            RenderPassEncoder combinePass = encoder.BeginRenderPass(new RenderPassDescriptor
            {
                ColorAttachments = [new RenderPassColorAttachment { View = hdrView, LoadOp = LoadOp.Load, StoreOp = StoreOp.Store }],
            });
            combinePass.SetPipeline(combinePipeline);
            combinePass.SetBindGroup(0, device.CreateBindGroup(new BindGroupDescriptor
            {
                Layout = combineLayout,
                Entries =
                [
                    new BindGroupEntry { Binding = 0, TextureView = copy.CreateView() },
                    new BindGroupEntry { Binding = 1, TextureView = depthView },
                ],
            }));
            combinePass.Draw(3);
            combinePass.End();

            // Eight bytes a pixel: a row of 32 is 256 bytes, as a copy's rows must be.
            const int Bytes = CombineSize * CombineSize * 8;
            Buffer readback = device.CreateBuffer(new BufferDescriptor { Size = Bytes, Usage = BufferUsage.CopyDst | BufferUsage.MapRead });
            encoder.CopyTextureToBuffer(new TexelCopyTextureInfo { Texture = hdr },
                new TexelCopyBufferInfo { Buffer = readback, Layout = new TexelCopyBufferLayout { BytesPerRow = CombineSize * 8, RowsPerImage = CombineSize } },
                new Extent3D(CombineSize, CombineSize, 1));
            gpu.Queue.Submit(encoder.Finish());
            readback.MapSync(MapMode.Read);
            Half[] halves = readback.GetConstMappedRange<Half[], Half>(0, Bytes / 2, span => span.ToArray());
            readback.Unmap();
            readback.Destroy();
            var (type, message) = device.PopErrorScopeSync(5_000_000_000);
            error = type == ErrorType.NoError ? null : $"{type}: {message}";
            foreach (Buffer buffer in buffers)
                buffer.Destroy();
            hdr.Destroy();
            copy.Destroy();
            depth.Destroy();
            var pixels = new Vector4[CombineSize * CombineSize];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Vector4((float)halves[i * 4], (float)halves[i * 4 + 1], (float)halves[i * 4 + 2], (float)halves[i * 4 + 3]);
            return pixels;
        }
    }

    // What the HDR target stores of a colour.
    private static Vector3 Stored(Vector3 colour) => new((float)(Half)colour.X, (float)(Half)colour.Y, (float)(Half)colour.Z);

    // The game's blend of a cut-out over what's behind it, by its coverage (Rdr2/subsample_alpha.wgsl's): over a solid
    // surface, the colours over their luminance mixed straight and the luminance's logarithm.
    private static Vector4 OverSolid(Vector3 behind, Vector3 front, float coverage)
    {
        static float Luminance(Vector3 colour) => MathF.Max(Vector3.Dot(colour, new Vector3(.3f, .59f, .11f)), .0001f);
        float behindLuminance = Luminance(behind), frontLuminance = Luminance(front);
        Vector3 chroma = Vector3.Lerp(behind / behindLuminance, front / frontLuminance, coverage);
        float mixed = MathF.Pow(behindLuminance, 1 - coverage) * MathF.Pow(frontLuminance, coverage);
        return new Vector4(chroma * mixed, 1);
    }

    // Every pixel against what's expected of it, within the HDR target's half floats (a 2048th at one) and the shader's
    // f32 logarithms.
    private static void AssertPixels(Vector4[] pixels, Func<int, int, Vector4> expected)
    {
        const float Tolerance = .002f;
        for (int y = 0; y < CombineSize; y++)
            for (int x = 0; x < CombineSize; x++)
            {
                Vector4 actual = pixels[y * CombineSize + x], wanted = expected(x, y);
                Vector4 off = Vector4.Abs(actual - wanted);
                Assert.True(MathF.Max(MathF.Max(off.X, off.Y), MathF.Max(off.Z, off.W)) <= Tolerance,
                    $"pixel ({x}, {y}) is {actual}, not {wanted}");
            }
    }

    [Fact]
    public void A_part_covering_cut_out_over_the_empty_backdrop_is_mixed_with_it_straight_on_every_pixel()
    {
        // The backdrop: nothing drawn, its alpha nought, its colour not black so the mix shows.
        var backdrop = new Vector4(.1f, .2f, .3f, 0);
        var leaf = new Vector3(.8f, .4f, .2f);
        // A quarter, so a blend weighted the wrong way round (by three quarters) shows.
        Vector4[] pixels = Combine(backdrop, [new Surface(leaf, .5f, .25f, Cutout: true)], out string? error);
        Assert.Null(error);
        // Both halves of the checkerboard alike: the kept with the dropped to their right, the dropped with the kept.
        Vector3 colour = Vector3.Lerp(Stored(new Vector3(backdrop.X, backdrop.Y, backdrop.Z)), Stored(leaf), .25f);
        AssertPixels(pixels, (_, _) => new Vector4(colour, .25f));
    }

    [Fact]
    public void A_part_covering_cut_out_over_a_solid_surface_is_blended_by_luminance_on_every_pixel()
    {
        var backdrop = new Vector4(0, 0, 0, 0);
        // Dark behind, bright in front: the logarithm's blend is far from a straight one.
        var wall = new Vector3(.05f, .1f, .4f);
        var leaf = new Vector3(1f, .8f, .2f);
        Vector4[] pixels = Combine(backdrop, [new Surface(wall, .25f), new Surface(leaf, .5f, .25f, Cutout: true)], out string? error);
        Assert.Null(error);
        Vector4 blended = OverSolid(Stored(wall), Stored(leaf), .25f);
        Vector4 straight = new(Vector3.Lerp(wall, leaf, .25f), 1);
        Assert.True(Vector4.Distance(blended, straight) > .05f, $"{blended} too near {straight} to tell them apart");
        AssertPixels(pixels, (_, _) => blended);
    }

    [Fact]
    public void A_surface_nearer_than_a_cut_out_is_left_as_it_is_and_does_not_bleed_into_it()
    {
        var backdrop = new Vector4(0, 0, 0, 0);
        var wall = new Vector3(.05f, .1f, .4f);
        var leaf = new Vector3(1f, .8f, .2f);
        // In front of the cut-out over the right half: a post, whose pixels border the cut-out's kept and dropped ones.
        var post = new Vector3(.9f, .1f, .6f);
        const int Edge = CombineSize / 2;
        Vector4[] pixels = Combine(backdrop,
            [new Surface(wall, .25f), new Surface(leaf, .5f, .25f, Cutout: true), new Surface(post, .75f, X0: Edge)], out string? error);
        Assert.Null(error);
        // Left of the post, the cut-out blended with the wall behind it only, up to the post's edge; the post whole.
        Vector4 blended = OverSolid(Stored(wall), Stored(leaf), .25f);
        AssertPixels(pixels, (x, _) => x < Edge ? blended : new Vector4(Stored(post), 1));
    }
}
