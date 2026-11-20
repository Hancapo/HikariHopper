using System.Numerics;
using CommonFury.Textures;
using Texture = FiveFury.Textures.Texture;
using CommonFury.Vertices;
using FiveFury;
using FiveFury.Drawables;
using FiveFury.Ydr;
using HikariHopper.Core;
using HikariHopper.Rendering;
using HikariHopper.Tests.Textures;

namespace HikariHopper.Tests;

// GTA V's night-only emissives and geometry, and the _reflect shaders' environment term.
[Collection(NightEmissivesCollection.Name)]
public sealed class NightReflectShaderTests
{
    // A flat square facing up, of the given shader, with a mid-grey diffuse map and white vertex colours; the
    // environment, if any, plain white (a cube's faces too).
    private static byte[] Square(GameTarget target, string shader, Action<DrawableMaterialInput>? setup = null, Texture? environment = null,
        string slot = "EnvironmentSampler")
    {
        Vector3[] positions = [new(-1, -1, 0), new(1, -1, 0), new(1, 1, 0), new(-1, 1, 0)];
        var mesh = new DrawableMeshInput(positions, [0, 1, 2, 0, 2, 3], "square");
        mesh.SetChannel(VertexSemantic.Normal, [.. Enumerable.Repeat(Vector4.UnitZ, 4)]);
        mesh.SetChannel(VertexSemantic.TexCoord0, [new(0, 0, 0, 0), new(1, 0, 0, 0), new(1, 1, 0, 0), new(0, 1, 0, 0)]);
        mesh.SetChannel(VertexSemantic.Color0, [.. Enumerable.Repeat(Vector4.One, 4)]);
        var material = new DrawableMaterialInput { Name = "square", Shader = shader };
        material.Textures["DiffuseSampler"] = new FiveFury.Textures.TextureReference("grey");
        if (environment is not null)
            material.Textures[slot] = new FiveFury.Textures.TextureReference(environment.Name!);
        setup?.Invoke(material);
        var builder = new DrawableBuilder { Name = "square" };
        builder.Materials.Add(material);
        builder.AdditionalTextures.Add(TextureTestKit.Bgra("grey", 4, 4, [64, 64, 64, 255]));
        if (environment is not null)
            builder.AdditionalTextures.Add(environment);
        var model = new DrawableModelInput();
        model.Meshes.Add(mesh);
        builder.Models.Add(model);
        return new Ydr(builder.Build(target), target).Write();
    }

    private static Texture White(string name = "env") => TextureTestKit.Bgra(name, 4, 4, [255, 255, 255, 255]);

    private static Texture WhiteCube(string name = "envcube") =>
        new(name, 4, 4, TextureFormat.B8G8R8A8Unorm, 1, TextureTestKit.Repeat([255, 255, 255, 255], 6 * 16), new TextureShape(TextureDimension.Cube));

    private static SurfaceMaterial Read(byte[] ydr) => Assert.Single(ModelContent.Read("square.ydr", ydr).Surfaces!.Materials);

    [Theory]
    [InlineData(20f, 7f, 12f, 0f)]
    [InlineData(20f, 7f, 19f, 0f)]
    // The hour before they come on, they fade in over its minutes.
    [InlineData(20f, 7f, 19.25f, .25f)]
    [InlineData(20f, 7f, 20f, 1f)]
    [InlineData(20f, 7f, 23.5f, 1f)]
    [InlineData(20f, 7f, 2f, 1f)]
    // The last hour they're on, they fade out over its minutes.
    [InlineData(20f, 7f, 6.75f, .25f)]
    [InlineData(20f, 7f, 7f, 0f)]
    // The settings are taken as whole hours, as the game reads them.
    [InlineData(19.9f, 6.5f, 19.5f, 1f)]
    [InlineData(19.9f, 6.5f, 5.5f, .5f)]
    public void The_day_night_balance_is_the_games_from_the_hour_and_the_night_emissives_hours(float start, float end, float hours, float balance) =>
        Assert.Equal(balance, NightEmissives.Balance(hours, start, end), 4);

    [Fact]
    public void The_night_emissives_hours_are_the_open_games_and_its_codes_defaults_without_one()
    {
        try
        {
            NightEmissives.Use(GameVisualSettings.Parse("emissive.night.start.time 18\nemissive.night.end.time 9 # comment\n"));
            Assert.Equal((18f, 9f), NightEmissives.Window);
            Assert.Equal(1, NightEmissives.Balance(18.5f));
            // A file without them: the code's defaults for those it lacks.
            NightEmissives.Use(GameVisualSettings.Parse("emissive.night.start.time 21\n"));
            Assert.Equal((21f, NightEmissives.DefaultEnd), NightEmissives.Window);
        }
        finally
        {
            NightEmissives.Use(null);
        }
        Assert.Equal((20f, 7f), NightEmissives.Window);
        Assert.Equal(0, NightEmissives.Balance(12));
    }

    [Theory]
    [InlineData(GameTarget.Legacy, "spec_reflect", "EnvironmentSampler")]
    [InlineData(GameTarget.Legacy, "normal_spec_reflect", "EnvironmentSampler")]
    [InlineData(GameTarget.Legacy, "reflect", "EnvironmentSampler")]
    // Enhanced names its sphere maps' slot apart from its cubes'.
    [InlineData(GameTarget.Enhanced, "spec_reflect", "EnvironmentTex2D")]
    public void A_sphere_reflect_shader_reads_its_environment_by_its_reflective_power(GameTarget target, string shader, string slot)
    {
        SurfaceMaterial material = Read(Square(target, shader, m => m.Parameters["reflectivePower"] = [.3f], White(), slot));
        Assert.Equal(EnvironmentKind.Sphere, material.Reflection);
        Assert.Equal("env", material.Environment?.Name);
        Assert.Contains(material.Environment, material.BoundTextures());
        Assert.Equal([1f, .3f], material.Uniforms[100..102]);
        // Unset, the shader's own default.
        Assert.Equal(.45f, Read(Square(target, shader, environment: White(), slot: slot)).Uniforms[101], 4);
    }

