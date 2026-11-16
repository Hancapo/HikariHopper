using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using RedFury.World;

namespace HikariHopper.Rendering.World;

/// <summary>
/// The world's covered placements flattened for culling, rebuilt only when the coverage changes (RedFury's, copied).
/// A covered node's placements don't move: they're prepared once, into a block of their own, and an update copies the
/// blocks' spheres and reaches into storage that's reused. Their matrices stay in the blocks, where the renderer reads
/// them only for the assets whose placements changed.
/// </summary>
internal sealed class WorldCullSet
{
    internal WorldLodNode[] Nodes = [];

    // Per node: its first entry and how many (a node without instances is one entry), and its prepared block.
    internal int[] First = [], Count = [];
    internal WorldRenderPass[] Passes = [];
    internal PreparedNode[] Blocks = [];

    // Per entry, as arrays for SIMD: its bounding sphere and its squared draw reach (the node's distance and radius).
    internal float[] X = [], Y = [], Z = [], R = [], Reach2 = [];
    internal int[] Node = [];

    // Per entry: which of its node's instances it is; -1 for a node without instances.
    internal int[] Instance = [];

    /// <summary>The room ambient of a placement inside an interior; null outside (GtaWorldIndex knows GTA V's).</summary>
    internal Func<WorldLodNode, InteriorAmbient?>? Ambient { get; set; }

    // Only the current coverage keeps its prepared placements; the arrays keep their largest size.
    private Dictionary<WorldLodNode, PreparedNode> prepared = new(ReferenceEqualityComparer.Instance);
    private Dictionary<WorldLodNode, PreparedNode> nextPrepared = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// A node's placements, prepared once: each one's world matrix and its inverse transpose (as rdr2_world.wgsl reads
    /// them), its sphere, its squared reach and which instance it is, a field to an array.
    /// </summary>
    internal sealed class PreparedNode
    {
        internal readonly Matrix4x4[] World, Normal;
        internal readonly float[] X, Y, Z, R, Reach2;
        internal readonly int[] Index;
        internal int Count;

        internal PreparedNode(WorldLodNode node, InteriorAmbient? room = null)
        {
            int capacity = node.Instances.Count == 0 ? (node.Grass is null ? 1 : 0) : node.Instances.Count;
            World = new Matrix4x4[capacity];
            Normal = new Matrix4x4[capacity];
            X = new float[capacity];
            Y = new float[capacity];
            Z = new float[capacity];
            R = new float[capacity];
            Reach2 = new float[capacity];
            Index = new int[capacity];
            if (node.Instances.Count == 0)
            {
                if (node.Grass is null)
                    Add(node.Transform, node.Center, node.Radius, float.PositiveInfinity, null, node.TintValue, -1, room);
            }
            else
                for (int i = 0; i < node.Instances.Count; i++)
                {
                    WorldInstance instance = node.Instances[i];
                    float reach = node.Distance + instance.Radius;
                    Add(instance.Transform, instance.Center, instance.Radius, reach * reach, instance.Grass, 0, i);
                }
        }

        private void Add(Matrix4x4 transform, Vector3 center, float radius, float reach2, WorldGrassAttributes? grass, uint tint, int index,
            InteriorAmbient? room = null)
        {
            // Degenerate or non-invertible transforms were never drawn.
            if (transform == default || !Matrix4x4.Invert(transform, out Matrix4x4 inverse))
                return;
            Matrix4x4 normal = Matrix4x4.Transpose(inverse);
            if (grass is { } attributes)
            {
                // A plant's colour, its fade and its occlusion, in lanes the normals never read (their input w is 0).
                normal.M14 = attributes.Color.X;
                normal.M24 = attributes.Color.Y;
                normal.M34 = attributes.Color.Z;
                normal.M41 = attributes.FadeStart;
                normal.M42 = attributes.FadeEnd;
                normal.M43 = 1;
                normal.M44 = attributes.AmbientOcclusion;
            }
            // An affine inverse transpose leaves M41 to M43 at zero, which the normals never read: an ordinary
            // placement's tint palette row rides there (0 keeps the material's), and an interior one's room ambient
            // (world_instances.wgsl's instance_room): its artificial ambient from above and below packed, and minus
            // one less its natural multiplier, which no plant's marker can be. M14 to M34 (the inverse's translation,
            // which the normals don't read either) carry its level of detail fade, none until the renderer sets one.
            else
            {
                normal.M14 = normal.M24 = normal.M34 = 0;
                normal.M41 = tint;
                if (room is { } ambient)
                {
                    normal.M42 = InteriorAmbient.Pack(ambient.Down);
                    normal.M43 = -1 - Math.Max(0, ambient.Natural);
                    normal.M44 = InteriorAmbient.Pack(ambient.Up);
                }
            }
            World[Count] = transform;
            Normal[Count] = normal;
            X[Count] = center.X;
            Y[Count] = center.Y;
            Z[Count] = center.Z;
            R[Count] = radius;
            Reach2[Count] = reach2;
            Index[Count] = index;
            Count++;
        }
    }

