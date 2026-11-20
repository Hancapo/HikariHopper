using System.Numerics;
using HikariHopper.Core;

namespace HikariHopper.Rendering;

/// <summary>
/// Where in GTA V's world the timecycle is taken, as the game works it out from the camera's position each frame:
/// how strongly the weather's second region (URBAN) is blended over GLOBAL, and the box
/// modifiers in force there, each with its strength, in the order the game applies them. No place, as in the model
/// viewer, is GLOBAL alone, as the game would have it with no position.
/// </summary>
internal sealed class TimecyclePlace
{
    /// <summary>The strength of the weather's second region, 0 to 1.</summary>
    public float Region { get; set; }

    /// <summary>The box modifiers in force, by their order among the game's modifiers, each with its strength.</summary>
    public List<(GameTimecycleModifier Modifier, float Strength)> Modifiers { get; } = [];

    /// <summary>Whether this is GLOBAL alone: no region and no modifier.</summary>
    public bool IsGlobal => Region <= 0 && Modifiers.Count == 0;

    /// <summary>Whether <paramref name="other"/> is the same place to the timecycle.</summary>
    public bool Matches(TimecyclePlace other)
    {
        if (Region != other.Region || Modifiers.Count != other.Modifiers.Count)
            return false;
        for (int i = 0; i < Modifiers.Count; i++)
            if (!ReferenceEquals(Modifiers[i].Modifier, other.Modifiers[i].Modifier) || Modifiers[i].Strength != other.Modifiers[i].Strength)
                return false;
        return true;
    }

    /// <summary>The largest change in a region's or a modifier's strength from <paramref name="other"/>; one where they differ in modifiers.</summary>
    public float Distance(TimecyclePlace other)
    {
        if (Modifiers.Count != other.Modifiers.Count)
            return 1;
        float distance = MathF.Abs(Region - other.Region);
        for (int i = 0; i < Modifiers.Count; i++)
        {
            if (!ReferenceEquals(Modifiers[i].Modifier, other.Modifiers[i].Modifier))
                return 1;
            distance = MathF.Max(distance, MathF.Abs(Modifiers[i].Strength - other.Modifiers[i].Strength));
        }
        return distance;
    }

    public void CopyFrom(TimecyclePlace other)
    {
        Region = other.Region;
        Modifiers.Clear();
        Modifiers.AddRange(other.Modifiers);
    }

    public void Clear()
    {
        Region = 0;
        Modifiers.Clear();
    }
}

/// <summary>
/// The timecycle's keyframe at one time and place, every field worked out once (the region blend, then the
/// box modifiers over it) and read from then on: made again only when the hour or the place changes.
/// </summary>
internal sealed class TimecycleFrame
{
    private readonly Dictionary<string, float> values = [];

    internal Dictionary<string, float> Values => values;

    /// <summary>The timecycle the fields were worked out from: another one starts them again.</summary>
    internal object? Source { get; set; }

    /// <summary>The field's value, nought where the timecycle hasn't it.</summary>
    public float Get(string name) => values.TryGetValue(name, out float value) ? value : 0;
}

/// <summary>
/// How a timecycle modifier changes each field it sets, as the game's variable table gives each field its
/// kind of change and the game applies it: in the game's code, not its data files. Fields not listed interpolate.
/// </summary>
internal static class TimecycleModifierBlend
{
    internal enum Kind : byte { Interpolate, None, InterpolateMultiplied, Clamp, Max }

    private static readonly Dictionary<string, Kind> Kinds = Build();

    internal static Kind Of(string name) => Kinds.GetValueOrDefault(name);

    /// <summary>A field's value with a modifier's values (<paramref name="a"/>, <paramref name="b"/>) applied at strength <paramref name="t"/>.</summary>
    internal static float Apply(Kind kind, float value, float a, float b, float t)
    {
        float inverse = 1 - t;
        switch (kind)
        {
            case Kind.Interpolate:
                return inverse * value + t * a;
            case Kind.InterpolateMultiplied:
                float local = b * t;
                return (1 - local) * value + local * a;
            case Kind.Clamp:
                // Both against the value before: the second, where both hold, wins.
                float result = value;
                if (value < a)
                    result = inverse * value + t * a;
                if (value > b)
                    result = inverse * value + t * b;
                return result;
            case Kind.Max:
                return MathF.Max(inverse * value, t * a);
            default:
                return value;
        }
    }

