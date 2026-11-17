using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using FiveFury.Drawables;
using FiveFury.Ymap;
using HikariHopper.Rendering.World;
using RedFury.World;
using GameTexture = FiveFury.Textures.Texture;

namespace HikariHopper.Rendering;

/// <summary>
/// The settings GTA V's coronas are drawn with from visualsettings.dat (the coronas' own, and the
/// LOD lights' and lights' that the coronas read), each with the
/// default the game's code reads it with where the file hasn't it.
/// </summary>
internal sealed record GtaCoronaVisuals
{
    public float SizeScaleGlobal { get; init; } = 1;
    public float IntensityScaleGlobal { get; init; } = 1;
    public float ScreenspaceExpansion { get; init; }
    public float ZBiasMultiplier { get; init; } = 10;
    public float ZBiasDistanceNear { get; init; } = 20;
    public float ZBiasDistanceFar { get; init; } = 50;
    public float ZBiasMin { get; init; }
    public float ZBiasMax { get; init; } = 1;
    public float ZBiasFromFinalSizeMultiplier { get; init; } = .5f;
    public float OcclusionSizeScale { get; init; } = .5f;
    public float OcclusionSizeMax { get; init; } = 32;
    public float ScreenEdgeMinDistanceForFade { get; init; } = .95f;
    public float DoNgCoronas { get; init; }
    public float RotationSpeed { get; init; }
    public float RotationSpeedRampedUp { get; init; }
    public float Layer1Multiplier { get; init; } = 1;
    public float Layer2Multiplier { get; init; } = 1;
    public float ScaleRampUp { get; init; } = 2;
    public float ScaleRampUpAngle { get; init; } = 30;
    public float FlareCoronaSizeRatio { get; init; } = .3f;
    public float FlareMinAngleDegreesToFadeIn { get; init; } = 30;
    public float FlareScreenCenterOffsetSizeMultiplier { get; init; } = 100;
    public float FlareScreenCenterOffsetVerticalSizeMultiplier { get; init; } = 30;
    public Vector3 FlareColourShift { get; init; } = new(.01f, .02f, .05f);
    public Vector3 BaseValue { get; init; }
    public float DirectionalFadeOffStart { get; init; } = 200;
    public float DirectionalFadeOffDistance { get; init; } = 50;

    /// <summary>lodlight.{small,medium,large}.range: how far each category's LOD lights reach.</summary>
    public Vector3 LodLightRange { get; init; } = new(150, 450, 1250);

    /// <summary>lodlight.{small,medium,large}.corona.range: how far each category's LOD lights have coronas.</summary>
    public Vector3 LodLightCoronaRange { get; init; } = new(150, 450, 1250);

    public float LodLightCoronaSize { get; init; } = 1;
    public float SphereExpandSizeMultiplier { get; init; } = 1;
    public float SphereExpandStart { get; init; } = 1;
    public float SphereExpandEnd { get; init; } = 1;

    /// <summary>lights.cutoff.distance: where lights that aren't the map's fade out.</summary>
    public float LightCutoffDistance { get; init; } = 1000;

    /// <summary>
    /// Where the map's lights fade out: from 50 m to lights.cutoff.map.distance by the
    /// distance scaling setting, here at its full.
    /// </summary>
    public float MapLightFadeDistance { get; init; } = 1000;

