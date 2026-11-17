using System.Numerics;
using HikariHopper.Core;
using HikariHopper.Rendering;

namespace HikariHopper.Tests;

// GTA V's cloud hats: clouds.xml's hats, cloudkeyframes.xml's weather clouds and weather.xml's weather as the game reads
// them, the hat the weather's list picks, the constants the game draws each layer with, and a frame with a hat over the
// sky.
[Collection(ExplorerCollection.Name)]
public sealed class GtaCloudHatsTests
{
    // Two hats as clouds.xml has them: the first with every field, the second with only a name and a layer's model.
    private const string Hats = """
        <?xml version="1.0" encoding="UTF-8"?>
        <CloudHatManager>
          <mCloudHatFrags>
            <Item>
              <mPosition x="0.000000" y="0.000000" z="25.000000" />
              <mRotation x="0.000000" y="0.000000" z="0.500000" />
              <mScale x="1800.000000" y="1800.000000" z="1500.000000" />
              <mName>Cloudy 01</mName>
              <mLayers>
                <Item>
                  <mFilename>horizonring</mFilename>
                  <mCostFactor value="0.000000" />
                  <mRotationScale value="0.000000" />
                  <mCamPositionScalerAdjust value="1.000000" />
                  <mHeightTigger value="500.000000" />
                  <mHeightFadeRange value="300.000000" />
                </Item>
                <Item>
                  <mFilename>cloudhat_cloudy_base</mFilename>
                  <mCostFactor value="0.600000" />
                  <mRotationScale value="0.100000" />
                  <mCamPositionScalerAdjust value="0.500000" />
                </Item>
                <Item>
                  <mFilename>cloudhat_cloudy_a</mFilename>
                  <mCostFactor value="0.600000" />
                </Item>
              </mLayers>
              <mTransitionAlphaRange value="0.499000" />
              <mTransitionMidPoint value="0.500000" />
              <mEnabled value="true" />
              <mAngularVelocity x="0.000000" y="0.000000" z="0.007600" />
              <mAnimBlendWeights x="1.000000" y="0.075000" z="0.150000" />
              <mUVVelocity content="vec2v_array">
                -0.000000	0.000000
                0.062500	0.000000
                -0.007000	0.006000
              </mUVVelocity>
              <mAnimMode content="char_array">
                0
                1
                1
              </mAnimMode>
              <mShowLayer>
                <Item value="true" />
                <Item value="false" />
                <Item value="true" />
              </mShowLayer>
              <mEnableAnimations value="true" />
            </Item>
            <Item>
              <mName>Bare</mName>
              <mLayers>
                <Item>
                  <mFilename>cloudhat_bare</mFilename>
                </Item>
              </mLayers>
            </Item>
          </mCloudHatFrags>
          <mDesiredTransitionTimeSec value="30.000000" />
          <mCamPositionScaler x="1.000000" y="1.000000" z="0.725000" />
          <mAltitudeScrollScaler value="0.000100" />
        </CloudHatManager>
        """;

    // A weather's clouds as cloudkeyframes.xml has them: both hats listed (and no hat), the second likelier; a colour
    // keyframed at six and eighteen, the density and scattering at midnight only.
    private const string Keyframes = """
        <?xml version="1.0" encoding="UTF-8"?>
        <CloudSettingsMap>
          <KeyframeTimes content="float_array">
            0.000000
            6.000000
            18.000000
          </KeyframeTimes>
          <SettingsMap>
            <Item>
              <Name>OTHERclouds</Name>
              <Settings />
            </Item>
            <Item>
              <Name>LIGHTclouds</Name>
              <Settings>
                <CloudList>
                  <mProbability content="int_array">
                    3
                    7
                    9
                  </mProbability>
                  <mBits bits="3" content="int_array">
                    0x00000007
                  </mBits>
                </CloudList>
                <CloudColor>
                  <keyData>
                    <numKeyEntries value="2" />
                    <keyEntryData content="float_array">
                      6.000000	0.2	0.4	0.6	1.000000
                      18.000000	0.4	0.8	1.0	1.000000
                    </keyEntryData>
                  </keyData>
                </CloudColor>
                <CloudDensityShift_Scale_ScatteringConst_Scale>
                  <keyData>
                    <numKeyEntries value="1" />
                    <keyEntryData content="float_array">
                      0.000000	0.1	1.1	0.84	2.0
                    </keyEntryData>
                  </keyData>
                </CloudDensityShift_Scale_ScatteringConst_Scale>
              </Settings>
            </Item>
          </SettingsMap>
        </CloudSettingsMap>
        """;

