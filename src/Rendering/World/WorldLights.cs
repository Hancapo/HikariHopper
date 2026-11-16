using System.Numerics;
using System.Runtime.InteropServices;
using RedFury.World;
using TransformMath = CommonFury.Numerics.TransformMath;
using VectorMath = CommonFury.Numerics.VectorMath;

namespace HikariHopper.Rendering.World;

/// <summary>The local lights' shapes.</summary>
internal enum LocalLightType
{
    Point,
    Spot,
    /// <summary>A segment of light (GTA V's capsule lights): its middle at the position, along the direction.</summary>
    Capsule,
}

/// <summary>
/// A finite light without a shadow, in the world's Z-up coordinates (RedFury's LocalLight, copied): distances in the
/// geometry's units.
/// </summary>
internal readonly record struct LocalLight
{
    public LocalLightType Type { get; }

    public Vector3 Position { get; }

    /// <summary>A linear RGB multiplier, never negative.</summary>
    public Vector3 Colour { get; }

    /// <summary>Its peak direct irradiance, before its falloff and cone.</summary>
    public float Intensity { get; }

    /// <summary>How far it reaches, in world units.</summary>
    public float Radius { get; }

    /// <summary>Into its cone, normalised, for a spot; zero for a point light.</summary>
    public Vector3 Direction { get; }

    /// <summary>The cosine of the spot's inner half-angle, where its cone reaches full strength.</summary>
    public float InnerCos { get; }

    /// <summary>The cosine of its outer half-angle, where the cone reaches nothing.</summary>
    public float OuterCos { get; }

    /// <summary>The exponent max(1 - distance / radius, 0) is raised to.</summary>
    public float FalloffExponent { get; }

    /// <summary>A capsule's length along its direction; nought for the others.</summary>
    public float Extent { get; }

    /// <exception cref="ArgumentOutOfRangeException">A value that isn't finite or is out of its range.</exception>
    public LocalLight(LocalLightType type, Vector3 position, Vector3 colour, float intensity, float radius,
        Vector3 direction = default, float innerCos = 1, float outerCos = 0, float falloffExponent = 2, float extent = 0)
    {
        if (type is not (LocalLightType.Point or LocalLightType.Spot or LocalLightType.Capsule))
            throw new ArgumentOutOfRangeException(nameof(type));
        if (!VectorMath.IsFinite(position))
            throw new ArgumentOutOfRangeException(nameof(position));
        if (!VectorMath.IsFinite(colour) || Vector3.Min(colour, Vector3.Zero) != Vector3.Zero)
            throw new ArgumentOutOfRangeException(nameof(colour));
        if (!float.IsFinite(intensity) || intensity < 0 || !VectorMath.IsFinite(colour * intensity))
            throw new ArgumentOutOfRangeException(nameof(intensity));
        if (!float.IsFinite(radius) || radius <= 0)
            throw new ArgumentOutOfRangeException(nameof(radius));
        if (!float.IsFinite(falloffExponent) || falloffExponent <= 0)
            throw new ArgumentOutOfRangeException(nameof(falloffExponent));
        if (!float.IsFinite(innerCos) || innerCos is < -1 or > 1)
            throw new ArgumentOutOfRangeException(nameof(innerCos));
        if (!float.IsFinite(outerCos) || outerCos < -1 || outerCos > innerCos)
            throw new ArgumentOutOfRangeException(nameof(outerCos));
        if (type != LocalLightType.Point && (!VectorMath.IsFinite(direction) || !float.IsFinite(direction.LengthSquared()) || direction.LengthSquared() < 1e-12f))
            throw new ArgumentOutOfRangeException(nameof(direction));
        if (!float.IsFinite(extent) || extent < 0)
            throw new ArgumentOutOfRangeException(nameof(extent));
        Type = type;
        Position = position;
        Colour = colour;
        Intensity = intensity;
        Radius = radius;
        Direction = type == LocalLightType.Point ? Vector3.Zero : Vector3.Normalize(direction);
        InnerCos = innerCos;
        OuterCos = outerCos;
        FalloffExponent = falloffExponent;
        Extent = type == LocalLightType.Capsule ? extent : 0;
    }
}

