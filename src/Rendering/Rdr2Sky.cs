using System.Numerics;
using System.Text;
using System.Text.Json;
using HikariHopper.Core;

namespace HikariHopper.Rendering;

/// <summary>
/// Red Dead Redemption 2's sky as its files set it: the weather's timecycle (its GLOBAL region's sky_scattering_* fields,
/// at the keyframes) and visualsettings.dat's sky.* settings. Works out, for an hour, what the game's sky update puts in
/// its frame's constants for the sky tables (rdr2_sky_tables.wgsl) and the dome (rdr2_sky.wgsl): the wavelengths'
/// Rayleigh scattering, the aerosols' extinction, the ozone's absorption, the scale heights, and the sun's light; and by
/// night the moon's light, its disc, the stars (their layers, how they turn and twinkle) and the air glow; and what the
/// game lights its world with from them (the key light's tint) and exposes and tone-maps it with.
/// </summary>
internal sealed class Rdr2Sky
{
    /// <summary>The tables' constants: rdr2_atmosphere.wgsl's Rdr2Atmosphere then the orders (Rdr2Tables).</summary>
    public const int TableVectors = 9, TableBytes = TableVectors * 16;

    /// <summary>The dome's: the tables' then the night's (rdr2_sky.wgsl's Rdr2Dome).</summary>
    public const int Vectors = TableVectors + 26, Bytes = Vectors * 16;

    /// <summary>
    /// Where the camera's rays are among the dome's constants, which the renderer writes: its forward way with the
    /// aerial perspective's far clip, then its right and up ways at the frame's edge, the latter with the near plane.
    /// </summary>
    public const int CameraRays = TableVectors + 17;

    /// <summary>Where the height fog's constants are among the dome's: four after the camera's rays.</summary>
    public const int Fog = TableVectors + 20;

    /// <summary>Where the camera relative to the scene's origin is among the dome's, which the renderer writes.</summary>
    public const int RelativeCamera = TableVectors + 24;

    /// <summary>Where the far haze's constants are among the dome's: after the camera relative to the scene's origin.</summary>
    public const int FarHaze = TableVectors + 25;

    /// <summary>
    /// The dome's radius in metres, which the stars' height blends are by: as far as the game puts the sun and moon (it
    /// draws its unit dome scaled round the camera).
    /// </summary>
    public const float DomeRadius = 20000;

    // How far across a disc of size one is, in metres at the dome's radius, as the game sizes the moon's.
    private const float DiscScale = 250;

    // What the air glow's and the sky base's colours are over.
    private const float AirGlowScale = 1000;

    // Below what a multiplier is off, and how far under the horizon the sun and moon are still drawn (the game's).
    private const float Off = .0001f, Below = -.1f;

    /// <summary>The scattering orders the tables add up: the single one and three more.</summary>
    public const int Orders = 4;

    /// <summary>The weather the viewer shows RDR2's world in, as GTA V's is its sunny one.</summary>
    internal const string Weather = "common:/data/timecycle/w_highpressure.xml";

    internal const string VisualSettingsPath = "common:/data/visualsettings.dat";

    // The Rayleigh scattering a metre at a wavelength in nanometres, over its fourth power and the molecule density.
    private const double RayleighConstant = 1169939.4332487835;

    // The aerosols' extinction a metre at a density of one, and the ozone's number density the cross-sections are by.
    private const float AerosolExtinction = 4e-6f, OzoneDensity = 3.054e15f;

    // The game's ozone cross-sections, by the nanometre from its first.
    private static readonly Lazy<(int First, double[] Values)> Ozone = new(() =>
    {
        using JsonDocument document = JsonDocument.Parse(ViewportRenderer.Read("rdr2_ozone.json"));
        JsonElement root = document.RootElement;
        return (root.GetProperty("first").GetInt32(), [.. root.GetProperty("values").EnumerateArray().Select(value => value.GetDouble())]);
    });