    public static GtaCoronaVisuals From(Core.GameVisualSettings? file)
    {
        var d = new GtaCoronaVisuals();
        if (file is null)
            return d;
        float Get(string name, float fallback) => file.Get(name, fallback);
        return new GtaCoronaVisuals
        {
            SizeScaleGlobal = Get("misc.coronas.sizeScaleGlobal", d.SizeScaleGlobal),
            IntensityScaleGlobal = Get("misc.coronas.intensityScaleGlobal", d.IntensityScaleGlobal),
            ScreenspaceExpansion = Get("misc.coronas.screenspaceExpansion", d.ScreenspaceExpansion),
            ZBiasMultiplier = Get("misc.coronas.zBiasMultiplier", d.ZBiasMultiplier),
            ZBiasDistanceNear = Get("misc.coronas.zBiasDistanceNear", d.ZBiasDistanceNear),
            ZBiasDistanceFar = Get("misc.coronas.zBiasDistanceFar", d.ZBiasDistanceFar),
            ZBiasMin = Get("misc.coronas.zBias.min", d.ZBiasMin),
            ZBiasMax = Get("misc.coronas.zBias.max", d.ZBiasMax),
            ZBiasFromFinalSizeMultiplier = Get("misc.coronas.zBias.fromFinalSizeMultiplier", d.ZBiasFromFinalSizeMultiplier),
            OcclusionSizeScale = Get("misc.coronas.occlusionSizeScale", d.OcclusionSizeScale),
            OcclusionSizeMax = Get("misc.coronas.occlusionSizeMax", d.OcclusionSizeMax),
            ScreenEdgeMinDistanceForFade = Math.Clamp(Get("misc.coronas.screenEdgeMinDistForFade", d.ScreenEdgeMinDistanceForFade), 0, 1),
            DoNgCoronas = Get("misc.coronas.m_doNGCoronas", d.DoNgCoronas),
            RotationSpeed = Get("misc.coronas.m_rotationSpeed", d.RotationSpeed),
            RotationSpeedRampedUp = Get("misc.coronas.m_rotationSpeedRampedUp", d.RotationSpeedRampedUp),
            Layer1Multiplier = Get("misc.coronas.m_layer1Muliplier", d.Layer1Multiplier),
            Layer2Multiplier = Get("misc.coronas.m_layer2Muliplier", d.Layer2Multiplier),
            ScaleRampUp = Get("misc.coronas.m_scaleRampUp", d.ScaleRampUp),
            ScaleRampUpAngle = Get("misc.coronas.m_scaleRampUpAngle", d.ScaleRampUpAngle),
            FlareCoronaSizeRatio = Get("misc.coronas.flareCoronaSizeRatio", d.FlareCoronaSizeRatio),
            FlareMinAngleDegreesToFadeIn = Get("misc.coronas.flareMinAngleDegToFadeIn", d.FlareMinAngleDegreesToFadeIn),
            FlareScreenCenterOffsetSizeMultiplier = Get("misc.coronas.flareScreenCenterOffsetSizeMult", d.FlareScreenCenterOffsetSizeMultiplier),
            FlareScreenCenterOffsetVerticalSizeMultiplier = Get("misc.coronas.flareScreenCenterOffsetVerticalSizeMult", d.FlareScreenCenterOffsetVerticalSizeMultiplier),
            FlareColourShift = new(Get("misc.coronas.flareColShiftR", d.FlareColourShift.X), Get("misc.coronas.flareColShiftG", d.FlareColourShift.Y),
                Get("misc.coronas.flareColShiftB", d.FlareColourShift.Z)),
            BaseValue = new(Get("misc.coronas.baseValueR", 0), Get("misc.coronas.baseValueG", 0), Get("misc.coronas.baseValueB", 0)),
            DirectionalFadeOffStart = Get("misc.coronas.dir.mult.fadeoff.start", d.DirectionalFadeOffStart),
            DirectionalFadeOffDistance = Get("misc.coronas.dir.mult.fadeoff.dist", d.DirectionalFadeOffDistance),
            LodLightRange = new(Get("lodlight.small.range", 150), Get("lodlight.medium.range", 450), Get("lodlight.large.range", 1250)),
            LodLightCoronaRange = new(Get("lodlight.small.corona.range", 150), Get("lodlight.medium.corona.range", 450), Get("lodlight.large.corona.range", 1250)),
            LodLightCoronaSize = Get("lodlight.corona.size", d.LodLightCoronaSize),
            SphereExpandSizeMultiplier = Get("lodlight.sphere.expand.size.mult", 1),
            SphereExpandStart = Get("lodlight.sphere.expand.start", 1),
            SphereExpandEnd = Get("lodlight.sphere.expand.end", 1),
            LightCutoffDistance = Get("lights.cutoff.distance", d.LightCutoffDistance),
            MapLightFadeDistance = Get("lights.cutoff.map.distance", d.MapLightFadeDistance),
        };
    }