/// <summary>
/// The local lights a frame draws (RedFury's LocalLightFrame, copied): the 512 nearest the camera that reach the screen,
/// and a fixed grid of it, whose bindings stay put whatever the size, each tile a 512-bit mask of its lights. Spheres
/// overestimate spots on purpose.
/// </summary>
internal sealed class LocalLightFrame
{
    internal const int Capacity = 512, Columns = 32, Rows = 18, Words = Capacity / 32, LightBytes = Capacity * 64, TileBytes = Columns * Rows * Words * 4;

    private readonly byte[] lightData = new byte[LightBytes];
    private readonly uint[] tileMasks = new uint[TileBytes / 4];
    private readonly LocalLight[] selected = new LocalLight[Capacity];
    private readonly double[] scores = new double[Capacity];
    private readonly TileBounds[] bounds = new TileBounds[Capacity];

    private readonly record struct TileBounds(int Left, int Top, int Right, int Bottom);

    internal int Count { get; private set; }

    internal ReadOnlySpan<byte> LightData => lightData;

    internal ReadOnlySpan<uint> TileMasks => tileMasks;

    /// <summary>
    /// The lights for a camera at <paramref name="eye"/>, positions relative to <paramref name="relativeTo"/>, where the
    /// scene is drawn from (the camera in a world, the model's middle in the viewer).
    /// </summary>
    internal void Update(IReadOnlyList<LocalLight> lights, Vector3 eye, Vector3 forward, float near, float aspect, Vector3 relativeTo)
    {
        Count = 0;
        Array.Clear(tileMasks);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitZ)), up = Vector3.Cross(right, forward);
        foreach (LocalLight light in lights)
        {
            // A default light is off; positions out of float range from the camera can't reach the shader.
            if (light.Radius <= 0 || light.Intensity <= 0 || light.Colour == Vector3.Zero || !VectorMath.IsFinite(light.Position - eye))
                continue;
            if (!Project(light, eye, forward, right, up, near, aspect, out TileBounds rectangle, out double distance))
                continue;
            int insertion = Count;
            while (insertion > 0 && distance < scores[insertion - 1])
                insertion--;
            if (insertion == Capacity)
                continue;
            int end = Math.Min(Count, Capacity - 1);
            for (int i = end; i > insertion; i--)
            {
                selected[i] = selected[i - 1];
                scores[i] = scores[i - 1];
                bounds[i] = bounds[i - 1];
            }
            selected[insertion] = light;
            scores[insertion] = distance;
            bounds[insertion] = rectangle;
            Count = Math.Min(Count + 1, Capacity);
        }
        Span<float> data = MemoryMarshal.Cast<byte, float>(lightData.AsSpan());
        for (int i = 0; i < Count; i++)
        {
            LocalLight light = selected[i];
            Span<float> target = data.Slice(i * 16, 16);
            target.Clear();
            (light.Position - relativeTo).CopyTo(target);
            target[3] = light.Radius;
            (light.Colour * light.Intensity).CopyTo(target[4..]);
            target[7] = (float)light.Type;
            light.Direction.CopyTo(target[8..]);
            target[11] = light.InnerCos;
            target[12] = light.OuterCos;
            target[13] = light.FalloffExponent;
            target[14] = light.Extent;
            TileBounds rectangle = bounds[i];
            uint mask = 1u << (i & 31);
            for (int y = rectangle.Top; y <= rectangle.Bottom; y++)
                for (int x = rectangle.Left; x <= rectangle.Right; x++)
                    tileMasks[(y * Columns + x) * Words + i / 32] |= mask;
        }
    }

    private static bool Project(in LocalLight light, Vector3 eye, Vector3 forward, Vector3 right, Vector3 up, float near, float aspect,
        out TileBounds bounds, out double distance)
    {
        bounds = default;
        // In double precision, so far authored bounds can't overflow choosing and projecting them.
        double dx = (double)light.Position.X - eye.X, dy = (double)light.Position.Y - eye.Y, dz = (double)light.Position.Z - eye.Z;
        // A capsule's sphere holds its whole length.
        double radius = light.Radius + light.Extent * .5;
        distance = Math.Max(0, Math.Sqrt(dx * dx + dy * dy + dz * dz) - radius);
        double z = dx * forward.X + dy * forward.Y + dz * forward.Z;
        if (z + radius < near)
            return false;
        if (z - radius <= near)
        {
            bounds = new TileBounds(0, 0, Columns - 1, Rows - 1);
            return true;
        }
        double x = dx * right.X + dy * right.Y + dz * right.Z, y = dx * up.X + dy * up.Y + dz * up.Z;
        // The box round it in the view, at both depths, projected; the denominators are positive here.
        double tangent = OrbitCamera.TanHalf;
        double left = Math.Min((x - radius) / (z - radius), (x - radius) / (z + radius)) / (tangent * aspect);
        double rightEdge = Math.Max((x + radius) / (z - radius), (x + radius) / (z + radius)) / (tangent * aspect);
        double bottom = Math.Min((y - radius) / (z - radius), (y - radius) / (z + radius)) / tangent;
        double top = Math.Max((y + radius) / (z - radius), (y + radius) / (z + radius)) / tangent;
        if (left > 1 || rightEdge < -1 || bottom > 1 || top < -1)
            return false;
        static int Tile(double value, int count) => Math.Clamp((int)Math.Floor(Math.Clamp(value, 0, 1) * count), 0, count - 1);
        bounds = new TileBounds(Tile(left * .5 + .5, Columns), Tile(.5 - top * .5, Rows), Tile(rightEdge * .5 + .5, Columns), Tile(.5 - bottom * .5, Rows));
        return true;
    }
}

