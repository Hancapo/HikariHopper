using System.Numerics;
using System.Runtime.InteropServices;
using CommonFury.Numerics;
using FiveFury.Ymap;

namespace HikariHopper.Rendering;

/// <summary>
/// GTA V's distant LOD lights (lodlights.rpf's distlodlights_* maps), as Gta5/distant_lights.wgsl draws them: each light
/// of the medium and large maps (the small ones the game leaves out, as noise), its colour and packed intensity, and
/// whether it is one of its map's street lights (the first of each map's). Laid out by map, with each map's streaming
/// box: the game draws a map's lights only while the camera is inside it, which is where the lights end far off.
/// </summary>
internal sealed class GtaDistantLights
{
    public static GtaDistantLights Empty { get; } = new([], []);

    /// <summary>A light as the GPU reads it (Gta5/distant_lights.wgsl's DistantLight).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct Light(Vector3 Position, uint Rgbi, uint Kind, uint Spare0 = 0, uint Spare1 = 0, uint Spare2 = 0)
    {
        public const int Bytes = 32;
    }

    /// <summary>A map's lights, where they are in the list, its category (1 medium, 2 large) and its streaming box.</summary>
    internal readonly record struct Map(int First, int Count, int Category, Aabb3 Streaming);

    // A light's kind: its category in the low bits, then whether it is a street light.
    internal const uint KindStreet = 1 << 4;

    public GtaDistantLights(Light[] lights, Map[] maps) => (Lights, Maps) = (lights, maps);

    public Light[] Lights { get; }

    public Map[] Maps { get; }

    /// <summary>The medium and large distant maps' lights; the small ones' are left out.</summary>
    public static GtaDistantLights From(IEnumerable<Ymap> distantMaps)
    {
        var lights = new List<Light>();
        var maps = new List<Map>();
        foreach (Ymap ymap in distantMaps)
        {
            DistantLodLights distant = ymap.DistantLights;
            int category = (int)distant.Category;
            if (category is not (1 or 2) || distant.Lights.Count == 0)
                continue;
            int first = lights.Count;
            for (int i = 0; i < distant.Lights.Count; i++)
                lights.Add(new Light(distant.Lights[i].Position, distant.Lights[i].Color.Packed,
                    (uint)category | (i < distant.StreetLightCount ? KindStreet : 0)));
            maps.Add(new Map(first, lights.Count - first, category, ymap.StreamingExtents));
        }
        return new GtaDistantLights([.. lights], [.. maps]);
    }

    /// <summary>
    /// The runs of lights to draw with the camera at <paramref name="eye"/>: the lights of each map whose streaming box
    /// holds it (as the game has it streamed in), adjacent maps' runs joined.
    /// </summary>
    public void Streamed(Vector3 eye, List<(int First, int Count)> into)
    {
        into.Clear();
        foreach (Map map in Maps)
        {
            if (map.Streaming.IsEmpty || Vector3.Clamp(eye, map.Streaming.Minimum, map.Streaming.Maximum) != eye)
                continue;
            if (into.Count > 0 && into[^1] is var last && last.First + last.Count == map.First)
                into[^1] = (last.First, last.Count + map.Count);
            else
                into.Add((map.First, map.Count));
        }
    }

    /// <summary>
    /// How much of the distant lights shows at <paramref name="hours"/> (game time, minutes in the fraction): nothing
    /// before an hour ahead of <paramref name="start"/>, up to whole at it, whole to an hour before
    /// <paramref name="end"/>, and down to nothing at it, round midnight.
    /// </summary>
    public static float HourFade(float hours, float start, float end)
    {
        float Since(float from) => ((hours - from) % 24 + 24) % 24;
        float risen = Since(start - 1), lit = Since(start), setting = Since(end - 1);
        float span = ((end - start) % 24 + 24) % 24;
        if (lit <= span - 1)
            return 1;
        if (setting <= 1 && lit <= span)
            return 1 - setting;
        return risen <= 1 ? risen : 0;
    }
}