    private const string Weathers = """
        <?xml version="1.0" encoding="UTF-8"?>
        <CWeatherTypeData>
          <WeatherTypes>
            <Item>
              <Name>CLOUDS</Name>
              <WindMin value="0.5"/>
              <WindMax value="1.0"/>
              <CloudSettingsName>OTHERclouds</CloudSettingsName>
            </Item>
            <Item>
              <Name>EXTRASUNNY</Name>
              <WindMin value="0.25"/>
              <WindMax value="1.0"/>
              <CloudSettingsName>LIGHTclouds</CloudSettingsName>
            </Item>
          </WeatherTypes>
        </CWeatherTypeData>
        """;

    private static GameClouds Clouds() => GameClouds.Parse(Hats, Keyframes, Weathers);

    [Fact]
    public void Clouds_xml_gives_each_hat_its_layers_and_animation_with_the_parsers_defaults_where_it_has_none()
    {
        GameClouds clouds = Clouds();
        Assert.Equal(2, clouds.Hats.Length);
        CloudHatDefinition hat = clouds.Hats[0];
        Assert.Equal("Cloudy 01", hat.Name);
        Assert.Equal(new Vector3(0, 0, 25), hat.Position);
        Assert.Equal(new Vector3(1800, 1800, 1500), hat.Scale);
        Assert.Equal(new Vector3(0, 0, .5f), hat.Rotation);
        Assert.Equal(3, hat.Layers.Length);
        Assert.Equal(new CloudHatLayerDefinition("horizonring", 0, 0, 1, 500, 300, 0, 0), hat.Layers[0]);
        // A layer's cost, rotation scale and share of the camera's height default to one.
        Assert.Equal(new CloudHatLayerDefinition("cloudhat_cloudy_a", .6f, 1, 1, 0, 0, 0, 0), hat.Layers[2]);
        Assert.Equal(.499f, hat.TransitionAlphaRange);
        Assert.Equal(new Vector3(0, 0, .0076f), hat.AngularVelocity);
        Assert.Equal(new Vector2(.0625f, 0), hat.UVVelocity[1]);
        Assert.Equal(new Vector2(-.007f, .006f), hat.UVVelocity[2]);
        Assert.Equal([0, 1, 1], hat.AnimMode);
        Assert.Equal([true, false, true], hat.ShowLayer);
        Assert.True(hat.EnableAnimations && hat.Enabled);
        // The bare hat: scale one, enabled, combining, only its first texture layer shown, no animation.
        CloudHatDefinition bare = clouds.Hats[1];
        Assert.Equal(Vector3.One, bare.Scale);
        Assert.True(bare.Enabled);
        Assert.Equal([true, false, false], bare.ShowLayer);
        Assert.False(bare.EnableAnimations);
        Assert.Equal(new CloudHatLayerDefinition("cloudhat_bare", 1, 1, 1, 0, 0, 0, 0), bare.Layers[0]);
        Assert.Equal(new Vector3(1, 1, .725f), clouds.CamPositionScaler);
        Assert.Equal(.0001f, clouds.AltitudeScrollScaler);
    }

    [Fact]
    public void Extrasunny_takes_the_cloud_settings_and_wind_weather_xml_names_for_it()
    {
        GameClouds clouds = Clouds();
        Assert.Equal((.25f, 1f), (clouds.WindMin, clouds.WindMax));
        CloudSettings settings = clouds.Settings!;
        Assert.Equal([true, true, true], settings.Bits);
        Assert.Equal([3, 7, 9], settings.Probability);
        // No weather file: no settings, and so no hat.
        Assert.Null(GameClouds.Parse(Hats, Keyframes, null).Settings);
    }