    private static Dictionary<string, Kind> Build()
    {
        var kinds = new Dictionary<string, Kind>();
        void Add(Kind kind, params string[] names)
        {
            foreach (string name in names)
                kinds[name] = kind;
        }
        Add(Kind.None,
            "sky_zenith_transition_position", "sky_zenith_transition_east_blend", "sky_zenith_transition_west_blend",
            "sky_zenith_blend_start", "sky_zenith_col_r", "sky_zenith_col_g", "sky_zenith_col_b", "sky_zenith_col_inten",
            "sky_zenith_transition_col_r", "sky_zenith_transition_col_g", "sky_zenith_transition_col_b",
            "sky_zenith_transition_col_inten", "sky_azimuth_transition_position", "sky_azimuth_east_col_r",
            "sky_azimuth_east_col_g", "sky_azimuth_east_col_b", "sky_azimuth_east_col_inten",
            "sky_azimuth_transition_col_r", "sky_azimuth_transition_col_g", "sky_azimuth_transition_col_b",
            "sky_azimuth_transition_col_inten", "sky_azimuth_west_col_r", "sky_azimuth_west_col_g",
            "sky_azimuth_west_col_b", "sky_azimuth_west_col_inten", "sky_hdr", "sky_sun_col_r", "sky_sun_col_g",
            "sky_sun_col_b", "sky_sun_disc_col_r", "sky_sun_disc_col_g", "sky_sun_disc_col_b", "sky_sun_disc_size",
            "sky_sun_hdr", "sky_sun_miephase", "sky_sun_miescatter", "sky_sun_mie_intensity_mult",
            "sky_sun_influence_radius", "sky_sun_scatter_inten", "sky_moon_col_r", "sky_moon_col_g", "sky_moon_col_b",
            "sky_moon_disc_size", "sky_moon_iten", "sky_stars_iten", "sky_moon_influence_radius", "sky_moon_scatter_inten",
            "sky_cloud_gen_frequency", "sky_cloud_gen_scale", "sky_cloud_gen_threshold", "sky_cloud_gen_softness",
            "sky_cloud_density_mult", "sky_cloud_density_bias", "sky_cloud_mid_col_r", "sky_cloud_mid_col_g",
            "sky_cloud_mid_col_b", "sky_cloud_base_col_r", "sky_cloud_base_col_g", "sky_cloud_base_col_b",
            "sky_cloud_base_strength", "sky_cloud_shadow_col_r", "sky_cloud_shadow_col_g", "sky_cloud_shadow_col_b",
            "sky_cloud_shadow_strength", "sky_cloud_gen_density_offset", "sky_cloud_offset", "sky_cloud_overall_strength",
            "sky_cloud_overall_color", "sky_cloud_edge_strength", "sky_cloud_fadeout", "sky_cloud_hdr",
            "sky_cloud_dither_strength", "sky_small_cloud_col_r", "sky_small_cloud_col_g", "sky_small_cloud_col_b",
            "sky_small_cloud_detail_strength", "sky_small_cloud_detail_scale", "sky_small_cloud_density_mult",
            "sky_small_cloud_density_bias", "cloud_shadow_density", "cloud_shadow_softness", "cloud_shadow_opacity");
        Add(Kind.InterpolateMultiplied,
            "light_dir_col_r", "light_dir_col_g", "light_dir_col_b", "light_dir_mult", "light_directional_amb_col_r",
            "light_directional_amb_col_g", "light_directional_amb_col_b", "light_natural_amb_down_col_r",
            "light_natural_amb_down_col_g", "light_natural_amb_down_col_b", "light_natural_amb_up_col_r",
            "light_natural_amb_up_col_g", "light_natural_amb_up_col_b", "light_artificial_int_down_col_r",
            "light_artificial_int_down_col_g", "light_artificial_int_down_col_b", "light_artificial_int_up_col_r",
            "light_artificial_int_up_col_g", "light_artificial_int_up_col_b", "light_artificial_ext_down_col_r",
            "light_artificial_ext_down_col_g", "light_artificial_ext_down_col_b", "light_artificial_ext_up_col_r",
            "light_artificial_ext_up_col_g", "light_artificial_ext_up_col_b", "ped_light_col_r", "ped_light_col_g",
            "ped_light_col_b", "ped_light_mult", "light_ray_col_r", "light_ray_col_g", "light_ray_col_b", "light_ray_mult",
            "light_ray_underwater_mult", "light_ray_heightfalloff", "postfx_shift_col_r", "postfx_shift_col_g",
            "postfx_shift_col_b", "sky_plane_r", "sky_plane_g", "sky_plane_b", "fog_near_col_r", "fog_near_col_g",
            "fog_near_col_b", "fog_near_col_a", "fog_col_r", "fog_col_g", "fog_col_b", "fog_col_a",
            "fog_sun_lighting_calc_pow", "fog_moon_col_r", "fog_moon_col_g", "fog_moon_col_b", "fog_moon_col_a",
            "fog_moon_lighting_calc_pow", "fog_east_col_r", "fog_east_col_g", "fog_east_col_b", "fog_east_col_a",
            "fog_hdr", "fog_haze_col_r", "fog_haze_col_g", "fog_haze_col_b", "fog_haze_hdr",
            "particle_light_intensity_mult");
        Add(Kind.Clamp,
            "light_ray_dist", "fog_start", "fog_density", "fog_falloff", "fog_base_height", "fog_alpha",
            "fog_horizon_tint_scale", "fog_haze_density", "fog_haze_alpha", "fog_haze_start", "far_clip");
        Add(Kind.Max,
            "no_weather_fx", "no_gpu_fx", "no_rain", "no_rain_ripples", "fogvolume_density_scalar",
            "fogvolume_density_scalar_interior", "fogvolume_fog_scaler", "enable_occlusion", "enable_shadow_occlusion",
            "render_exterior", "portal_weight");
        return kinds;
    }
}

