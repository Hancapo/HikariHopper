using System.Numerics;
using System.Runtime.InteropServices;
using HikariHopper.Rendering.World;
using RedFury.World;

namespace HikariHopper.Tests;

// RedFury's tests of the world's local lights, for what the world viewer copied: the frame's packing and screen tiles,
// and binding the cut's static lights to their placements.
public sealed class WorldLightTests
{
    private static LocalLight Point(Vector3 position, float radius = 1) => new(LocalLightType.Point, position, new(1, .5f, .25f), 10, radius);

    // A camera at the eye looking along +Y, its near plane at 0.1, square.
    private static void Update(LocalLightFrame frame, IReadOnlyList<LocalLight> lights, Vector3 eye = default, Vector3? forward = null) =>
        frame.Update(lights, eye, forward ?? Vector3.UnitY, .1f, 1);

    [Fact]
    public void Lights_are_packed_relative_to_the_camera_their_tiles_follow_it_and_removed_lights_clear()
    {
        var frame = new LocalLightFrame();
        Vector3 eye = new(10000, 20000, 30000);
        Update(frame, [Point(eye + new Vector3(0, 10, 0))], eye);
        Assert.Equal(1, frame.Count);
        ReadOnlySpan<float> packed = MemoryMarshal.Cast<byte, float>(frame.LightData);
        Assert.Equal(0, packed[0]);
        Assert.Equal(10, packed[1]);
        Assert.Equal(0, packed[2]);
        Assert.Equal(1, packed[3]);
        Assert.Equal(10, packed[4]);
        Assert.Equal(5, packed[5]);
        Assert.Equal(2.5f, packed[6]);
        Assert.NotEqual(0u, frame.TileMasks[(9 * LocalLightFrame.Columns + 16) * 4]);
        Assert.Equal(0u, frame.TileMasks[0]);
        Update(frame, [Point(eye + new Vector3(0, 10, 0))], eye, -Vector3.UnitY);
        Assert.Equal(0, frame.Count);
        Assert.All(frame.TileMasks.ToArray(), mask => Assert.Equal(0u, mask));
        Update(frame, [], eye);
        Assert.Equal(0, frame.Count);
    }

    [Fact]
    public void At_most_128_are_drawn_the_nearest_whatever_their_order()
    {
        LocalLight[] lights = [.. Enumerable.Range(0, 140).Select(i => Point(new(0, 200 - i, 0)))];
        var frame = new LocalLightFrame();
        Update(frame, lights);
        Assert.Equal(128, frame.Count);
        ReadOnlySpan<float> packed = MemoryMarshal.Cast<byte, float>(frame.LightData);
        Assert.Equal(61, packed[1]);
        Assert.Equal(188, packed[127 * 16 + 1]);
    }

    [Fact]
    public void A_light_across_the_near_plane_covers_every_tile_and_a_spots_direction_is_normalised()
    {
        var spot = new LocalLight(LocalLightType.Spot, Vector3.Zero, Vector3.One, 10, 2, new(0, 5, 0), .9f, .5f, 3);
        var frame = new LocalLightFrame();
        Update(frame, [spot]);
        Assert.Equal(Vector3.UnitY, spot.Direction);
        Assert.All(frame.TileMasks.ToArray().Where((_, index) => index % 4 == 0), mask => Assert.Equal(1u, mask));
        ReadOnlySpan<float> packed = MemoryMarshal.Cast<byte, float>(frame.LightData);
        Assert.Equal(1, packed[7]);
        Assert.Equal(.9f, packed[11]);
        Assert.Equal(.5f, packed[12]);
        Assert.Equal(3, packed[13]);
    }

    [Fact]
    public void The_tile_masks_use_all_four_words_and_clear_those_no_longer_used()
    {
        LocalLight[] lights = [.. Enumerable.Repeat(Point(Vector3.Zero, 2), 128)];
        var frame = new LocalLightFrame();
        Update(frame, lights);
        Assert.All(frame.TileMasks.ToArray(), mask => Assert.Equal(uint.MaxValue, mask));
        Update(frame, [.. lights.Take(33)]);
        ReadOnlySpan<uint> masks = frame.TileMasks;
        for (int tile = 0; tile < LocalLightFrame.Columns * LocalLightFrame.Rows; tile++)
        {
            Assert.Equal(uint.MaxValue, masks[tile * 4]);
            Assert.Equal(1u, masks[tile * 4 + 1]);
            Assert.Equal(0u, masks[tile * 4 + 2]);
            Assert.Equal(0u, masks[tile * 4 + 3]);
        }
    }