    [Fact]
    public void Keyframes_are_synced_to_the_cloud_times_with_midnight_again_at_24_and_read_between_entries()
    {
        CloudSettings settings = Clouds().Settings!;
        // Synced, a keyframe has the cloud times and 24; before its second entry it runs on along its first two, so its
        // midnight is before six's value by half of six to eighteen.
        Assert.Equal([0f, 6, 18, 24], settings.CloudColor.Entries.Select(entry => entry.Time));
        Assert.True(Vector4.Distance(new Vector4(.1f, .2f, .4f, 1), settings.CloudColor.Query(0)) < 1e-5f);
        Assert.True(Vector4.Distance(new Vector4(.15f, .3f, .5f, 1), settings.CloudColor.Query(3)) < 1e-5f);
        Assert.True(Vector4.Distance(new Vector4(.3f, .6f, .8f, 1), settings.CloudColor.Query(12)) < 1e-5f);
        Assert.Equal(settings.CloudColor.Query(0), settings.CloudColor.Query(24));
        // A keyframe the file hasn't: one entry of ones.
        Assert.Equal(Vector4.One, settings.AmbientColor.Query(9));
        var keyframe = new CloudKeyframe([(0, Vector4.Zero), (10, new Vector4(10))]);
        Assert.Equal(new Vector4(2.5f), keyframe.Query(2.5f));
        Assert.Equal(new Vector4(10), keyframe.Query(20));
    }

    [Fact]
    public void A_list_made_for_fewer_hats_keeps_its_no_hat_entry_at_the_end()
    {
        var (bits, probability) = GameClouds.Fixup([true, false, true], [4, 5, 6], 4);
        Assert.Equal([true, false, false, false, true], bits);
        Assert.Equal([4, 5, 1, 1, 6], probability);
    }

    [Fact]
    public void The_weather_shows_its_likeliest_hat_the_first_on_a_tie_and_no_hat_where_it_lists_none()
    {
        CloudSettings settings = Clouds().Settings!;
        // The third entry is no hat: with two hats, index 2.
        Assert.Equal(2, GtaCloudHats.Select(settings, 2));
        Assert.Equal(1, GtaCloudHats.Select(settings with { Probability = [3, 7, 7] }, 2));
        Assert.Equal(0, GtaCloudHats.Select(settings with { Bits = [true, false, false] }, 2));
        Assert.Equal(2, GtaCloudHats.Select(settings with { Bits = [false, false, false] }, 2));
    }

    [Fact]
    public void Level_meta_lists_its_permanent_archetype_files()
    {
        const string Meta = """
            <?xml version="1.0" encoding="UTF-8"?>
            <CDataFileMgr__ContentsOfDataFileXml>
              <includedXmlFiles>
                <Item><xi:include href="commoncrc:/data/common.meta" xpointer="xpointer(/dataFiles)"/></Item>
              </includedXmlFiles>
              <dataFiles>
                <Item><filename>platform:/levels/gta5/generic/gtxd.ityp</filename><fileType>PERMANENT_ITYP_FILE</fileType></Item>
                <Item><filename>platform:/levels/gta5/vehicles.rpf</filename><fileType>RPF_FILE</fileType></Item>
                <Item><filename>platform:/levels/gta5/cloudhats/v_clouds.ityp</filename><fileType>PERMANENT_ITYP_FILE</fileType></Item>
              </dataFiles>
            </CDataFileMgr__ContentsOfDataFileXml>
            """;
        Assert.Equal(["gtxd", "v_clouds"], GameClouds.PermanentArchetypes(Meta));
    }

    private static T WithWeather<T>(Func<T> read, params (string Field, float Value)[] fields)
    {
        string xml = $"""
            <timecycle_keyframe_data version="1.000000">
              <cycle name="EXTRASUNNY" regions="1">
                <region name="GLOBAL">
            {string.Join("\n", fields.Select(f => $"      <{f.Field}>{string.Join(' ', Enumerable.Repeat(f.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), 13))}</{f.Field}>"))}
                </region>
              </cycle>
            </timecycle_keyframe_data>
            """;
        try
        {
            Timecycle.Use(GameTimecycle.From(GameTimecycle.ParseWeather(xml), []));
            return read();
        }
        finally
        {
            Timecycle.Use(null);
        }
    }

