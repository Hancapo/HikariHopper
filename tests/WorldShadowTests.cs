using System.Numerics;
using HikariHopper.Rendering;
using HikariHopper.Rendering.World;
using RedFury.World;

namespace HikariHopper.Tests;

// RedFury's tests of the world's shadow cascades, their schedule and their casters' cull, for what the world viewer
// copied; and of the shared indirect arguments' allocator.
public sealed class WorldShadowTests
{
    private static readonly Vector3 Sun = Vector3.Normalize(new Vector3(.3f, .2f, .9f));
    private const float Aspect = 16 / 9f;

    [Fact]
    public void The_splits_rise_and_end_at_the_shadow_distance()
    {
        float[] splits = WorldShadowCascades.Splits(.5f, 500);
        Assert.Equal(WorldShadowCascades.Count, splits.Length);
        Assert.Equal(500, splits[^1]);
        for (int i = 1; i < splits.Length; i++)
            Assert.True(splits[i] > splits[i - 1]);
        // The first cascade keeps its resolution near the camera.
        Assert.True(splits[0] < 50);
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldShadowCascades.Splits(0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldShadowCascades.Splits(10, 5));
    }

    [Theory]
    [InlineData(0f, .3f)]
    [InlineData(2f, -.9f)]
    [InlineData(-1f, 0f)]
    public void Each_cascade_holds_its_whole_slice_of_the_view(float yaw, float pitch)
    {
        var forward = new Vector3(MathF.Cos(pitch) * MathF.Cos(yaw), MathF.Cos(pitch) * MathF.Sin(yaw), MathF.Sin(pitch));
        ShadowCascade[] cascades = WorldShadowCascades.Compute(new Vector3(2483, -1298, 56), forward, Aspect, Sun, 2048);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitZ)), up = Vector3.Cross(right, forward);
        float tangent = OrbitCamera.TanHalf;
        for (int c = 0; c < cascades.Length; c++)
        {
            float near = c == 0 ? WorldShadowCascades.Near : cascades[c - 1].Split, far = cascades[c].Split;
            foreach (float distance in new[] { near, far })
                foreach (var (x, y) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
                {
                    // Render space: the world less the camera.
                    Vector3 corner = forward * distance + right * (x * distance * tangent * Aspect) + up * (y * distance * tangent);
                    Vector4 clip = Vector4.Transform(new Vector4(corner, 1), cascades[c].ViewProjection);
                    Assert.InRange(clip.X, -1.0001f, 1.0001f);
                    Assert.InRange(clip.Y, -1.0001f, 1.0001f);
                    Assert.InRange(clip.Z, 0, 1);
                    Assert.True(cascades[c].Intersects(corner, 0));
                }
            // Casters between the slice and the sun stay inside the depth range.
            Vector3 caster = cascades[c].Center + Sun * (WorldShadowCascades.CasterDepth * .9f);
            Assert.InRange(Vector4.Transform(new Vector4(caster, 1), cascades[c].ViewProjection).Z, 0, 1);
            Assert.False(cascades[c].Intersects(cascades[c].Center + right * (cascades[c].Radius * 3), 0));
        }
    }

    [Fact]
    public void The_cascades_snap_to_a_texel_grid_fixed_in_the_world_as_the_camera_moves()
    {
        foreach (Vector3 eye in new[] { new Vector3(2483, -1298, 56), new Vector3(2483.013f, -1297.991f, 56.004f), new Vector3(2490.7f, -1301.2f, 57) })
            foreach (ShadowCascade cascade in WorldShadowCascades.Compute(eye, Vector3.Normalize(new Vector3(1, .3f, -.2f)), Aspect, Sun, 2048))
            {
                // CreateLookAt keeps the light's axes in the matrix's columns.
                var right = new Vector3(cascade.View.M11, cascade.View.M21, cascade.View.M31);
                var up = new Vector3(cascade.View.M12, cascade.View.M22, cascade.View.M32);
                foreach (Vector3 axis in new[] { right, up })
                {
                    double coordinate = ((double)cascade.Center.X + eye.X) * axis.X + ((double)cascade.Center.Y + eye.Y) * axis.Y
                        + ((double)cascade.Center.Z + eye.Z) * axis.Z;
                    double texels = coordinate / cascade.TexelSize;
                    Assert.True(Math.Abs(texels - Math.Round(texels)) < .05, $"The cascade's middle is {texels} texels along a light axis.");
                }
            }
    }

    [Fact]
    public void While_moving_the_near_cascade_is_cast_every_frame_and_each_within_four()
    {
        for (long start = 0; start < 8; start++)
        {
            var seen = new bool[WorldShadowCascades.Count];
            for (long slot = start; slot < start + 4; slot++)
            {
                int due = 0;
                for (int c = 0; c < WorldShadowCascades.Count; c++)
                    if (WorldShadowCascades.Due(c, slot))
                    {
                        seen[c] = true;
                        due++;
                    }
                Assert.True(WorldShadowCascades.Due(0, slot));
                Assert.InRange(due, 1, 2);
            }
            Assert.All(seen, Assert.True);
        }
    }

    [Fact]
    public void A_cascade_moved_to_a_new_camera_reads_the_same_point_of_the_world()
    {
        Vector3 first = new(2483, -1298, 56), second = new(2490.5f, -1301.25f, 57.5f), world = new(2500, -1290, 50);
        foreach (ShadowCascade cascade in WorldShadowCascades.Compute(first, Vector3.Normalize(new Vector3(1, .3f, -.2f)), Aspect, Sun, 2048))
        {
            ShadowCascade moved = cascade.Relocate(second - first);
            Vector4 expected = Vector4.Transform(new Vector4(world - first, 1), cascade.ViewProjection);
            Vector4 actual = Vector4.Transform(new Vector4(world - second, 1), moved.ViewProjection);
            Assert.Equal(expected.X, actual.X, 3);
            Assert.Equal(expected.Y, actual.Y, 3);
            Assert.Equal(expected.Z, actual.Z, 3);
            Assert.Equal(cascade.Intersects(world - first, 1), moved.Intersects(world - second, 1));
        }
    }

    [Theory]
    [InlineData(0u, 1)]
    [InlineData(0x00400000u, 0)]
    [InlineData(0x80400000u, 0)]
    [InlineData(0x00800000u, 1)]
    [InlineData(0x00C00000u, 0)]
    [InlineData(0x80000000u, 1)]
    public void Authored_pass_flags_say_whether_a_placement_casts(uint flags, int expected)
    {
        WorldLodNode node = Node(1) with { Center = new Vector3(10, 0, 0), Transform = Matrix4x4.CreateTranslation(10, 0, 0), EntityFlags = flags };
        var shadows = new List<int>();
        WorldCulling.Cascade(new WorldCullSet([node]), 0, 1, Vector3.Zero, Cascade(1), shadows);
        Assert.Equal(expected, shadows.Count);
    }

    [Fact]
    public void A_shared_asset_can_cast_from_some_placements_and_not_others()
    {
        WorldLodNode a = Node(1) with { EntityFlags = 0x00400000, Instances = [.. Enumerable.Range(1, 9).Select(i => Instance(i))] };
        WorldLodNode b = Node(2) with { Asset = a.Asset, Instances = [.. Enumerable.Range(10, 9).Select(i => Instance(i))] };
        var shadows = new List<int>();
        WorldCulling.Cascade(new WorldCullSet([a, b]), 0, 18, Vector3.Zero, Cascade(1), shadows);
        Assert.Equal(Enumerable.Range(9, 9), shadows);
    }

    [Fact]
    public void The_SIMD_caster_cull_matches_the_scalar_light_volume_test()
    {
        var random = new Random(11);
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
        var set = new WorldCullSet(nodes);
        var eye = new Vector3(-30, 25, 80);
        var view = Matrix4x4.CreateLookAt(new Vector3(400, 300, 900), Vector3.Zero, Vector3.UnitZ);
        var cascade = new ShadowCascade(view, Matrix4x4.Identity, Vector3.Zero, 700, 2500, 400, 1);
        var actual = new List<int>();
        for (int n = 0; n < set.Nodes.Length; n++)
            WorldCulling.Cascade(set, set.First[n], set.Count[n], eye, cascade, actual);
        IEnumerable<int> expected = Enumerable.Range(0, nodes.Sum(node => node.Instances.Count)).Where(i =>
        {
            var center = new Vector3(set.X[i], set.Y[i], set.Z[i]);
            return Vector3.DistanceSquared(eye, center) <= set.Reach2[i] && set.R[i] >= cascade.TexelSize * WorldCulling.MinimumCasterTexels
                && cascade.Intersects(center - eye, set.R[i]);
        });
        Assert.NotEmpty(actual);
        Assert.Equal(expected, actual.Order());
    }

    [Fact]
    public void Casters_smaller_than_a_cascade_texel_are_left_out_of_that_cascade_alone()
    {
        WorldInstance small = Instance(5) with { Radius = .4f }, large = Instance(6) with { Radius = 3 };
        WorldLodNode node = Node(1) with { Instances = [.. Enumerable.Repeat(small, 8), large, small] };
        var set = new WorldCullSet([node]);
        var coarse = new List<int>();
        var fine = new List<int>();
        WorldCulling.Cascade(set, 0, 10, Vector3.Zero, Cascade(1), coarse);
        WorldCulling.Cascade(set, 0, 10, Vector3.Zero, Cascade(.1f), fine);
        Assert.Equal([8], coarse);
        Assert.Equal(10, fine.Count);
    }

    [Fact]
    public void The_arguments_reuse_freed_ranges_without_moving_live_ones()
    {
        var allocator = new WorldArgumentAllocator();
        Assert.Equal(0, allocator.Allocate(10));
        Assert.Equal(10, allocator.Allocate(20));
        allocator.Free(0, 10);
        Assert.Equal(0, allocator.Allocate(6));
        Assert.Equal(6, allocator.Allocate(4));
        Assert.Equal(30, allocator.Length);
        allocator.Free(10, 20);
        allocator.Free(6, 4);
        Assert.Equal(6, allocator.Allocate(24));
        Assert.Equal(30, allocator.Length);
    }

    private static ShadowCascade Cascade(float texel) =>
        new(Matrix4x4.CreateTranslation(0, 0, -10), Matrix4x4.Identity, Vector3.Zero, 100, 200, 100, texel);

    private static WorldLodNode Node(uint id) => new(new(id, 0), null, new(id, 2), Matrix4x4.Identity, Vector3.Zero, 1, 100, 0, 0);

    private static WorldInstance Instance(float x) => new(new(new(1, 0, 0, x), new(0, 1, 0, 0), new(0, 0, 1, 0)), new(x, 0, 0), 1);
}
