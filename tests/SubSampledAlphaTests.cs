using System.Numerics;
using CommonFury.Vertices;
using FiveFury;
using FiveFury.Drawables;
using FiveFury.Ydr;
using HikariHopper.Rendering;
using HikariHopper.Tests.Textures;

namespace HikariHopper.Tests;

// GTA V's sub-sampled alpha: its cut-outs' references and HardAlphaBlend, and the combine that blends the checkerboard
// they're drawn on.
public sealed class SubSampledAlphaTests
{
    // A cut-out (the cut-out bucket) two metres square, standing up and facing -Y, its middle at (0, 0, 1), its diffuse
    // map one grey of this alpha.
    private static byte[] CutOut(string shader, byte alpha, Action<DrawableMaterialInput>? setup = null)
    {
        Vector3[] positions = [new(-1, 0, 0), new(1, 0, 0), new(1, 0, 2), new(-1, 0, 2)];
        var mesh = new DrawableMeshInput(positions, [0, 1, 2, 0, 2, 3], "fence");
        mesh.SetChannel(VertexSemantic.Normal, [.. Enumerable.Repeat(-Vector4.UnitY, 4)]);
        mesh.SetChannel(VertexSemantic.TexCoord0, [new(0, 1, 0, 0), new(1, 1, 0, 0), new(1, 0, 0, 0), new(0, 0, 0, 0)]);
        mesh.SetChannel(VertexSemantic.Color0, [.. Enumerable.Repeat(Vector4.One, 4)]);
        var builder = new DrawableBuilder { Name = "fence" };
        builder.AdditionalTextures.Add(TextureTestKit.Bgra("fence", 4, 4, [230, 230, 230, alpha]));
        var material = new DrawableMaterialInput { Name = "fence", Shader = shader, RenderBucket = 3 };
        material.Textures["DiffuseSampler"] = new FiveFury.Textures.TextureReference("fence");
        setup?.Invoke(material);
        builder.Materials.Add(material);
        var model = new DrawableModelInput();
        model.Meshes.Add(mesh);
        builder.Models.Add(model);
        return new Ydr(builder.Build(GameTarget.Legacy), GameTarget.Legacy).Write();
    }

    private static SurfaceMaterial Material(byte[] ydr) => ModelContent.Read("fence.ydr", ydr).Surfaces!.Materials.Single();

    [Fact]
    public void Cut_outs_take_the_game_s_alpha_references_and_their_own_hard_alpha_blend()
    {
        SurfaceMaterial fence = Material(CutOut("normal_spec", 255));
        Assert.Equal(AlphaMode.Mask, fence.AlphaMode);
        // global_alphaRef, as the game's code sets it, and HardAlphaBlend's default, whole.
        Assert.Equal(90 / 255f, fence.Uniforms[12], 6);
        Assert.Equal(1, fence.Uniforms[74]);
        // A material's own HardAlphaBlend.
        Assert.Equal(.25f, Material(CutOut("normal_spec", 255, m => m.Parameters["HardAlphaBlend"] = [.25f])).Uniforms[74]);
        // The vehicles' own reference, all but nought.
        Assert.Equal(1 / 255f, Material(CutOut("vehicle_mesh", 255)).Uniforms[12], 6);
    }

    // The view of the cut-out, without anti-aliasing, as the window's bytes, with the frames' GPU error, if any.
    private const int Size = 64;

