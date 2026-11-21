using System.Numerics;
using FiveFury.Textures;

namespace HikariHopper.Rendering;

/// <summary>
/// GTA V's water as Gta5/water.wgsl shades it, a port of water.fxc's PS_WaterSinglePass: the textures the game binds
/// (graphics.ytd's waterfog, waterbump, waterbump2, waternoise and waterfoam; a river's own fog texture, read on its UVs)
/// and the values it tunes it with (watertune.xml's, and a water shader's own RippleScale, SpecularIntensity and
/// SpecularFalloff). The game's CPU sets three more that aren't found yet: the wave bump's tiling, the static bump's
/// strength and the sunlight scattered back out of the water, here <see cref="WaveTiling"/>, <see cref="StaticBump"/>
/// and <see cref="SunScatter"/>.
/// </summary>
internal sealed record GtaWater
{
    /// <summary>The bytes of Gta5/water.wgsl's Water uniform.</summary>
    public const int UniformBytes = 64;

    /// <summary>The fog colour: over the whole map by world position (the ocean's), or a river's own on its UVs.</summary>
    public Texture? Fog { get; init; }

    public Texture? Bump { get; init; }

    /// <summary>The wave bump: the game simulates it; the viewer reads graphics.ytd's waterbump2 as it is.</summary>
    public Texture? WaveBump { get; init; }

    public Texture? Noise { get; init; }

    public Texture? Foam { get; init; }

    /// <summary>Whether <see cref="Fog"/> covers the map, <see cref="FogMin"/> to <see cref="FogMax"/>, by world position.</summary>
    public bool WorldFog { get; init; }

    public Vector2 FogMin { get; init; } = new(-4000, -4000);

    public Vector2 FogMax { get; init; } = new(4500, 8000);

    public float RippleScale { get; init; } = .04f;

    public float SpecularIntensity { get; init; } = 1;

    public float SpecularFalloff { get; init; } = 1118;

    public float FoamScale { get; init; } = .05f;

    /// <summary>The wave bump's tiling, per metre (inferred).</summary>
    public const float WaveTiling = 1 / 16f;

    /// <summary>How much the two drifting static bumps bend the surface (inferred).</summary>
    public const float StaticBump = .5f;

    /// <summary>How much sunlight comes back out through the surface (inferred).</summary>
    public const float SunScatter = 1;

    /// <summary>The textures it binds, for putting them up first.</summary>
    public IEnumerable<Texture?> Textures => [Fog, Bump, WaveBump, Noise, Foam];

    /// <summary>Gta5/water.wgsl's Water uniform.</summary>
    public float[] Uniforms() =>
    [
        FogMin.X, FogMin.Y, FogMax.X, FogMax.Y,
        RippleScale, SpecularIntensity, SpecularFalloff, FoamScale,
        WorldFog ? 1 : 0, WaveTiling, StaticBump, SunScatter,
        0, 0, 0, 0,
    ];
}