    /// <summary>
    /// How much further the LOD lights reach with the camera this high over the ground.
    /// </summary>
    public float SphereExpansion(float heightAboveGround)
    {
        float span = SphereExpandEnd - SphereExpandStart;
        float blend = span == 0 ? (heightAboveGround >= SphereExpandEnd ? 1 : 0) : Math.Clamp((heightAboveGround - SphereExpandStart) / span, 0, 1);
        return float.Lerp(1, SphereExpandSizeMultiplier, blend);
    }
}

/// <summary>The corona values a drawable's light carries beside its light: its corona's size and intensity, and the distance it fades out at (none, nought).</summary>
internal readonly record struct DrawableLightCorona(float Size, float Intensity, byte LightFadeDistance)
{
    public static DrawableLightCorona From(FiveFury.Extensions.LightAttrDef light) => new(light.CoronaSize, light.CoronaIntensity, light.LightFadeDistance);
}

/// <summary>A static light of the world placed where its placement puts it, with its corona's values: what the game registers a corona from.</summary>
internal readonly record struct WorldCoronaLight(Vector3 Position, Vector3 Direction, WorldLightDefinition Light, DrawableLightCorona Corona, bool Interior);

/// <summary>
/// GTA V's LOD lights (lodlights.rpf's distlodlights_* and lodlights_* maps, paired by parent): where each is, its
/// colour and packed intensity, its direction, cone, corona intensity, hours and category, as the game holds
/// them.
/// </summary>
internal sealed class GtaLodLights
{
    public static GtaLodLights Empty { get; } = new([]);

    internal readonly record struct Light(Vector3 Position, uint Rgbi, Vector3 Direction, uint TimeAndStateFlags, byte ConeOuterAngle,
        byte CoronaIntensity, byte Category);

    // The lights by category, then in cells of the ground, so those near the camera are found without visiting all of
    // them.
    private const float Cell = 128;
    private readonly Light[] lights;
    private readonly Dictionary<(int Category, int X, int Y), (int Start, int Count)> cells = [];

    public GtaLodLights(IReadOnlyList<Light> lights)
    {
        this.lights = [.. lights.OrderBy(light => CellOf(light.Category, light.Position))];
        for (int i = 0; i < this.lights.Length;)
        {
            var cell = CellOf(this.lights[i].Category, this.lights[i].Position);
            int start = i;
            while (i < this.lights.Length && CellOf(this.lights[i].Category, this.lights[i].Position) == cell)
                i++;
            cells[cell] = (start, i - start);
        }
    }

    public int Count => lights.Length;

    public ReadOnlySpan<Light> All => lights;

    private static (int Category, int X, int Y) CellOf(int category, Vector3 position) =>
        (category, (int)MathF.Floor(position.X / Cell), (int)MathF.Floor(position.Y / Cell));

    /// <summary>
    /// Each category's lights in the cells within its reach (<paramref name="reach"/>'s x, y and z for the small,
    /// medium and large ones) of <paramref name="eye"/> across the ground.
    /// </summary>
    public void Near(Vector3 eye, Vector3 reach, List<Light> into)
    {
        for (int category = 0; category < 3; category++)
        {
            float r = reach[category];
            var (_, x0, y0) = CellOf(category, eye - new Vector3(r, r, 0));
            var (_, x1, y1) = CellOf(category, eye + new Vector3(r, r, 0));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if (cells.TryGetValue((category, x, y), out var range))
                        into.AddRange(lights.AsSpan(range.Start, range.Count));
        }
    }

    /// <summary>Each LOD map's lights at its distant map's positions and colours; a pair whose counts differ is left out.</summary>
    public static GtaLodLights From(IEnumerable<(Ymap Distant, Ymap Lod)> pairs)
    {
        var lights = new List<Light>();
        foreach (var (distant, lod) in pairs)
        {
            List<DistantLodLight> places = distant.DistantLights.Lights;
            List<LodLight> values = lod.LodLights.Lights;
            if (places.Count != values.Count)
                continue;
            byte category = (byte)Math.Min((int)distant.DistantLights.Category, 2);
            for (int i = 0; i < values.Count; i++)
            {
                LodLight light = values[i];
                lights.Add(new Light(places[i].Position, places[i].Color.Packed, light.Direction, light.TimeAndStateFlags, light.ConeOuterAngleOrCapExtent,
                    light.CoronaIntensity, category));
            }
        }
        return new GtaLodLights(lights);
    }
}

