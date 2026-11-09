using System.Numerics;
using CommonFury.Assets;
using CommonFury.Numerics;
using CommonFury.Vertices;
using FiveFury.Drawables;
using FiveFury.Textures;

namespace HikariHopper.Rendering;

/// <summary>A run of a scene's indices drawn with one material: one geometry of a drawable's model, at one LOD.</summary>
/// <param name="Center">The middle of its triangles, relative to the scene's origin: blended parts are drawn far to near.</param>
/// <param name="Model">The scene model (<see cref="DrawableScene.Models"/>) it belongs to.</param>
/// <param name="Lod">Its level of detail: 0 high, 1 medium, 2 low, 3 very low.</param>
/// <param name="Group">The drawable model it is a geometry of: the bone it is bound to, or "" when its drawable has one.</param>
/// <param name="Primary">Whether it is at its drawable's most detailed level and not only drawn damaged: what is shown at first.</param>
/// <param name="VertexCount">Its geometry's vertices.</param>
/// <param name="EdgeCount">Its triangles' distinct edges.</param>
internal readonly record struct SurfacePart(int Material, uint FirstIndex, uint IndexCount, int BaseVertex, Vector3 Center,
    int Model = 0, int Lod = 0, string Group = "", bool Primary = true, int VertexCount = 0, int EdgeCount = 0)
{
    public int TriangleCount => (int)(IndexCount / 3);
}

/// <summary>Which state of a fragment a model is drawn in: always, only intact, or only damaged.</summary>
internal enum ModelVariant
{
    Always,
    Intact,
    Damaged,
}

/// <summary>A drawable placed in a scene: a fragment's wheel at its bone, say; <paramref name="Group"/> gathers such models.</summary>
/// <param name="BoneGroups">
/// For a fragment's skinned drawable, the name of the part each bone belongs to (its physics child): its geometries are
/// split by the bone their triangles are bound to, one part each.
/// </param>
/// <param name="Variant">Whether it is drawn always, or only with the fragment intact or damaged.</param>
internal sealed record SceneModel(string Name, Drawable Drawable, Matrix4x4 Transform, string? Group = null,
    IReadOnlyDictionary<int, string>? BoneGroups = null, ModelVariant Variant = ModelVariant.Always);