/// <summary>
/// An asset's bones by tag, in its bind pose, in its own coordinates (RedFury's WorldLightBones, copied): where its
/// lights are attached.
/// </summary>
internal sealed class WorldLightBones
{
    /// <summary>No skeleton: lights use the placement's frame.</summary>
    public static WorldLightBones Empty { get; } = new(false, []);

    private readonly Dictionary<ushort, Matrix4x4?> transforms;

    private WorldLightBones(bool hasSkeleton, Dictionary<ushort, Matrix4x4?> transforms)
    {
        HasSkeleton = hasSkeleton;
        this.transforms = transforms;
    }

    /// <summary>
    /// The lights an asset carries itself (GTA V's drawables and fragments have theirs), in its own coordinates, at its
    /// bones already; each placement of it has them, beside any its archetype names.
    /// </summary>
    public IReadOnlyList<WorldLightDefinition> Own { get; private init; } = [];

    /// <summary>An asset without bones to attach lights to, carrying these lights itself.</summary>
    public static WorldLightBones Carrying(IReadOnlyList<WorldLightDefinition> lights) =>
        lights.Count == 0 ? Empty : new WorldLightBones(false, []) { Own = lights };

    /// <summary>Whether any drawable had a skeleton, even an incomplete one.</summary>
    public bool HasSkeleton { get; }

    internal long Bytes => transforms.Count * 96L + Own.Count * 160L;

    /// <summary>Each bone's bind matrix, then its model's placement; a tag bound two ways stays unresolved.</summary>
    public static WorldLightBones From(IReadOnlyList<Rdr2Scene.Placed> models)
    {
        bool hasSkeleton = false;
        var transforms = new Dictionary<ushort, Matrix4x4?>();
        foreach (Rdr2Scene.Placed model in models)
        {
            if (model.Drawable.Skeleton is not { } skeleton)
                continue;
            hasSkeleton = true;
            if (!skeleton.HasBindPose || !VectorMath.IsFinite(model.Transform))
                continue;
            Matrix4x4[] pose = skeleton.BindPoseMatrices();
            for (int i = 0; i < skeleton.Bones.Count; i++)
            {
                ushort tag = skeleton.Bones[i].Tag;
                Matrix4x4 matrix = pose[i] * model.Transform;
                if (!VectorMath.IsFinite(matrix))
                    continue;
                if (!transforms.TryGetValue(tag, out Matrix4x4? previous))
                    transforms.Add(tag, matrix);
                else if (previous != matrix)
                    transforms[tag] = null;
            }
        }
        return hasSkeleton ? new WorldLightBones(true, transforms) : Empty;
    }