/// <summary>
/// GTA V's world height map (common/data/levels/gta5/heightmap.dat): the lowest and highest ground in each
/// cell of a grid over the map, a byte each between its bounds, rows run-length coded. What the LOD lights' reach is
/// worked out from (the camera's height over the lowest ground near it).
/// </summary>
internal sealed class GtaHeightMap
{
    private const uint Tag = 0x484D4150; // 'HMAP'
    private readonly int columns, rows;
    private readonly float minX, minY, minZ, maxZ, oneOverCellX, oneOverCellY, cellX, cellY;
    private readonly (int Start, int Count, int Offset)[]? runs;
    private readonly byte[] lowest;
    private readonly int lowestAt;

    private GtaHeightMap(int columns, int rows, float minX, float minY, float minZ, float maxX, float maxY, float maxZ,
        (int, int, int)[]? runs, byte[] data, int lowestAt)
    {
        (this.columns, this.rows, this.minX, this.minY, this.minZ, this.maxZ) = (columns, rows, minX, minY, minZ, maxZ);
        cellX = (maxX - minX) / columns;
        cellY = (maxY - minY) / rows;
        oneOverCellX = columns / (maxX - minX);
        oneOverCellY = rows / (maxY - minY);
        this.runs = runs;
        lowest = data;
        this.lowestAt = lowestAt;
    }

    /// <summary>A height map file; null when it isn't one of byte cells, or its cells aren't all in it.</summary>
    public static GtaHeightMap? Read(byte[] file)
    {
        if (file.Length < 44)
            return null;
        bool big = BinaryPrimitives.ReadUInt32LittleEndian(file) != Tag;
        if (big && BinaryPrimitives.ReadUInt32BigEndian(file) != Tag)
            return null;
        uint U32(int at) => big ? BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at)) : BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at));
        ushort U16(int at) => big ? BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan(at)) : BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(at));
        float F32(int at) => BitConverter.Int32BitsToSingle((int)U32(at));
        byte cellBytes = file[5];
        uint flags = U32(8);
        int columns = U16(12), rows = U16(14);
        float minX = F32(16), minY = F32(20), minZ = F32(24), maxX = F32(28), maxY = F32(32), maxZ = F32(36);
        int size = (int)U32(40);
        if (cellBytes != 1 || columns == 0 || rows == 0 || size <= 0 || 44 + (long)size > file.Length)
            return null;
        byte[] data = file.AsSpan(44, size).ToArray();
        if ((flags & 1) != 0)
        {
            // Each row's first column, how many it has, and where they start in the max and min data after the runs:
            // each within the map's columns and that data.
            if ((long)rows * 8 > size)
                return null;
            long half = (size - rows * 8) / 2;
            var runs = new (int, int, int)[rows];
            for (int j = 0; j < rows; j++)
            {
                int at = 44 + j * 8;
                uint offset = U32(at + 4);
                runs[j] = (U16(at), U16(at + 2), (int)offset);
                var (start, count, _) = runs[j];
                if (count > 0 && (start + count > columns || offset + (long)start + count > half))
                    return null;
            }
            return new GtaHeightMap(columns, rows, minX, minY, minZ, maxX, maxY, maxZ, runs, data, (int)(rows * 8 + half));
        }
        if (2L * columns * rows > size)
            return null;
        return new GtaHeightMap(columns, rows, minX, minY, minZ, maxX, maxY, maxZ, null, data, size / 2);
    }

    /// <summary>The lowest ground in a cell: the bounds' bottom outside the map's cells.</summary>
    public float MinimumAt(int i, int j)
    {
        byte z = 0;
        if (j >= 0 && j < rows)
        {
            if (runs is not null)
            {
                var (start, count, offset) = runs[j];
                if (i >= start && i <= start + count - 1)
                    z = lowest[lowestAt + offset + i];
            }
            else if (i >= 0 && i < columns)
                z = lowest[lowestAt + i + j * columns];
        }
        return minZ + (maxZ - minZ) * (z / 255f);
    }

    /// <summary>
    /// The lowest ground under a point, interpolated between the cells round it, each the lowest of its neighbours
    /// (a min filter).
    /// </summary>
    public float MinimumInterpolated(float x, float y)
    {
        x -= .5f * cellX;
        y -= .5f * cellY;
        int i = (int)MathF.Floor((x - minX) * oneOverCellX), j = (int)MathF.Floor((y - minY) * oneOverCellY);
        float xf = (x - minX) * oneOverCellX - i, yf = (y - minY) * oneOverCellY - j;
        Span<float> g = stackalloc float[16];
        for (int jj = 0; jj < 4; jj++)
            for (int ii = 0; ii < 4; ii++)
                g[ii * 4 + jj] = MinimumAt(i + ii - 1, j + jj - 1);
        Span<float> h = stackalloc float[4];
        for (int jj = 0; jj < 2; jj++)
            for (int ii = 0; ii < 2; ii++)
            {
                float m = float.MaxValue;
                for (int b = 0; b < 3; b++)
                    for (int a = 0; a < 3; a++)
                        m = MathF.Min(g[(ii + a) * 4 + jj + b], m);
                h[ii * 2 + jj] = m;
            }
        float h0 = h[0] + (h[1] - h[0]) * yf, h1 = h[2] + (h[3] - h[2]) * yf;
        return h0 + (h1 - h0) * xf;
    }
}

