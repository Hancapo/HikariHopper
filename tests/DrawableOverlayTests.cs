using System.Numerics;
using CommonFury.Numerics;
using FiveFury.Bounds;
using FiveFury.Drawables;
using HikariHopper.Rendering;

namespace HikariHopper.Tests;

public sealed class DrawableOverlayTests
{
    // A root bone and an arm two metres above it, a box of collision two metres across, a spot light on the arm and a
    // point light at the root.
    private static Drawable Rig()
    {
        var skeleton = new Skeleton();
        skeleton.Bones.Add(new Bone { Name = "root", Tag = 0, Index = 0 });
        skeleton.Bones.Add(new Bone { Name = "arm", Tag = 10, Index = 1, ParentIndex = 0, Translation = new Vector3(0, 0, 2) });
        var drawable = new Drawable { Skeleton = skeleton, Bound = new BoundBox(new Aabb3(new Vector3(-1), new Vector3(1))) };
        var spot = new DrawableLight();
        spot.Attributes.LightType = 2;
        spot.Attributes.Falloff = 4;
        spot.Attributes.ConeOuterAngle = 30;
        spot.Attributes.BoneTag = 10;
        spot.Attributes.Position = new Vector3(0, 1, 0);
        spot.Attributes.Color = new FiveFury.Colors.Rgb24(255, 200, 100);
        var point = new DrawableLight();
        point.Attributes.LightType = 1;
        point.Attributes.Falloff = 2;
        drawable.Lights.Add(spot);
        drawable.Lights.Add(point);
        return drawable;
    }

    private static readonly Vector3 Origin = new(0, 0, 1);

    private static DrawableOverlays Build(Bound? fragmentBound = null) =>
        DrawableOverlays.Build([new SceneModel("rig", Rig(), Matrix4x4.CreateTranslation(5, 0, 0))], fragmentBound, Origin, 2);

    [Fact]
    public void A_skeleton_draws_each_bone_to_its_parent_with_a_cross_at_each_joint()
    {
        ViewportSegment[] skeleton = Build().Skeleton;
        // Two joints of three lines each, and the one bone between them, where the drawable is placed.
        Assert.Equal(7, skeleton.Length);
        Assert.All(skeleton, segment => Assert.Equal(DrawableOverlays.BoneColour, segment.Colour));
        Assert.Equal(new Vector3(5, 0, 0) - Origin, skeleton[6].A);
        Assert.Equal(new Vector3(5, 0, 2) - Origin, skeleton[6].B);
    }

    [Fact]
    public void Bounds_draw_their_triangles_edges_once()
    {
        ViewportSegment[] bounds = Build().Bounds;
        // A box of twelve triangles: its twelve edges and a diagonal across each face.
        Assert.Equal(18, bounds.Length);
        Assert.All(bounds, segment =>
        {
            Assert.Equal(DrawableOverlays.BoundColour, segment.Colour);
            Assert.InRange(segment.A.X + Origin.X, 4 - 1e-4f, 6 + 1e-4f);
            Assert.InRange(segment.B.Z + Origin.Z, -1 - 1e-4f, 1 + 1e-4f);
        });
    }

    [Fact]
    public void A_fragments_physics_bounds_are_drawn_in_place_of_its_drawables()
    {
        ViewportSegment[] bounds = Build(new BoundBox(new Aabb3(new Vector3(-.5f), new Vector3(.5f)))).Bounds;
        Assert.Equal(18, bounds.Length);
        // The fragment's composite is where the fragment is, not moved with a drawable.
        Assert.All(bounds, segment => Assert.InRange(segment.A.X + Origin.X, -.5f - 1e-4f, .5f + 1e-4f));
    }

    [Fact]
    public void Lights_draw_where_they_shine_from_in_their_colour_with_their_reach()
    {
        ViewportSegment[] lights = Build().Lights;
        // The spot: a cross, the cone's rim and four lines to it. The point light: a cross and three circles.
        Assert.Equal(3 + 32 + 4 + 3 + 3 * 32, lights.Length);
        Assert.Equal(ViewportMesh.Pack(255, 200, 100), lights[0].Colour);
        // On the arm's bone: placed, up two metres, and its own metre along Y.
        Assert.Equal(new Vector3(5, 1, 2) - Origin, (lights[0].A + lights[0].B) / 2);
        // The point light's circles are its reach from where it is, the drawable's origin.
        ViewportSegment rim = lights[^1];
        Assert.Equal(2, Vector3.Distance(rim.B + Origin, new Vector3(5, 0, 0)), 0.001);
    }

    [Fact]
    public void A_piece_placed_at_a_bone_with_its_owners_skeleton_does_not_draw_it_again()
    {
        Drawable owner = Rig();
        // A wheel: placed at a bone, in a group, carrying the vehicle's skeleton as wheels do.
        var wheel = new Drawable { Skeleton = owner.Skeleton };
        DrawableOverlays overlays = DrawableOverlays.Build(
            [new SceneModel("car", owner, Matrix4x4.Identity), new SceneModel("wheel_lf", wheel, Matrix4x4.CreateTranslation(1, 1, 0), "Wheels")],
            null, Vector3.Zero, 2);
        Assert.Equal(7, overlays.Skeleton.Length);
    }

    [Fact]
    public void A_fragments_own_lights_move_with_its_main_drawables_bones()
    {
        var light = new DrawableLight();
        light.Attributes.BoneTag = 10;
        light.Attributes.Position = new Vector3(0, 1, 0);
        DrawableOverlays overlays = DrawableOverlays.Build([new SceneModel("car", Rig(), Matrix4x4.Identity)], null, Vector3.Zero, 2, [light]);
        // The rig's two lights, then the fragment's: a cross on the arm, with no reach.
        ViewportSegment[] lights = overlays.Lights;
        Assert.Equal(3 + 32 + 4 + 3 + 3 * 32 + 3, lights.Length);
        Assert.Equal(new Vector3(0, 1, 2), (lights[^1].A + lights[^1].B) / 2);
    }

    [Fact]
    public void A_drawable_without_them_has_no_overlays()
    {
        DrawableOverlays overlays = DrawableOverlays.Build([new SceneModel("plain", new Drawable(), Matrix4x4.Identity)], null, Vector3.Zero, 1);
        Assert.Empty(overlays.Skeleton);
        Assert.Empty(overlays.Bounds);
        Assert.Empty(overlays.Lights);
    }
}