/// <summary>
/// GTA V's timecycle boxes over its world, and the place the camera is in among them:
/// the region boxes the game's code adds over Los Santos for the weather's URBAN region
/// (the game's five, the one place their values are the code's), and the modifier boxes its maps add
/// (CMapData's timeCycleModifiers). Each box is at full strength inside it and fades
/// out over its range around it (three times as fast upwards and downwards), within its hours; a region or a modifier
/// takes its strongest box. Only modifiers the outdoors applies are kept: not those for under water or interiors alone.
/// </summary>
internal sealed class TimecycleZones
{
    private readonly record struct Box(Vector3 Min, Vector3 Max, float Strength, float Range, float RangeReciprocal, int StartHour, int EndHour, int Modifier);

    /// <summary>The URBAN region's boxes, as the game adds them: each its corners, at full strength, over 750 m.</summary>
    internal static readonly TimecycleBox[] UrbanBoxes =
    [
        new(Urban, new(-1747.178f, -3506.0f, 0), new(1264.279f, 619.43f, 500), 100, 750, 0, 0),
        new(Urban, new(-2517.747f, -3347.342f, 0), new(-1713.271f, -133.549f, 271.342f), 100, 750, 0, 0),
        new(Urban, new(-2017.02f, -282.304f, 0), new(417, 1085, 271.342f), 100, 750, 0, 0),
        new(Urban, new(1254, -2489.396f, 0), new(1444, -197.25f, 267.59f), 100, 750, 0, 0),
        new(Urban, new(1254, -196.606f, 0), new(1756.208f, 112.971f, 267.59f), 100, 750, 0, 0),
    ];

    private const uint Urban = 0x7d7b904f;

    // The modifiers the game leaves out (the street lights', unless a command line asks for them).
    private static readonly uint[] Skipped = [0xA502C509, 0x77F1BF19, 0x2D909E01];

    private readonly Box[] regionBoxes;
    private readonly Box[] modifierBoxes;
    private readonly GameTimecycleModifier[] modifiers;
    private readonly float[] strengths;
    private readonly List<int> touched = [];