    /// <summary>A tag's bone; -1, and an asset without a skeleton, are the identity. False for a tag missing or bound two ways.</summary>
    public bool TryResolve(short tag, out Matrix4x4 transform)
    {
        transform = Matrix4x4.Identity;
        if (tag == -1 || !HasSkeleton)
            return true;
        if (!transforms.TryGetValue(unchecked((ushort)tag), out Matrix4x4? found) || found is null)
            return false;
        transform = found.Value;
        return true;
    }
}

/// <summary>
/// The world's static lights, bound to the placements that hold them (RedFury's WorldLightBinding, copied): entity
/// overrides applied to their archetype's primary lights, then the entity's secondary ones, at the bones' bind pose, at
/// most <see cref="DefaultMaximumCandidates"/> nearest the camera.
/// </summary>
internal sealed record WorldLightBinding(IReadOnlyList<LocalLight> Lights, IReadOnlyList<string> Diagnostics)
{
    public const int DefaultMaximumCandidates = 4096;

    /// <summary>The emitting lights visited, kept or not.</summary>
    public long TotalLightCount { get; private init; }

    /// <summary>Whether the limit left lights out: they're bound again as the camera moves.</summary>
    public bool IsTruncated => TotalLightCount > Lights.Count;

    /// <summary>
    /// Every light of the cut, off screen too. An asset without its bones yet (null) has none yet. A non-null
    /// <paramref name="hour"/> (0 to 23) leaves out lights whose hours don't include it.
    /// </summary>
    public static WorldLightBinding Resolve(WorldSelection selection, Func<WorldAssetKey, WorldLightBones?> residentBones,
        Vector3 eye = default, int maximumCandidates = DefaultMaximumCandidates, int? hour = null)
    {
        CheckHour(hour);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(residentBones);
        var candidates = new Candidates(eye, maximumCandidates);
        foreach (WorldLodNode node in selection.Coverage)
        {
            bool named = node.ArchetypeLights.Primary.Count != 0 || node.ArchetypeLights.Secondary.Count != 0 || node.EntityLights.Secondary.Count != 0;
            if (residentBones(node.Asset) is not { } bones || !named && bones.Own.Count == 0)
                continue;
            Bind(node, bones, candidates, $"{node.Asset}: ", hour);
        }
        return candidates.Result();
    }

    private static void CheckHour(int? hour)
    {
        if (hour is < 0 or > 23)
            throw new ArgumentOutOfRangeException(nameof(hour));
    }

    private static void Bind(WorldLodNode node, WorldLightBones bones, Candidates candidates, string prefix, int? hour)
    {
        foreach (WorldLightDefinition original in node.ArchetypeLights.Primary)
        {
            WorldLightDefinition effective = original;
            // The base's identity at +8 matches the entity override's at +12, not the byte LightHash.
            foreach (WorldLightDefinition replacement in node.EntityLights.Primary)
                if (original.Value66657D69 == replacement.ValueCED22A4C)
                {
                    effective = replacement;
                    break;
                }
            BindDefinition(effective);
        }
        foreach (WorldLightDefinition definition in node.EntityLights.Secondary)
            BindDefinition(definition);
        foreach (WorldLightDefinition definition in bones.Own)
            BindDefinition(definition);
        if (node.ArchetypeLights.Secondary.Count != 0)
            candidates.Diagnose(prefix, "Archetype secondary light activation is not implemented.");

        void BindDefinition(WorldLightDefinition definition)
        {
            // An override replaces the whole definition, its hours included.
            if (hour is { } h && !ActiveAt(definition, h))
                return;
            if (definition.LightType is not (1 or 2 or 4))
            {
                candidates.Diagnose(prefix, $"Unsupported local light type {definition.LightType}.");
                return;
            }
            if (!bones.TryResolve(definition.BoneTag, out Matrix4x4 bone))
            {
                candidates.Diagnose(prefix, $"Local light bone tag {unchecked((ushort)definition.BoneTag)} is missing or ambiguous; light omitted.");
                return;
            }
            // The game reads each light's position as it is, not its extension's offset.
            if (node.Instances.Count == 0)
                Add(node.Transform);
            else
                foreach (WorldInstance instance in node.Instances)
                    Add(instance.Transform);

            void Add(Matrix4x4 placement)
            {
                if (ToLocal(definition, bone * placement) is { } light)
                    candidates.Add(light);
                else
                    candidates.Diagnose(prefix, "Local light contains invalid geometry, cone or photometric values; light omitted.");
            }
        }
    }

