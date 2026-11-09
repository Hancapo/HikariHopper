using System.Numerics;
using CommonFury.Assets;
using CommonFury.Numerics;
using CommonFury.Vertices;
using FiveFury.Drawables;
using FiveFury.Textures;

namespace HikariHopper.Rendering;

/// <summary>A run of a scene's indices drawn with one material.</summary>
/// <param name="Center">The middle of its triangles, relative to the scene's origin: blended parts are drawn far to near.</param>
internal readonly record struct SurfacePart(int Material, uint FirstIndex, uint IndexCount, int BaseVertex, Vector3 Center);

/// <summary>A drawable placed in a scene: a fragment's wheel at its bone, say.</summary>
internal sealed record SceneModel(string Name, Drawable Drawable, Matrix4x4 Transform);

/// <summary>
/// Drawables' surfaces made ready for the viewport (after RedFury's ViewerDocument): their most detailed level's
/// triangles in one vertex and index buffer, relative to the middle of their box, with a material for each geometry.
/// Each model is placed by its transform; parts bound rigidly to a bone are placed where the skeleton holds them, and
/// skinned ones are drawn in their bind pose. What couldn't be shown as the game shows it is in <see cref="Diagnostics"/>.
/// </summary>
internal sealed class DrawableScene
{
    public required Aabb3 Box { get; init; }
    public required Vector3 Origin { get; init; }
    public required SurfaceVertex[] Vertices { get; init; }
    public required uint[] Indices { get; init; }
    public required IReadOnlyList<SurfacePart> Parts { get; init; }
    public required IReadOnlyList<SurfaceMaterial> Materials { get; init; }

    /// <summary>What isn't shown as the game shows it: textures not found, shaders not rebuilt yet, skinned models.</summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = [];

    public int TriangleCount => Indices.Length / 3;

    public int VertexCount => Vertices.Length;

    /// <summary>The triangles' distinct edges, a shared edge once, as modelling tools count them.</summary>
    public int EdgeCount { get; init; }

    /// <summary>Builds a drawable's scene, finding textures in its own dictionary and then in <paramref name="dictionaries"/>.</summary>
    public static DrawableScene Build(Drawable drawable, IReadOnlyList<TextureDictionary> dictionaries) =>
        Build([new SceneModel(drawable.Name ?? "drawable", drawable, Matrix4x4.Identity)], new TextureLookup(dictionaries, []));

