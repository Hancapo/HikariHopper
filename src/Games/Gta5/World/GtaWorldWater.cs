using System.Globalization;
using System.Numerics;
using System.Text;
using System.Xml;
using CommonFury.Numerics;
using FiveFury.Water;
using HikariHopper.Rendering;
using RedFury.World;

namespace HikariHopper.Core;

/// <summary>
/// GTA V's open water, as the game has it in its files: water.xml's quads (the ocean, lakes, pools and the rest, each at
/// its height, its corners' alphas setting how fast it fogs) and watertune.xml's tuning (the fog map's extent on the
/// map, the ripple scale, the specular falloff and the foam's scale), the update's over the base game's. Beyond the
/// quads the ocean goes on to the horizon (the game's far water): a frame of four more around them, at sea level.
/// </summary>
internal sealed class GtaWorldWater
{
    private GtaWorldWater(IReadOnlyList<(Vector3[] Corners, byte[] Alphas)> quads, GtaWater tuning) => (Quads, Tuning) = (quads, tuning);

    /// <summary>Each quad's corners on the map, and their alphas.</summary>
    public IReadOnlyList<(Vector3[] Corners, byte[] Alphas)> Quads { get; }

    /// <summary>watertune.xml's values, as the ocean's material takes them (its textures filled in by the index).</summary>
    public GtaWater Tuning { get; }

    /// <summary>The asset extension the LOD graph names a water quad with (HikariHopper's own; no game file has it).</summary>
    public const byte Extension = 200;

    /// <summary>The map id the water's placements are in.</summary>
    public static readonly uint Map = new CommonFury.Hashing.Hash32("hikarihopper_water").Value;

    /// <summary>How far the water is seen from.</summary>
    private const float Reach = 12000;

    /// <summary>How far past the quads the far water goes.</summary>
    private const float Horizon = 30000;

    /// <summary>The water of the GTA V game <paramref name="game"/>; none when its files can't be read.</summary>
    public static GtaWorldWater Read(LoadedGame game)
    {
        var quads = new List<(Vector3[] Corners, byte[] Alphas)>();
        GtaWater tuning = new() { WorldFog = true };
        try
        {
            if (GameFiles.ReadText(game, [("update/update.rpf", "common/data/levels/gta5/water.xml"), ("common.rpf", "data/levels/gta5/water.xml")]) is { } text)
            {
                WaterData data = WaterData.Read(Encoding.UTF8.GetBytes(text));
                foreach (WaterQuad quad in data.Quads)
                    if (!quad.IsInvisible)
                    {
                        // A quad whose first alpha is nought takes the game's default.
                        WaterCornerAlphas alphas = quad.UsesDefaultAlpha ? new WaterCornerAlphas(26) : quad.Alphas;
                        quads.Add((quad.Corners(), [alphas.Southwest, alphas.Southeast, alphas.Northeast, alphas.Northwest]));
                    }
                // The far water: sea-level quads framing them.
                var seaLevel = quads.Where(quad => quad.Corners.All(corner => corner.Z == 0)).SelectMany(quad => quad.Corners).ToArray();
                if (seaLevel.Length > 0)
                {
                    float minX = seaLevel.Min(c => c.X), maxX = seaLevel.Max(c => c.X), minY = seaLevel.Min(c => c.Y), maxY = seaLevel.Max(c => c.Y);
                    void Frame(float x0, float y0, float x1, float y1) =>
                        quads.Add(([new(x0, y0, 0), new(x1, y0, 0), new(x1, y1, 0), new(x0, y1, 0)], [26, 26, 26, 26]));
                    Frame(minX - Horizon, minY - Horizon, maxX + Horizon, minY);
                    Frame(minX - Horizon, maxY, maxX + Horizon, maxY + Horizon);
                    Frame(minX - Horizon, minY, minX, maxY);
                    Frame(maxX, minY, maxX + Horizon, maxY);
                }
            }
            if (GameFiles.ReadText(game, [("update/update.rpf", "common/data/watertune.xml"), ("common.rpf", "data/watertune.xml")]) is { } tune)
                tuning = Tune(tune, tuning);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or XmlException or ArgumentException or FormatException)
        {
        }
        return new GtaWorldWater(quads, tuning);
    }

