using System.Numerics;
using CommonFury.Assets;
using CommonFury.Vertices;
using FiveFury.Drawables;
using FiveFury.Textures;
using GameTexture = FiveFury.Textures.Texture;

namespace HikariHopper.Rendering;

/// <summary>
/// GTA V's sky dome and the textures its sky is drawn with, as the game has them: <c>models/skydome.ydd</c>'s dome (a
/// sphere of radius one, with the clouds' coordinates and the stars' and dither's) and <c>textures/skydome.ytd</c>'s
/// noise, detail, dither, star field and moon.
/// </summary>
/// <param name="Vertices">Each vertex's place, then its two sets of coordinates: <see cref="Stride"/> bytes each.</param>
/// <param name="Indices">The dome's triangles.</param>
/// <param name="Visuals">visualsettings.dat's settings for the sky.</param>
internal sealed record GtaSkyDome(float[] Vertices, uint[] Indices, GameTexture? Noise, GameTexture? HighDetail, GameTexture? Dither,
    GameTexture? Stars, GameTexture? Moon, GtaSkyVisuals Visuals)
{
    /// <summary>A vertex's bytes: its place, its clouds' coordinates and its stars' and dither's.</summary>
    public const int Stride = 28;

    /// <summary>The dome from its drawable and its dictionary; null when the drawable has no triangles.</summary>
    public static GtaSkyDome? From(Drawable dome, TextureDictionary? textures, GtaSkyVisuals visuals)
    {
        var vertices = new List<float>();
        var indices = new List<uint>();
        foreach (DrawableModel model in dome.Lods[DrawableLod.High].Models)
            foreach (DrawableGeometry geometry in model.Geometries)
            {
                if (geometry.PrimitiveType != 3)
                    continue;
                VertexBuffer buffer = geometry.Vertices;
                uint first = (uint)(vertices.Count / (Stride / sizeof(float)));
                for (int i = 0; i < buffer.Count; i++)
                {
                    Vector4 place = buffer.ReadElement(i, VertexSemantic.Position);
                    Vector4 clouds = buffer.Layout.TryFind(VertexSemantic.TexCoord0, out _) ? buffer.ReadElement(i, VertexSemantic.TexCoord0) : default;
                    Vector4 stars = buffer.Layout.TryFind(VertexSemantic.TexCoord1, out _) ? buffer.ReadElement(i, VertexSemantic.TexCoord1) : default;
                    vertices.AddRange([place.X, place.Y, place.Z, clouds.X, clouds.Y, stars.X, stars.Y]);
                }
                ReadOnlySpan<ushort> from = geometry.Indices.Indices;
                int start = (int)Math.Min(geometry.IndexBufferOffset, (uint)from.Length);
                int count = Math.Min(geometry.IndexCount > 0 ? geometry.IndexCount : from.Length, from.Length - start) / 3 * 3;
                for (int i = 0; i < count; i++)
                    indices.Add(from[start + i] < buffer.Count ? first + from[start + i] : first);
            }
        if (indices.Count == 0)
            return null;
        GameTexture? Named(string name) =>
            textures?.Textures.FirstOrDefault(texture => string.Equals(texture.Name, name, StringComparison.OrdinalIgnoreCase));
        return new GtaSkyDome([.. vertices], [.. indices], Named("baseperlinnoise3channel"), Named("noise16_p"), Named("dither"),
            Named("starfield"), Named("moon-new"), visuals);
    }
}

/// <summary>
/// The sky's settings in visualsettings.dat: the clouds' edge and overlay detail scales,
/// and the speeds of the large clouds, the small ones, the overall detail and the edge detail.
/// </summary>
internal sealed record GtaSkyVisuals(float EdgeDetailScale, float OverallDetailScale, float CloudSpeed, float SmallCloudSpeed, float OverallDetailSpeed,
    float EdgeDetailSpeed)
{
    /// <summary>The game's file's settings, each with the default the game's code reads it with where the file hasn't it.</summary>
    public static GtaSkyVisuals From(Core.GameVisualSettings? file) => new(
        file?.Get("cloudgen.edge.detail.scale", 16) ?? 16,
        file?.Get("cloudgen.overlay.detail.scale", 8) ?? 8,
        file?.Get("cloud.speed.large", 5) ?? 5,
        file?.Get("cloud.speed.small", 1) ?? 1,
        file?.Get("cloud.speed.overall", 1) ?? 1,
        file?.Get("cloud.speed.edge", 1) ?? 1);
}

/// <summary>
/// The constants GTA V draws its sky with (gta_sky.wgsl's GtaSky), as the game works them out each frame from the
/// timecycle: its sky and noise settings, their colours as the game sends them
/// (squared, times their intensities), where the sun and moon
/// are on the dome, which of its passes it draws, and the fog it
/// takes them through (globalFogParams and the fog colours). The cloud noise drifts as the game
/// moves it with no wind, along +X.
/// </summary>
internal static class GtaSky
{
    /// <summary>The constants' size: gta_sky.wgsl's GtaSky.</summary>
    public const int Bytes = 39 * 16;

