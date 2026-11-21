using System.Numerics;
using HikariHopper.Rendering.World;
using RedFury.World;

namespace HikariHopper.Tests;

// RedFury's tests of the culling the world viewer copied from it, for what's copied: the camera's cull, the cull set's
// prepared placements, and the drawable level of detail.
public sealed class WorldCullingTests
{
    [Theory]
    [InlineData(0u, 1)]
    [InlineData(0x00400000u, 1)]
    [InlineData(0x00800000u, 0)]
    [InlineData(0x00C00000u, 0)]
    [InlineData(0x80000000u, 1)]
    public void Authored_pass_flags_keep_the_coverage_but_say_whether_the_camera_draws_it(uint flags, int expected)
    {
        WorldLodNode node = Node(1) with { Center = new Vector3(10, 0, 0), Transform = Matrix4x4.CreateTranslation(10, 0, 0), EntityFlags = flags };
        var set = new WorldCullSet([node]);
        var camera = new List<int>();
        WorldCulling.Camera(set, 0, 1, Vector3.Zero, new CameraFrustum(Vector3.Zero, Vector3.UnitX, 1), camera);
        Assert.Equal(expected, camera.Count);
        Assert.Single(new WorldLodGraph([node]).Select(Vector3.Zero, _ => true).Coverage);
    }

    [Theory]
    [InlineData(0x02000000u)]
    [InlineData(0x08000000u)]
    [InlineData(0x20000000u)]
    public void Reflection_only_batches_never_reach_the_camera(uint flags)
    {
        WorldLodNode proxy = Node(1) with { EntityFlags = flags, Instances = [.. Enumerable.Range(1, 9).Select(i => Instance(i))] };
        WorldLodNode normal = Node(2) with { Asset = proxy.Asset, Instances = [.. Enumerable.Range(10, 9).Select(i => Instance(i))] };
        var set = new WorldCullSet([proxy, normal]);
        var camera = new List<int>();
        WorldCulling.Camera(set, 2, 15, Vector3.Zero, new CameraFrustum(Vector3.Zero, Vector3.UnitX, 1), camera);
        Assert.Equal(Enumerable.Range(9, 8), camera);
    }

    [Fact]
    public void The_SIMD_cull_matches_the_scalar_frustum_and_reach_tests()
    {
        // Node sizes that aren't multiples of the SIMD width, so the scalar tail runs too.
        var random = new Random(7);
        var nodes = new List<WorldLodNode>();
        for (int n = 0; n < 5; n++)
        {
            var instances = new WorldInstance[997 + n * 3];
            for (int i = 0; i < instances.Length; i++)
            {
                var center = new Vector3(random.NextSingle() * 4000 - 2000, random.NextSingle() * 4000 - 2000, random.NextSingle() * 400 - 100);
                instances[i] = new WorldInstance(new(new(1, 0, 0, center.X), new(0, 1, 0, center.Y), new(0, 0, 1, center.Z)), center, .5f + random.NextSingle() * 60);
            }
            nodes.Add(new WorldLodNode(new((uint)n, 0), null, new WorldAssetKey((uint)n + 1, 2), Matrix4x4.Identity, Vector3.Zero, 3000, 900 + n * 200, 0, 0)
            {
                Instances = instances,
            });
        }
        nodes.Add(new WorldLodNode(new(99, 0), null, new WorldAssetKey(99, 2), Matrix4x4.CreateTranslation(300, 10, 0), new(300, 10, 0), 25, 1000, 0, 0));
        var set = new WorldCullSet(nodes);
        var eye = new Vector3(15, -40, 60);
        var frustum = new CameraFrustum(eye, Vector3.Normalize(new Vector3(1, .3f, -.1f)), 16 / 9f);
        var actual = new List<int>();
        for (int n = 0; n < set.Nodes.Length; n++)
            WorldCulling.Camera(set, set.First[n], set.Count[n], eye, frustum, actual);
        IEnumerable<int> expected = Enumerable.Range(0, nodes.Sum(node => Math.Max(1, node.Instances.Count))).Where(i =>
        {
            var center = new Vector3(set.X[i], set.Y[i], set.Z[i]);
            return Vector3.DistanceSquared(eye, center) <= set.Reach2[i] && frustum.IsVisible(center, set.R[i]);
        });
        Assert.NotEmpty(actual);
        Assert.Equal(expected, actual.Order());
    }