    internal WorldCullSet()
    {
    }

    internal WorldCullSet(IReadOnlyList<WorldLodNode> coverage) => Update(coverage);

    /// <summary>An entry's world matrix (an even <paramref name="pair"/>) or its inverse transpose (odd), from its block.</summary>
    internal Matrix4x4 Pair(int pair)
    {
        int entry = pair / 2, node = Node[entry];
        PreparedNode block = Blocks[node];
        return pair % 2 == 0 ? block.World[entry - First[node]] : block.Normal[entry - First[node]];
    }

    internal void Update(IReadOnlyList<WorldLodNode> coverage)
    {
        if (Nodes.Length != coverage.Count)
        {
            Nodes = new WorldLodNode[coverage.Count];
            First = new int[coverage.Count];
            Count = new int[coverage.Count];
            Passes = new WorldRenderPass[coverage.Count];
            Blocks = new PreparedNode[coverage.Count];
        }
        int capacity = 0;
        foreach (WorldLodNode node in coverage)
            capacity = checked(capacity + Math.Max(1, node.Instances.Count));
        if (capacity > X.Length)
        {
            capacity = Math.Max(capacity, checked(X.Length + X.Length / 2));
            // Every live slot is filled below, so growing needn't copy the cut before.
            X = new float[capacity];
            Y = new float[capacity];
            Z = new float[capacity];
            R = new float[capacity];
            Reach2 = new float[capacity];
            Node = new int[capacity];
            Instance = new int[capacity];
        }
        int entry = 0;
        for (int n = 0; n < Nodes.Length; n++)
        {
            WorldLodNode node = coverage[n];
            Nodes[n] = node;
            Passes[n] = WorldVisibility.Resolve(node.EntityFlags, node.ArchetypeFlags);
            if (!prepared.TryGetValue(node, out PreparedNode? block) && !nextPrepared.TryGetValue(node, out block))
                block = new PreparedNode(node, Ambient?.Invoke(node));
            nextPrepared[node] = block;
            Blocks[n] = block;
            int count = block.Count;
            First[n] = entry;
            Count[n] = count;
            // A block at a time.
            Array.Copy(block.X, 0, X, entry, count);
            Array.Copy(block.Y, 0, Y, entry, count);
            Array.Copy(block.Z, 0, Z, entry, count);
            Array.Copy(block.R, 0, R, entry, count);
            Array.Copy(block.Reach2, 0, Reach2, entry, count);
            Array.Copy(block.Index, 0, Instance, entry, count);
            Node.AsSpan(entry, count).Fill(n);
            entry += count;
        }
        (prepared, nextPrepared) = (nextPrepared, prepared);
        // The nodes no longer covered, and their prepared arrays, go now.
        nextPrepared.Clear();
    }
}

/// <summary>Sphere culling of placements, eight lanes at a time where AVX is there (RedFury's, copied).</summary>
internal static class WorldCulling
{
    /// <summary>Adds the entries of [first, first + count) inside the camera's frustum and within their node's draw reach.</summary>
    internal static void Camera(WorldCullSet set, int first, int count, Vector3 eye, in CameraFrustum frustum, List<int> output)
    {
        int end = first + count;
        while (first < end)
        {
            int node = set.Node[first];
            int next = Math.Min(end, set.First[node] + set.Count[node]);
            if ((set.Passes[node] & WorldRenderPass.Main) != 0)
                CameraRange(set, first, next - first, eye, frustum, output);
            first = next;
        }
    }