    // The timecycle's fields at its keyframes, and the defaults its variable table gives the sky's.
    private readonly IReadOnlyDictionary<string, float[]> weather;
    private readonly GameVisualSettings visuals;

    private static readonly Dictionary<string, float> Defaults = new()
    {
        ["sky_scattering_sun_col_r"] = 1, ["sky_scattering_sun_col_g"] = 1, ["sky_scattering_sun_col_b"] = 1,
        ["sky_scattering_sky_col_r"] = 1, ["sky_scattering_sky_col_g"] = 1, ["sky_scattering_sky_col_b"] = 1,
        ["sky_scattering_multiple_scattering_intensity"] = 1,
        ["sky_scattering_aerosol_height"] = 1.2f,
        ["sky_scattering_aerosol_density_scale"] = 1,
        ["sky_scattering_aerosol_absorption_scale"] = .1f,
        ["sky_scattering_mie_scatter"] = .76f,
        ["sky_scattering_mie_intensity"] = 1,
        ["sky_scattering_altitude_bias"] = .01f,
        ["sky_scattering_molecule_desnity_scaler"] = 1,
        ["sky_scattering_ozone_concentration"] = 0,
        ["sky_scattering_red_wavelength"] = 680,
        ["sky_scattering_green_wavelength"] = 550,
        ["sky_scattering_blue_wavelength"] = 440,
        ["sky_scattering_sun_intensity_multiplier"] = 1,
        ["sky_scattering_moon_intensity_multiplier"] = 1,
        ["sky_scattering_moon_col_r"] = 1, ["sky_scattering_moon_col_g"] = 1, ["sky_scattering_moon_col_b"] = 1,
        ["sky_moon_col_r"] = 1, ["sky_moon_col_g"] = 1, ["sky_moon_col_b"] = 1,
        ["sky_moon_disc_size"] = 2.5f,
        ["sky_moon_iten"] = 1,
        ["sky_stars_iten"] = 1,
        ["sky_stars_cloud_intensity_mult"] = 1, ["sky_stars_dense_intensity_mult"] = 1, ["sky_stars_sparse_intensity_mult"] = 1,
        ["sky_stars_frequency_mult"] = 1, ["sky_stars_height_blend_mult"] = 1, ["sky_stars_blink_height_blend_mult"] = 1,
        ["sky_air_glow_color_r"] = .184f, ["sky_air_glow_color_g"] = 1, ["sky_air_glow_color_b"] = .494f,
        ["sky_air_glow_intensity"] = .2f,
        ["sky_air_glow_base_color_r"] = .072f, ["sky_air_glow_base_color_g"] = .35f, ["sky_air_glow_base_color_b"] = 1,
        ["sky_air_glow_base_intensity"] = .1f,
        ["light_dir_col_r"] = .89f, ["light_dir_col_g"] = .675f, ["light_dir_col_b"] = .392f,
        ["light_dir_mult"] = 14.75f,
        ["postfx_exposure"] = 0,
        ["postfx_exposure_min"] = -11, ["postfx_exposure_max"] = 32,
        ["postfx_exposure_blend_curve_weight"] = 0,
        ["far_clip"] = 1500,
        ["far_haze_start"] = 100, ["far_haze_power"] = 1, ["far_haze_ambient_intensity"] = .5f,
        ["fog_isotropic_phase"] = 4,
    };

    /// <summary>The dome it's drawn on and its textures, from the game's files; none without them (a sphere stands in).</summary>
    public Rdr2SkyDome? Dome { get; init; }

    /// <summary>The game's weather colour tables, from its files; none without them (the frame isn't graded by weather).</summary>
    public Rdr2Grading? Grading { get; init; }

    /// <summary>
    /// The climate the weather's colour tables are blended for: the viewer simulates none, so the middle of the game's
    /// base bands (WeatherColorCorrection.Humidity's and .Temperature's BaseMin to BaseMax) stands in, the temperate,
    /// middling table alone.
    /// </summary>
    internal (float Humidity, float Temperature) Climate =>
        ((visuals.Get("WeatherColorCorrection.Humidity.BaseMin", .49f) + visuals.Get("WeatherColorCorrection.Humidity.BaseMax", .51f)) / 2,
         (visuals.Get("WeatherColorCorrection.Temperature.BaseMin", 14) + visuals.Get("WeatherColorCorrection.Temperature.BaseMax", 16)) / 2);

