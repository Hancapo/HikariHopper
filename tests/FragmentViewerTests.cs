using System.Numerics;
using CommonFury.Textures;
using CommonFury.Vertices;
using FiveFury;
using FiveFury.Drawables;
using FiveFury.Textures;
using FiveFury.Yft;
using HikariHopper.Rendering;
using HikariHopper.ViewModels;
using GameFormat = CommonFury.Textures.TextureFormat;

namespace HikariHopper.Tests;

public sealed class FragmentViewerTests
{
    private static Drawable Box(string material, Skeleton? skeleton = null)
    {
        Vector3[] positions = [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0)];
        var mesh = new DrawableMeshInput(positions, [0, 1, 2, 0, 2, 3], material);
        mesh.SetChannel(VertexSemantic.Normal, [.. Enumerable.Repeat(Vector4.UnitZ, 4)]);
        mesh.SetChannel(VertexSemantic.TexCoord0, [.. Enumerable.Repeat(Vector4.Zero, 4)]);
        var builder = new DrawableBuilder { Name = material, Skeleton = skeleton };
        builder.Materials.Add(new DrawableMaterialInput { Name = material, Shader = "default" });
        var model = new DrawableModelInput();
        model.Meshes.Add(mesh);
        builder.Models.Add(model);
        return builder.Build(GameTarget.Legacy);
    }

    [Fact]
    public void AVehiclesOneWheelIsPutOnEveryWheelBoneFacingOut()
    {
        var skeleton = new Skeleton();
        (string Name, ushort Tag, Vector3 At)[] bones =
        [
            ("chassis", 0, Vector3.Zero),
            ("wheel_lf", 27922, new(-.8f, 1.4f, -.25f)),
            ("wheel_rf", 26418, new(.8f, 1.4f, -.25f)),
            ("wheel_lr", 27902, new(-.75f, -1.4f, -.23f)),
            ("wheel_rr", 26398, new(.75f, -1.4f, -.23f)),
        ];
        for (int i = 0; i < bones.Length; i++)
            skeleton.Bones.Add(new Bone { Name = bones[i].Name, Tag = bones[i].Tag, Index = (ushort)i, ParentIndex = -1, Translation = bones[i].At });
        Drawable body = Box("body", skeleton), wheel = Box("wheel");
        var fragment = new Yft { MainDrawable = new FragmentDrawable(body) };
        var physics = new PhysicsLod();
        foreach (var bone in bones)
            physics.Children.Add(new PhysicsChild { BoneId = bone.Tag, UndamagedDrawable = bone.Name == "wheel_lf" ? new FragmentDrawable(wheel) : null });
        fragment.PhysicsLods[0] = physics;

        IReadOnlyList<SceneModel> models = FragmentModels.Of(fragment, "car");

        // The body, named for the fragment, then the one wheel at each wheel bone, gathered as the wheels; the
        // chassis isn't a wheel.
        Assert.Equal(("car", (string?)null), (models[0].Name, models[0].Group));
        Assert.Equal(["wheel_lf", "wheel_rf", "wheel_lr", "wheel_rr"], models.Skip(1).Select(m => m.Name));
        Assert.All(models.Skip(1), model => Assert.Equal("Wheels", model.Group));
        Assert.Same(body, models[0].Drawable);
        Assert.All(models.Skip(1), model => Assert.Same(wheel, model.Drawable));
        foreach (SceneModel model in models.Skip(1))
        {
            Vector3 at = bones.Single(b => b.Name == model.Name).At;
            Assert.Equal(at, model.Transform.Translation);
            // Left wheels as modelled; right ones turned half round about the vertical, so their outer face is out.
            Vector3 outward = Vector3.TransformNormal(-Vector3.UnitX, model.Transform);
            Assert.Equal(Math.Sign(at.X), Math.Sign(outward.X));
            Assert.True(model.Transform.GetDeterminant() > 0, "turned, not mirrored");
        }

        // One set of materials for the four wheels.
        DrawableScene scene = DrawableScene.Build(models, TextureLookup.None);
        Assert.Equal(2, scene.Materials.Count);
        Assert.Equal(5 * 2, scene.TriangleCount);
    }

    // A skeleton: the chassis, a door under it with a handle under the door, and an extra; the chassis and the door are
    // physics children.
    private static (Skeleton Skeleton, PhysicsLod Physics) Rig()
    {
        var skeleton = new Skeleton();
        (string Name, ushort Tag, short Parent)[] bones = [("chassis", 0, -1), ("door_dside_f", 1, 0), ("handle_dside_f", 2, 1), ("extra_1", 3, 0)];
        for (int i = 0; i < bones.Length; i++)
            skeleton.Bones.Add(new Bone { Name = bones[i].Name, Tag = bones[i].Tag, Index = (ushort)i, ParentIndex = bones[i].Parent });
        var physics = new PhysicsLod();
        physics.Children.Add(new PhysicsChild { BoneId = 0 });
        physics.Children.Add(new PhysicsChild { BoneId = 1 });
        return (skeleton, physics);
    }

    // A skinned drawable of four triangles, each bound to one of the rig's bones, as vehicles bind them.
    private static Drawable Skinned(Skeleton skeleton, int triangles = 4)
    {
        var positions = new List<Vector3>();
        var indices = new List<int>();
        var blend = new List<Vector4>();
        for (int t = 0; t < triangles; t++)
        {
            positions.AddRange([new(t, 0, 0), new(t + 1, 0, 0), new(t, 1, 0)]);
            indices.AddRange([3 * t, 3 * t + 1, 3 * t + 2]);
            blend.AddRange(Enumerable.Repeat(new Vector4(t % 4, 0, 0, 0), 3));
        }
        var mesh = new DrawableMeshInput([.. positions], [.. indices], "body");
        mesh.SetChannel(VertexSemantic.BlendIndices, [.. blend]);
        mesh.SetChannel(VertexSemantic.BlendWeights, [.. Enumerable.Repeat(new Vector4(1, 0, 0, 0), positions.Count)]);
        var builder = new DrawableBuilder { Name = "body", Skeleton = skeleton };
        builder.Materials.Add(new DrawableMaterialInput { Name = "body", Shader = "default" });
        var model = new DrawableModelInput { SkeletonBinding = new SkeletonBinding(0, 1, 0, 0) };
        model.Meshes.Add(mesh);
        builder.Models.Add(model);
        return builder.Build(GameTarget.Legacy);
    }

    [Fact]
    public void A_fragments_bones_belong_to_the_physics_child_above_them_or_are_extras()
    {
        var (skeleton, physics) = Rig();
        IReadOnlyDictionary<int, string> parts = FragmentModels.PartsOf(skeleton, physics);
        Assert.Equal(["chassis", "door_dside_f", "door_dside_f", "extra_1"], Enumerable.Range(0, 4).Select(bone => parts[bone]));
    }

    [Fact]
    public void A_skinned_fragment_is_split_into_its_children_by_the_bones_its_triangles_are_bound_to()
    {
        var (skeleton, physics) = Rig();
        var fragment = new Yft { MainDrawable = new FragmentDrawable(Skinned(skeleton)) };
        fragment.PhysicsLods[0] = physics;

        DrawableScene scene = DrawableScene.Build(FragmentModels.Of(fragment, "car"), TextureLookup.None);
        // The door's triangle and its handle's are the door's; the extra is a part of its own.
        Assert.Equal(["chassis", "door_dside_f", "extra_1"], scene.Parts.Select(part => part.Group));
        Assert.Equal([1, 2, 1], scene.Parts.Select(part => part.TriangleCount));
        Assert.Equal(4, scene.TriangleCount);

        var visibility = new PartVisibility(scene);
        PartNode car = Assert.Single(visibility.Tree());
        Assert.Equal(["chassis", "door_dside_f", "extra_1"], car.Children.Select(child => child.Name));
        visibility.Show(car.Children[1], false);
        Assert.Equal([true, false, true], visibility.Mask());
        Assert.False(scene.HasDamaged);
    }

    [Fact]
    public void A_fragments_damaged_drawable_is_drawn_in_place_of_its_intact_one_while_damaged()
    {
        var (skeleton, physics) = Rig();
        var fragment = new Yft { MainDrawable = new FragmentDrawable(Skinned(skeleton)) };
        fragment.Drawables.Add(new FragmentDrawableEntry("damaged", new FragmentDrawable(Skinned(skeleton, 2))));
        fragment.DamagedDrawableIndex = 0;
        fragment.PhysicsLods[0] = physics;

        IReadOnlyList<SceneModel> models = FragmentModels.Of(fragment, "sign");
        Assert.Equal([ModelVariant.Intact, ModelVariant.Damaged], models.Select(model => model.Variant));
        DrawableScene scene = DrawableScene.Build(models, TextureLookup.None);
        Assert.True(scene.HasDamaged);
        // At first, intact: the damaged drawable isn't counted.
        Assert.Equal(4, scene.TriangleCount);

        var visibility = new PartVisibility(scene);
        Assert.Equal((4, 12, 12), visibility.Stats());
        visibility.Damaged = true;
        Assert.Equal((2, 6, 6), visibility.Stats());
        // One row for the sign either way, named for it.
        Assert.Equal("sign", Assert.Single(visibility.Tree()).Name);
    }

    [Fact]
    public void AVehiclesTexturesAreLookedForInItsOwnDictionariesAndTheOnesVehiclesShare()
    {
        Assert.Equal(["adder+hi.ytd", "adder.ytd", "vehshare.ytd"], ModelFile.DictionaryNames("adder_hi.yft"));
        Assert.Equal(["ADDER.ytd", "vehshare.ytd"], ModelFile.DictionaryNames("ADDER.yft"));
        Assert.Equal(["prop_bench_01a.ytd"], ModelFile.DictionaryNames("prop_bench_01a.ydr"));
        Assert.Equal(["head_000.ytd"], ModelFile.DictionaryNames("head_000.ydd"));
        Assert.Empty(ModelFile.DictionaryNames("ch2_04_15.ybn"));
        Assert.True(ExplorerViewModel.IsModelName("adder_hi.yft"));
        Assert.True(ExplorerViewModel.IsModelName("head_000.ydd"));
    }

    [Fact]
    public void TexturesBesideAModelAreReadOnlyAsFarAsAMissingOneNeeds()
    {
        static byte[] Dictionary(params string[] names)
        {
            var dictionary = new TextureDictionary();
            foreach (string name in names)
                dictionary.Textures.Add(new Texture(name, 4, 4, GameFormat.BC1Unorm, 1, new byte[8]));
            return new FiveFury.Ytd.Ytd(dictionary).Write();
        }
        var named = FiveFury.Ytd.Ytd.Read(Dictionary("adder_body")).Dictionary;
        var reads = new List<string>();
        NearbyDictionary Nearby(string file, params string[] names)
        {
            byte[] data = Dictionary(names);
            return new NearbyDictionary(file, data.Length, () => { reads.Add(file); return data; });
        }
        var lookup = new TextureLookup([named], [Nearby("vehicles_supergt_interior.ytd", "supergt_interior"), Nearby("other.ytd", "other")]);

        // What the named dictionaries hold reads nothing beside them.
        Assert.Equal("adder_body", lookup.Find("ADDER_BODY")?.Name);
        Assert.Empty(reads);
        // A missing texture reads them in order until one has it, and no further.
        Assert.Equal("supergt_interior", lookup.Find("supergt_interior")?.Name);
        Assert.Equal(["vehicles_supergt_interior.ytd"], reads);
        Assert.Null(lookup.Find("nowhere"));
        Assert.Equal(["vehicles_supergt_interior.ytd", "other.ytd"], reads);
        Assert.Empty(lookup.Diagnostics);

        // The search stops at its limit, and says so.
        var bounded = new TextureLookup([], [new NearbyDictionary("huge.ytd", TextureLookup.SearchLimit + 1, () => throw new InvalidOperationException("not read"))]);
        Assert.Null(bounded.Find("anything"));
        Assert.Single(bounded.Diagnostics);
    }
}
