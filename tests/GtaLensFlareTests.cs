using System.Numerics;
using FiveFury.Cryptography;
using HikariHopper.Core;
using HikariHopper.Rendering;

namespace HikariHopper.Tests;

// GTA V's lens flare over its world: its settings as the game's files have them, its visibility, fade and layout as
// the game works them out, and a frame with it over the sky.
[Collection(ExplorerCollection.Name)]
public sealed class GtaLensFlareTests
{
    // A lensflare_*.xml as the game's are written: an artefact, a ring, a corona and a streak, and the settings.
    private const string Settings = """
        <?xml version="1.0" encoding="UTF-8"?>
        <CLensFlareSettings>
          <arrFlareTypeFX>
            <Item>
              <arrFlareFX>
                <Item>
                  <fDistFromLight value="-1.500000" />
                  <fSize value="0.020000" />
                  <fWidthRotate value="0.000000" />
                  <fIntensityScale value="66.599998" />
                  <color value="0xFF0066FF" />
                  <fGradientMultiplier value="1.650000" />
                  <nTexture value="1" />
                  <nSubGroup value="4" />
                </Item>
                <Item>
                  <fDistFromLight value="0.500000" />
                  <fSize value="0.300000" />
                  <fWidthRotate value="0.050000" />
                  <fIntensityScale value="10.000000" />
                  <fIntensityFade value="1.000000" />
                  <color value="0x80FF0000" />
                  <nTexture value="2" />
                  <nSubGroup value="0" />
                  <fTextureColorDesaturate value="0.500000" />
                </Item>
                <Item>
                  <fDistFromLight value="0.000000" />
                  <fSize value="0.100000" />
                  <fWidthRotate value="0.000000" />
                  <fIntensityScale value="20.000000" />
                  <color value="0xFFFFFFFF" />
                  <nTexture value="3" />
                  <nSubGroup value="0" />
                </Item>
                <Item>
                  <fDistFromLight value="0.000000" />
                  <fSize value="0.050000" />
                  <fWidthRotate value="0.000000" />
                  <fIntensityScale value="5.000000" />
                  <fAnimorphicScaleFactorU value="5.000000" />
                  <bAnimorphicBehavesLikeArtefact value="true" />
                  <color value="0xFFFFFFFF" />
                  <nTexture value="0" />
                  <nSubGroup value="0" />
                </Item>
              </arrFlareFX>
            </Item>
          </arrFlareTypeFX>
          <fSunVisibilityFactor value="0.400000" />
          <fSunFogFactor value="0.500000" />
          <fChromaticTexUSize value="0.050000" />
          <fExposureScaleFactor value="12.557490" />
          <fExposureIntensityFactor value="0.010584" />
          <fExposureRotationFactor value="0.500000" />
          <fChromaticFadeFactor value="0.750000" />
          <fArtefactDistanceFadeFactor value="2.000000" />
          <fArtefactRotationFactor value="0.000000" />
          <fArtefactScaleFactor value="6.000000" />
          <fCoronaDistanceAdditionalSize value="0.300000" />
          <fMinExposureScale value="0.500000" />
          <fMaxExposureScale value="1.000000" />
          <fMinExposureIntensity value="0.500000" />
          <fMaxExposureIntensity value="1.500000" />
        </CLensFlareSettings>
        """;

    private static GtaLensFlare Flare() => new(GtaLensFlareSettings.Parse(Settings), GtaLensFlareVisuals.From(null), new FiveFury.Textures.Texture?[4]);