    internal Rdr2Sky(IReadOnlyDictionary<string, float[]> weather, GameVisualSettings visuals)
    {
        this.weather = weather;
        this.visuals = visuals;
    }

    /// <summary>
    /// The sky from the game's files, through <paramref name="read"/> (a virtual path's bytes, or null), on the dome
    /// <paramref name="dome"/> reads; null without its weather, and no dome where that can't be read.
    /// </summary>
    public static Rdr2Sky? Read(Func<string, byte[]?> read, Func<Rdr2SkyDome?>? dome = null, Func<Rdr2Grading?>? grading = null)
    {
        IReadOnlyDictionary<string, float[]> weather;
        GameVisualSettings visuals;
        try
        {
            if (read(Weather) is not { } xml)
                return null;
            byte[]? settings = read(VisualSettingsPath);
            weather = GameTimecycle.ParseWeather(Text(xml));
            visuals = GameVisualSettings.Parse(settings is null ? "" : Text(settings));
        }
        catch (Exception error) when (error is IOException or InvalidDataException or System.Xml.XmlException or FormatException
            or ArgumentException or EndOfStreamException or NotSupportedException)
        {
            return null;
        }
        Rdr2SkyDome? shape = null;
        Rdr2Grading? tables = null;
        try
        {
            shape = dome?.Invoke();
        }
        // The dome is optional: whatever its files throw, the sky's drawn on the sphere instead.
        catch (Exception error) when (error is not OutOfMemoryException)
        {
        }
        try
        {
            tables = grading?.Invoke();
        }
        // The grading is optional too: whatever its files throw, the frame isn't graded by weather.
        catch (Exception error) when (error is not OutOfMemoryException)
        {
        }
        return new Rdr2Sky(weather, visuals) { Dome = shape, Grading = tables };
    }

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');

    // A field at the hour, between the keyframes either side, or its default where the weather hasn't it.
    private float Field(string name, TimecycleSample at, float? fallback = null)
    {
        if (weather.TryGetValue(name, out float[]? keyframes) && keyframes.Length > Math.Max(at.Index, at.Next))
            return keyframes[at.Index] + (keyframes[at.Next] - keyframes[at.Index]) * at.T;
        return fallback ?? Defaults.GetValueOrDefault(name, 0);
    }

    private Vector3 Colour(string name, TimecycleSample at, float? fallback = null) =>
        new(Field(name + "_r", at, fallback), Field(name + "_g", at, fallback), Field(name + "_b", at, fallback));

    /// <summary>The ozone's cross-section at a wavelength in nanometres, between the nanometres either side.</summary>
    internal static double OzoneAt(float wavelength)
    {
        var (first, values) = Ozone.Value;
        double at = Math.Clamp(wavelength - first, 0, values.Length - 1);
        int below = (int)Math.Floor(at), above = Math.Min(below + 1, values.Length - 1);
        return values[below] + (values[above] - values[below]) * (at - below);
    }

