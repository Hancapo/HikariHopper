using System.Numerics;
using CommonFury.Numerics;
using FiveFury;
using FiveFury.Textures;
using FiveFury.Drawables;
using FiveFury.Ybn;
using FiveFury.Ydd;
using FiveFury.Ydr;
using FiveFury.Yft;
using FiveFury.Ynv;
using FiveFury.Ytd;

namespace HikariHopper.Rendering;

/// <summary>
/// A 3D file's content for the viewport: its triangles, and the box around them. Collisions and navmeshes are placed
/// at their map coordinates, so the viewer goes to them rather than to the world's origin; a drawable is shown around
/// its own origin, with its materials, as <see cref="Surfaces"/>.
/// </summary>
internal sealed record ModelContent(Aabb3 Box, ViewportMesh Mesh, DrawableScene? Surfaces = null)
{
    /// <summary>
    /// Reads a collision (.ybn), a navmesh (.ynv), a drawable (.ydr), a drawable dictionary (.ydd, its drawables
    /// together) or a fragment (.yft: its main drawable, a vehicle's body and parts, and its wheels). A drawable's
    /// textures come from its own dictionary, then from <paramref name="dictionaries"/> (the .ytd files named for it,
    /// in order), and then from <paramref name="nearby"/> (the other .ytd files beside it), which are read only for a
    /// texture still missing. Dictionaries that can't be read are left out.
    /// </summary>
    /// <summary>
    /// The textures a .ydr, .ydd or .yft carries in its drawables' own dictionaries, each name once, and its edition:
    /// what its viewer's textures button shows.
    /// </summary>
    /// <exception cref="InvalidDataException">The file isn't one this can read.</exception>
    public static (IReadOnlyList<Texture> Textures, GameTarget Target) EmbeddedTextures(string name, ReadOnlySpan<byte> data)
    {
        IEnumerable<Drawable> drawables;
        GameTarget target;
        switch (Path.GetExtension(name).ToLowerInvariant())
        {
            case ".ydr":
                Ydr ydr = Ydr.Read(data);
                (drawables, target) = ([ydr.Drawable], ydr.Target);
                break;
            case ".ydd":
                Ydd ydd = Ydd.Read(data);
                (drawables, target) = ([.. ydd.Drawables.Select(entry => entry.Drawable)], ydd.Target);
                break;
            case ".yft":
                Yft yft = Yft.Read(data);
                (drawables, target) = ([.. FragmentModels.Of(yft, name).Select(model => model.Drawable)], yft.Target);
                break;
            default:
                throw new InvalidDataException($"{name} isn't a drawable.");
        }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var textures = new List<Texture>();
        foreach (TextureDictionary dictionary in drawables.Select(d => d.TextureDictionary).OfType<TextureDictionary>().Distinct())
            foreach (Texture texture in dictionary.Textures)
                if (texture.Name is { } own && seen.Add(own))
                    textures.Add(texture);
        return (textures, target);
    }

    /// <exception cref="InvalidDataException">The file isn't one this can read.</exception>
    public static ModelContent Read(string name, ReadOnlySpan<byte> data, IReadOnlyList<byte[]>? dictionaries = null,
        IReadOnlyList<NearbyDictionary>? nearby = null, IReadOnlyList<FiveFury.Textures.Texture>? overrides = null)
    {
        string extension = Path.GetExtension(name).ToLowerInvariant();
        Yft? fragment = extension == ".yft" ? Yft.Read(data) : null;
        IReadOnlyList<SceneModel>? models = extension switch
        {
            ".ydr" => [new SceneModel(Path.GetFileNameWithoutExtension(name), Ydr.Read(data).Drawable, Matrix4x4.Identity)],
            ".ydd" => [.. Ydd.Read(data).Drawables.Select(entry => new SceneModel(entry.Drawable.Name is { Length: > 0 } own ? own : $"0x{entry.Name.Value:X8}",
                entry.Drawable, Matrix4x4.Identity))],
            ".yft" => FragmentModels.Of(fragment!, Path.GetFileNameWithoutExtension(name)),
            _ => null,
        };
        if (models is not null)
        {
            var named = new List<TextureDictionary>();
            foreach (byte[] dictionary in dictionaries ?? [])
            {
                try
                {
                    named.Add(Ytd.Read(dictionary).Dictionary);
                }
                catch (Exception error) when (error is InvalidDataException or EndOfStreamException or ArgumentException or NotSupportedException)
                {
                }
            }
            DrawableScene scene = DrawableScene.Build(models, new TextureLookup(named, nearby ?? [], overrides),
                fragment?.PhysicsLods.FirstOrDefault(lod => lod is not null)?.CompositeBound, fragment?.Lights);
            return new ModelContent(scene.Box, new ViewportMesh(scene.Origin), scene);
        }
        if (name.EndsWith(".ybn", StringComparison.OrdinalIgnoreCase))
        {
            Ybn ybn = Ybn.Read(data);
            return new ModelContent(ybn.Bound.Box, CollisionMesh.Build(ybn));
        }
        if (name.EndsWith(".ynv", StringComparison.OrdinalIgnoreCase))
        {
            // Navmesh vertices are decoded to world coordinates.
            Ynv navmesh = Ynv.Read(data);
            List<Vector3> vertices = navmesh.Vertices;
            if (vertices.Count == 0)
                return new ModelContent(Aabb3.Empty, new ViewportMesh(Vector3.Zero));
            Vector3 minimum = vertices[0], maximum = vertices[0];
            foreach (Vector3 vertex in vertices)
            {
                minimum = Vector3.Min(minimum, vertex);
                maximum = Vector3.Max(maximum, vertex);
            }
            var box = new Aabb3(minimum, maximum);
            return new ModelContent(box, NavmeshMesh.Build(navmesh, box.Center));
        }
        throw new InvalidDataException($"{name} isn't a collision, navmesh, drawable or fragment file.");
    }
}