/// <summary>
/// Drawables' surfaces made ready for the viewport (after RedFury's ViewerDocument): every level of detail's triangles
/// in one vertex and index buffer, relative to the middle of the most detailed levels' box, with a material for each
/// geometry; the parts say which model, level and drawable model they are, so any of them can be shown.
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

    /// <summary>The textures the drawables carry in their own dictionaries, each once.</summary>
    public int EmbeddedTextureCount { get; init; }

    /// <summary>What can be drawn over the drawables: their skeletons, bounds and lights.</summary>
    public DrawableOverlays Overlays { get; init; } = DrawableOverlays.None;

    /// <summary>The drawables placed, by name and group, as <see cref="SurfacePart.Model"/> counts them.</summary>
    public IReadOnlyList<(string Name, string? Group)> Models { get; init; } = [];

    /// <summary>Whether each model is drawn always, or only with the fragment intact or damaged.</summary>
    public IReadOnlyList<ModelVariant> Variants { get; init; } = [];

    /// <summary>Whether the scene is a fragment with a damaged state to show.</summary>
    public bool HasDamaged => Variants.Contains(ModelVariant.Damaged);

    /// <summary>What isn't shown as the game shows it: textures not found, shaders not rebuilt yet, skinned models.</summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = [];

    /// <summary>The most detailed levels' triangles, what is shown at first.</summary>
    public int TriangleCount => Parts.Where(part => part.Primary).Sum(part => part.TriangleCount);

    /// <summary>The most detailed levels' vertices.</summary>
    public int VertexCount => Parts.Where(part => part.Primary).Sum(part => part.VertexCount);

    /// <summary>The most detailed levels' distinct edges, a shared edge once, as modelling tools count them.</summary>
    public int EdgeCount => Parts.Where(part => part.Primary).Sum(part => part.EdgeCount);

    /// <summary>Builds a drawable's scene, finding textures in its own dictionary and then in <paramref name="dictionaries"/>.</summary>
    public static DrawableScene Build(Drawable drawable, IReadOnlyList<TextureDictionary> dictionaries) =>
        Build([new SceneModel(drawable.Name ?? "drawable", drawable, Matrix4x4.Identity)], new TextureLookup(dictionaries, []));

    /// <summary>
    /// Builds one scene of placed drawables, finding textures in the drawables' own dictionaries and then through
    /// <paramref name="lookup"/>. A drawable placed more than once (a wheel at each of its bones) shares its materials,
    /// as does one using another's (a fragment's wheel, its main drawable's). A fragment's bounds are its physics
    /// composite, <paramref name="fragmentBound"/>, and its lights its own, <paramref name="fragmentLights"/>.
    /// </summary>
    public static DrawableScene Build(IReadOnlyList<SceneModel> models, TextureLookup lookup, FiveFury.Bounds.Bound? fragmentBound = null,
        IReadOnlyList<DrawableLight>? fragmentLights = null)
    {
        var own = new Dictionary<string, Texture>(StringComparer.OrdinalIgnoreCase);
        foreach (TextureDictionary dictionary in models.Select(m => m.Drawable.TextureDictionary).OfType<TextureDictionary>())
            foreach (Texture texture in dictionary.Textures)
                if (texture.Name is { } name)
                    own.TryAdd(name, texture);
        Texture? Resolve(TextureReference reference) =>
            (reference.Name ?? reference.EmbeddedTexture?.Name) is { } key && lookup.Override(key) is { } edited ? edited
                : reference.EmbeddedTexture ?? (reference.Name is { } name ? own.GetValueOrDefault(name) ?? lookup.Find(name) : null);

        var diagnostics = new List<string>();
        var materials = new List<SurfaceMaterial>();
        // By material list: a drawable placed more than once shares its materials, as do drawables using another's.
        var firstMaterial = new Dictionary<List<DrawableMaterial>, int>(ReferenceEqualityComparer.Instance);
        var placed = new List<(DrawableGeometry Geometry, Matrix4x4 Transform, int Material, int Model, int Lod, string Group, bool Primary,
            IReadOnlyDictionary<int, string>? Bones)>();
        bool skinned = false;
        for (int index = 0; index < models.Count; index++)
        {
            SceneModel scene = models[index];
            Drawable drawable = scene.Drawable;
            // A drawable can use the shaders of another, its owner (as a fragment's pieces do).
            List<DrawableMaterial> sources = drawable.Materials.Count > 0 ? drawable.Materials : drawable.ShaderOwner?.Materials ?? [];
            if (!firstMaterial.TryGetValue(sources, out int first))
            {
                first = materials.Count;
                firstMaterial.Add(sources, first);
                materials.AddRange(sources.Select(material => SurfaceMaterial.From(material, Resolve)));
            }

            int best = Enumerable.Range(0, 4).Where(lod => drawable.Lods[(DrawableLod)lod].Models.Count > 0).DefaultIfEmpty(-1).First();
            Matrix4x4[]? bones = drawable.Skeleton?.DeriveAbsoluteTransforms();
            for (int lod = 0; lod < 4; lod++)
            {
                List<DrawableModel> level = drawable.Lods[(DrawableLod)lod].Models;
                for (int m = 0; m < level.Count; m++)
                {
                    DrawableModel model = level[m];
                    Matrix4x4 transform = scene.Transform;
                    if (model.SkeletonBinding.HasSkin != 0)
                        skinned |= lod == best;
                    else if (bones is not null && model.SkeletonBinding.BoneIndex < bones.Length)
                        transform = bones[model.SkeletonBinding.BoneIndex] * scene.Transform;
                    string group = level.Count == 1 ? "" : ModelName(drawable, model, m);
                    // A fragment's skinned drawable is split by the part each triangle's bone belongs to.
                    IReadOnlyDictionary<int, string>? boneParts = model.SkeletonBinding.HasSkin != 0 ? scene.BoneGroups : null;
                    foreach (DrawableGeometry geometry in model.Geometries)
                        if (geometry.MaterialIndex < sources.Count)
                            placed.Add((geometry, transform, first + geometry.MaterialIndex, index, lod, group,
                                lod == best && scene.Variant != ModelVariant.Damaged, boneParts));
                }
            }
        }
        foreach (string texture in materials.SelectMany(m => m.MissingTextures).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
            diagnostics.Add($"Texture not found: {texture}");
        foreach (string shader in materials.Where(m => !m.Rebuilt && !m.Terrain && !m.Ped).Select(m => m.Shader).Distinct().Order())
            diagnostics.Add($"Not rebuilt yet, drawn with the default material: {shader}");
        foreach (string shader in materials.Where(m => !m.Rebuilt && m.Ped).Select(m => m.Shader).Distinct().Order())
            diagnostics.Add($"Not rebuilt yet, drawn as the closest ped shader: {shader}");
        foreach (string shader in materials.Where(m => !m.Rebuilt && m.Terrain).Select(m => m.Shader).Distinct().Order())
            diagnostics.Add($"Not rebuilt yet, drawn as the closest terrain shader: {shader}");
        if (skinned)
            diagnostics.Add("Skinned parts are shown in their bind pose.");
        diagnostics.AddRange(lookup.Diagnostics);

        Aabb3 box = Aabb3.Empty;
        foreach (var (geometry, transform, _, _, _, _, _, _) in placed.Where(p => p.Primary))
            for (int i = 0; i < geometry.Vertices.Count; i++)
            {
                Vector3 point = Vector3.Transform(geometry.Vertices.ReadElement(i, VertexSemantic.Position).AsVector3(), transform);
                box = box.Merge(new Aabb3(point, point));
            }
        Vector3 origin = box.IsEmpty ? Vector3.Zero : box.Center;

        var vertices = new List<SurfaceVertex>();
        var indices = new List<uint>();
        var parts = new List<SurfacePart>();
        foreach (var (geometry, transform, material, model, lod, group, primary, boneParts) in placed)
        {
            if (geometry.PrimitiveType != 3)
                continue;
            int baseVertex = vertices.Count;
            VertexBuffer source = geometry.Vertices;
            for (int i = 0; i < source.Count; i++)
                vertices.Add(Read(source, i, transform, origin));
            ReadOnlySpan<ushort> from = geometry.Indices.Indices;
            int start = (int)Math.Min(geometry.IndexBufferOffset, (uint)from.Length);
            int count = Math.Min(geometry.IndexCount > 0 ? geometry.IndexCount : from.Length, from.Length - start) / 3 * 3;
            uint[] corners = new uint[count];
            for (int i = 0; i < count; i++)
                corners[i] = from[start + i] < source.Count ? from[start + i] : 0u;
            // One part for the geometry, or one per part its triangles' bones belong to, in the order they first come.
            foreach (var (name, triangles) in Split(geometry, corners, boneParts, group))
            {
                uint first = (uint)indices.Count;
                var used = new HashSet<uint>();
                foreach (uint index in triangles)
                {
                    indices.Add(index);
                    used.Add(index);
                }
                Vector3 sum = Vector3.Zero;
                foreach (uint index in used)
                    sum += vertices[baseVertex + (int)index].Position;
                if (triangles.Count > 0)
                    parts.Add(new SurfacePart(material, first, (uint)triangles.Count, baseVertex, used.Count > 0 ? sum / used.Count : Vector3.Zero,
                        model, lod, name, primary, used.Count, CountEdges(indices, (int)first, triangles.Count)));
            }
        }
        return new DrawableScene
        {
            Box = box,
            Origin = origin,
            Vertices = [.. vertices],
            Indices = [.. indices],
            Parts = parts,
            Models = [.. models.Select(m => (m.Name, m.Group))],
            Variants = [.. models.Select(m => m.Variant)],
            EmbeddedTextureCount = own.Count,
            Overlays = DrawableOverlays.Build(models, fragmentBound, origin, box.IsEmpty ? 1 : box.Size.Length() / 2, fragmentLights),
            Materials = [.. materials],
            Diagnostics = diagnostics,
        };
    }

    /// <summary>
    /// A geometry's triangles, whole under <paramref name="group"/>, or split by the part named for the bone each is
    /// bound to: the vertices' heaviest influence, through the geometry's bone table. Vehicles bind rigidly, every
    /// triangle to one bone.
    /// </summary>
    private static List<(string Name, List<uint> Triangles)> Split(DrawableGeometry geometry, uint[] triangles,
        IReadOnlyDictionary<int, string>? bones, string group)
    {
        VertexLayout layout = geometry.Vertices.Layout;
        if (bones is null || !layout.TryFind(VertexSemantic.BlendIndices, out VertexElement table) || !layout.TryFind(VertexSemantic.BlendWeights, out _))
            return [(group, [.. triangles])];
        // Indices are kept as bytes, read back as they are or over 255.
        float scale = table.Format == VertexElementFormat.UNorm8x4 ? 255 : 1;
        string PartOf(uint vertex)
        {
            Vector4 indices = geometry.Vertices.ReadElement((int)vertex, VertexSemantic.BlendIndices);
            Vector4 weights = geometry.Vertices.ReadElement((int)vertex, VertexSemantic.BlendWeights);
            float best = weights.X;
            float slot = indices.X;
            if (weights.Y > best) (best, slot) = (weights.Y, indices.Y);
            if (weights.Z > best) (best, slot) = (weights.Z, indices.Z);
            if (weights.W > best) slot = indices.W;
            int entry = (int)MathF.Round(slot * scale);
            int bone = entry >= 0 && entry < geometry.BoneIndices.Count ? geometry.BoneIndices[entry] : 0;
            return bones.TryGetValue(bone, out string? name) ? name : group;
        }
        var split = new List<(string Name, List<uint> Triangles)>();
        var at = new Dictionary<string, int>();
        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            string name = PartOf(triangles[i]);
            if (!at.TryGetValue(name, out int slot))
            {
                at[name] = slot = split.Count;
                split.Add((name, []));
            }
            split[slot].Triangles.AddRange([triangles[i], triangles[i + 1], triangles[i + 2]]);
        }
        return split;
    }

    /// <summary>A geometry's triangles' distinct edges; a geometry has vertices of its own, so none is shared with another.</summary>
    private static int CountEdges(List<uint> indices, int first, int count)
    {
        var edges = new HashSet<ulong>();
        for (int i = first; i + 2 < first + count; i += 3)
        {
            uint a = indices[i], b = indices[i + 1], c = indices[i + 2];
            edges.Add(Edge(a, b));
            edges.Add(Edge(b, c));
            edges.Add(Edge(c, a));
        }
        return edges.Count;

        static ulong Edge(uint a, uint b) => a < b ? (ulong)a << 32 | b : (ulong)b << 32 | a;
    }

    /// <summary>A drawable model's name: the bone it is bound to, "skinned" for one weighted to the skeleton, else its number.</summary>
    private static string ModelName(Drawable drawable, DrawableModel model, int index) =>
        model.SkeletonBinding.HasSkin != 0 ? "skinned"
        : drawable.Skeleton is { } skeleton && model.SkeletonBinding.BoneIndex < skeleton.Bones.Count
            && skeleton.Bones[model.SkeletonBinding.BoneIndex].Name is { Length: > 0 } bone ? bone
        : $"model {index + 1}";

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