    /// <summary>
    /// The constants at <paramref name="hours"/>, with the sun towards <paramref name="sun"/> and the moon towards
    /// <paramref name="moon"/> (unit vectors, Z up), the world lit by the sun (<paramref name="sunLights"/>) or else the
    /// moon, the camera at <paramref name="eye"/>, and <paramref name="seconds"/> of the stars' twinkling.
    /// </summary>
    public void Write(Span<Vector4> values, float hours, Vector3 sun, Vector3 moon, bool sunLights, Vector3 eye, double seconds)
    {
        TimecycleSample at = Timecycle.Sample(hours);
        Vector3 wavelengths = new(Field("sky_scattering_red_wavelength", at), Field("sky_scattering_green_wavelength", at),
            Field("sky_scattering_blue_wavelength", at));
        float molecules = Field("sky_scattering_molecule_desnity_scaler", at);
        float Rayleigh(float wavelength) => (float)(RayleighConstant / (Math.Pow(wavelength, 4) * molecules));
        float aerosols = Field("sky_scattering_aerosol_density_scale", at) * AerosolExtinction * (Field("sky_scattering_aerosol_absorption_scale", at) + 1);
        float ozone = Field("sky_scattering_ozone_concentration", at) * OzoneDensity;
        float sunIntensity = visuals.Get("sky.SunIntensity", 100000) * Field("sky_scattering_sun_intensity_multiplier", at);
        float moonIntensity = visuals.Get("sky.MoonIntensity", 1) * Field("sky_scattering_moon_intensity_multiplier", at);
        float g = Field("sky_scattering_mie_scatter", at);
        float lit = sunIntensity + moonIntensity;
        values[0] = new(visuals.Get("sky.AltitudeRemapFactor", 1), visuals.Get("sky.AtmosphereThickness", 8) * 1000,
            Field("sky_scattering_aerosol_height", at) * 1000, Field("sky_scattering_altitude_bias", at, visuals.Get("sky.AltitudeBias", .01f)) * 1000);
        values[1] = new(Rayleigh(wavelengths.X), Rayleigh(wavelengths.Y), Rayleigh(wavelengths.Z), aerosols);
        values[2] = new((float)OzoneAt(wavelengths.X) * ozone, (float)OzoneAt(wavelengths.Y) * ozone, (float)OzoneAt(wavelengths.Z) * ozone, 0);
        values[3] = new(Colour("sky_scattering_sky_col", at) * sunIntensity, lit);
        values[4] = new(Colour("sky_scattering_sun_col", at) * sunIntensity, Field("sky_scattering_sun_disk_intensity_multiplier", at, 1));
        values[5] = new(3 / (8 * MathF.PI) * (1 - g * g) / (2 + g * g), -2 * g, 1 + g * g, Field("sky_scattering_mie_intensity", at));
        // The disc's angular radius as the moon's: its size times 250 at the dome's radius.
        values[6] = new(sun, Field("sky_sun_disc_size", at, 2.5f) * DiscScale / DomeRadius);
        values[7] = new(Field("sky_scattering_curved_transmittance_density_multiplier", at, 1),
            Field("sky_scattering_curved_transmittance_density_multiplier_power", at, 1), 1, 1);
        values[8] = new(Field("sky_scattering_multiple_scattering_intensity", at) * visuals.Get("sky.MultipleScattering", 1), 0, 0, 0);
        WriteNight(values[TableVectors..], at, hours, sun, moon, moonIntensity, seconds);
        // The camera, and the timecycle's tint of the key light the world's lit by.
        values[TableVectors + 15] = new(eye, 0);
        values[TableVectors + 16] = new(Colour("light_dir_col", at) * Field("light_dir_mult", at), sunLights ? 1 : 0);
        // The height fog, as the local atmosphere's: its density, the height it's thickest below and one over the
        // height it thins out over; its anisotropy; the tints of the key light and of the sky's ambient through it, and
        // its fill ambient, over a thousand as the air glow's colours are taken.
        values[Fog] = new(Field("local_atmosphere_fog_density", at), Field("local_atmosphere_fog_height", at),
            1 / MathF.Max(Field("local_atmosphere_fog_falloff", at, 500), 1), Math.Min(Field("local_atmosphere_anisotropy_high_density", at, .5f), .99f));
        // Then how much the sun's shadow in the fog is lifted, back towards the light.
        values[Fog + 1] = new(Colour("local_atmosphere_direct_light_color", at, 1) * Field("local_atmosphere_direct_light_intensity", at, 1),
            Field("local_atmosphere_fog_shadow_amount", at, 0));
        values[Fog + 2] = new(Colour("local_atmosphere_sky_col", at, 1) * Field("local_atmosphere_sky_intensity", at, 1), 0);
        values[Fog + 3] = new(Colour("local_atmosphere_fill_ambient_light_color", at, 0) * Field("local_atmosphere_fill_ambient_light_intensity", at, 1)
            / AirGlowScale, 0);
        // The far haze, as the game's haze is laid over the world: where it starts and one over how far it builds up,
        // out to the camera's far clip (the timecycle's far_clip), at least as far again as it starts; its power; how
        // much of the sky's ambient it takes in, over pi times the isotropic phase's divisor.
        float start = Field("far_haze_start", at);
        values[FarHaze] = new(start, 1 / (MathF.Max(Field("far_clip", at), start * 2) - start), Field("far_haze_power", at),
            Field("far_haze_ambient_intensity", at) / Math.Max(Field("fog_isotropic_phase", at), 1e-3f));
    }

