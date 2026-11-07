using System.Numerics;
using CommonFury.Assets;
using FiveFury.Drawables;
using FiveFury.Yft;

namespace HikariHopper.Rendering;

/// <summary>
/// What a fragment draws, placed: its main drawable, which holds the whole intact model (a vehicle's body and parts,
/// bound to its skeleton), and its wheels. A vehicle's wheels are physics children at its wheel bones, and usually
/// only one of them has a model (wheel_lf): the game puts that one on every wheel bone without its own, turned to face
/// out on the other side.
/// </summary>
internal static class FragmentModels
{
    public static IReadOnlyList<SceneModel> Of(Yft fragment)
    {
        if (fragment.MainDrawable?.Core is not { } main)
            return [];
        var models = new List<SceneModel> { new(main.Name ?? "main", main, Matrix4x4.Identity) };
        if (main.Skeleton is not { } skeleton || fragment.PhysicsLods.FirstOrDefault(lod => lod is not null) is not { } physics)
            return models;
        Matrix4x4[] bones = skeleton.DeriveAbsoluteTransforms();
        var wheels = new List<(Bone Bone, Drawable? Model)>();
        foreach (PhysicsChild child in physics.Children)
            if (skeleton.FindBone(child.BoneId) is { Name: { } name } bone && name.StartsWith("wheel_", StringComparison.OrdinalIgnoreCase)
                && bone.Index < bones.Length)
                wheels.Add((bone, child.UndamagedDrawable?.Core is { } drawable && HasGeometry(drawable) ? drawable : null));
        if (wheels.FirstOrDefault(wheel => wheel.Model is not null) is not ({ } shared, { } sharedModel))
            return models;
        foreach (var (bone, model) in wheels)
        {
            // A wheel on the other side from the one it borrows is turned half round, its outer face out.
            bool turned = model is null && Side(bone.Name!) != Side(shared.Name!);
            Matrix4x4 turn = turned ? Matrix4x4.CreateRotationZ(MathF.PI) : Matrix4x4.Identity;
            models.Add(new SceneModel(bone.Name!, model ?? sharedModel, turn * bones[bone.Index]));
        }
        return models;
    }

    /// <summary>Which side of the vehicle a wheel bone is on: wheel_lf, wheel_lm1 left; wheel_rf, wheel_rr right.</summary>
    internal static char Side(string bone) => bone.Length > 6 ? char.ToLowerInvariant(bone[6]) : ' ';

    private static bool HasGeometry(Drawable drawable) =>
        Enumerable.Range(0, 4).Any(lod => drawable.Lods[(DrawableLod)lod].Models.Any(model => model.Geometries.Count > 0));
}