    [Fact]
    public void The_light_is_the_timecycles_sun_and_moon_squared_by_how_far_each_is_faded_in()
    {
        CloudSettings settings = Clouds().Settings!;
        CloudFrame noon = WithWeather(() => GtaCloudHats.Frame(settings, 12, null), ("sky_sun_col_r", .5f), ("sky_sun_col_g", 1), ("sky_sun_col_b", 0),
            ("sky_moon_col_r", 1), ("sky_moon_col_g", 1), ("sky_moon_col_b", 1));
        Assert.Equal(new Vector3(.25f, 1, 0), noon.LightColor);
        Assert.Equal(DayCycle.SunDirection(12), noon.SunDirection);
        // Scattering's scale is the keyframe's by 0.2; its constant and the density's shift and scale as they are.
        Assert.Equal(.4f, noon.ScatteringScale, 6);
        Assert.Equal(.84f, noon.ScatteringConst, 6);
        Assert.Equal(new Vector2(.1f, 1.1f), noon.DensityShiftScale);
        // At night the light comes from the moon, by its fade (the moon is full: its phase leaves it whole).
        CloudFrame night = WithWeather(() => GtaCloudHats.Frame(settings, 23, null), ("sky_moon_col_r", 1), ("sky_moon_col_g", .5f), ("sky_moon_col_b", 1));
        float moon = DayCycle.Fades(23).Moon;
        Assert.Equal(DayCycle.MoonDirection(23), night.SunDirection);
        Assert.True(Vector3.Distance(new Vector3(1, .5f, 1) * moon * new Vector3(1, .5f, 1) * moon * GtaSky.MoonPhaseIntensity, night.LightColor) < 1e-5f);
    }

    // The first hat with a geometry of each layer, and the weather's settings.
    private static GtaCloudHats Hat()
    {
        GameClouds clouds = Clouds();
        GtaCloudHatGeometry geometry = new([0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 0, 0, 1, 0, 0, 1], [0, 0, 0], new CloudHatEffect(true, true, false, false),
            [new(.1f, .2f, .3f, .4f), new(.5f, .6f, 1, 1), new(1, 1, 1, 1), new(175, .75f, 1, 1), new(1, 1, 1, 1)], new FiveFury.Textures.Texture?[6]);
        GtaCloudHatLayer[] layers = [.. clouds.Hats[0].Layers.Select(layer => new GtaCloudHatLayer(layer, [geometry]))];
        return new GtaCloudHats(clouds.Hats[0], layers, clouds.Settings!, clouds.CamPositionScaler, clouds.AltitudeScrollScaler, 1, clouds.WindMin, clouds.WindMax);
    }

    [Fact]
    public void Layers_show_while_their_cost_is_covered_and_altitude_ones_fade_in_over_their_range()
    {
        GtaCloudHats hats = Hat();
        Span<bool> active = stackalloc bool[3];
        // On the ground: the horizon ring isn't streamed in, the base costs 0.6 and the next can't be covered.
        hats.Active(62, active);
        Assert.Equal([false, true, false], active.ToArray());
        hats.Active(460, active);
        Assert.True(active[0]);
        Assert.Equal(0, GtaCloudHats.Fade(hats.Hat.Layers[0], 499));
        Assert.Equal(.5f, GtaCloudHats.Fade(hats.Hat.Layers[0], 650), 6);
        Assert.Equal(1, GtaCloudHats.Fade(hats.Hat.Layers[0], 900));
        var high = hats.Hat.Layers[0] with { HeightTrigger2 = 2000, HeightFadeRange2 = 100 };
        Assert.Equal(.5f, GtaCloudHats.Fade(high, 1950), 6);
        Assert.Equal(0, GtaCloudHats.Fade(high, 2001));
    }