    /// <summary>How far the aerial perspective reaches, in metres (sky.AerialPerspectiveFarClip).</summary>
    public float AerialFarClip => visuals.Get("sky.AerialPerspectiveFarClip", 10000);

    /// <summary>
    /// How the game exposes and tone-maps its world at an hour: PS_CalculateExposure's curve and blend curve
    /// (visualsettings.dat's), the timecycle's tweak, range, blend weight and adaptation step, and the filmic curves; and
    /// how it meters: the timecycle's spot meter (its middle and size, the frame -1 to 1 across), the lowlights and
    /// highlights left out (the latter at most 0.99 less the former, as the game takes it), visualsettings.dat's least
    /// luminance, and the timecycle's cubemap amount.
    /// </summary>
    public Rdr2Exposure Exposure(float hours)
    {
        TimecycleSample at = Timecycle.Sample(hours);
        Vector4 Curve(string name) => new(visuals.Get($"Exposure.curve{name}.scale", -1.15f), visuals.Get($"Exposure.curve{name}.verticalOffset", 6.9f),
            visuals.Get($"Exposure.curve{name}.horizontalOffset", 0), visuals.Get($"Exposure.curve{name}.bending", 0));
        return new Rdr2Exposure(Curve(""), Curve(".blend"), Field("postfx_exposure_blend_curve_weight", at), Field("postfx_exposure", at),
            Field("postfx_exposure_min", at), Field("postfx_exposure_max", at),
            Field("postfx_exposure_adaption_step_mult", at, visuals.Get("Adaptation.step.size.mult", .65f)),
            visuals.Get("Adaptation.max.step.size", 100), visuals.Get("Adaptation.min.step.size", .01f), visuals.Get("Adaptation.threshold", 0),
            GtaToneMapVisuals.From(visuals))
        {
            SpotMeter = SpotMeter(at),
            Lowlights = Field("postfx_exposure_lowlights_percent", at),
            Highlights = MathF.Min(.99f - Field("postfx_exposure_lowlights_percent", at), Field("postfx_exposure_highlights_percent", at)),
            AbsoluteMin = visuals.Get("Exposure.AbsoluteMinThreshod", 1e-6f),
            CubemapAmount = Field("postfx_exposure_cubemap_amount", at),
        };
    }

