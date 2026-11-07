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

/// <summary>
/// A drawable's surfaces made ready for the viewport: its most detailed level's triangles in one vertex and index
/// buffer, relative to the middle of its box, with a material for each geometry. Parts bound rigidly to a bone are
/// placed where the skeleton holds them; skinned ones are drawn in their bind pose.
/// </summary>
internal sealed class DrawableScene
{
    public required Aabb3 Box { get; init; }
    public required Vector3 Origin { get; init; }
    public required SurfaceVertex[] Vertices { get; init; }
    public required uint[] Indices { get; init; }
    public required IReadOnlyList<SurfacePart> Parts { get; init; }
    public required IReadOnlyList<SurfaceMaterial> Materials { get; init; }

    public int TriangleCount => Indices.Length / 3;

    /// <summary>Builds a drawable's scene, finding textures in its own dictionary and then in <paramref name="dictionaries"/>.</summary>
    public static DrawableScene Build(Drawable drawable, IReadOnlyList<TextureDictionary> dictionaries)
    {
        var textures = new Dictionary<string, Texture>(StringComparer.OrdinalIgnoreCase);
        foreach (TextureDictionary dictionary in new[] { drawable.TextureDictionary }.OfType<TextureDictionary>().Concat(dictionaries))
            foreach (Texture texture in dictionary.Textures)
                if (texture.Name is { } name)
                    textures.TryAdd(name, texture);
        Texture? Resolve(TextureReference reference) =>
            reference.EmbeddedTexture ?? (reference.Name is { } name && textures.TryGetValue(name, out Texture? found) ? found : null);
        // A drawable can use the shaders of another, its owner (as a fragment's pieces do).
        List<DrawableMaterial> sources = drawable.Materials.Count > 0 ? drawable.Materials : drawable.ShaderOwner?.Materials ?? [];
        SurfaceMaterial[] materials = [.. sources.Select(material => SurfaceMaterial.From(material, Resolve))];

        DrawableLodLevel? level = Enumerable.Range(0, 4).Select(lod => drawable.Lods[(DrawableLod)lod]).FirstOrDefault(l => l.Models.Count > 0);
        Matrix4x4[]? bones = drawable.Skeleton?.DeriveAbsoluteTransforms();
        var placed = new List<(DrawableGeometry Geometry, Matrix4x4 Transform)>();
        foreach (DrawableModel model in level?.Models ?? [])
        {
            Matrix4x4 transform = Matrix4x4.Identity;
            if (bones is not null && model.SkeletonBinding.HasSkin == 0 && model.SkeletonBinding.BoneIndex < bones.Length)
                transform = bones[model.SkeletonBinding.BoneIndex];
            foreach (DrawableGeometry geometry in model.Geometries)
                placed.Add((geometry, transform));
        }

        Aabb3 box = Aabb3.Empty;
        foreach (var (geometry, transform) in placed)
            for (int i = 0; i < geometry.Vertices.Count; i++)
            {
                Vector3 point = Vector3.Transform(geometry.Vertices.ReadElement(i, VertexSemantic.Position).AsVector3(), transform);
                box = box.Merge(new Aabb3(point, point));
            }
        Vector3 origin = box.IsEmpty ? Vector3.Zero : box.Center;

        var vertices = new List<SurfaceVertex>();
        var indices = new List<uint>();
        var parts = new List<SurfacePart>();
        foreach (var (geometry, transform) in placed)
        {
            if (geometry.PrimitiveType != 3 || geometry.MaterialIndex >= materials.Length)
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
                parts.Add(new SurfacePart(geometry.MaterialIndex, first, (uint)count, baseVertex, source.Count > 0 ? sum / source.Count : Vector3.Zero));
        }
        return new DrawableScene
        {
            Box = box,
            Origin = origin,
            Vertices = [.. vertices],
            Indices = [.. indices],
            Parts = parts,
            Materials = materials,
        };
    }

    private static SurfaceVertex Read(VertexBuffer buffer, int i, Matrix4x4 transform, Vector3 origin)
    {
        VertexLayout layout = buffer.Layout;
        Vector4 Element(VertexSemantic semantic, Vector4 missing) => layout.TryFind(semantic, out _) ? buffer.ReadElement(i, semantic) : missing;
        Vector3 position = Vector3.Transform(Element(VertexSemantic.Position, default).AsVector3(), transform) - origin;
        Vector3 normal = Vector3.TransformNormal(Element(VertexSemantic.Normal, Vector4.UnitZ).AsVector3(), transform);
        Vector4 tangent = Element(VertexSemantic.Tangent, Vector4.UnitX + Vector4.UnitW);
        var direction = Vector3.TransformNormal(tangent.AsVector3(), transform);
        // A missing colour reads as white: full natural and artificial ambient, emissive where the shader is.
        return new SurfaceVertex(
            position,
            normal,
            new Vector4(direction, tangent.W < 0 ? -1 : 1),
            Element(VertexSemantic.TexCoord0, default).AsVector2(),
            Element(VertexSemantic.TexCoord1, default).AsVector2(),
            SurfaceVertex.Pack(Element(VertexSemantic.Color0, Vector4.One)),
            SurfaceVertex.Pack(Element(VertexSemantic.Color1, Vector4.One)));
    }
}
