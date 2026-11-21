using System.Numerics;
using CommonFury.Assets;
using FiveFury.Drawables;
using FiveFury.Yft;

namespace HikariHopper.Rendering;

/// <summary>
/// What a fragment draws, placed: its main drawable, which holds the whole intact model (a vehicle's body and parts,
/// bound to its skeleton), split into its physics children (the bonnet, the doors) by the bones its triangles are bound
/// to; its damaged drawable, when it has one, in place of the main one while damaged; its children's own drawables at
/// their bones, damaged ones in place of intact ones; and its wheels. A vehicle's wheels are physics children at its
/// wheel bones, and usually only one of them has a model (wheel_lf): the game puts that one on every wheel bone
/// without its own, turned to face out on the other side.
/// </summary>
internal static class FragmentModels
{
    /// <summary>What <paramref name="fragment"/> draws; its main drawable is named <paramref name="title"/>, the fragment's.</summary>
    public static IReadOnlyList<SceneModel> Of(Yft fragment, string title)
    {
        if (fragment.MainDrawable?.Core is not { } main)
            return [];
        PhysicsLod? physics = fragment.PhysicsLods.FirstOrDefault(lod => lod is not null);
        Skeleton? skeleton = main.Skeleton;
        IReadOnlyDictionary<int, string>? parts = skeleton is not null && physics is not null ? PartsOf(skeleton, physics) : null;
        Drawable? damaged = fragment.DamagedDrawableIndex >= 0 && fragment.DamagedDrawableIndex < fragment.Drawables.Count
            && fragment.Drawables[fragment.DamagedDrawableIndex].Drawable?.Core is { } wreck && HasGeometry(wreck) ? wreck : null;
        var models = new List<SceneModel>
        {
            new(title, main, Matrix4x4.Identity, BoneGroups: parts, Variant: damaged is null ? ModelVariant.Always : ModelVariant.Intact),
        };
        if (damaged is not null)
        {
            IReadOnlyDictionary<int, string>? damagedParts = damaged.Skeleton is { } own && physics is not null ? PartsOf(own, physics) : parts;
            models.Add(new SceneModel(title, damaged, Matrix4x4.Identity, BoneGroups: damagedParts, Variant: ModelVariant.Damaged));
        }
        if (skeleton is null || physics is null)
            return models;
        Matrix4x4[] bones = skeleton.DeriveAbsoluteTransforms();
        var wheels = new List<(Bone Bone, Drawable? Model)>();
        foreach (PhysicsChild child in physics.Children)
        {
            if (skeleton.FindBone(child.BoneId) is not { } bone || bone.Index >= bones.Length)
                continue;
            Drawable? intact = child.UndamagedDrawable?.Core is { } whole && HasGeometry(whole) ? whole : null;
            if (bone.Name is { } name && name.StartsWith("wheel_", StringComparison.OrdinalIgnoreCase))
            {
                wheels.Add((bone, intact));
                continue;
            }
            // A child with geometry of its own, at its bone; its damaged one in its place while damaged.
            Drawable? broken = child.DamagedDrawable?.Core is { } piece && !ReferenceEquals(piece, intact) && HasGeometry(piece) ? piece : null;
            string label = bone.Name is { Length: > 0 } boneName ? boneName : $"child {physics.Children.IndexOf(child) + 1}";
            if (intact is not null)
                models.Add(new SceneModel(label, intact, bones[bone.Index], "Children", Variant: broken is null ? ModelVariant.Always : ModelVariant.Intact));
            if (broken is not null)
                models.Add(new SceneModel(label, broken, bones[bone.Index], "Children", Variant: ModelVariant.Damaged));
        }
        if (wheels.FirstOrDefault(wheel => wheel.Model is not null) is not ({ } shared, { } sharedModel))
            return models;
        foreach (var (bone, model) in wheels)
        {
            // A wheel on the other side from the one it borrows is turned half round, its outer face out.
            bool turned = model is null && Side(bone.Name!) != Side(shared.Name!);
            Matrix4x4 turn = turned ? Matrix4x4.CreateRotationZ(MathF.PI) : Matrix4x4.Identity;
            models.Add(new SceneModel(bone.Name!, model ?? sharedModel, turn * bones[bone.Index], "Wheels"));
        }
        return models;
    }

    /// <summary>
    /// The part each bone of a skeleton belongs to: the physics child at it or nearest above it (a door's handle is
    /// the door's), or a vehicle extra (extra_1), which is a part of its own; the root's child, or the root, for the rest.
    /// </summary>
    internal static IReadOnlyDictionary<int, string> PartsOf(Skeleton skeleton, PhysicsLod physics)
    {
        var children = new HashSet<int>();
        foreach (PhysicsChild child in physics.Children)
            if (skeleton.FindBone(child.BoneId) is { } bone)
                children.Add(bone.Index);
        string Name(int index) => skeleton.Bones[index].Name is { Length: > 0 } name ? name : $"bone {index}";
        var parts = new Dictionary<int, string>();
        for (int index = 0; index < skeleton.Bones.Count; index++)
        {
            int at = index;
            while (at >= 0 && at < skeleton.Bones.Count && !children.Contains(at)
                && !Name(at).StartsWith("extra_", StringComparison.OrdinalIgnoreCase))
                at = skeleton.Bones[at].ParentIndex;
            parts[index] = at >= 0 && at < skeleton.Bones.Count ? Name(at) : Name(0);
        }
        return parts;
    }

    /// <summary>Which side of the vehicle a wheel bone is on: wheel_lf, wheel_lm1 left; wheel_rf, wheel_rr right.</summary>
    internal static char Side(string bone) => bone.Length > 6 ? char.ToLowerInvariant(bone[6]) : ' ';

    private static bool HasGeometry(Drawable drawable) =>
        Enumerable.Range(0, 4).Any(lod => drawable.Lods[(DrawableLod)lod].Models.Any(model => model.Geometries.Count > 0));
}