    /// <summary>
    /// How the game grades its frame at an hour (Rdr2Grading.cs, tonemap.wgsl): the timecycle's colour correction (its
    /// cut-off at least 0.0001, as the game takes it), shift and desaturation for the levels table; the weather tables'
    /// weights for the climate (at postfx_weather_color_correction); the scotopic colour with its blue shift's intensity,
    /// and the scotopic weight's scale and bias.
    /// </summary>
    public Rdr2GradeValues GradeAt(float hours)
    {
        TimecycleSample at = Timecycle.Sample(hours);
        Vector4 Bands(string name) => new(visuals.Get($"WeatherColorCorrection.{name}.Min", name == "Humidity" ? 0 : -10),
            visuals.Get($"WeatherColorCorrection.{name}.BaseMin", name == "Humidity" ? .49f : 14),
            visuals.Get($"WeatherColorCorrection.{name}.BaseMax", name == "Humidity" ? .51f : 16),
            visuals.Get($"WeatherColorCorrection.{name}.Max", name == "Humidity" ? 1 : 35));
        var (humidity, temperature) = Climate;
        return new Rdr2GradeValues(
            new Vector4(Colour("postfx_correct_col", at, 1), MathF.Max(Field("postfx_correct_cutoff", at), .0001f)),
            new Vector4(Colour("postfx_shift_col", at, 1), Field("postfx_shift_cutoff", at)),
            Field("postfx_desaturation", at, 1),
            Rdr2Grading.WeatherWeights(humidity, temperature, Bands("Humidity"), Bands("Temperature"), Field("postfx_weather_color_correction", at, 1)),
            new Vector4(Colour("postfx_scotopic_col", at, 1), Field("postfx_scotopic_blue_shift_intensity", at)),
            new Vector2(Field("postfx_scotopic_weight_scale", at), Field("postfx_scotopic_weight_bias", at)));
    }

    // The spot meter over the frame (0 to 1) from its middle and size (-1 to 1), as the game's: middle less and plus half
    // the size, then plus one over two.
    private Vector4 SpotMeter(TimecycleSample at)
    {
        float x = Field("postfx_exposure_spot_meter_x", at), y = Field("postfx_exposure_spot_meter_y", at);
        float width = Field("postfx_exposure_spot_meter_width", at, 2) / 2, height = Field("postfx_exposure_spot_meter_height", at, 2) / 2;
        return (new Vector4(x - width, y - height, x + width, y + height) + Vector4.One) * .5f;
    }

    // The night's constants (rdr2_sky.wgsl's Rdr2Night), as the game's sky update fills its dome's: the passes it draws,
    // the moon, the stars and the air glow.
    private void WriteNight(Span<Vector4> values, TimecycleSample at, float hours, Vector3 sun, Vector3 moon, float moonIntensity,
        double seconds)
    {
        bool sunDrawn = Field("sky_scattering_sun_intensity_multiplier", at) >= Off && sun.Z >= Below;
        bool moonDrawn = Field("sky_scattering_moon_intensity_multiplier", at) >= Off && moon.Z >= Below;
        bool starsDrawn = Field("sky_stars_iten", at) >= Off;
        values[0] = new(sunDrawn ? 1 : 0, moonDrawn ? 1 : 0, starsDrawn ? 1 : 0, DomeRadius);
        // The disc as the game places it: its size times 250 across from its middle, the dome's radius away.
        float across = Field("sky_moon_disc_size", at) * DiscScale / DomeRadius;
        values[1] = new(moon, across);
        values[2] = new(Colour("sky_scattering_sky_col", at) * moonIntensity, 1 / MathF.Sqrt(1 + across * across));
        values[3] = new(Colour("sky_scattering_moon_col", at) * moonIntensity, 0);
        values[4] = new(Colour("sky_moon_col", at) * Field("sky_moon_iten", at), 0);
        // Each layer's intensity under the galaxy's light (the game's moonless night, its stars at their brightest),
        // times its colour, the timecycle's multiplier and its multiplier under that light.
        Vector3 Layer(string name) => visuals.Get($"stars.{name}.intensityGalaxy", 1) * Visual($"stars.{name}.color")
            * Field($"sky_stars_{name}_intensity_mult", at) * MathF.Max(Field($"sky_stars_{name}_galaxy_light_mult", at, 1), 1);
        values[5] = new(Layer("cloud"), visuals.Get("stars.cloud.axisBlend", 4));
        values[6] = new(Layer("dense"), visuals.Get("stars.dense.axisBlend", 4));
        values[7] = new(Layer("sparse"), visuals.Get("stars.sparse.axisBlend", .5f));
        float time = (float)(seconds % 3600) * visuals.Get("stars.frequency", 2) * Field("sky_stars_frequency_mult", at);
        values[8] = new(visuals.Get("stars.dense.tiling", 4), visuals.Get("stars.sparse.tiling", 4), time, visuals.Get("stars.noiseTiling", 4));
        values[9] = new(1 / (visuals.Get("stars.heightBlend", 3000) * Field("sky_stars_height_blend_mult", at)),
            1 / (visuals.Get("stars.blinkHeightBlend", 24000) * Field("sky_stars_blink_height_blend_mult", at)),
            visuals.Get("stars.blinkGain.mult", 8), visuals.Get("stars.blinkGain.bias", .5f));
        Matrix4x4 rotation = StarsRotation(hours);
        values[10] = new(rotation.M11, rotation.M12, rotation.M13, 0);
        values[11] = new(rotation.M21, rotation.M22, rotation.M23, 0);
        values[12] = new(rotation.M31, rotation.M32, rotation.M33, 0);
        // The air glow and the sky's base colour by their intensities, over a thousand as the game takes them, and by
        // their multiplier under the galaxy's light.
        float glow = MathF.Max(Field("sky_air_glow_galaxy_light_mult", at, 1), 1) / AirGlowScale;
        values[13] = new(Colour("sky_air_glow_color", at) * Field("sky_air_glow_intensity", at) * glow, 0);
        values[14] = new(Colour("sky_air_glow_base_color", at) * Field("sky_air_glow_base_intensity", at) * glow, 0);
    }