    // The dome's scale (DOME_RADIUS) and the discs' (the sun's by its disc size multiplier, the moon's by 250).
    private const float DomeScale = 20000, DiscScale = 250;

    // The noise's phase moves by the wind's direction times the cloud speed times g_NoisePhaseMult squared a second.
    private const float NoisePhaseMult = .01f;

    // The water's level, which the dome is centred at and the horizon is.
    private const float WaterLevel = 0;

    /// <summary>
    /// Where the viewer is in the game's lunar cycle (fmod(time, 27) / 26): with
    /// its light straight from the viewer, the moon is full, as DayCycle puts it opposite the sun.
    /// </summary>
    private const float LunarCycle = 2.9f / 5.6f;

    /// <summary>Whether the game draws each of its sky passes: the sun, the moon, the clouds, the stars.</summary>
    internal readonly record struct Passes(bool Sun, bool Moon, bool Clouds, bool Stars);

    /// <summary>
    /// The sky's constants at <paramref name="hours"/>, with visualsettings.dat's <paramref name="visuals"/>, seen from
    /// <paramref name="camera"/> (in the world), with the cloud noise <paramref name="seconds"/> into its drift.
    /// </summary>
    internal static Passes Write(Span<Vector4> values, GtaSkyVisuals visuals, float hours, SkyPath? path, Vector3 camera, double seconds)
    {
        if (values.Length < Bytes / 16)
            throw new ArgumentException("Too small for the sky's constants.", nameof(values));
        TimecycleSample tc = Timecycle.Sample(hours);
        float Get(string name) => tc.Get(name);
        Vector3 Colour(string name) => tc.Colour(name);
        Vector3 Linear(string name) => Square(Colour(name));
        Vector3 Sky(string name) => Linear(name) * Get(name + "_inten");

        // Where the sun and moon are: their sprites' directions, on the dome around the camera.
        Vector3 sunSprite = DayCycle.SunDirection(hours, path), moonSprite = DayCycle.MoonDirection(hours, path);
        var centre = new Vector3(camera.X, camera.Y, WaterLevel);
        Vector3 sunPosition = centre + sunSprite * DomeScale, moonPosition = centre + moonSprite * DomeScale;

        float sunHdr = Get("sky_sun_hdr"), moonIntensity = Get("sky_moon_iten"), stars = Get("sky_stars_iten");
        float density = Get("sky_cloud_density_mult"), bias = Get("sky_cloud_density_bias");
        float smallStrength = Get("sky_small_cloud_detail_strength"), smallDensity = Get("sky_small_cloud_density_mult"), smallBias = Get("sky_small_cloud_density_bias");
        var passes = new Passes(
            sunHdr >= .0001f && sunSprite.Z >= -1e-5f,
            moonIntensity >= .0001f && moonSprite.Z >= -1e-5f,
            density > bias || (smallDensity > smallBias && smallStrength > 0),
            stars >= .0001f);

        float cloudHdr = Get("sky_cloud_hdr"), shadowStrength = Get("sky_cloud_shadow_strength");
        Vector3 cloudBase = Linear("sky_cloud_base_col"), cloudShadow = Linear("sky_cloud_shadow_col"), cloudMid = Linear("sky_cloud_mid_col");
        // The detail scrolls with the time of day (the cycle's time, in days).
        float time = WrapHours(hours) / 24;
        static float Fraction(float value) => value - MathF.Floor(value);

        // The sun's glow: Cornette-Shanks with g from the mie phase.
        float g = -.49f - Get("sky_sun_miephase") / 2, g2 = g * g;

        // The lunar cycle's light: the Y axis turned about Z across the cycle.
        float lunar = float.Lerp(-2.9f, 2.9f - .2f, LunarCycle);
        var lunarLight = new Vector3(-MathF.Sin(lunar), MathF.Cos(lunar), 0);

        // The cloud noise's phase: from one, along the wind (none: +X), wrapped.
        float drift = (float)(visuals.CloudSpeed * NoisePhaseMult * NoisePhaseMult * (seconds % 1e6));
        var phase = new Vector2(Fraction(1 + drift), 0);

        values[0] = new(Sky("sky_azimuth_east_col"), Get("sky_azimuth_transition_position"));
        values[1] = new(Sky("sky_azimuth_west_col"), Get("sky_hdr"));
        values[2] = new(Sky("sky_azimuth_transition_col"), WaterLevel);
        values[3] = new(Sky("sky_zenith_col"), stars);
        values[4] = new(Sky("sky_zenith_transition_col"), moonIntensity);
        values[5] = new(Get("sky_zenith_transition_position"), Get("sky_zenith_transition_east_blend"), Get("sky_zenith_transition_west_blend"),
            1 - Get("sky_zenith_blend_start"));
        values[6] = new(Linear("sky_sun_col"), Get("sky_sun_disc_size") * DiscScale);
        values[7] = new(Linear("sky_sun_col") * sunHdr, Get("sky_moon_disc_size") * DiscScale);
        values[8] = new(Linear("sky_sun_disc_col") * sunHdr, 0);
        values[9] = new(2 * g, g2 + 1, Get("sky_sun_miescatter") / 1000 * (1.5f * ((1 - g2) / (2 + g2))), Get("sky_sun_mie_intensity_mult"));
        // As the game sends them: the positions on the dome, made unit from the world's origin.
        values[10] = new(Vector3.Normalize(sunPosition), 0);
        values[11] = new(Vector3.Normalize(moonPosition), 0);
        values[12] = new(sunPosition, 0);
        values[13] = new(moonPosition, 0);
        values[14] = new(cloudBase - cloudMid, 0);
        values[15] = new(cloudMid, 0);
        values[16] = new((cloudShadow - cloudBase) * shadowStrength, 0);
        values[17] = new(Get("sky_cloud_edge_strength"), visuals.EdgeDetailScale, Get("sky_cloud_overall_strength"), visuals.OverallDetailScale);
        values[18] = new(Get("sky_cloud_base_strength"), density, bias, Get("sky_cloud_fadeout"));
        values[19] = new(shadowStrength, Get("sky_cloud_offset"), Get("sky_cloud_overall_color"), cloudHdr);
        values[20] = new(Get("sky_small_cloud_detail_scale"), smallStrength, smallDensity, smallBias);
        values[21] = new(Colour("sky_small_cloud_col") * cloudHdr, 0);
        values[22] = new(Get("sky_sun_influence_radius"), Get("sky_sun_scatter_inten"), Get("sky_moon_influence_radius"), Get("sky_moon_scatter_inten"));
        values[23] = new(Fraction(.5f + visuals.SmallCloudSpeed * time / 11), Fraction(.5f + visuals.OverallDetailSpeed * time / 11), Fraction(.5f + visuals.EdgeDetailSpeed * time / 11), 0);
        values[24] = new(lunarLight, 0);
        values[25] = new(Linear("sky_moon_col"), 0);
        values[26] = new(Get("sky_cloud_gen_frequency"), Get("sky_cloud_gen_scale"), Get("sky_cloud_gen_threshold"), Get("sky_cloud_gen_softness"));
        values[27] = new(Get("sky_cloud_gen_density_offset"), phase.X, phase.Y, 0);
        values[28] = new(passes.Sun ? 1 : 0, passes.Moon ? 1 : 0, passes.Clouds ? 1 : 0, passes.Stars ? 1 : 0);
        Fog(values[29..38], tc, hours, path, camera.Z, sunSprite, moonSprite);
        values[38] = new(camera, 0);
        return passes;
    }