    /// <summary>Whether a light is on at <paramref name="hour"/> (0 to 23), by its hours.</summary>
    public static bool ActiveAt(in WorldLightDefinition definition, int hour) => RedFury.Ytyp.TimeArchetype.ActiveAt(definition.TimeFlags, hour);

    /// <summary>
    /// A point, spot or capsule light (types 1, 2 and 4) placed by <paramref name="transform"/>, from its own coordinates;
    /// null for another type, or values it can't be drawn with. A capsule's length is its extents' x.
    /// </summary>
    public static LocalLight? ToLocal(in WorldLightDefinition definition, Matrix4x4 transform)
    {
        if (definition.LightType is not (1 or 2 or 4))
            return null;
        bool capsule = definition.LightType == 4;
        float inner = definition.ConeInnerAngle, outer = definition.ConeOuterAngle;
        bool spot = definition.LightType == 2;
        if (spot && (!float.IsFinite(inner) || !float.IsFinite(outer) || inner < 0 || outer < inner || outer >= 180))
            return null;
        try
        {
            float scale = TransformMath.RadiusScale(transform);
            return new LocalLight(spot ? LocalLightType.Spot : capsule ? LocalLightType.Capsule : LocalLightType.Point,
                Vector3.Transform(definition.Position, transform), definition.Color, definition.Intensity,
                definition.Falloff * scale,
                Vector3.TransformNormal(definition.Direction, transform),
                spot ? MathF.Cos(inner * MathF.PI / 180) : 1, spot ? MathF.Cos(outer * MathF.PI / 180) : 0, definition.FalloffExponent,
                capsule ? Math.Max(0, definition.Extents.X) * scale : 0);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private sealed class Candidates
    {
        private readonly Vector3 eye;
        private readonly int maximum;

        // The distance and order negated, so the queue's least is the worst kept.
        private readonly PriorityQueue<(LocalLight Light, long Order), (double Distance, long Order)> nearest = new();
        private readonly HashSet<string> diagnostics = [];
        private long count;

        internal Candidates(Vector3 eye, int maximum)
        {
            if (!VectorMath.IsFinite(eye))
                throw new ArgumentOutOfRangeException(nameof(eye));
            if (maximum <= 0)
                throw new ArgumentOutOfRangeException(nameof(maximum), "Candidate limit must be positive.");
            this.eye = eye;
            this.maximum = maximum;
        }

        internal void Add(LocalLight light)
        {
            if (light.Intensity <= 0 || light.Colour == Vector3.Zero)
                return;
            long order = count++;
            double x = (double)light.Position.X - eye.X, y = (double)light.Position.Y - eye.Y, z = (double)light.Position.Z - eye.Z;
            double distance = Math.Max(0, Math.Sqrt(x * x + y * y + z * z) - light.Radius);
            var priority = (-distance, -order);
            if (nearest.Count < maximum)
                nearest.Enqueue((light, order), priority);
            else if (nearest.TryPeek(out _, out var worst) && priority.CompareTo(worst) > 0)
                nearest.DequeueEnqueue((light, order), priority);
        }

        internal void Diagnose(string prefix, string message)
        {
            if (diagnostics.Count < 64)
                diagnostics.Add(prefix + message);
        }

        internal WorldLightBinding Result()
        {
            LocalLight[] lights = [.. nearest.UnorderedItems.OrderBy(item => item.Element.Order).Select(item => item.Element.Light)];
            return new WorldLightBinding(Array.AsReadOnly(lights), [.. diagnostics]) { TotalLightCount = count };
        }
    }
}