    [Fact]
    public void The_cull_set_prepares_normal_matrices_and_drops_degenerate_transforms()
    {
        var good = new WorldInstance(new(new(2, 0, 0, 5), new(0, 2, 0, 6), new(0, 0, 2, 7)), new(5, 6, 7), 1);
        var degenerate = new WorldInstance(new(new(0, 0, 0, 1), new(0, 0, 0, 2), new(0, 0, 0, 3)), new(1, 2, 3), 1);
        var node = new WorldLodNode(new(1, 0), null, new WorldAssetKey(1, 2), Matrix4x4.Identity, Vector3.Zero, 10, 100, 0, 0) { Instances = [good, degenerate] };
        var set = new WorldCullSet([node]);
        Assert.Equal(1, set.Count[0]);
        Assert.True(Matrix4x4.Invert(good.Transform, out Matrix4x4 inverse));
        Assert.Equal(good.Transform, set.Pair(0));
        // The inverse transpose, its fourth column (which the normals don't read) cleared for the level of detail fade.
        Matrix4x4 normal = Matrix4x4.Transpose(inverse);
        normal.M14 = normal.M24 = normal.M34 = 0;
        Assert.Equal(normal, set.Pair(1));
        Assert.Equal(101f * 101f, set.Reach2[0]);
    }

    [Fact]
    public void A_single_placement_carries_its_tint_palette_row()
    {
        var set = new WorldCullSet([Node(1) with { TintValue = 3 }]);
        Assert.Equal(3, set.Pair(1).M41);
    }

    [Fact]
    public void A_new_coverage_reuses_the_storage_and_prepared_nodes_in_its_order()
    {
        var placements = new CountingInstances([Instance(10), Instance(20)]);
        WorldLodNode a = Node(1) with { Instances = placements };
        WorldLodNode b = Node(2) with { Transform = Matrix4x4.CreateTranslation(30, 0, 0), Center = new(30, 0, 0) };
        var set = new WorldCullSet([a, b]);
        float[] positions = set.X;
        float[] spheres = set.Y;
        int reads = placements.Reads;
        set.Update([b, a]);
        Assert.Same(positions, set.X);
        Assert.Same(spheres, set.Y);
        Assert.Equal(reads, placements.Reads);
        Assert.Equal([b, a], set.Nodes);
        Assert.Equal([0, 1], set.First);
        Assert.Equal([1, 2], set.Count);
        Assert.Equal([30f, 10f, 20f], set.X.AsSpan(0, 3).ToArray());
        Assert.Equal([0, 1, 1], set.Node.AsSpan(0, 3).ToArray());
        Assert.Equal(30f, set.Pair(0).M41);
        Assert.Equal(10f, set.Pair(2).M41);
    }

    [Fact]
    public void Shrinking_and_growing_drop_what_left_the_coverage_and_keep_replacements()
    {
        var placements = new CountingInstances([Instance(10), Instance(20)]);
        WorldLodNode a = Node(1) with { Instances = placements };
        WorldLodNode b = Node(2);
        var set = new WorldCullSet([a, b]);
        float[] storage = set.Y;
        int reads = placements.Reads;
        set.Update([b]);
        Assert.Same(storage, set.Y);
        Assert.Single(set.Nodes);
        Assert.Equal(1, set.Count[0]);
        set.Update([a, b]);
        // What left the coverage keeps nothing prepared.
        Assert.True(placements.Reads > reads);
        Assert.Same(storage, set.Y);
        WorldLodNode replaced = a with { Instances = [Instance(40), Instance(50), Instance(60), Instance(70)] };
        set.Update([replaced]);
        Assert.NotSame(storage, set.Y);
        Assert.Equal(4, set.Count[0]);
        Assert.Equal([40f, 50f, 60f, 70f], set.X.AsSpan(0, 4).ToArray());
        set.Update([]);
        Assert.Empty(set.Nodes);
        set.Update([b]);
        Assert.Equal(1, set.Count[0]);
        Assert.Equal(0, set.First[0]);
    }