    [Fact]
    public void The_wind_scrolls_the_hats_fully_from_five_metres_a_second_stronger_up_high()
    {
        GtaCloudHats hats = Hat() with { WindMin = 0, WindMax = .5f };
        // The middle of 0 and 0.5, by twelve metres a second: three, three fifths of full.
        Assert.Equal(.6f, WithWeather(() => hats.Wind(10, 12), ("wind_speed_mult", 1)), 5);
        Assert.Equal(.3f, WithWeather(() => hats.Wind(10, 12), ("wind_speed_mult", .5f)), 5);
        // Twice as strong from 200 m up.
        Assert.Equal(1, WithWeather(() => hats.Wind(250, 12), ("wind_speed_mult", 1)), 5);
        var animation = new CloudHatAnimation();
        animation.Advance(hats, 10, .5f);
        Assert.True(Vector3.Distance(new Vector3(0, 0, .076f), animation.Rotation) < 1e-6f);
        Assert.True(Vector2.Distance(new Vector2(.3125f, 0), animation.Offsets[1]) < 1e-6f);
    }

    [Fact]
    public void Each_layer_is_drawn_with_the_constants_the_game_sets_where_the_hat_follows_the_camera()
    {
        GtaCloudHats hats = Hat();
        CloudSettings settings = hats.Settings;
        CloudFrame frame = WithWeather(() => GtaCloudHats.Frame(settings, 12, null), ("sky_sun_col_r", 1), ("sky_sun_col_g", 1), ("sky_sun_col_b", 1)) with
        {
            ScaleSkyBounceEastWest = new Vector4(2, 3, 4, 5),
            EastColor = new Vector3(1, 0, 0),
            WestColor = new Vector3(0, 1, 0),
            SkyColor = Vector3.One,
            WrapAmount = 1,
        };
        var values = new Vector4[3 * GtaCloudHats.Bytes / 16];
        var drawn = new bool[3];
        var camera = new Vector3(100, -200, 62);
        Assert.Equal(1, hats.Write(values, drawn, frame, new CloudHatAnimation(), camera, .005f, 0));
        Assert.Equal([false, true, false], drawn);
        Span<Vector4> v = values.AsSpan(GtaCloudHats.Bytes / 16, GtaCloudHats.Bytes / 16);
        Assert.Equal(new Vector4(2, 2, 2, 0), v[0]);
        // East less west, each by its scale; west by its own.
        Assert.Equal(new Vector4(4, -5, 0, 0), v[1]);
        Assert.Equal(new Vector4(0, 5, 0, 0), v[2]);
        // Fully transitioned in: the density's own shift and scale, with a depth buffer to fade against.
        Assert.Equal(new Vector4(.1f, 1.1f, 1, 0), v[8]);
        Assert.Equal(new Vector4(.5f, .5f, 1, 0), v[12]);
        Assert.Equal(new Vector4(.005f, 0, 1, -.005f), v[13]);
        // Combine for the first texture layer, sculpt for the others; the hidden second weighs nothing.
        Assert.Equal(new Vector4(1, 0, 0, 0), v[14]);
        Assert.Equal(new Vector4(0, 1, 1, 0), v[15]);
        Assert.Equal(new Vector4(1, 0, .15f, 0), v[16]);
        Assert.Equal(new Vector4(camera, 1), v[19]);
        Assert.Equal(new Vector4(.1f, .2f, .3f, .4f), v[20]);
        // Turned by half a radian, each axis scaled by the hat's scale; placed at its offset with the camera's x and y,
        // and the base layer's half share of the scaler's 0.725 of its height, relative to the camera.
        Vector3 x = new Vector3(MathF.Cos(.5f), MathF.Sin(.5f), 0) * hats.Hat.Scale;
        Assert.True(Vector3.Distance(x, v[25].AsVector3()) < 1e-3f, $"{v[25]}");
        Assert.True(Vector3.Distance(new Vector3(0, 0, 25 + 62 * (.725f * .5f - 1)), v[28].AsVector3()) < 1e-3f, $"{v[28]}");
        Assert.Equal(new Vector4(1, 1, 0, 0), v[29]);
        // Above its trigger the horizon ring shows, faded in, and lit less as the sun sets.
        Assert.Equal(2, hats.Write(values, drawn, frame, new CloudHatAnimation(), camera with { Z = 650 }, .005f, 0));
        Assert.True(drawn[0]);
    }