    // watertune.xml's values over the defaults.
    private static GtaWater Tune(string xml, GtaWater tuning)
    {
        var document = new XmlDocument();
        document.LoadXml(xml);
        float Value(string name, float fallback) =>
            document.SelectSingleNode($"//{name}/@value") is { } value && float.TryParse(value.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number)
                ? number : fallback;
        Vector2 Point(string name, Vector2 fallback) =>
            document.SelectSingleNode($"//{name}") is XmlElement element
            && float.TryParse(element.GetAttribute("x"), NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
            && float.TryParse(element.GetAttribute("y"), NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ? new Vector2(x, y) : fallback;
        return tuning with
        {
            RippleScale = Value("RippleScale", tuning.RippleScale),
            SpecularFalloff = Value("SpecularFalloff", tuning.SpecularFalloff),
            FoamScale = Value("OceanFoamScale", tuning.FoamScale),
            FogMin = Point("FogMin", tuning.FogMin),
            FogMax = Point("FogMax", tuning.FogMax),
        };
    }

    /// <summary>The water's placements, a quad each, numbered by quad, as the LOD graph's roots.</summary>
    public IEnumerable<WorldLodNode> Nodes()
    {
        for (int i = 0; i < Quads.Count; i++)
        {
            Vector3[] corners = Quads[i].Corners;
            Vector3 min = corners.Aggregate(Vector3.Min), max = corners.Aggregate(Vector3.Max), center = (min + max) * .5f;
            yield return new WorldLodNode(new WorldEntityId(Map, i), null, Asset(i), Matrix4x4.CreateTranslation(center), center,
                (max - min).Length() * .5f, Reach, 0, 0);
        }
    }

    /// <summary>The asset a quad's surface is.</summary>
    public static WorldAssetKey Asset(int quad) => new(new CommonFury.Hashing.Hash32("hikarihopper_water").Value, Extension, (uint)quad, 0);

    /// <summary>A quad's surface, relative to its middle, with the ocean's material.</summary>
    public DrawableScene Surface(int quad, GtaWater material)
    {
        (Vector3[] corners, byte[] alphas) = Quads[quad];
        Vector3 min = corners.Aggregate(Vector3.Min), max = corners.Aggregate(Vector3.Max), center = (min + max) * .5f;
        // A rectangle's corners go south-west, south-east, north-east, north-west; a triangle's are three of them.
        var vertices = new SurfaceVertex[corners.Length];
        for (int c = 0; c < corners.Length; c++)
        {
            int corner = corners.Length == 4 ? c : CornerOf(corners[c], min, max);
            vertices[c] = new SurfaceVertex(corners[c] - center, Vector3.UnitZ, new Vector4(1, 0, 0, 1), new(corners[c].X, corners[c].Y), default,
                SurfaceVertex.Pack(new Vector4(1, 1, 1, alphas[corner] / 255f)), 0);
        }
        uint[] indices = corners.Length == 4 ? [0, 1, 2, 0, 2, 3] : [0, 1, 2];
        return new DrawableScene
        {
            Box = new Aabb3(min - center, max - center),
            Origin = Vector3.Zero,
            Vertices = vertices,
            Indices = indices,
            Parts = [new SurfacePart(0, 0, (uint)indices.Length, 0, Vector3.Zero, VertexCount: vertices.Length)],
            Materials = [SurfaceMaterial.ForWater(material)],
        };
    }

    // Which corner of the bounds a triangle's corner is, for its alpha.
    private static int CornerOf(Vector3 corner, Vector3 min, Vector3 max) =>
        (corner.X > (min.X + max.X) * .5f, corner.Y > (min.Y + max.Y) * .5f) switch
        {
            (false, false) => 0,
            (true, false) => 1,
            (true, true) => 2,
            _ => 3,
        };
}