    [Fact]
    public void A_plant_keeps_its_packed_colour_fade_and_occlusion_its_mirroring_and_its_bounds()
    {
        WorldInstance grass = Instance(5) with
        {
            TransformRows = new(new(-2, 0, 0, 5), new(0, 3, 0, 0), new(0, 0, 4, 0)),
            Grass = new WorldGrassAttributes(new(.2f, .3f, .4f), .75f, 30, 90),
        };
        WorldLodNode a = Node(1) with { Instances = [grass, Instance(6) with { TransformRows = default }] };
        WorldLodNode b = Node(2);
        var set = new WorldCullSet([a, b]);
        set.Update([b, a]);
        Assert.Equal(1, set.Count[1]);
        Assert.Equal(5, set.X[1]);
        Assert.Equal(1, set.R[1]);
        Assert.Equal(101f * 101f, set.Reach2[1]);
        Assert.Equal(grass.Transform, set.Pair(2));
        Assert.True(set.Pair(2).GetDeterminant() < 0);
        Assert.Equal(-.5f, set.Pair(3).M11);
        Assert.Equal(1 / 3f, set.Pair(3).M22);
        Assert.Equal(.25f, set.Pair(3).M33);
        Assert.Equal(.2f, set.Pair(3).M14);
        Assert.Equal(.3f, set.Pair(3).M24);
        Assert.Equal(.4f, set.Pair(3).M34);
        Assert.Equal(30, set.Pair(3).M41);
        Assert.Equal(90, set.Pair(3).M42);
        Assert.Equal(1, set.Pair(3).M43);
        Assert.Equal(.75f, set.Pair(3).M44);
    }

    [Fact]
    public void Archetype_flags_change_the_passes_without_changing_the_asset()
    {
        WorldLodNode node = Node(1) with { Instances = [.. Enumerable.Range(1, 9).Select(i => Instance(i))] };
        var set = new WorldCullSet([node]);
        var output = new List<int>();
        var frustum = new CameraFrustum(Vector3.Zero, Vector3.UnitX, 1);
        WorldCulling.Camera(set, 0, 9, Vector3.Zero, frustum, output);
        Assert.Equal(9, output.Count);
        set.Update([node with { ArchetypeFlags = 0x8000000000000800 }]);
        output.Clear();
        WorldCulling.Camera(set, 0, 9, Vector3.Zero, frustum, output);
        Assert.Empty(output);
        Assert.Equal(WorldRenderPass.Shadows, set.Passes[0]);
    }

    // Retail switch distances use 9998 for "no further level"; levels without parts are skipped.
    [Theory]
    [InlineData(0b0011, 10f, 0)]
    [InlineData(0b0011, 15f, 0)]
    [InlineData(0b0011, 16f, 1)]
    [InlineData(0b0011, 20000f, 1)]
    [InlineData(0b0101, 30f, 2)]
    [InlineData(0b0100, 1f, 2)]
    [InlineData(0b1111, 50f, 2)]
    [InlineData(0b1111, 5000f, 3)]
    public void A_placement_draws_the_most_detailed_level_it_has_within_its_switch_distance(int mask, float distance, int expected)
    {
        float[] reach = [15, 30, 60, 9998];
        Assert.Equal(expected, WorldLodSelection.Level(mask, [.. reach.Select(r => r * r)], distance * distance));
    }

    // The game's shadow pass: every level casting, or none, casts the level in view; one casting level casts at any
    // distance; several cast the level in view, but none more detailed than the first of them.
    [Theory]
    [InlineData(0b0111, 0b0111, 20f, 1)]
    [InlineData(0b0111, 0b0000, 20f, 1)]
    [InlineData(0b0001, 0b0001, 20f, 0)]
    [InlineData(0b0111, 0b0001, 5f, 0)]
    [InlineData(0b0111, 0b0001, 20f, 0)]
    [InlineData(0b0111, 0b0001, 5000f, 0)]
    [InlineData(0b0111, 0b0100, 5f, 2)]
    [InlineData(0b0111, 0b0110, 5f, 1)]
    [InlineData(0b0111, 0b0110, 20f, 1)]
    [InlineData(0b0111, 0b0110, 50f, 2)]
    [InlineData(0b0011, 0b0101, 20f, 0)]
    public void A_placement_casts_from_a_level_that_casts_as_the_game_s_shadow_pass_picks_it(int mask, int casters, float distance, int expected)
    {
        float[] reach = [15, 30, 60, 9998];
        Assert.Equal(expected, WorldLodSelection.ShadowLevel(mask, casters, [.. reach.Select(r => r * r)], distance * distance));
    }

    private static WorldLodNode Node(uint id) => new(new(id, 0), null, new(id, 2), Matrix4x4.Identity, Vector3.Zero, 1, 100, 0, 0);

    private static WorldInstance Instance(float x) => new(new(new(1, 0, 0, x), new(0, 1, 0, 0), new(0, 0, 1, 0)), new(x, 0, 0), 1);

    private sealed class CountingInstances(WorldInstance[] values) : IReadOnlyList<WorldInstance>
    {
        internal int Reads { get; private set; }

        public int Count => values.Length;

        public WorldInstance this[int index]
        {
            get
            {
                Reads++;
                return values[index];
            }
        }

        public IEnumerator<WorldInstance> GetEnumerator()
        {
            for (int i = 0; i < values.Length; i++)
                yield return this[i];
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