    [Fact]
    public void Values_that_arent_finite_or_in_range_never_reach_the_GPU()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Point(new(float.NaN, 0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Point(Vector3.Zero, float.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => Point(Vector3.Zero, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LocalLight(LocalLightType.Point, Vector3.Zero, new(-1, 1, 1), 1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LocalLight(LocalLightType.Spot, Vector3.Zero, Vector3.One, 1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LocalLight(LocalLightType.Spot, Vector3.Zero, Vector3.One, 1, 2, Vector3.UnitY, .2f, .9f));
        var frame = new LocalLightFrame();
        Update(frame, [default]);
        Assert.Equal(0, frame.Count);
    }

    private static WorldLightDefinition Definition => new(Vector3.UnitX, -Vector3.UnitZ, Vector3.UnitX, Vector3.One,
        3, 5, 2, 1, -1, 0, 0, 20, 40, Vector3.Zero, Vector3.Zero);

    private static WorldLodNode Node(WorldLightSet lights) => new(new(1, 0), null, new(2, 2),
        Matrix4x4.CreateScale(2) * Matrix4x4.CreateTranslation(10, 20, 30), Vector3.Zero, 10, 100, 0, 0)
    {
        ArchetypeLights = lights,
    };

    private static WorldLightBinding Resolve(WorldLodNode node) =>
        WorldLightBinding.Resolve(new WorldSelection([], [node.Asset]) { Coverage = [node] }, _ => WorldLightBones.Empty);

    [Fact]
    public void The_cut_keeps_its_lights_off_screen_too_but_not_those_of_assets_not_uploaded()
    {
        WorldLodNode node = Node(new([Definition], []));
        WorldLodNode absent = node with { Id = new(1, 1), Asset = new(3, 2) };
        var selection = new WorldSelection([], [node.Asset, absent.Asset]) { Coverage = [node, absent] };
        Assert.Single(WorldLightBinding.Resolve(selection, key => key == node.Asset ? WorldLightBones.Empty : null).Lights);
        Assert.Empty(WorldLightBinding.Resolve(new WorldSelection([], []), _ => WorldLightBones.Empty).Lights);
        Assert.Empty(WorldLightBinding.Resolve(selection, _ => null).Lights);
    }

    [Fact]
    public void Entity_overrides_replace_by_their_whole_identity_and_secondary_lights_add()
    {
        WorldLodNode node = Node(new([Definition with { Value66657D69 = 0x12345678 }, Definition with { Intensity = 4, Value66657D69 = 9 }], []))
            with
        {
            EntityLights = new([Definition with { Intensity = 7, ValueCED22A4C = 0x12345678 }, Definition with { Intensity = 99, ValueCED22A4C = 8 }],
                [Definition with { Intensity = 11 }]),
        };
        WorldLightBinding lights = Resolve(node);
        Assert.Equal([7f, 4f, 11f], lights.Lights.Select(l => l.Intensity));
        Assert.All(lights.Lights, light =>
        {
            Assert.Equal(new Vector3(12, 20, 30), light.Position);
            Assert.Equal(10, light.Radius);
        });
    }

    [Fact]
    public void A_spot_takes_its_half_angles_in_degrees_and_its_direction_placed()
    {
        WorldLodNode node = Node(new([Definition with { LightType = 2, Direction = Vector3.UnitX, ConeInnerAngle = 30, ConeOuterAngle = 60 }], []))
            with
        {
            Transform = Matrix4x4.CreateRotationZ(MathF.PI / 2),
        };
        LocalLight light = Assert.Single(Resolve(node).Lights);
        Assert.Equal(LocalLightType.Spot, light.Type);
        Assert.True(Vector3.Distance(Vector3.UnitY, light.Direction) < .00001f);
        Assert.Equal(.8660254f, light.InnerCos, 5);
        Assert.Equal(.5f, light.OuterCos, 5);
    }

    [Fact]
    public void Without_a_skeleton_lights_use_the_placement_and_unsupported_or_invalid_ones_are_said()
    {
        WorldLodNode node = Node(new([Definition with { BoneTag = 111 }, Definition with { LightType = 4 }, Definition with { Falloff = float.NaN }], []));
        WorldLightBinding result = Resolve(node);
        Assert.Equal(new Vector3(12, 20, 30), Assert.Single(result.Lights).Position);
        Assert.Contains(result.Diagnostics, d => d.Contains("type 4"));
        Assert.Contains(result.Diagnostics, d => d.Contains("invalid"));
    }
}