    [Fact]
    public void Settings_are_read_as_the_game_writes_them_colours_as_ARGB_missing_fields_at_the_parsers_defaults()
    {
        GtaLensFlareSettings settings = GtaLensFlareSettings.Parse(Settings);
        Assert.Equal(4, settings.Elements.Length);
        Assert.Equal(.4f, settings.SunVisibilityFactor);
        Assert.Equal(12.55749f, settings.ExposureScaleFactor, 5);
        GtaFlareElement artefact = settings.Elements[0], ring = settings.Elements[1];
        Assert.Equal(new Vector4(0, 0x66, 0xFF, 0xFF) / 255f, artefact.Colour);
        Assert.Equal((1, 4), (artefact.Texture, artefact.SubGroup));
        Assert.Equal(1.65f, artefact.GradientMultiplier);
        // Fields the file lacks: the psc's init values (the scroll's 0.15).
        Assert.Equal(.15f, ring.ScrollSpeed);
        Assert.Equal(new Vector4(1, 0, 0, 0x80 / 255f), ring.Colour);
        Assert.True(settings.Elements[3].AnamorphicBehavesLikeArtefact);
        // visualsettings.dat's lengths, with the game's defaults where the file hasn't them.
        Assert.Equal(new GtaLensFlareVisuals(1000, 1), GtaLensFlareVisuals.From(null));
        Assert.Equal(new GtaLensFlareVisuals(250, 2), GtaLensFlareVisuals.From(GameVisualSettings.Parse("lensflare.hide.length 250\nlensflare.desaturation.boost 2\n")));
    }