    /// <summary>
    /// The fog's constants, as the game sets them for a camera at this height: globalFogParams, then
    /// the fog's colours towards the sun, of the atmosphere and of the ground (each with a channel of the haze's), and
    /// towards the moon.
    /// </summary>
    private static void Fog(Span<Vector4> values, TimecycleSample tc, float hours, SkyPath? path, float height, Vector3 sun, Vector3 moon)
    {
        float Get(string name) => tc.Get(name);
        float start = Get("fog_start");
        float end = MathF.Max(Get("far_clip"), start);
        float nearDensity = Get("fog_density") / 10000, falloff = Get("fog_falloff") / 1000;
        // Kept from exploding under the fog's base.
        float atViewer = MathF.Exp(MathF.Min(-falloff * (height - Get("fog_base_height")), 20));
        float farDensity = Get("fog_haze_density") / 10000, farAlpha = Get("fog_haze_alpha");
        values[0] = new(start, end, 0, 0);
        values[1] = new(-farDensity, farAlpha, Get("fog_horizon_tint_scale") / end, -MathF.Min(1, atViewer * nearDensity));
        values[2] = new(Get("fog_haze_start"), Get("fog_alpha"), falloff, Math.Clamp(1 - MathF.Exp(farDensity * end), 0, 1) * farAlpha);
        values[3] = new(sun, Get("fog_sun_lighting_calc_pow"));
        values[4] = new(moon, Get("fog_moon_lighting_calc_pow"));

        float hdr = Get("fog_hdr");
        Vector3 sunColour = tc.Colour("fog_col") * hdr, moonColour = tc.Colour("fog_moon_col") * hdr;
        Vector3 atmosphere = tc.Colour("fog_east_col") * hdr, ground = tc.Colour("fog_near_col") * hdr;
        Vector3 haze = tc.Colour("fog_haze_col") * Get("fog_haze_hdr");
        // The far colour towards the near one by how dense the fog is at the camera.
        atmosphere = Vector3.Lerp(atmosphere, ground, MathF.Pow(Math.Clamp(atViewer, 0, 1), .3f));
        var (sunFade, moonFade) = DayCycle.Fades(hours, path);
        values[5] = new(Vector3.Lerp(atmosphere, sunColour, MathF.Max(sunFade, moonFade)), haze.X);
        values[6] = new(atmosphere, haze.Y);
        values[7] = new(ground, haze.Z);
        values[8] = new(moonColour, 0);
    }

    private static Vector3 Square(Vector3 colour) => colour * colour;

    private static float WrapHours(float hours)
    {
        float hour = hours % 24;
        return hour < 0 ? hour + 24 : hour;
    }
}
