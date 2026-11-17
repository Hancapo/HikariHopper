using System.Numerics;
using FiveFury.Drawables;
using FiveFury.Extensions;
using RedFury.World;

namespace HikariHopper.Rendering;

/// <summary>
/// The lights GTA V's drawables carry, in the scene's coordinates: each at the bone it moves with, then where its drawable
/// is placed, as DrawableOverlays draws them; a fragment's own lights move with its main drawable. What the 3D viewer
/// lights a model with, and what a world's placements of it shine.
/// </summary>
internal static class DrawableLights
{
    public static IReadOnlyList<WorldLightDefinition> Of(IReadOnlyList<SceneModel> models, IReadOnlyList<DrawableLight>? fragmentLights = null)
    {
        var lights = new List<WorldLightDefinition>();
        foreach (SceneModel model in models)
        {
            if (model.Drawable.Lights.Count == 0)
                continue;
            Matrix4x4[]? bones = model.Drawable.Skeleton?.DeriveAbsoluteTransforms();
            foreach (DrawableLight light in model.Drawable.Lights)
                lights.Add(Placed(light.Attributes, DrawableOverlays.BoneOf(model.Drawable, bones, light.Attributes.BoneTag) * model.Transform));
        }
        if (fragmentLights is { Count: > 0 } && models.Count > 0)
        {
            SceneModel main = models[0];
            Matrix4x4[]? bones = main.Drawable.Skeleton?.DeriveAbsoluteTransforms();
            foreach (DrawableLight light in fragmentLights)
                lights.Add(Placed(light.Attributes, DrawableOverlays.BoneOf(main.Drawable, bones, light.Attributes.BoneTag) * main.Transform));
        }
        return lights;
    }

    /// <summary>Each light's corona values, in the order <see cref="Of"/> gives the lights.</summary>
    public static IReadOnlyList<DrawableLightCorona> CoronasOf(IReadOnlyList<SceneModel> models, IReadOnlyList<DrawableLight>? fragmentLights = null)
    {
        var coronas = new List<DrawableLightCorona>();
        foreach (SceneModel model in models)
            foreach (DrawableLight light in model.Drawable.Lights)
                coronas.Add(DrawableLightCorona.From(light.Attributes));
        if (fragmentLights is { Count: > 0 } && models.Count > 0)
            foreach (DrawableLight light in fragmentLights)
                coronas.Add(DrawableLightCorona.From(light.Attributes));
        return coronas;
    }

    // The light's values, its position and directions placed; at no bone now (-1), as it is where its bone puts it.
    private static WorldLightDefinition Placed(LightAttrDef light, Matrix4x4 transform) =>
        new(Vector3.Transform(light.Position, transform), Vector3.TransformNormal(light.Direction, transform),
            Vector3.TransformNormal(light.Tangent, transform), new Vector3(light.Color.R, light.Color.G, light.Color.B) / 255f,
            light.Intensity, light.Falloff, light.FalloffExponent, light.LightType, -1, light.TimeFlags, light.Flags,
            light.ConeInnerAngle, light.ConeOuterAngle, light.Extents, Vector3.Zero, light.LightHash, light.Flashiness, light.GroupId);
}