    /// <summary>
    /// Builds one scene of placed drawables, finding textures in the drawables' own dictionaries and then through
    /// <paramref name="lookup"/>. A drawable placed more than once (a wheel at each of its bones) shares its materials,
    /// as does one using another's (a fragment's wheel, its main drawable's).
    /// </summary>
    public static DrawableScene Build(IReadOnlyList<SceneModel> models, TextureLookup lookup)
    {
        var own = new Dictionary<string, Texture>(StringComparer.OrdinalIgnoreCase);
        foreach (TextureDictionary dictionary in models.Select(m => m.Drawable.TextureDictionary).OfType<TextureDictionary>())
            foreach (Texture texture in dictionary.Textures)
                if (texture.Name is { } name)
                    own.TryAdd(name, texture);
        Texture? Resolve(TextureReference reference) =>
            reference.EmbeddedTexture ?? (reference.Name is { } name ? own.GetValueOrDefault(name) ?? lookup.Find(name) : null);

        var diagnostics = new List<string>();
        var materials = new List<SurfaceMaterial>();
        // By material list: a drawable placed more than once shares its materials, as do drawables using another's.
        var firstMaterial = new Dictionary<List<DrawableMaterial>, int>(ReferenceEqualityComparer.Instance);
        var placed = new List<(DrawableGeometry Geometry, Matrix4x4 Transform, int Material)>();
        bool skinned = false;
        foreach (SceneModel scene in models)
        {
            Drawable drawable = scene.Drawable;
            // A drawable can use the shaders of another, its owner (as a fragment's pieces do).
            List<DrawableMaterial> sources = drawable.Materials.Count > 0 ? drawable.Materials : drawable.ShaderOwner?.Materials ?? [];
            if (!firstMaterial.TryGetValue(sources, out int first))
            {
                first = materials.Count;
                firstMaterial.Add(sources, first);
                materials.AddRange(sources.Select(material => SurfaceMaterial.From(material, Resolve)));
            }

            DrawableLodLevel? level = Enumerable.Range(0, 4).Select(lod => drawable.Lods[(DrawableLod)lod]).FirstOrDefault(l => l.Models.Count > 0);
            Matrix4x4[]? bones = drawable.Skeleton?.DeriveAbsoluteTransforms();
            foreach (DrawableModel model in level?.Models ?? [])
            {
                Matrix4x4 transform = scene.Transform;
                if (model.SkeletonBinding.HasSkin != 0)
                    skinned = true;
                else if (bones is not null && model.SkeletonBinding.BoneIndex < bones.Length)
                    transform = bones[model.SkeletonBinding.BoneIndex] * scene.Transform;
                foreach (DrawableGeometry geometry in model.Geometries)
                    if (geometry.MaterialIndex < sources.Count)
                        placed.Add((geometry, transform, first + geometry.MaterialIndex));
            }
        }
        foreach (string texture in materials.SelectMany(m => m.MissingTextures).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
            diagnostics.Add($"Texture not found: {texture}");
        foreach (string shader in materials.Where(m => !m.Rebuilt && !m.Terrain).Select(m => m.Shader).Distinct().Order())
            diagnostics.Add($"Not rebuilt yet, drawn with the default material: {shader}");
        foreach (string shader in materials.Where(m => !m.Rebuilt && m.Terrain).Select(m => m.Shader).Distinct().Order())
            diagnostics.Add($"Not rebuilt yet, drawn as the closest terrain shader: {shader}");
        if (skinned)
            diagnostics.Add("Skinned parts are shown in their bind pose.");
        diagnostics.AddRange(lookup.Diagnostics);

        Aabb3 box = Aabb3.Empty;
        foreach (var (geometry, transform, _) in placed)
            for (int i = 0; i < geometry.Vertices.Count; i++)
            {
                Vector3 point = Vector3.Transform(geometry.Vertices.ReadElement(i, VertexSemantic.Position).AsVector3(), transform);
                box = box.Merge(new Aabb3(point, point));
            }
        Vector3 origin = box.IsEmpty ? Vector3.Zero : box.Center;

        var vertices = new List<SurfaceVertex>();
        var indices = new List<uint>();
        var parts = new List<SurfacePart>();
        foreach (var (geometry, transform, material) in placed)
        {
            if (geometry.PrimitiveType != 3)
                continue;
            int baseVertex = vertices.Count;
            Vector3 sum = Vector3.Zero;
            VertexBuffer source = geometry.Vertices;
            for (int i = 0; i < source.Count; i++)
            {
                SurfaceVertex vertex = Read(source, i, transform, origin);
                vertices.Add(vertex);
                sum += vertex.Position;
            }
            uint first = (uint)indices.Count;
            ReadOnlySpan<ushort> from = geometry.Indices.Indices;
            int start = (int)Math.Min(geometry.IndexBufferOffset, (uint)from.Length);
            int count = Math.Min(geometry.IndexCount > 0 ? geometry.IndexCount : from.Length, from.Length - start) / 3 * 3;
            foreach (ushort index in from.Slice(start, count))
                indices.Add(index < source.Count ? index : 0u);
            if (count > 0)
                parts.Add(new SurfacePart(material, first, (uint)count, baseVertex, source.Count > 0 ? sum / source.Count : Vector3.Zero));
        }
        return new DrawableScene
        {
            Box = box,
            Origin = origin,
            Vertices = [.. vertices],
            Indices = [.. indices],
            Parts = parts,
            EdgeCount = CountEdges(indices, parts),
            Materials = [.. materials],
            Diagnostics = diagnostics,
        };
    }

    private static int CountEdges(List<uint> indices, List<SurfacePart> parts)
    {
        var edges = new HashSet<ulong>();
        foreach (SurfacePart part in parts)
            for (int i = (int)part.FirstIndex; i + 2 < part.FirstIndex + part.IndexCount; i += 3)
            {
                uint a = indices[i] + (uint)part.BaseVertex, b = indices[i + 1] + (uint)part.BaseVertex, c = indices[i + 2] + (uint)part.BaseVertex;
                edges.Add(Edge(a, b));
                edges.Add(Edge(b, c));
                edges.Add(Edge(c, a));
            }
        return edges.Count;

        static ulong Edge(uint a, uint b) => a < b ? (ulong)a << 32 | b : (ulong)b << 32 | a;
    }

    private static SurfaceVertex Read(VertexBuffer buffer, int i, Matrix4x4 transform, Vector3 origin)
    {
        VertexLayout layout = buffer.Layout;
        Vector4 Element(VertexSemantic semantic, Vector4 missing) => layout.TryFind(semantic, out _) ? buffer.ReadElement(i, semantic) : missing;
        Vector3 position = Vector3.Transform(Element(VertexSemantic.Position, default).AsVector3(), transform) - origin;
        // As the vertex shaders: a normal shorter than about a third (squared under 0.1) is taken as straight up.
        Vector3 own = Element(VertexSemantic.Normal, Vector4.UnitZ).AsVector3();
        Vector3 normal = Vector3.TransformNormal(own.LengthSquared() < .1f ? Vector3.UnitZ : own, transform);
        Vector4 tangent = Element(VertexSemantic.Tangent, Vector4.UnitX + Vector4.UnitW);
        var direction = Vector3.TransformNormal(tangent.AsVector3(), transform);
        // A missing first colour reads as white: full natural and artificial ambient, emissive where the shader is. A
        // missing second colour reads as nothing, so terrain weighted by it draws its first layer (or its lookup map).
        return new SurfaceVertex(
            position,
            normal,
            new Vector4(direction, tangent.W < 0 ? -1 : 1),
            Element(VertexSemantic.TexCoord0, default).AsVector2(),
            Element(VertexSemantic.TexCoord1, default).AsVector2(),
            SurfaceVertex.Pack(Element(VertexSemantic.Color0, Vector4.One)),
            SurfaceVertex.Pack(Element(VertexSemantic.Color1, Vector4.Zero)),
            (layout.TryFind(VertexSemantic.TexCoord3, out _) ? Element(VertexSemantic.TexCoord3, default) : Element(VertexSemantic.TexCoord2, default)).AsVector2());
    }
}