    // A colour visualsettings.dat gives as its .red, .green and .blue.
    private Vector3 Visual(string name) => new(visuals.Get(name + ".red", 1), visuals.Get(name + ".green", 1), visuals.Get(name + ".blue", 1));

    // An axis visualsettings.dat gives as its .x, .y and .z, made a unit vector.
    private Vector3 Axis(string name, Vector3 fallback)
    {
        var axis = new Vector3(visuals.Get(name + ".x", fallback.X), visuals.Get(name + ".y", fallback.Y), visuals.Get(name + ".z", fallback.Z));
        return axis.LengthSquared() > 0 ? Vector3.Normalize(axis) : Vector3.UnitZ;
    }

    /// <summary>
    /// How the stars are turned at an hour, as the game turns them (a direction times it): about the north axis by its
    /// angle and the rotation rate times the time of day in radians, then from the zenith to the galaxy's axis, then
    /// about Z by the galaxy's angle.
    /// </summary>
    internal Matrix4x4 StarsRotation(float hours)
    {
        Vector3 north = Axis("stars.northAxis", new Vector3(0, .888f, .46f)), galaxy = Axis("stars.galaxyAxis", Vector3.UnitY);
        float day = Timecycle.WrapHours(hours) * MathF.PI / 12;
        float turn = day * visuals.Get("stars.rotationRate", .15f) % (2 * MathF.PI) + visuals.Get("stars.northAxisAngle", 2.048f);
        Matrix4x4 rotation = Matrix4x4.CreateFromAxisAngle(north, turn);
        Vector3 between = Vector3.Cross(Vector3.UnitZ, galaxy);
        if (between.LengthSquared() > 1e-12f)
            rotation *= Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(between), MathF.Acos(Math.Clamp(galaxy.Z, -1, 1)));
        return rotation * Matrix4x4.CreateRotationZ(visuals.Get("stars.galaxyAxisAngle", 2.048f));
    }

    /// <summary>Whether the tables made from <paramref name="a"/> are those made from <paramref name="b"/>: the same air and orders.</summary>
    public static bool SameTables(ReadOnlySpan<Vector4> a, ReadOnlySpan<Vector4> b) =>
        a[0].X == b[0].X && a[0].Y == b[0].Y && a[0].Z == b[0].Z && a[1] == b[1] && a[2] == b[2] && a[8] == b[8];

    /// <summary>The constants the tables are built from (those <see cref="SameTables"/> compares), from one set to another.</summary>
    public static void CopyTables(ReadOnlySpan<Vector4> from, Span<Vector4> to)
    {
        to[0] = new Vector4(from[0].X, from[0].Y, from[0].Z, to[0].W);
        to[1] = from[1];
        to[2] = from[2];
        to[8] = from[8];
    }
}