    [Fact]
    public void A_cubemap_reflect_shader_reads_a_cube_and_leaves_an_image_unread()
    {
        SurfaceMaterial cube = Read(Square(GameTarget.Legacy, "normal_spec_cubemap_reflect", environment: WhiteCube()));
        Assert.Equal(EnvironmentKind.Cube, cube.Reflection);
        Assert.Equal(2, cube.Uniforms[100]);
        // The map's _cubemap_reflect materials name images, which the game would read as cubes: nothing is read.
        SurfaceMaterial image = Read(Square(GameTarget.Legacy, "normal_spec_cubemap_reflect", environment: White()));
        Assert.Equal(EnvironmentKind.None, image.Reflection);
        Assert.Null(image.Environment);
        Assert.Equal([0f, 0f], image.Uniforms[100..102]);
        // Without an environment, a sphere shader reflects nothing either.
        Assert.Equal(EnvironmentKind.None, Read(Square(GameTarget.Legacy, "spec_reflect")).Reflection);
    }

    [Theory]
    [InlineData("emissivenight", true, false, false)]
    [InlineData("decal_emissivenight_only", true, false, false)]
    [InlineData("normal_spec_reflect_emissivenight", true, false, false)]
    // Its emissive is whole while it's there: only its vertices follow the night (the deferred program's emissive).
    [InlineData("emissivenight_geomnightonly", false, true, false)]
    [InlineData("emissive", false, false, false)]
    [InlineData("normal_spec_reflect", false, false, true)]
    public void Night_only_shaders_are_marked_as_the_game_compiles_them(string shader, bool emissive, bool geometry, bool reflects)
    {
        SurfaceMaterial material = Read(Square(GameTarget.Legacy, shader, environment: reflects ? White() : null));
        Assert.Equal(emissive ? 1 : 0, material.Uniforms[102]);
        Assert.Equal(geometry ? 1 : 0, material.Uniforms[103]);
        Assert.Equal(geometry, material.NightOnly);
        Assert.Equal(reflects, material.Reflection != EnvironmentKind.None);
    }

    // The square drawn from above at the centre of a small view at an hour; the centre's colour, and whether the frame
    // raised no GPU error.
    private static (byte R, byte G, byte B) Centre(byte[]? ydr, float hours)
    {
        GpuDevice gpu = TestGpu.Device();
        const int Size = 64;
        lock (gpu.Gate)
        {
            gpu.Device.PushErrorScope(WebGpuSharp.ErrorFilter.Validation);
            var renderer = new ViewportRenderer(gpu);
            renderer.Resize(Size, Size);
            var box = new CommonFury.Numerics.Aabb3(new Vector3(-1, -1, -.01f), new Vector3(1, 1, .01f));
            if (ydr is not null)
                renderer.Show(ModelContent.Read("square.ydr", ydr).Surfaces!);
            else
                renderer.Show(new ViewportMesh(Vector3.Zero));
            renderer.Frame(box);
            renderer.Camera.Orbit(0, 1000);
            renderer.SetTime(hours);
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
            target.Destroy();
            renderer.Dispose();
            var (type, message) = gpu.Device.PopErrorScopeSync(5_000_000_000);
            Assert.True(type == WebGpuSharp.ErrorType.NoError, message);
            int at = (Size / 2 * Size + Size / 2) * 4;
            return (pixels[at], pixels[at + 1], pixels[at + 2]);
        }
    }

    private static int Sum((byte R, byte G, byte B) c) => c.R + c.G + c.B;

    [Fact]
    public void A_reflect_shaders_environment_brightens_its_diffuse_from_a_sphere_map_and_from_a_cube()
    {
        int plain = Sum(Centre(Square(GameTarget.Legacy, "spec_reflect"), 13));
        Assert.True(Sum(Centre(Square(GameTarget.Legacy, "spec_reflect", environment: White()), 13)) > plain + 30);
        int cubePlain = Sum(Centre(Square(GameTarget.Legacy, "normal_spec_cubemap_reflect"), 13));
        Assert.True(Sum(Centre(Square(GameTarget.Legacy, "normal_spec_cubemap_reflect", environment: WhiteCube()), 13)) > cubePlain + 30);
    }

    [Fact]
    public void Night_only_geometry_is_gone_by_day_and_there_by_night_and_night_emissives_glow_only_by_night()
    {
        byte[] lamp = Square(GameTarget.Legacy, "emissivenight_geomnightonly");
        Assert.Equal(Centre(null, 13), Centre(lamp, 13));
        Assert.NotEqual(Centre(null, 23), Centre(lamp, 23));
        // A night emissive at noon is the plain shader's colour; at night it glows over that.
        byte[] night = Square(GameTarget.Legacy, "emissivenight", m => m.Parameters["emissiveMultiplier"] = [16]);
        byte[] plain = Square(GameTarget.Legacy, "default");
        Assert.Equal(Centre(plain, 13), Centre(night, 13));
        Assert.True(Sum(Centre(night, 23)) > Sum(Centre(plain, 23)) + 30);
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NightEmissivesCollection
{
    public const string Name = "Night emissives";
}