    private static byte[] Render(byte[]? ydr, bool combine, out string? error)
    {
        GpuDevice gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            var renderer = new ViewportRenderer(gpu) { Antialiasing = false, Shadows = false, SubSampledCombine = combine };
            renderer.Resize(Size, Size);
            gpu.Device.PushErrorScope(WebGpuSharp.ErrorFilter.Validation);
            if (ydr is not null)
                renderer.Show(ModelContent.Read("fence.ydr", ydr).Surfaces!);
            else
                renderer.Show(new ViewportMesh(Vector3.Zero));
            renderer.Frame(new CommonFury.Numerics.Aabb3(new Vector3(-1, -1, 0), new Vector3(1, 1, 2)));
            WebGpuSharp.Texture target = gpu.Device.CreateTexture(new WebGpuSharp.TextureDescriptor
            {
                Usage = WebGpuSharp.TextureUsage.RenderAttachment | WebGpuSharp.TextureUsage.CopySrc,
                Dimension = WebGpuSharp.TextureDimension.D2,
                Size = new WebGpuSharp.Extent3D(Size, Size, 1),
                Format = renderer.PresentFormat,
                MipLevelCount = 1,
                SampleCount = 1,
            });
            WebGpuSharp.TextureView view = target.CreateView();
            for (int i = 0; i < 4; i++)
                renderer.RenderTo(view);
            WebGpuSharp.Buffer readback = gpu.Device.CreateBuffer(new WebGpuSharp.BufferDescriptor
            {
                Size = Size * Size * 4,
                Usage = WebGpuSharp.BufferUsage.CopyDst | WebGpuSharp.BufferUsage.MapRead,
            });
            WebGpuSharp.CommandEncoder encoder = gpu.Device.CreateCommandEncoder();
            encoder.CopyTextureToBuffer(new WebGpuSharp.TexelCopyTextureInfo { Texture = target },
                new WebGpuSharp.TexelCopyBufferInfo { Buffer = readback, Layout = new WebGpuSharp.TexelCopyBufferLayout { BytesPerRow = Size * 4, RowsPerImage = Size } },
                new WebGpuSharp.Extent3D(Size, Size, 1));
            gpu.Queue.Submit(encoder.Finish());
            readback.MapSync(WebGpuSharp.MapMode.Read);
            byte[] pixels = readback.GetConstMappedRange<byte[], byte>(0, Size * Size * 4, span => span.ToArray());
            readback.Unmap();
            readback.Destroy();
            var (type, message) = gpu.Device.PopErrorScopeSync(5_000_000_000);
            error = type == WebGpuSharp.ErrorType.NoError ? null : $"{type}: {message}";
            target.Destroy();
            renderer.Dispose();
            return pixels;
        }
    }

    // Over the middle of the view: the mean green, and how far a pixel's green is from the next one's on its row.
    private static (double Level, double Step) Middle(byte[] pixels)
    {
        double level = 0, step = 0;
        int count = 0;
        for (int y = Size / 2 - 8; y < Size / 2 + 8; y++)
            for (int x = Size / 2 - 8; x < Size / 2 + 8; x++)
            {
                int at = (y * Size + x) * 4 + 1;
                level += pixels[at];
                step += Math.Abs(pixels[at] - pixels[at + 4]);
                count++;
            }
        return (level / count, step / count);
    }

    [Fact]
    public void The_combine_blends_a_part_covering_cut_out_s_checkerboard_with_what_is_behind_it()
    {
        // Alpha 100 of 255: past the reference, under the hard ramp's top, so drawn on half the pixels.
        byte[] fence = CutOut("normal_spec", 100);
        (double Level, double Step) empty = Middle(Render(null, combine: true, out _));
        (double Level, double Step) dithered = Middle(Render(fence, combine: false, out string? error));
        Assert.Null(error);
        (double Level, double Step) blended = Middle(Render(fence, combine: true, out error));
        Assert.Null(error);
        // Left alone, a checkerboard of the grey and the backdrop; combined, the two blended evenly, between them.
        Assert.True(dithered.Step > 20, $"dithered {dithered}");
        Assert.True(blended.Step < 4, $"blended {blended}");
        Assert.True(blended.Level > empty.Level + 10, $"blended {blended}, empty {empty}");
    }

    // Upright quads facing -Y, each its own material and one-colour diffuse map: (shader, bucket, colour, y, x from, x to,
    // z from, z to).
    private readonly record struct Quad(string Shader, int Bucket, byte[] Bgra, float Y, float X0, float X1, float Z0, float Z1);

    private static byte[] Quads(params Quad[] quads)
    {
        var builder = new DrawableBuilder { Name = "scene" };
        var model = new DrawableModelInput();
        for (int i = 0; i < quads.Length; i++)
        {
            Quad quad = quads[i];
            string name = $"quad{i}";
            Vector3[] positions = [new(quad.X0, quad.Y, quad.Z0), new(quad.X1, quad.Y, quad.Z0), new(quad.X1, quad.Y, quad.Z1), new(quad.X0, quad.Y, quad.Z1)];
            var mesh = new DrawableMeshInput(positions, [0, 1, 2, 0, 2, 3], name);
            mesh.SetChannel(VertexSemantic.Normal, [.. Enumerable.Repeat(-Vector4.UnitY, 4)]);
            mesh.SetChannel(VertexSemantic.TexCoord0, [new(0, 1, 0, 0), new(1, 1, 0, 0), new(1, 0, 0, 0), new(0, 0, 0, 0)]);
            mesh.SetChannel(VertexSemantic.Color0, [.. Enumerable.Repeat(Vector4.One, 4)]);
            model.Meshes.Add(mesh);
            builder.AdditionalTextures.Add(TextureTestKit.Bgra(name, 4, 4, quad.Bgra));
            var material = new DrawableMaterialInput { Name = name, Shader = quad.Shader, RenderBucket = (byte)quad.Bucket };
            material.Textures["DiffuseSampler"] = new FiveFury.Textures.TextureReference(name);
            builder.Materials.Add(material);
        }
        builder.Models.Add(model);
        return new Ydr(builder.Build(GameTarget.Legacy), GameTarget.Legacy).Write();
    }

    // A dark blue wall a metre behind a grey cut-out of alpha 100 (sub-sampled at four sevenths) that fills the view,
    // with, if asked, a red strip of opaque surface a metre in front of it down the middle of the view, and a whole grey
    // cut-out over the fence's lower part, drawn on every pixel.
    private static byte[] FenceOverWall(bool nearStrip) => Quads(
    [
        new("normal_spec", 0, [90, 30, 20, 255], 1, -20, 20, -20, 20),
        new("normal_spec", 3, [230, 230, 230, 100], 0, -10, 10, -10, 10),
        .. (nearStrip
            ? new[] { new Quad("normal_spec", 0, [40, 40, 200, 255], -1, -.15f, .15f, -20, 20), new Quad("normal_spec", 3, [200, 200, 200, 255], -.01f, -10, 10, -10, .6f) }
            : Array.Empty<Quad>()),
    ]);

    // The HDR target as the frame's surfaces left it (the copy the combine reads), the sub-sampled alpha beside it, and
    // the HDR target once combined, after a frame.
    private sealed record Frame(int Width, int Height, Vector4[] Before, byte[] Alpha, Vector4[] After);

    private static Frame ReadFrame(GpuDevice gpu, ViewportRenderer renderer)
    {
        WebGpuSharp.Texture Field(string name) => (WebGpuSharp.Texture)typeof(ViewportRenderer)
            .GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(renderer)!;
        WebGpuSharp.Texture hdr = Field("hdr");
        int width = (int)hdr.GetWidth(), height = (int)hdr.GetHeight();
        Vector4[] Colours(byte[] bytes, int row) => [.. Enumerable.Range(0, width * height).Select(i =>
        {
            int at = i / width * row + i % width * 8;
            return new Vector4((float)BitConverter.ToHalf(bytes, at), (float)BitConverter.ToHalf(bytes, at + 2),
                (float)BitConverter.ToHalf(bytes, at + 4), (float)BitConverter.ToHalf(bytes, at + 6));
        })];
        (byte[] before, int colourRow) = Read(gpu, Field("waterBackground"), 8);
        (byte[] after, _) = Read(gpu, hdr, 8);
        (byte[] alpha, int alphaRow) = Read(gpu, Field("subSampled"), 1);
        return new Frame(width, height, Colours(before, colourRow), [.. Enumerable.Range(0, width * height).Select(i => alpha[i / width * alphaRow + i % width])],
            Colours(after, colourRow));
    }

    private static (byte[] Bytes, int Row) Read(GpuDevice gpu, WebGpuSharp.Texture texture, int texel)
    {
        uint width = texture.GetWidth(), height = texture.GetHeight(), row = (width * (uint)texel + 255) / 256 * 256;
        WebGpuSharp.Buffer readback = gpu.Device.CreateBuffer(new WebGpuSharp.BufferDescriptor
        {
            Size = row * height,
            Usage = WebGpuSharp.BufferUsage.CopyDst | WebGpuSharp.BufferUsage.MapRead,
        });
        WebGpuSharp.CommandEncoder encoder = gpu.Device.CreateCommandEncoder();
        encoder.CopyTextureToBuffer(new WebGpuSharp.TexelCopyTextureInfo { Texture = texture },
            new WebGpuSharp.TexelCopyBufferInfo { Buffer = readback, Layout = new WebGpuSharp.TexelCopyBufferLayout { BytesPerRow = row, RowsPerImage = height } },
            new WebGpuSharp.Extent3D(width, height, 1));
        gpu.Queue.Submit(encoder.Finish());
        readback.MapSync(WebGpuSharp.MapMode.Read);
        byte[] bytes = readback.GetConstMappedRange<byte[], byte>(0, (nuint)(row * height), span => span.ToArray());
        readback.Unmap();
        readback.Destroy();
        return (bytes, (int)row);
    }

    // The game's single-pass combine (PS_SubSampleAlphaSinglePass, as Enhanced's is compiled), over the HDR target's
    // premultiplied colour and coverage: what a texel becomes, or null where it's left alone, and whether both it and its
    // neighbour to the right were covered and blended by an alpha between nought and whole.
    private static (Vector4? Colour, bool Blended) Combined(Frame frame, int x, int y)
    {
        float Stored(int px) => (frame.Alpha[y * frame.Width + px] & 8) == 0 ? 0 : MathF.Max(.001f, (frame.Alpha[y * frame.Width + px] & 7) / 7f);
        int next = Math.Min(x + 1, frame.Width - 1);
        float own = Stored(x);
        bool opaque = ((x + .5f + y + .5f) * .5f) % 1 < .5f;
        float amount;
        if (opaque)
        {
            float beside = Stored(next);
            if (beside == 0 || (beside == 1 && own == 1))
                return (null, false);
            amount = beside;
        }
        else
        {
            if (own == 0 || own == 1)
                return (null, false);
            amount = own;
        }
        Vector4 here = frame.Before[y * frame.Width + x], there = frame.Before[y * frame.Width + next];
        Vector4 under = opaque ? here : there, over = opaque ? there : here;
        static Vector3 Unpremultiplied(Vector4 c) => c.W > 0 ? new Vector3(c.X, c.Y, c.Z) / MathF.Max(c.W, 1e-6f) : Vector3.Zero;
        Vector3 colour = Unpremultiplied(under.W <= 0 ? over : under);
        bool both = under.W > 0 && over.W > 0;
        if (both)
        {
            // Luminance blended in log, the colour over it linearly.
            var weights = new Vector3(.3f, .59f, .11f);
            Vector3 u = Unpremultiplied(under), o = Unpremultiplied(over);
            float underLevel = MathF.Max(Vector3.Dot(u, weights), .0001f), overLevel = MathF.Max(Vector3.Dot(o, weights), .0001f);
            Vector3 chroma = Vector3.Lerp(u / underLevel, o / overLevel, amount);
            float level = MathF.Pow(2, float.Lerp(MathF.Log2(underLevel * .4f), MathF.Log2(overLevel * .4f), amount)) * 2.5f;
            colour = level * chroma;
        }
        float coverage = float.Lerp(under.W, over.W, amount);
        return (new Vector4(colour * coverage, coverage), both && amount is > 0 and < 1);
    }

    private static bool Close(Vector4 expected, Vector4 actual)
    {
        // Half floats, and the GPU's own exp2 and log2: a hundredth of the brightest channel, and a little more.
        float scale = MathF.Max(MathF.Max(MathF.Abs(expected.X), MathF.Abs(expected.Y)), MathF.Max(MathF.Abs(expected.Z), MathF.Abs(expected.W)));
        Vector4 off = Vector4.Abs(expected - actual);
        return MathF.Max(MathF.Max(off.X, off.Y), MathF.Max(off.Z, off.W)) <= .01f * scale + 2e-3f;
    }

    // Each texel of the frame as the game's combine leaves it; how many were blended between two covered texels, and of
    // those how many with the red strip, as the texel behind (the strip's own, by the cut-out's alpha beside it) or as
    // the one in front (the cut-out's, over the strip beside it).
    private static (int Blended, int StripBehind, int StripInFront) AssertCombined(Frame frame)
    {
        int blended = 0, stripBehind = 0, stripInFront = 0;
        var wrong = new List<string>();
        static bool Red(Vector4 c) => c.X > 2 * c.Y && c.X > 2 * c.Z;
        for (int y = 0; y < frame.Height; y++)
            for (int x = 0; x < frame.Width; x++)
            {
                int at = y * frame.Width + x;
                (Vector4? expected, bool both) = Combined(frame, x, y);
                Vector4 actual = frame.After[at];
                if (expected is null)
                {
                    if (actual != frame.Before[at])
                        wrong.Add($"({x},{y}) left alone {frame.Before[at]}, got {actual}");
                    continue;
                }
                if (!Close(expected.Value, actual))
                    wrong.Add($"({x},{y}) expected {expected.Value}, got {actual}, from {frame.Before[at]} and {frame.Before[Math.Min(at + 1, at - x + frame.Width - 1)]}");
                if (!both)
                    continue;
                blended++;
                if (x + 1 < frame.Width)
                {
                    bool opaque = ((x + .5f + y + .5f) * .5f) % 1 < .5f;
                    if (Red(frame.Before[opaque ? at : at + 1]))
                    {
                        if (opaque)
                            stripBehind++;
                        else
                            stripInFront++;
                    }
                }
            }
        Assert.True(wrong.Count == 0, $"{wrong.Count} texels off: {string.Join("; ", wrong.Take(8))}");
        return (blended, stripBehind, stripInFront);
    }

    // The 3D viewer's frame of the scene, framed on the cut-out's middle metres.
    private static Frame ViewerFrame(byte[] ydr)
    {
        GpuDevice gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            var renderer = new ViewportRenderer(gpu) { Antialiasing = false, Shadows = false };
            renderer.Resize(Size, Size);
            gpu.Device.PushErrorScope(WebGpuSharp.ErrorFilter.Validation);
            renderer.Show(ModelContent.Read("scene.ydr", ydr).Surfaces!);
            renderer.Frame(new CommonFury.Numerics.Aabb3(new Vector3(-1, -1, 0), new Vector3(1, 1, 2)));
            return Finish(gpu, renderer);
        }
    }

    // A GTA V world's frame of the scene, under its sky by day, from four metres in front of the cut-out.
    private static Frame WorldFrame(byte[] ydr)
    {
        GpuDevice gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            var renderer = new ViewportRenderer(gpu) { Antialiasing = false };
            renderer.Resize(Size, Size);
            gpu.Device.PushErrorScope(WebGpuSharp.ErrorFilter.Validation);
            renderer.WorldShown = true;
            var eye = new Vector3(0, -4, 1);
            renderer.Camera.Frame(eye, 20);
            renderer.Camera.Free = true;
            renderer.Camera.Place(eye, -MathF.PI / 2, 0);
            renderer.SetTime(13);
            Drawable drawable = Ydr.Read(ydr).Drawable;
            var key = new RedFury.World.WorldAssetKey(1, 2);
            Assert.True(renderer.AddWorldAsset(Rendering.World.WorldAsset.FromGta(key, DrawableScene.Build(drawable, []), [drawable]),
                new HashSet<RedFury.World.WorldAssetKey>(), long.MaxValue));
            while (renderer.HasPendingWorldUpload)
                renderer.ProcessWorldUploads();
            var node = new RedFury.World.WorldLodNode(new RedFury.World.WorldEntityId(1, 0), null, key, Matrix4x4.Identity, Vector3.Zero, 30, 100000, 0, 0);
            renderer.SetWorldSelection(new RedFury.World.WorldSelection([node], [key]));
            renderer.ShowWorldSky(GtaSkyTests.Octahedron());
            Frame frame = Finish(gpu, renderer);
            renderer.ClearWorld();
            return frame;
        }
    }

    private static Frame Finish(GpuDevice gpu, ViewportRenderer renderer)
    {
        WebGpuSharp.Texture target = gpu.Device.CreateTexture(new WebGpuSharp.TextureDescriptor
        {
            Usage = WebGpuSharp.TextureUsage.RenderAttachment,
            Dimension = WebGpuSharp.TextureDimension.D2,
            Size = new WebGpuSharp.Extent3D(Size, Size, 1),
            Format = renderer.PresentFormat,
            MipLevelCount = 1,
            SampleCount = 1,
        });
        WebGpuSharp.TextureView view = target.CreateView();
        for (int i = 0; i < 4; i++)
            renderer.RenderTo(view);
        Frame frame = ReadFrame(gpu, renderer);
        var (type, message) = gpu.Device.PopErrorScopeSync(5_000_000_000);
        Assert.True(type == WebGpuSharp.ErrorType.NoError, message);
        target.Destroy();
        renderer.Dispose();
        return frame;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_cut_out_over_an_opaque_surface_is_blended_with_it_as_the_game_s_combine_blends_them(bool world)
    {
        Frame frame = world ? WorldFrame(FenceOverWall(nearStrip: false)) : ViewerFrame(FenceOverWall(nearStrip: false));
        // The cut-out over the wall fills the view: half its texels sub-sampled at four sevenths, all of them covered.
        int sampled = frame.Alpha.Count(a => a == (8 | 4));
        Assert.True(sampled > frame.Width * frame.Height / 3, $"{sampled} sub-sampled");
        Assert.All(frame.Before, c => Assert.Equal(1, c.W));
        (int blended, _, _) = AssertCombined(frame);
        Assert.True(blended > frame.Width * frame.Height * 3 / 4, $"{blended} blended");
        // In log: the bright grey over the dark wall darker than a linear blend of the two, as the game's is.
        int middle = frame.Height / 2 * frame.Width + frame.Width / 2;
        int cut = frame.Alpha[middle] == (8 | 4) ? middle : middle + 1, wall = cut == middle ? middle + 1 : middle;
        static float Level(Vector4 c) => .3f * c.X + .59f * c.Y + .11f * c.Z;
        float linear = float.Lerp(Level(frame.Before[wall]), Level(frame.Before[cut]), 4 / 7f);
        Assert.True(Level(frame.After[cut]) < linear * .8f, $"{Level(frame.After[cut])} against {linear}, from {frame.Before[wall]} and {frame.Before[cut]}");
        Assert.Equal(Level(frame.After[cut]), Level(frame.After[wall]), .05f * Level(frame.After[cut]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_nearer_opaque_surface_over_part_of_a_pair_is_blended_with_the_cut_out_beside_it_and_kept_inside(bool world)
    {
        Frame frame = world ? WorldFrame(FenceOverWall(nearStrip: true)) : ViewerFrame(FenceOverWall(nearStrip: true));
        // Within the strip, nothing sub-sampled; at its edges, the game's blend with what is beside it whichever side it
        // is on, and within, the strip left as it is drawn; within the whole cut-out, left as it is drawn too
        // (AssertCombined checks each texel).
        (_, int stripBehind, int stripInFront) = AssertCombined(frame);
        Assert.True(stripBehind >= frame.Height / 4, $"{stripBehind} of the strip's texels blended with the cut-out beside them");
        Assert.True(stripInFront >= frame.Height / 4, $"{stripInFront} of the cut-out's texels blended over the strip beside them");
        int kept = 0;
        for (int y = 0; y < frame.Height; y++)
            for (int x = 0; x + 1 < frame.Width; x++)
            {
                int at = y * frame.Width + x;
                Vector4 c = frame.Before[at], n = frame.Before[at + 1];
                if (c.X > 2 * c.Y && c.X > 2 * c.Z && n.X > 2 * n.Y && n.X > 2 * n.Z && frame.After[at] == c)
                    kept++;
            }
        Assert.True(kept >= frame.Height, $"{kept} of the strip's texels kept");
        int whole = Enumerable.Range(0, frame.Width * frame.Height - 1)
            .Count(at => frame.Alpha[at] == (8 | 7) && frame.Alpha[at + 1] == (8 | 7) && frame.After[at] == frame.Before[at]);
        Assert.True(whole >= frame.Width * frame.Height / 8, $"{whole} of the whole cut-out's texels kept");
    }

    [Fact]
    public void Whole_and_all_but_empty_cut_outs_are_left_as_they_are_drawn()
    {
        // Whole: drawn on every pixel, which the combine leaves alone within it.
        byte[] whole = CutOut("normal_spec", 255);
        Assert.Equal(Middle(Render(whole, combine: false, out _)), Middle(Render(whole, combine: true, out string? error)));
        Assert.Null(error);
        // Under the reference: nothing of it drawn, the backdrop as it is without it (but for its exposure's settling).
        (double Level, double Step) empty = Middle(Render(null, combine: true, out _));
        (double Level, double Step) under = Middle(Render(CutOut("normal_spec", 80), combine: true, out error));
        Assert.Null(error);
        Assert.Equal(empty.Level, under.Level, .5);
        Assert.Equal(empty.Step, under.Step, .5);
    }
}