    private static void CameraRange(WorldCullSet set, int first, int count, Vector3 eye, in CameraFrustum frustum, List<int> output)
    {
        int i = first, end = first + count;
        if (Vector256.IsHardwareAccelerated && count >= Vector256<float>.Count)
        {
            var ex = Vector256.Create(eye.X);
            var ey = Vector256.Create(eye.Y);
            var ez = Vector256.Create(eye.Z);
            var fx = Vector256.Create(frustum.Forward.X);
            var fy = Vector256.Create(frustum.Forward.Y);
            var fz = Vector256.Create(frustum.Forward.Z);
            var rx = Vector256.Create(frustum.Right.X);
            var ry = Vector256.Create(frustum.Right.Y);
            var rz = Vector256.Create(frustum.Right.Z);
            var ux = Vector256.Create(frustum.Up.X);
            var uy = Vector256.Create(frustum.Up.Y);
            var uz = Vector256.Create(frustum.Up.Z);
            var horizontal = Vector256.Create(frustum.Horizontal);
            var horizontalRadius = Vector256.Create(frustum.HorizontalRadius);
            var vertical = Vector256.Create(CameraFrustum.Vertical);
            var verticalRadius = Vector256.Create(CameraFrustum.VerticalRadius);
            var far = Vector256.Create(CameraFrustum.Far);
            for (; i + Vector256<float>.Count <= end; i += Vector256<float>.Count)
            {
                var dx = Load(set.X, i) - ex;
                var dy = Load(set.Y, i) - ey;
                var dz = Load(set.Z, i) - ez;
                var r = Load(set.R, i);
                var z = dx * fx + dy * fy + dz * fz;
                var depth = Vector256.Max(z, Vector256<float>.Zero);
                var mask = Vector256.LessThanOrEqual(dx * dx + dy * dy + dz * dz, Load(set.Reach2, i))
                    & Vector256.GreaterThanOrEqual(z, -r)
                    & Vector256.LessThanOrEqual(z, far + r)
                    & Vector256.LessThanOrEqual(Vector256.Abs(dx * rx + dy * ry + dz * rz), depth * horizontal + r * horizontalRadius)
                    & Vector256.LessThanOrEqual(Vector256.Abs(dx * ux + dy * uy + dz * uz), depth * vertical + r * verticalRadius);
                Emit(mask.ExtractMostSignificantBits(), i, output);
            }
        }
        for (; i < end; i++)
        {
            var center = new Vector3(set.X[i], set.Y[i], set.Z[i]);
            if (Vector3.DistanceSquared(eye, center) <= set.Reach2[i] && frustum.IsVisible(center, set.R[i]))
                output.Add(i);
        }
    }

    /// <summary>
    /// A caster narrower than this many of a cascade's texels covers a texel or two of its map at most: skipping it
    /// leaves most small props out of the wide far cascades, where drawing them costs most.
    /// </summary>
    internal const float MinimumCasterTexels = 1;

    /// <summary>
    /// Adds the entries of [first, first + count) that may cast into the cascade, within their node's draw reach and at
    /// least <see cref="MinimumCasterTexels"/> across.
    /// </summary>
    internal static void Cascade(WorldCullSet set, int first, int count, Vector3 eye, in ShadowCascade cascade, List<int> output)
    {
        int end = first + count;
        // Whole authored batches are skipped before their spheres are tested: the flags are each placement's.
        while (first < end)
        {
            int node = set.Node[first];
            int next = Math.Min(end, set.First[node] + set.Count[node]);
            if ((set.Passes[node] & WorldRenderPass.Shadows) != 0)
                CascadeRange(set, first, next - first, eye, cascade, output);
            first = next;
        }
    }