    // A camera at the origin looking along +Y, 90 degrees across, a 1000-pixel-wide target.
    private static GtaLensFlareView View()
    {
        Matrix4x4 viewMatrix = Matrix4x4.CreateLookAt(Vector3.Zero, Vector3.UnitY, Vector3.UnitZ);
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 2, 1, 1, 1000);
        return new GtaLensFlareView(viewMatrix * projection, Vector3.Zero, Vector3.Zero, Vector3.UnitY, 1, 1, 1000);
    }

    [Fact]
    public void The_suns_visibility_is_its_pixels_over_its_discs_times_the_timecycles_kept_from_its_last_sighting()
    {
        var state = new GtaLensFlareState(Flare());
        GtaLensFlareView view = View();
        var sun = new Vector3(0, 20000, 0);
        // A disc 100 m across at 20 km: 2.5 pixels across half a 1000-pixel width at tan 45.
        float whole = MathF.PI * 2.5f * 2.5f;
        Assert.Equal(whole, GtaLensFlareState.ExpectedPixels(sun, 100, view), 4);
        // On a target twice as wide, four times the pixels.
        Assert.Equal(whole * 4, GtaLensFlareState.ExpectedPixels(sun, 100, view with { PixelWidth = 2000 }), 3);
        state.UpdateVisibility(whole / 2, whole, 1);
        Assert.Equal(.5f, state.SunVisibility, 4);
        state.UpdateVisibility(whole, whole, .3f);
        Assert.Equal(.3f, state.SunVisibility, 4);
        state.UpdateVisibility(whole * 3, whole, 1);
        Assert.Equal(1, state.SunVisibility);
        state.UpdateVisibility(0, whole, 1);
        Assert.Equal(0, state.SunVisibility);
        Assert.Equal(1, state.CachedVisibility);
        Assert.Equal(40, state.VisibilityRadius(100));
    }

    [Fact]
    public void The_exposure_scales_and_brightens_the_elements_within_the_settings_ranges()
    {
        var state = new GtaLensFlareState(Flare());
        Assert.Equal((.5f, .5f), (state.ExposureFactors(0).Scale, state.ExposureFactors(0).Intensity));
        (float scale, float intensity, float rotation) = state.ExposureFactors(.06f);
        Assert.Equal(12.55749f * .06f, scale, 5);
        Assert.Equal(.5f, intensity);
        Assert.Equal(.5f * .06f, rotation, 5);
        Assert.Equal(1, state.ExposureFactors(1).Scale);
        Assert.Equal(.5f, GtaLensFlareState.LinearExposure(0, -2, 2));
        Assert.Equal(1, GtaLensFlareState.LinearExposure(5, -2, 2));
    }

    [Fact]
    public void The_flare_shows_while_the_sun_does_goes_at_once_when_it_is_hidden_and_fades_out_as_it_leaves_the_screen()
    {
        var state = new GtaLensFlareState(Flare());
        GtaLensFlareView view = View();
        var sun = new Vector3(0, 20000, 0);
        Assert.Equal(new Vector2(.5f, .5f), GtaLensFlareState.ScreenPosition(sun, view)!.Value);
        // Hidden: none.
        Assert.False(state.Advance(new(.5f, .5f), 0));
        // In view: whole at once.
        state.UpdateVisibility(100, 50, 1);
        Assert.True(state.Advance(new(.5f, .5f), 10));
        Assert.Equal(0, state.CurrentFade);
        // Off the screen: out over the hide length.
        Assert.True(state.Advance(new(1.2f, .5f), 100));
        Assert.True(state.Advance(new(1.2f, .5f), 600));
        Assert.Equal(.5f, state.CurrentFade, 3);
        Assert.False(state.Advance(new(1.2f, .5f), 1100));
        // Back, and seen: whole again.
        Assert.True(state.Advance(new(.4f, .5f), 1200));
        Assert.Equal(0, state.CurrentFade);
    }

    [Fact]
    public void A_sun_behind_the_camera_has_no_place_on_the_screen_and_its_flare_fades_out_undrawn_as_off_the_screen()
    {
        var state = new GtaLensFlareState(Flare());
        GtaLensFlareView view = View();
        var sun = new Vector3(0, 20000, 0);
        // Behind the camera the projection would mirror it onto the centre: it has no place.
        Assert.Null(GtaLensFlareState.ScreenPosition(-sun, view));
        Assert.Null(GtaLensFlareState.ScreenPosition(Vector3.Zero, view));
        state.UpdateVisibility(100, 50, 1);
        Assert.True(state.Advance(new(.5f, .5f), 0));
        // Turned away, with the last count still saying the sun shows: nothing drawn, the fade out under way.
        Assert.False(state.Advance(null, 100));
        Assert.False(state.Advance(null, 600));
        Assert.Equal(.5f, state.CurrentFade, 3);
        // Back before the fade ends, and seen: whole again.
        Assert.True(state.Advance(new(.5f, .5f), 700));
        Assert.Equal(0, state.CurrentFade);
    }

    [Fact]
    public void The_elements_lie_along_the_line_through_the_centre_the_rings_twice_the_artefacts_dark_at_the_centre()
    {
        var state = new GtaLensFlareState(Flare());
        GtaLensFlareView view = View();
        state.UpdateVisibility(1000, 10, 1);
        (int First, int Count)[] layout = state.Layout();
        Assert.Equal([(0, 1), (1, 1), (2, 2), (4, 1)], layout);
        var sprites = new Vector4[5 * GtaLensFlareState.SpriteVectors];
        // At the centre: the artefacts fade to nothing (their colour), the rest stay where the sun is.
        state.Advance(new(.5f, .5f), 0);
        int[] written = state.Build(sprites, new(.5f, .5f), Vector3.One, 1, 0, .5f, 0);
        Assert.Equal([1, 1, 2, 1], written);
        Span<Vector4> artefact = sprites.AsSpan(GtaLensFlareState.SpriteVectors, GtaLensFlareState.SpriteVectors);
        Assert.Equal(Vector3.Zero, artefact[3].AsVector3());
        Assert.Equal(GtaLensFlareState.GradientQuad, artefact[6].Y);
        // Its cell: the fourth of the sheet's (the whole texture's first) without the sheet is the whole texture.
        Assert.Equal(new Vector4(0, 0, 1, 1), artefact[2]);
        // The corona, at the sun, half its size (an exposure scale of a half) each side.
        Span<Vector4> corona = sprites.AsSpan(4 * GtaLensFlareState.SpriteVectors, GtaLensFlareState.SpriteVectors);
        Assert.Equal(new Vector4(.05f, .05f, -.05f, .05f), corona[0]);
        // Its intensity: the visibility times its own times the exposure's (a half).
        Assert.Equal(10, corona[3].W, 4);
        // The rings: both at the sun, scrolling opposite ways, desaturated as the file says.
        Span<Vector4> ring = sprites.AsSpan(2 * GtaLensFlareState.SpriteVectors, 2 * GtaLensFlareState.SpriteVectors);
        Assert.Equal(new Vector2(0, 0), new Vector2(ring[0].X, ring[0].Y));
        Assert.Equal(-ring[2].Z, ring[GtaLensFlareState.SpriteVectors + 2].Z);
        Assert.Equal(new Vector4(0, 0, .5f, 1), ring[5]);
        Assert.Equal(GtaLensFlareState.Ring, ring[6].Y);
        // Off the centre, up and to the left: the artefact lies past the centre, its colour back.
        written = state.Build(sprites, new(.1f, .1f), Vector3.One, 1, 0, 0, 0);
        Assert.Equal(1, written[GtaLensFlare.Artefact]);
        Assert.True(artefact[3].Z > 0);
        Vector2 centre = (new Vector2(artefact[0].X, artefact[0].Y) + new Vector2(artefact[1].Z, artefact[1].W)) / 2;
        Assert.True(centre.X > 0 && centre.Y < 0, $"{centre}");
    }

    [Fact]
    public void The_games_own_flare_is_read_from_its_files_Michaels_settings_and_the_four_textures()
    {
        string root = TestGames.Legacy();
        string executable = Path.Combine(root, File.Exists(Path.Combine(root, "GTA5.exe")) ? "GTA5.exe" : "GTA5_Enhanced.exe");
        // Its own two files, as the world index reads them, without making the whole world's index.
        var keys = new Rpf7Keys(GameKeys.Load(executable));
        FiveFury.GameTarget target = executable.EndsWith("GTA5.exe", StringComparison.OrdinalIgnoreCase) ? FiveFury.GameTarget.Legacy : FiveFury.GameTarget.Enhanced;
        using var game = FiveFury.Assets.GameInstallation.Open(root, target, keys.Crypto,
            new FiveFury.Assets.GameInstallationOptions { DlcLevel = FiveFury.Assets.DlcLevel.Base });
        GtaLensFlare flare = GtaWorldIndex.ReadLensFlare(game.FileSystem, GameVisualSettings.Read(new LoadedGame(root, target, keys.Crypto)))!;
        Assert.Equal(114, flare.Settings.Elements.Length);
        Assert.Equal(.4f, flare.Settings.SunVisibilityFactor);
        Assert.Equal(.5f, flare.Settings.SunFogFactor);
        Assert.All(flare.Textures, Assert.NotNull);
        Assert.Equal((256, 512), (flare.Textures[GtaLensFlare.Artefact]!.Width, flare.Textures[GtaLensFlare.Artefact]!.Height));
        Assert.Contains(flare.Settings.Elements, element => element.Texture == GtaLensFlare.Chromatic);
    }

    [Fact]
    public void A_worlds_flare_counts_the_suns_pixels_and_is_drawn_over_its_sky_with_no_GPU_errors() =>
        WithFlareOverSky((renderer, _) =>
        {
            Assert.True(renderer.WorldSunVisibility > .5f, $"visibility {renderer.WorldSunVisibility}");
            Assert.Equal(5, renderer.WorldLensFlareSprites);
            renderer.ClearWorld();
            Assert.False(renderer.HasWorldLensFlare);
        });

    [Fact]
    public void A_count_is_measured_against_the_disc_of_its_own_frame_so_a_new_render_scale_leaves_the_sun_as_visible() =>
        WithFlareOverSky((renderer, render) =>
        {
            // Settled at the window's size: every count since is the whole sun's.
            for (int frame = 0; frame < 8; frame++)
                render();
            float settled = renderer.WorldSunVisibility;
            Assert.True(settled > .5f, $"visibility {settled}");
            // Twice the size: four times the pixels, while counts from the smaller target are still coming back.
            renderer.RenderScale = 2;
            for (int frame = 0; frame < 8; frame++)
            {
                render();
                Assert.True(renderer.WorldSunVisibility > settled * .8f, $"frame {frame}: visibility {renderer.WorldSunVisibility}, {settled} before");
            }
        });

    [Fact]
    public void A_flare_shown_in_place_of_another_goes_by_none_of_the_counts_drawn_before_it() =>
        WithFlareOverSky((renderer, render) =>
        {
            Assert.True(renderer.WorldSunVisibility > .5f, $"visibility {renderer.WorldSunVisibility}");
            // A new world's flare, the sun now behind the camera: the last world's counts still being read back say
            // it showed, its own that it doesn't.
            renderer.ShowWorldLensFlare(Flare());
            Vector3 sun = DayCycle.SunDirection(14);
            renderer.Camera.Place(new Vector3(0, 0, 50), MathF.Atan2(sun.Y, sun.X), MathF.Asin(sun.Z));
            for (int frame = 0; frame < 8; frame++)
            {
                render();
                Assert.Equal(0, renderer.WorldSunVisibility);
                Assert.Equal(0, renderer.WorldLensFlareSprites);
            }
        });

    // A world's sky looking straight at the sun with the flare over it, drawn until the flare shows (its count comes
    // back a frame or so later, never waited for), then the test, with no validation errors throughout.
    private static void WithFlareOverSky(Action<ViewportRenderer, Action> test)
    {
        try
        {
            Timecycle.Use(GameTimecycle.From(GameTimecycle.ParseWeather(Weather(("sky_sun_hdr", 255), ("sky_sun_col_r", 1), ("sky_sun_col_g", 1),
                ("sky_sun_col_b", 1), ("sky_sun_disc_col_r", 1), ("sky_sun_disc_size", 40), ("Lensflare_visibility", 1))), []));
            GpuDevice gpu = TestGpu.Device();
            lock (gpu.Gate)
            {
                var renderer = new ViewportRenderer(gpu);
                const int Width = 128, Height = 64;
                WebGpuSharp.Texture target = gpu.Device.CreateTexture(new WebGpuSharp.TextureDescriptor
                {
                    Usage = WebGpuSharp.TextureUsage.RenderAttachment | WebGpuSharp.TextureUsage.CopySrc,
                    Dimension = WebGpuSharp.TextureDimension.D2,
                    Size = new WebGpuSharp.Extent3D(Width, Height, 1),
                    Format = renderer.PresentFormat,
                    MipLevelCount = 1,
                    SampleCount = 1,
                });
                gpu.Device.PushErrorScope(WebGpuSharp.ErrorFilter.Validation);
                renderer.Resize(Width, Height);
                renderer.WorldShown = true;
                renderer.Camera.Frame(new Vector3(0, 0, 50), 20);
                renderer.Camera.Free = true;
                renderer.SetTime(14);
                Vector3 sun = DayCycle.SunDirection(14);
                // Looking straight at the sun.
                renderer.Camera.Place(new Vector3(0, 0, 50), MathF.Atan2(-sun.Y, -sun.X), -MathF.Asin(sun.Z));
                renderer.ShowWorldSky(Octahedron());
                renderer.ShowWorldLensFlare(Flare());
                Assert.True(renderer.HasWorldLensFlare);
                WebGpuSharp.TextureView view = target.CreateView();
                void Render()
                {
                    renderer.RenderTo(view);
                    Thread.Sleep(5);
                }
                for (int frame = 0; frame < 60 && renderer.WorldLensFlareSprites == 0; frame++)
                    Render();
                test(renderer, Render);
                var (type, message) = gpu.Device.PopErrorScopeSync(5_000_000_000);
                Assert.True(type == WebGpuSharp.ErrorType.NoError, message);
                renderer.Dispose();
                target.Destroy();
            }
        }
        finally
        {
            Timecycle.Use(null);
        }
    }

    private static string Weather(params (string Field, float Value)[] fields) => $"""
        <timecycle_keyframe_data version="1.000000">
          <cycle name="EXTRASUNNY" regions="1">
            <region name="GLOBAL">
        {string.Join("\n", fields.Select(f => $"      <{f.Field}>{string.Join(' ', Enumerable.Repeat(f.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), 13))}</{f.Field}>"))}
            </region>
          </cycle>
        </timecycle_keyframe_data>
        """;

    // A dome of eight triangles, with no textures.
    private static GtaSkyDome Octahedron()
    {
        Vector3[] corners = [Vector3.UnitX, Vector3.UnitY, -Vector3.UnitX, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];
        float[] vertices = [.. corners.SelectMany(c => new[] { c.X, c.Y, c.Z, c.X * .5f + .5f, c.Y * .5f + .5f, c.X * .5f + .5f, c.Z * .5f + .5f })];
        uint[] indices = [0, 1, 4, 1, 2, 4, 2, 3, 4, 3, 0, 4, 1, 0, 5, 2, 1, 5, 3, 2, 5, 0, 3, 5];
        return new GtaSkyDome(vertices, indices, null, null, null, null, null, GtaSkyVisuals.From(null));
    }
}