/// <summary>What the coronas are drawn with, read from the game: graphics.ytd's two textures, the settings, the LOD lights and the height map.</summary>
internal sealed record GtaCoronaData(GameTexture? Corona, GameTexture? Flare, GtaCoronaVisuals Visuals, GtaLodLights LodLights, GtaHeightMap? HeightMap);

/// <summary>
/// One corona as the GPU draws it (gta_coronas.wgsl's Corona): where it is and what the game registers it from, a
/// map light's or a LOD light's; the rest the vertex shader works out each frame from the camera, as the game does.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GtaCoronaInstance
{
    public const int Bytes = 64;

    public Vector3 Position;
    // The corona's flags in the low 16 bits, its cone's inner angle in degrees above them, then its outer.
    public uint Packed;
    public Vector3 Direction;
    // Red, green and blue bytes.
    public uint Colour;
    // A map light's corona size; nought for a LOD light (the frame's size).
    public float Size;
    // A map light's corona intensity; a LOD light's intensity times its corona intensity.
    public float Intensity;
    // A map light's falloff (its radius before the timecycle's multiplier) and its capsule's length.
    public float Falloff;
    public float Extent;
    public uint Kind;
    // Where the light fades out, in metres.
    public float FadeDistance;
    public uint LightFlags;
    // A map light's own intensity.
    public float LightIntensity;
}

/// <summary>
/// GTA V's coronas, registered from the world's static lights: each map light the cut has (as the
/// game registers them) and the LOD lights round the camera at night, those whose map light isn't in
/// the cut. What depends on the camera, the game works out each frame (on the CPU, then in the im effect's
/// VS_corona); here gta_coronas.wgsl does, from the constants <see cref="Write"/> sets.
/// </summary>
internal static class GtaCoronas
{
    // The corona's flags.
    internal const uint DontReflect = 1 << 0, HasDirection = 1 << 1, DontRenderUnderwater = 1 << 2, OnlyInReflection = 1 << 3,
        DontRenderFlare = 1 << 4, FadeOffDirMult = 1 << 5, FromLod = 1 << 6, DontRotate = 1 << 7, DontRampUp = 1 << 8;

    // What a corona is registered from (Kind): a LOD light, its category in bits 1 and 2; a light in an interior, whose
    // sprites the timecycle doesn't scale; one that keeps its size (interior only, or fading at its own distance); one
    // lit by the sun's light (CALC_FROM_SUN); a corona-only light, whose radius stays the light's default; its type above.
    internal const uint KindLod = 1, KindInterior = 1 << 3, KindFixedSize = 1 << 4, KindFromSun = 1 << 5, KindCoronaOnly = 1 << 6;
    internal const int KindTypeShift = 8;