    // With no world in view, the hat's soft geometry has the scene's depth copied for it in the frame.
    [Fact]
    public void A_cloud_hat_is_drawn_over_the_worlds_sky_with_no_GPU_errors_until_the_world_goes()
    {
        GpuDevice gpu = TestGpu.Device();
        lock (gpu.Gate)
        {
            var renderer = new ViewportRenderer(gpu);
            const int Width = 64, Height = 32;
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
            renderer.Camera.Place(new Vector3(0, 0, 50), 0, -.3f);
            renderer.SetTime(14);
            // A hat with no sky isn't drawn: it takes the sky's fog.
            renderer.ShowWorldCloudHats(Ring());
            Assert.True(renderer.HasWorldCloudHats);
            renderer.RenderTo(target.CreateView());
            Assert.Equal(0, renderer.WorldCloudHatDraws);
            renderer.ShowWorldSky(Dome());
            renderer.RenderTo(target.CreateView());
            Assert.Equal(2, renderer.WorldCloudHatDraws);
            // At night, the animation moving on, and the hat shown again in place of itself.
            renderer.SetTime(23);
            renderer.RenderTo(target.CreateView());
            renderer.ShowWorldCloudHats(Ring());
            renderer.RenderTo(target.CreateView());
            Assert.Equal(2, renderer.WorldCloudHatDraws);
            var (type, message) = gpu.Device.PopErrorScopeSync(5_000_000_000);
            Assert.True(type == WebGpuSharp.ErrorType.NoError, message);
            renderer.ClearWorld();
            Assert.False(renderer.HasWorldCloudHats);
            renderer.Dispose();
            target.Destroy();
        }
    }

    // A sky dome of eight triangles with no textures.
    private static GtaSkyDome Dome()
    {
        Vector3[] corners = [Vector3.UnitX, Vector3.UnitY, -Vector3.UnitX, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];
        float[] vertices = [.. corners.SelectMany(c => new[] { c.X, c.Y, c.Z, c.X * .5f + .5f, c.Y * .5f + .5f, c.X * .5f + .5f, c.Z * .5f + .5f })];
        uint[] indices = [0, 1, 4, 1, 2, 4, 2, 3, 4, 3, 0, 4, 1, 0, 5, 2, 1, 5, 3, 2, 5, 0, 3, 5];
        return new GtaSkyDome(vertices, indices, null, null, null, null, null, GtaSkyVisuals.From(null));
    }

    // A hat of one layer round the camera, one fast geometry (at the far plane) and one soft animated one, wound both
    // ways and with no textures (the renderer's stand-in: the density map reads nothing, so the clouds are whole).
    private static GtaCloudHats Ring()
    {
        GameClouds clouds = Clouds();
        Vector3[] corners = [Vector3.UnitX, Vector3.UnitY, -Vector3.UnitX, -Vector3.UnitY, Vector3.UnitZ * .5f, -Vector3.UnitZ * .2f];
        float[] vertices = [.. corners.SelectMany(c => new[] { c.X, c.Y, c.Z, -c.X, -c.Y, -c.Z, 0, 1, 1, 1, c.X * .5f + .5f, c.Z, 0, 0, 1, 1 })];
        uint[] faces = [0, 1, 4, 1, 2, 4, 2, 3, 4, 3, 0, 4, 1, 0, 5, 2, 1, 5, 3, 2, 5, 0, 3, 5];
        uint[] indices = [.. faces, .. faces.Reverse()];
        Vector4[] material = [Vector4.Zero, new(0, 0, 1, 1), Vector4.One, new(175, .75f, 1, 1), Vector4.One];
        GtaCloudHatGeometry fast = new(vertices, indices, new CloudHatEffect(false, false, true, false), material, new FiveFury.Textures.Texture?[6]);
        GtaCloudHatGeometry soft = new(vertices, indices, new CloudHatEffect(true, true, false, false), material, new FiveFury.Textures.Texture?[6]);
        var layer = new CloudHatLayerDefinition("ring", 0, 1, 1, 0, 0, 0, 0);
        CloudHatDefinition hat = clouds.Hats[0] with { Layers = [layer], Scale = new Vector3(2000, 2000, 1500) };
        return new GtaCloudHats(hat, [new GtaCloudHatLayer(layer, [fast, soft])], clouds.Settings!, clouds.CamPositionScaler, clouds.AltitudeScrollScaler, 1,
            clouds.WindMin, clouds.WindMax);
    }
}