    private static void CascadeRange(WorldCullSet set, int first, int count, Vector3 eye, in ShadowCascade cascade, List<int> output)
    {
        int i = first, end = first + count;
        if (Vector256.IsHardwareAccelerated && count >= Vector256<float>.Count)
        {
            Matrix4x4 m = cascade.View;
            var ex = Vector256.Create(eye.X);
            var ey = Vector256.Create(eye.Y);
            var ez = Vector256.Create(eye.Z);
            var m11 = Vector256.Create(m.M11);
            var m21 = Vector256.Create(m.M21);
            var m31 = Vector256.Create(m.M31);
            var m41 = Vector256.Create(m.M41);
            var m12 = Vector256.Create(m.M12);
            var m22 = Vector256.Create(m.M22);
            var m32 = Vector256.Create(m.M32);
            var m42 = Vector256.Create(m.M42);
            var m13 = Vector256.Create(m.M13);
            var m23 = Vector256.Create(m.M23);
            var m33 = Vector256.Create(m.M33);
            var m43 = Vector256.Create(m.M43);
            var radius = Vector256.Create(cascade.Radius);
            var depthLimit = Vector256.Create(cascade.Depth);
            var minimum = Vector256.Create(cascade.TexelSize * MinimumCasterTexels);
            for (; i + Vector256<float>.Count <= end; i += Vector256<float>.Count)
            {
                // Cascades are in render space (the world less the camera), as ShadowCascade.Intersects has them.
                var dx = Load(set.X, i) - ex;
                var dy = Load(set.Y, i) - ey;
                var dz = Load(set.Z, i) - ez;
                var r = Load(set.R, i);
                var px = dx * m11 + dy * m21 + dz * m31 + m41;
                var py = dx * m12 + dy * m22 + dz * m32 + m42;
                var pz = dx * m13 + dy * m23 + dz * m33 + m43;
                var extent = radius + r;
                var mask = Vector256.LessThanOrEqual(dx * dx + dy * dy + dz * dz, Load(set.Reach2, i))
                    & Vector256.GreaterThanOrEqual(r, minimum)
                    & Vector256.LessThanOrEqual(Vector256.Abs(px), extent)
                    & Vector256.LessThanOrEqual(Vector256.Abs(py), extent)
                    & Vector256.GreaterThanOrEqual(r - pz, Vector256<float>.Zero)
                    & Vector256.LessThanOrEqual(-pz - r, depthLimit);
                Emit(mask.ExtractMostSignificantBits(), i, output);
            }
        }
        for (; i < end; i++)
        {
            var center = new Vector3(set.X[i], set.Y[i], set.Z[i]);
            if (Vector3.DistanceSquared(eye, center) <= set.Reach2[i] && set.R[i] >= cascade.TexelSize * MinimumCasterTexels
                && cascade.Intersects(center - eye, set.R[i]))
                output.Add(i);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<float> Load(float[] values, int index) =>
        Vector256.LoadUnsafe(ref MemoryMarshal.GetArrayDataReference(values), (nuint)index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Emit(uint bits, int start, List<int> output)
    {
        while (bits != 0)
        {
            output.Add(start + BitOperations.TrailingZeroCount(bits));
            bits &= bits - 1;
        }
    }
}

/// <summary>
/// A placement's drawable level of detail (RedFury's, copied): the most detailed level it has whose switch distance
/// still reaches the camera; past them all, its least detailed (the entity's own distance culls it).
/// </summary>
internal static class WorldLodSelection
{
    internal static int Level(int mask, ReadOnlySpan<float> reach2, float distance2)
    {
        if ((mask & (mask - 1)) == 0)
            return mask == 0 ? 0 : BitOperations.TrailingZeroCount(mask);
        int last = 0;
        for (int level = 0; level < reach2.Length; level++)
        {
            if ((mask >> level & 1) == 0)
                continue;
            if (distance2 <= reach2[level])
                return level;
            last = level;
        }
        return last;
    }
}

/// <summary>The camera's view as a sphere test (RedFury's, copied): the viewer's 55° field of view, out to 30 km.</summary>
internal readonly struct CameraFrustum
{
    private readonly Vector3 position, forward, right, up;
    private readonly float horizontal, horizontalRadius;

    internal static readonly float Vertical = OrbitCamera.TanHalf;
    internal static readonly float VerticalRadius = MathF.Sqrt(1 + Vertical * Vertical);
    internal const float Far = 30000;

    internal Vector3 Forward => forward;
    internal Vector3 Right => right;
    internal Vector3 Up => up;
    internal float Horizontal => horizontal;
    internal float HorizontalRadius => horizontalRadius;

    internal CameraFrustum(Vector3 position, Vector3 forward, float aspect)
    {
        this.position = position;
        this.forward = forward;
        right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitZ));
        up = Vector3.Cross(right, forward);
        horizontal = Vertical * Math.Max(1, aspect);
        horizontalRadius = MathF.Sqrt(1 + horizontal * horizontal);
    }

    internal bool IsVisible(Vector3 center, float radius)
    {
        Vector3 delta = center - position;
        float z = Vector3.Dot(delta, forward);
        if (z < -radius || z > Far + radius)
            return false;
        return Math.Abs(Vector3.Dot(delta, right)) <= Math.Max(0, z) * horizontal + radius * horizontalRadius
            && Math.Abs(Vector3.Dot(delta, up)) <= Math.Max(0, z) * Vertical + radius * VerticalRadius;
    }
}