    // LOD lights' loading is turned off by day (between its hours to turn off and on).
    internal const int LodLightsOff = 7, LodLightsOn = 19;

    // The most a LOD light's intensity, corona intensity and cone angle can be: what its bytes are out of.
    internal const float LodIntensityRange = 48, LodCoronaIntensityRange = 32, LodConeRange = 180;

    // The corona's cone angles, as Register clamps them into its bytes.
    private static uint Packed(uint flags, float inner, float outer) =>
        (flags & 0xffff) | (uint)(byte)Math.Clamp(inner, 0, 90) << 16 | (uint)(byte)Math.Clamp(outer, 0, 90) << 24;

    private static uint Rgb(Vector3 colour)
    {
        Vector3 c = Vector3.Clamp(colour, Vector3.Zero, Vector3.One) * 255 + new Vector3(.5f);
        return (uint)c.X | (uint)c.Y << 8 | (uint)c.Z << 16;
    }

    /// <summary>
    /// A map light's corona, or none where the game registers none whatever the camera does: no corona size or
    /// intensity, no light, or one only seen in reflections (none are drawn here).
    /// </summary>
    public static GtaCoronaInstance? FromLight(in WorldCoronaLight light, in GtaCoronaVisuals visuals)
    {
        WorldLightDefinition definition = light.Light;
        var flags = (DrawableLightFlags)definition.Flags;
        if (light.Corona.Size <= 0 || light.Corona.Intensity <= 0 || definition.Intensity <= 0 || (flags & DrawableLightFlags.OnlyInReflection) != 0
            || !float.IsFinite(definition.Intensity) || !float.IsFinite(light.Corona.Size) || !float.IsFinite(light.Corona.Intensity))
            return null;
        uint corona = (definition.LightType == 2 ? HasDirection : 0) | ((flags & DrawableLightFlags.NotInReflection) != 0 ? DontReflect : 0);
        bool interior = light.Interior;
        bool coronaOnly = (flags & DrawableLightFlags.CoronaOnly) != 0;
        uint kind = (interior ? KindInterior : 0)
            | ((flags & DrawableLightFlags.InteriorOnly) != 0 || light.Corona.LightFadeDistance > 0 ? KindFixedSize : 0)
            | ((flags & DrawableLightFlags.CalcFromSun) != 0 ? KindFromSun : 0)
            | (coronaOnly ? KindCoronaOnly : 0)
            | (uint)definition.LightType << KindTypeShift;
        // Its fade distance: its own distance, or the map's for a map light outside, or the lights' default.
        float fade = light.Corona.LightFadeDistance > 0 ? light.Corona.LightFadeDistance : !interior ? visuals.MapLightFadeDistance : visuals.LightCutoffDistance;
        Vector3 direction = light.Direction.LengthSquared() > 1e-12f ? Vector3.Normalize(light.Direction) : -Vector3.UnitZ;
        return new GtaCoronaInstance
        {
            Position = light.Position,
            Packed = Packed(corona, definition.ConeInnerAngle, definition.ConeOuterAngle),
            Direction = direction,
            Colour = Rgb(definition.Color),
            Size = light.Corona.Size,
            Intensity = light.Corona.Intensity,
            LightIntensity = definition.Intensity,
            Falloff = coronaOnly ? 1 : definition.Falloff,
            Extent = definition.LightType == 4 ? definition.Extents.X : 0,
            Kind = kind,
            FadeDistance = fade,
            LightFlags = definition.Flags,
        };
    }

