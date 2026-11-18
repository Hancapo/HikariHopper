using System.Numerics;
using System.Text;
using System.Text.Json;
using HikariHopper.Core;

namespace HikariHopper.Rendering;

/// <summary>
/// Red Dead Redemption 2's sky as its files set it: the weather's timecycle (its GLOBAL region's sky_scattering_* fields,
/// at the keyframes) and visualsettings.dat's sky.* settings. Works out, for an hour, what the game's sky update puts in
/// its frame's constants for the sky tables (rdr2_sky_tables.wgsl) and the dome (rdr2_sky.wgsl): the wavelengths'
/// Rayleigh scattering, the aerosols' extinction, the ozone's absorption, the scale heights, and the sun's light.
/// </summary>
internal sealed class Rdr2Sky
{
    /// <summary>The constants' size: rdr2_atmosphere.wgsl's Rdr2Atmosphere then the tables' orders (Rdr2Tables).</summary>
    public const int Vectors = 9, Bytes = Vectors * 16;

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
    };

    internal Rdr2Sky(IReadOnlyDictionary<string, float[]> weather, GameVisualSettings visuals)
    {
        this.weather = weather;
        this.visuals = visuals;
    }

    /// <summary>The sky from the game's files, through <paramref name="read"/> (a virtual path's bytes, or null); null without its weather.</summary>
    public static Rdr2Sky? Read(Func<string, byte[]?> read)
    {
        try
        {
            if (read(Weather) is not { } xml)
                return null;
            byte[]? settings = read(VisualSettingsPath);
            return new Rdr2Sky(GameTimecycle.ParseWeather(Text(xml)), GameVisualSettings.Parse(settings is null ? "" : Text(settings)));
        }
        catch (Exception error) when (error is IOException or InvalidDataException or System.Xml.XmlException or FormatException
            or ArgumentException or EndOfStreamException or NotSupportedException)
        {
            return null;
        }
    }

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes).TrimStart('﻿');

    // A field at the hour, between the keyframes either side, or its default where the weather hasn't it.
    private float Field(string name, TimecycleSample at, float? fallback = null)
    {
        if (weather.TryGetValue(name, out float[]? keyframes) && keyframes.Length > Math.Max(at.Index, at.Next))
            return keyframes[at.Index] + (keyframes[at.Next] - keyframes[at.Index]) * at.T;
        return fallback ?? Defaults.GetValueOrDefault(name, 0);
    }

    private Vector3 Colour(string name, TimecycleSample at) => new(Field(name + "_r", at), Field(name + "_g", at), Field(name + "_b", at));

    /// <summary>The ozone's cross-section at a wavelength in nanometres, between the nanometres either side.</summary>
    internal static double OzoneAt(float wavelength)
    {
        var (first, values) = Ozone.Value;
        double at = Math.Clamp(wavelength - first, 0, values.Length - 1);
        int below = (int)Math.Floor(at), above = Math.Min(below + 1, values.Length - 1);
        return values[below] + (values[above] - values[below]) * (at - below);
    }

    /// <summary>
    /// The constants at <paramref name="hours"/>, with the sun towards <paramref name="sun"/> (a unit vector, Z up) and
    /// the viewer's directional light <paramref name="directional"/>, which the sky's radiance is scaled to as the world
    /// is lit by it.
    /// </summary>
    public void Write(Span<Vector4> values, float hours, Vector3 sun, Vector3 directional)
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
        // The viewer lights a surface facing the sun with its directional light as it is: an irradiance of pi times it.
        float luminance = Vector3.Dot(directional, new Vector3(.2126f, .7152f, .0722f));
        float scale = MathF.PI * luminance / MathF.Max(lit, 1e-3f);
        values[0] = new(visuals.Get("sky.AltitudeRemapFactor", 1), visuals.Get("sky.AtmosphereThickness", 8) * 1000,
            Field("sky_scattering_aerosol_height", at) * 1000, Field("sky_scattering_altitude_bias", at, visuals.Get("sky.AltitudeBias", .01f)) * 1000);
        values[1] = new(Rayleigh(wavelengths.X), Rayleigh(wavelengths.Y), Rayleigh(wavelengths.Z), aerosols);
        values[2] = new((float)OzoneAt(wavelengths.X) * ozone, (float)OzoneAt(wavelengths.Y) * ozone, (float)OzoneAt(wavelengths.Z) * ozone, 0);
        values[3] = new(Colour("sky_scattering_sky_col", at) * sunIntensity, lit);
        values[4] = new(Colour("sky_scattering_sun_col", at) * sunIntensity, Field("sky_scattering_sun_disk_intensity_multiplier", at, 1));
        values[5] = new(3 / (8 * MathF.PI) * (1 - g * g) / (2 + g * g), -2 * g, 1 + g * g, Field("sky_scattering_mie_intensity", at));
        // The disc's angular radius as GTA V's: its size times 250 at the dome's 20 km.
        values[6] = new(sun, Field("sky_sun_disc_size", at, 2.5f) * 250 / 20000);
        values[7] = new(Field("sky_scattering_curved_transmittance_density_multiplier", at, 1),
            Field("sky_scattering_curved_transmittance_density_multiplier_power", at, 1), 1, scale);
        values[8] = new(Field("sky_scattering_multiple_scattering_intensity", at) * visuals.Get("sky.MultipleScattering", 1), 0, 0, 0);
    }

    /// <summary>Whether the tables made from <paramref name="a"/> are those made from <paramref name="b"/>: the same air and orders.</summary>
    public static bool SameTables(ReadOnlySpan<Vector4> a, ReadOnlySpan<Vector4> b) =>
        a[0].X == b[0].X && a[0].Y == b[0].Y && a[0].Z == b[0].Z && a[1] == b[1] && a[2] == b[2] && a[8] == b[8];
}