    /// <summary>The boxes of a game with this timecycle, and the modifier boxes of its maps.</summary>
    public TimecycleZones(GameTimecycle timecycle, IEnumerable<TimecycleBox> boxes)
    {
        // The region boxes find their region by name; the game blends no more than two regions, so only the second counts.
        int region = -1;
        for (int i = 0; i < timecycle.Regions.Count; i++)
            if (new CommonFury.Hashing.Hash32(timecycle.Regions[i].Name.ToLowerInvariant()).Value == Urban)
                region = i;
        regionBoxes = region == 1 ? [.. UrbanBoxes.Select(box => Make(box, -1))] : [];
        var used = new List<GameTimecycleModifier>();
        var numbers = new Dictionary<GameTimecycleModifier, int>(ReferenceEqualityComparer.Instance);
        var kept = new List<Box>();
        foreach (TimecycleBox box in boxes)
        {
            if (Array.IndexOf(Skipped, box.Name) >= 0 || !timecycle.Modifiers.TryGetValue(box.Name, out GameTimecycleModifier? modifier)
                || (modifier.Flags & (GameTimecycleModifier.UnderwaterOnly | GameTimecycleModifier.InteriorOnly)) != 0
                || !(box.Min.X <= box.Max.X && box.Min.Y <= box.Max.Y && box.Min.Z <= box.Max.Z))
                continue;
            if (!numbers.TryGetValue(modifier, out int number))
            {
                numbers[modifier] = number = used.Count;
                used.Add(modifier);
            }
            kept.Add(Make(box, number));
        }
        modifierBoxes = [.. kept];
        modifiers = [.. used];
        strengths = new float[modifiers.Length];
    }

    /// <summary>How many modifier boxes there are.</summary>
    public int ModifierBoxes => modifierBoxes.Length;

    private static Box Make(TimecycleBox box, int modifier)
    {
        // The game's fix-up: a box with hours runs to the end of its last hour.
        int end = box.StartHour != box.EndHour ? (box.EndHour + 1) % 24 : box.EndHour;
        return new Box(box.Min, box.Max, box.Percentage * .01f, box.Range, box.Range == 0 ? 1 : 1 / box.Range, box.StartHour, end, modifier);
    }

    /// <summary>The place a camera at <paramref name="position"/> is in at <paramref name="hours"/> after midnight.</summary>
    public void Evaluate(Vector3 position, float hours, TimecyclePlace place)
    {
        hours = Timecycle.WrapHours(hours);
        int hour = Math.Clamp((int)hours, 0, 23);
        float within = hours - hour;
        float region = 0;
        foreach (Box box in regionBoxes)
        {
            // The region's strength is its boxes' alone (the box's own strength isn't applied to regions).
            float strength = Strength(box, position, hour, within);
            if (strength > 0)
                region = MathF.Max(region, strength);
        }
        place.Region = region;
        place.Modifiers.Clear();
        foreach (Box box in modifierBoxes)
        {
            // tcBoxTree's query first: the box grown by its range against the point grown by 0.1 m.
            float range = box.Range + .1f;
            if (position.X < box.Min.X - range || position.X > box.Max.X + range || position.Y < box.Min.Y - range
                || position.Y > box.Max.Y + range || position.Z < box.Min.Z - range || position.Z > box.Max.Z + range)
                continue;
            float strength = Strength(box, position, hour, within) * box.Strength;
            if (strength <= 0)
                continue;
            if (strengths[box.Modifier] == 0)
                touched.Add(box.Modifier);
            strengths[box.Modifier] = MathF.Max(strengths[box.Modifier], strength);
        }
        foreach (int number in touched)
        {
            place.Modifiers.Add((modifiers[number], strengths[number]));
            strengths[number] = 0;
        }
        touched.Clear();
        // Applied in the game's order of its modifiers.
        if (place.Modifiers.Count > 1)
            place.Modifiers.Sort(static (a, b) => a.Modifier.Order.CompareTo(b.Modifier.Order));
    }

    // A box's strength: one less the distance outside the box (heights counted three times) over its range, by
    // how far into its hours the time is.
    private static float Strength(in Box box, Vector3 position, int hour, float within)
    {
        Vector3 centre = (box.Min + box.Max) / 2, extent = (box.Max - box.Min) / 2;
        Vector3 outside = Vector3.Max(Vector3.Zero, Vector3.Abs(position - centre) - extent) * new Vector3(1, 1, 3);
        return (1 - box.RangeReciprocal * outside.Length()) * TimeStrength(box.StartHour, box.EndHour, hour, within);
    }

    // A box's strength by the hour: full strength in its hours, fading in over the hour before them and out over the last.
    internal static float TimeStrength(int start, int end, int hour, float within)
    {
        if (start == end)
            return 1;
        if ((hour + 1) % 24 == start)
            return within;
        if (hour == end)
            return 1 - within;
        if (start < end)
            return hour >= start && hour <= end ? 1 : 0;
        return hour >= end && hour < start ? 0 : 1;
    }
}