    /// <summary>
    /// A LOD light's corona, or none where the game registers none at this hour: off, no intensity or corona
    /// intensity. Its corona range, the small ones' fade and the direction it faces are the shader's.
    /// </summary>
    public static GtaCoronaInstance? FromLodLight(in GtaLodLights.Light light, int hour)
    {
        uint timeFlags = light.TimeAndStateFlags & 0xffffff;
        if ((timeFlags & 1u << hour) == 0)
            return null;
        float intensity = (light.Rgbi >> 24) * (LodIntensityRange / 255f), coronaIntensity = light.CoronaIntensity * (LodCoronaIntensityRange / 255f);
        if (intensity <= 0 || coronaIntensity <= 0)
            return null;
        uint type = (light.TimeAndStateFlags >> 26) & 31;
        bool spot = type == 2;
        uint flags = DontRenderUnderwater | FromLod | DontRotate | DontRampUp | DontReflect | (spot ? HasDirection | (light.Category > 0 ? FadeOffDirMult : 0) : 0);
        float outer = spot ? light.ConeOuterAngle * (LodConeRange / 255f) : 90;
        return new GtaCoronaInstance
        {
            Position = light.Position,
            Packed = Packed(flags, 0, outer),
            Direction = spot ? light.Direction : Vector3.Zero,
            Colour = (light.Rgbi >> 16) & 0xff | ((light.Rgbi >> 8) & 0xff) << 8 | (light.Rgbi & 0xff) << 16,
            Intensity = intensity * coronaIntensity,
            Kind = KindLod | (uint)light.Category << 1 | type << KindTypeShift,
        };
    }

    // How close a LOD light has to be to a map light to be that light's (the game matches them by a hash of the light's
    // entity and its place among its effects; the LOD light is where that light is).
    private const float SameLight = .1f;

    /// <summary>
    /// The coronas for a camera at <paramref name="eye"/> at <paramref name="hour"/>, into <paramref name="coronas"/>
    /// (cleared first): the map lights' given, then, at night, the LOD lights near enough to have one whose map light
    /// isn't among <paramref name="sceneLights"/>.
    /// </summary>
    public static void Build(List<GtaCoronaInstance> coronas, IReadOnlyList<WorldCoronaLight> lights, IReadOnlyList<Vector3> sceneLights,
        GtaLodLights lodLights, GtaCoronaVisuals visuals, Vector3 eye, int hour, float reachMargin)
    {
        coronas.Clear();
        foreach (WorldCoronaLight light in lights)
            if (FromLight(light, visuals) is { } corona)
                coronas.Add(corona);
        if (hour >= LodLightsOff && hour < LodLightsOn || lodLights.Count == 0)
            return;
        var taken = new HashSet<(int, int, int)>(sceneLights.Count);
        static (int, int, int) Key(Vector3 p) => ((int)MathF.Floor(p.X / SameLight), (int)MathF.Floor(p.Y / SameLight), (int)MathF.Floor(p.Z / SameLight));
        foreach (Vector3 position in sceneLights)
            taken.Add(Key(position));
        var near = new List<GtaLodLights.Light>();
        lodLights.Near(eye, visuals.LodLightCoronaRange + new Vector3(reachMargin), near);
        foreach (GtaLodLights.Light light in near)
        {
            float range = visuals.LodLightCoronaRange[light.Category] + reachMargin;
            if (Vector3.DistanceSquared(light.Position, eye) > range * range || FromLodLight(light, hour) is not { } corona)
                continue;
            var (x, y, z) = Key(light.Position);
            bool mapped = false;
            for (int dz = -1; dz <= 1 && !mapped; dz++)
                for (int dy = -1; dy <= 1 && !mapped; dy++)
                    for (int dx = -1; dx <= 1 && !mapped; dx++)
                        mapped = taken.Contains((x + dx, y + dy, z + dz));
            if (!mapped)
                coronas.Add(corona);
        }
    }

    /// <summary>The frame's constants' size: gta_coronas.wgsl's CoronaFrame.</summary>
    public const int Bytes = 18 * 16;

    /// <summary>
    /// The frame's constants (gta_coronas.wgsl's CoronaFrame), as the game and its
    /// registrations take them from the timecycle at <paramref name="hours"/> and from visualsettings.dat.
    /// </summary>
    /// <param name="seconds">How long the coronas have turned for.</param>
    /// <param name="heightAboveGround">The camera's height over the lowest ground near it.</param>
    public static void Write(Span<Vector4> values, GtaCoronaVisuals visuals, float hours, Vector3 right, Vector3 up, Vector3 forward, float tanHalfFov,
        float aspect, int targetWidth, double seconds, float heightAboveGround, int count)
    {
        TimecycleSample sample = Timecycle.Sample(hours);
        // The timecycle's sprite fields, with the game's defaults where a timecycle hasn't them.
        float spriteSize = sample.GetOr("sprite_size", 1), spriteBrightness = sample.GetOr("sprite_brightness", 1);
        float expansion = sample.GetOr("sprite_corona_screenspace_expansion", 0) + visuals.ScreenspaceExpansion;
        float falloffMultiplier = sample.GetOr("light_falloff_mult", 1);
        // Lights lit by the sun's light (CALC_FROM_SUN): the directional light's multiplier and the sun's or moon's fade.
        var (sunFade, moonFade) = DayCycle.Fades(hours);
        float fromSun = sample.Get("light_dir_mult") * MathF.Max(sunFade, moonFade);
        float sizeMultiplier = 2.5f * spriteSize;
        float lodSize = visuals.LodLightCoronaSize * sizeMultiplier;
        float lodZBias = Math.Clamp(lodSize * visuals.ZBiasFromFinalSizeMultiplier, visuals.ZBiasMin, visuals.ZBiasMax);
        // The small lights' fade: their LOD lights' reach, further the higher the camera, and its fade range.
        float sphere = visuals.SphereExpansion(heightAboveGround);
        float smallRange = visuals.LodLightRange.X * sphere, smallFade = visuals.LodLightRange.X;
        double turn = 2 * Math.PI;
        float slow = (float)(seconds * visuals.RotationSpeed % turn), fast = (float)(seconds * visuals.RotationSpeedRampedUp % turn);
        // The view's third row, as the game's near fade reads it (gWorldView[2]): each camera axis's height.
        Vector3 back = -forward;
        values[0] = new Vector4(right.Z, up.Z, back.Z, 0);
        values[1] = new Vector4(slow, fast, tanHalfFov * aspect / Math.Max(targetWidth, 1), visuals.DoNgCoronas);
        values[2] = new Vector4(sizeMultiplier, spriteBrightness, expansion, lodSize);
        values[3] = new Vector4(visuals.SizeScaleGlobal, visuals.IntensityScaleGlobal, visuals.Layer1Multiplier, visuals.Layer2Multiplier);
        values[4] = new Vector4(visuals.ZBiasMin, visuals.ZBiasMax, visuals.ZBiasMultiplier, visuals.ZBiasFromFinalSizeMultiplier);
        values[5] = new Vector4(visuals.ZBiasDistanceNear, visuals.ZBiasDistanceFar, lodZBias, fromSun);
        values[6] = new Vector4(visuals.ScaleRampUp, MathF.Cos(visuals.ScaleRampUpAngle * MathF.PI / 180), visuals.FlareMinAngleDegreesToFadeIn * MathF.PI / 180,
            visuals.ScreenEdgeMinDistanceForFade);
        values[7] = new Vector4(visuals.OcclusionSizeScale, visuals.OcclusionSizeMax, 1f / Math.Max(targetWidth, 1), 0);
        values[8] = new Vector4(visuals.DirectionalFadeOffStart, visuals.DirectionalFadeOffDistance, falloffMultiplier, 1);
        values[9] = new Vector4(smallRange * smallRange, smallFade * smallFade, visuals.LodLightCoronaSize, 0);
        Vector3 coronaRange = visuals.LodLightCoronaRange;
        values[10] = new Vector4(coronaRange * coronaRange, 0);
        values[11] = new Vector4(visuals.FlareCoronaSizeRatio, visuals.FlareScreenCenterOffsetSizeMultiplier, visuals.FlareScreenCenterOffsetVerticalSizeMultiplier, 0);
        values[12] = new Vector4(visuals.FlareColourShift, 0);
        values[13] = new Vector4(visuals.BaseValue, 0);
        values[14] = new Vector4(BitConverter.Int32BitsToSingle(count), 0, 0, 0);
        values[15] = values[16] = values[17] = Vector4.Zero;
    }
}
