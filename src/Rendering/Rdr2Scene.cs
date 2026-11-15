using System.Numerics;
using CommonFury.Assets;
using CommonFury.Numerics;
using FiveFury.Textures;
using RedDrawable = RedFury.Drawables.Drawable;
using RedGeometry = RedFury.Drawables.DrawableGeometry;
using RedModel = RedFury.Drawables.DrawableModel;
using RedReference = RedFury.Drawables.TextureReference;
using RedSemantic = RedFury.Drawables.VertexSemantic;
using RedTexture = RedFury.Textures.Texture;
using RedYtd = RedFury.Ytd.Ytd;

namespace HikariHopper.Rendering;

/// <summary>
/// Red Dead Redemption 2's drawables (.ydr, .ydd, .yft), read through RedFury, made into the viewer's scene as GTA V's
/// are (after RedFury's ViewerDocument): every level of detail, each model placed by its transform and the bone it's
/// bound to, skinned ones in their authored pose, a part per geometry. Its materials are RedFury's, drawn with
/// rdr2.wgsl (<see cref="SurfaceMaterial.FromRdr2"/>).
/// </summary>
internal static class Rdr2Scene
{
    /// <summary>A drawable placed in the scene, and whether it's drawn always or with its fragment intact or damaged.</summary>
    /// <param name="BoneGroups">
    /// For a fragment's drawable, the physics group each bone belongs to: its parts are its groups, a rigid model's its
    /// bone's, a skinned one's split by the bone each triangle is bound to.
    /// </param>
    /// <param name="Only">The groups of it drawn here, when only some are; the rest are drawn by another placement of it.</param>
    /// <param name="Except">The groups of it not drawn here.</param>
    internal sealed record Placed(string Name, RedDrawable Drawable, Matrix4x4 Transform, ModelVariant Variant = ModelVariant.Always,
        IReadOnlyDictionary<int, string>? BoneGroups = null, IReadOnlySet<string>? Only = null, IReadOnlySet<string>? Except = null);

    /// <summary>
    /// Reads an RDR2 drawable, drawable dictionary or fragment: its textures from its drawables' own dictionaries, then
    /// <paramref name="lookup"/>'s (the dictionaries named for it, then the ones beside it).
    /// </summary>
    /// <exception cref="InvalidDataException">The file isn't one this can read.</exception>
    public static DrawableScene Read(string name, ReadOnlySpan<byte> data, TextureLookup lookup) => Build(Models(name, data), lookup);

    // The drawables of an RDR2 drawable, drawable dictionary or fragment, placed.
    private static IReadOnlyList<Placed> Models(string name, ReadOnlySpan<byte> data)
    {
        string title = Path.GetFileNameWithoutExtension(name);
        return Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".ydr" => [new Placed(title, RedFury.Ydr.Ydr.Read(data).Drawable, Matrix4x4.Identity)],
            ".ydd" => [.. RedFury.Ydd.Ydd.Read(data).Drawables.Select(member => new Placed(member.Label, member.Drawable, Matrix4x4.Identity))],
            ".yft" => Fragment(RedFury.Yft.Yft.Read(data), title),
            _ => throw new InvalidDataException($"{name} isn't a drawable."),
        };
    }

    /// <summary>
    /// The textures an RDR2 drawable, drawable dictionary or fragment carries in its drawables' own dictionaries, each
    /// name once, as a standalone RDR2 texture dictionary: each texture as the game stores it. What its viewer's textures
    /// button opens, to look at and save elsewhere, as they can't be written back into it yet.
    /// </summary>
    /// <exception cref="InvalidDataException">The file isn't one this can read.</exception>
    /// <exception cref="NotSupportedException">A texture RedFury can't write back as it is (a CPU-side texture, say).</exception>
    public static byte[] EmbeddedDictionary(string name, ReadOnlySpan<byte> data) =>
        RedYtd.Create(Embedded(name, data).Select(RedFury.Textures.TextureImage.FromTexture)).Write();

    /// <summary>The same textures, as the viewer's texture model has them (what Extract writes as DDS); volume textures left out.</summary>
    public static IReadOnlyList<Texture> EmbeddedTextures(string name, ReadOnlySpan<byte> data) =>
        [.. Embedded(name, data).Select(Rdr2Textures.Of).OfType<Texture>()];

    // The drawables' own textures, each name once.
    private static List<RedTexture> Embedded(string name, ReadOnlySpan<byte> data)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var textures = new List<RedTexture>();
        foreach (RedYtd dictionary in Models(name, data).Select(model => model.Drawable.EmbeddedTextures).OfType<RedYtd>().Distinct())
            foreach (RedTexture texture in dictionary.Textures)
                if (seen.Add(texture.Name))
                    textures.Add(texture);
        return textures;
    }

    /// <summary>
    /// What an RDR2 fragment draws, placed. Unlike GTA V's, its physics children's drawables hold no geometry: its pieces
    /// are its main drawable's models, each bound to the bone of its physics group (a skinned one weighted to them), so
    /// its parts are its groups. Its damaged drawable (one of its extra drawables) holds the damaged pieces, each bound to
    /// a bone under its group's: damaged, those groups are drawn from it and the rest stay as they are; a damaged piece
    /// in no group (bound to the root) stands for the whole fragment damaged. Its other extra drawables are drawn always.
    /// </summary>
    internal static List<Placed> Fragment(RedFury.Yft.Yft fragment, string title)
    {
        var models = new List<Placed>();
        if (fragment.MainDrawable is not { } main)
            return models;
        RedFury.Yft.FragmentDrawable? damaged = fragment.DamagedDrawableIndex >= 0 && fragment.DamagedDrawableIndex < fragment.ExtraDrawables.Count
            ? fragment.ExtraDrawables[fragment.DamagedDrawableIndex] : null;
        IReadOnlyDictionary<int, string>? groups = PhysicsGroups(fragment, main.Drawable.Skeleton);
        // The groups the damaged drawable replaces; null when it replaces the whole fragment.
        HashSet<string>? replaced = null;
        if (damaged is not null && groups is not null)
        {
            replaced = [];
            foreach (RedModel model in damaged.Drawable.Models())
                if (model.Binding.RigidBoneIndex is int bone && groups.TryGetValue(bone, out string? group))
                    replaced.Add(group);
                else
                {
                    replaced = null;
                    break;
                }
        }
        // Replacing every group the main drawable draws is the whole fragment damaged.
        if (replaced is not null && groups is not null)
        {
            var drawn = new HashSet<string>();
            foreach (RedModel model in main.Drawable.Models())
                if (model.Binding.RigidBoneIndex is int bone && groups.TryGetValue(bone, out string? group))
                    drawn.Add(group);
                else if (model.Binding.IsSkinned)
                    drawn.UnionWith(groups.Values);
            if (drawn.IsSubsetOf(replaced))
                replaced = null;
        }
        if (damaged is null)
            models.Add(new Placed(title, main.Drawable, main.Transform.Matrix, BoneGroups: groups));
        else if (replaced is null)
            models.Add(new Placed(title, main.Drawable, main.Transform.Matrix, ModelVariant.Intact, groups));
        else
        {
            models.Add(new Placed(title, main.Drawable, main.Transform.Matrix, BoneGroups: groups, Except: replaced));
            models.Add(new Placed(title, main.Drawable, main.Transform.Matrix, ModelVariant.Intact, groups, Only: replaced));
        }
        for (int i = 0; i < fragment.ExtraDrawables.Count; i++)
            if (fragment.ExtraDrawables[i] is { } extra)
                models.Add(new Placed(extra.Label, extra.Drawable, extra.Transform.Matrix,
                    ReferenceEquals(extra, damaged) ? ModelVariant.Damaged : ModelVariant.Always, ReferenceEquals(extra, damaged) ? groups : null));
        return models;
    }

    /// <summary>
    /// The physics group each bone of a fragment's skeleton belongs to: the group at it or nearest above it; none for a
    /// bone above every group (the root, often). Null when the fragment has no physics, or it can't be read.
    /// </summary>
    internal static IReadOnlyDictionary<int, string>? PhysicsGroups(RedFury.Yft.Yft fragment, RedFury.Drawables.Skeleton? skeleton)
    {
        if (skeleton is null)
            return null;
        RedFury.Yft.FragmentPhysicsLod? physics;
        try
        {
            physics = RedFury.Yft.FragmentPhysics.Read(fragment).FirstOrDefault(lod => lod is not null);
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or ArgumentException)
        {
            return null;
        }
        if (physics is null || physics.Groups.Count == 0)
            return null;
        var byTag = new Dictionary<ushort, string>();
        foreach (RedFury.Yft.FragmentPhysicsGroupInput group in physics.Groups)
            byTag.TryAdd(group.BoneId, group.Name.Length > 0 ? group.Name : $"group {byTag.Count + 1}");
        var groups = new Dictionary<int, string>();
        for (int bone = 0; bone < skeleton.Bones.Count; bone++)
            for (int at = bone; at >= 0 && at < skeleton.Bones.Count; at = at < skeleton.ParentIndices.Count ? skeleton.ParentIndices[at] : -1)
                if (byTag.TryGetValue(skeleton.Bones[at].Tag, out string? name))
                {
                    groups[bone] = name;
                    break;
                }
        return groups;
    }

    /// <summary>Builds one scene of placed RDR2 drawables, as <see cref="DrawableScene.Build"/> does GTA V's.</summary>
    internal static DrawableScene Build(IReadOnlyList<Placed> models, TextureLookup lookup)
    {
        // Every drawable's own textures, for all of them: a fragment's extra drawables use its main drawable's materials,
        // and its textures, without a dictionary of their own.
        var own = new List<RedYtd>();
        foreach (RedYtd dictionary in models.Select(model => model.Drawable.EmbeddedTextures).OfType<RedYtd>())
            if (!own.Contains(dictionary))
                own.Add(dictionary);
        Texture? Resolve(RedReference reference)
        {
            if (lookup.Override(reference.Name) is { } edited)
                return edited;
            foreach (RedYtd dictionary in own)
                if (((reference.Name.Length > 0 ? dictionary.Find(reference.Name) : null) ?? dictionary.Find(reference.NameHash)) is { } found)
                    return Rdr2Textures.Of(found);
            return reference.Name.Length > 0 ? lookup.Find(reference.Name) : null;
        }

        var diagnostics = new List<string>();
        var materials = new List<SurfaceMaterial>();
        var firstMaterial = new Dictionary<IReadOnlyList<RedFury.Drawables.Material>, int>(ReferenceEqualityComparer.Instance);
        var placed = new List<(RedGeometry Geometry, Matrix4x4 Transform, int Material, int Model, int Lod, string Group, bool Primary, RedModel Source)>();
        bool skinned = false;
        for (int index = 0; index < models.Count; index++)
        {
            Placed scene = models[index];
            RedDrawable drawable = scene.Drawable;
            if (!firstMaterial.TryGetValue(drawable.Materials, out int first))
            {
                first = materials.Count;
                firstMaterial.Add(drawable.Materials, first);
                materials.AddRange(drawable.Materials.Select(material => SurfaceMaterial.FromRdr2(material, Resolve)));
            }
            int best = drawable.PrimaryLod is { } primary ? (int)primary : -1;
            for (int lod = 0; lod < 4; lod++)
            {
                if (!drawable.Lods.TryGetValue((DrawableLod)lod, out IReadOnlyList<RedModel>? level))
                    continue;
                var transforms = new Dictionary<RedModel, Matrix4x4>(ReferenceEqualityComparer.Instance);
                foreach (var (_, geometry, model) in TreeModels(level))
                {
                    if (!transforms.TryGetValue(model, out Matrix4x4 transform))
                    {
                        transform = scene.Transform;
                        if (model.Binding.IsSkinned)
                            skinned |= lod == best;
                        else if (model.Binding.Validate(drawable.Skeleton).IsValid)
                            transform = model.Binding.ResolveRigidTransform(drawable.Skeleton) * scene.Transform;
                        transforms.Add(model, transform);
                    }
                    if (geometry.MaterialIndex < 0 || geometry.MaterialIndex >= drawable.Materials.Count)
                        continue;
                    int number = 0;
                    for (int m = 0; m < level.Count && !ReferenceEquals(level[m], model); m++)
                        number++;
                    placed.Add((geometry, transform, first + geometry.MaterialIndex, index, lod,
                        level.Count == 1 ? "" : ModelName(drawable, model, number), lod == best && scene.Variant != ModelVariant.Damaged, model));
                }
            }
        }
        foreach (string texture in materials.SelectMany(m => m.MissingTextures).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
            diagnostics.Add($"Texture not found: {texture}");
        foreach (string shader in materials.Where(m => m.Rdr2?.Water == true).Select(m => m.Shader).Distinct().Order())
            diagnostics.Add($"Water drawn with the generic material, until the viewer copies the scene behind it: {shader}");
        if (skinned)
            diagnostics.Add("Skinned parts are shown in their authored pose.");
        diagnostics.AddRange(lookup.Diagnostics);

        // Each geometry's vertices, decoded once: the box needs the primary levels' before anything is placed.
        var decoded = new List<(Vector3[] Positions, uint[] Indices)>();
        Aabb3 box = Aabb3.Empty;
        foreach (var item in placed)
        {
            (Vector3[] Positions, uint[] Indices) corners;
            try
            {
                corners = (item.Geometry.Vertices.DecodePositions(), item.Geometry.Indices.Decode());
            }
            catch (Exception error) when (error is NotSupportedException or InvalidDataException or ArgumentException)
            {
                diagnostics.Add($"A geometry of {models[item.Model].Name} couldn't be read: {error.Message}");
                corners = ([], []);
            }
            decoded.Add(corners);
            if (item.Primary)
                foreach (Vector3 position in corners.Positions)
                {
                    Vector3 point = Vector3.Transform(position, item.Transform);
                    if (float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z))
                        box = box.Merge(new Aabb3(point, point));
                }
        }
        Vector3 origin = box.IsEmpty ? Vector3.Zero : box.Center;

        var vertices = new List<SurfaceVertex>();
        var indices = new List<uint>();
        var parts = new List<SurfacePart>();
        for (int i = 0; i < placed.Count; i++)
        {
            var (geometry, transform, material, model, lod, group, primary, source) = placed[i];
            var (positions, corners) = decoded[i];
            int count = corners.Length / 3 * 3;
            if (positions.Length == 0 || count == 0)
                continue;
            Placed scene = models[model];
            uint[] triangles = new uint[count];
            for (int c = 0; c < count; c++)
                triangles[c] = corners[c] < positions.Length ? corners[c] : 0u;
            // One part for the geometry, or one per physics group its triangles belong to, those drawn here.
            var split = Split(geometry, source, triangles, scene.BoneGroups, group)
                .Where(piece => piece.Triangles.Count > 0 && (scene.Only is null || scene.Only.Contains(piece.Name))
                    && (scene.Except is null || !scene.Except.Contains(piece.Name)))
                .ToList();
            if (split.Count == 0)
                continue;
            int baseVertex = vertices.Count;
            Read(geometry, positions, transform, origin, materials[material].Rdr2, vertices);
            foreach (var (name, piece) in split)
            {
                uint first = (uint)indices.Count;
                var used = new HashSet<uint>();
                foreach (uint corner in piece)
                {
                    indices.Add(corner);
                    used.Add(corner);
                }
                Vector3 sum = Vector3.Zero;
                foreach (uint corner in used)
                    sum += vertices[baseVertex + (int)corner].Position;
                parts.Add(new SurfacePart(material, first, (uint)piece.Count, baseVertex, sum / used.Count, model, lod, name, primary,
                    used.Count, DrawableScene.CountEdges(indices, (int)first, piece.Count)));
            }
        }
        return new DrawableScene
        {
            Box = box,
            Origin = origin,
            Vertices = [.. vertices],
            Indices = [.. indices],
            Parts = parts,
            Materials = materials,
            Models = [.. models.Select(m => (m.Name, (string?)null))],
            Variants = [.. models.Select(m => m.Variant)],
            Overlays = Overlays(models, origin, box.IsEmpty ? 1 : box.Size.Length() / 2),
            EmbeddedTextureCount = own.SelectMany(dictionary => dictionary.Textures).Select(texture => texture.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            Diagnostics = diagnostics,
        };
    }

    /// <summary>
    /// A geometry's triangles, whole under <paramref name="group"/>, or, in a fragment, by physics group: a rigid model's
    /// all its bone's, a skinned one's each the group of the bone its corners weigh most (RDR2's blend indices are the
    /// skeleton's bones). Triangles of a bone in no group stay under <paramref name="group"/>.
    /// </summary>
    private static List<(string Name, List<uint> Triangles)> Split(RedGeometry geometry, RedModel model, uint[] triangles,
        IReadOnlyDictionary<int, string>? bones, string group)
    {
        if (bones is null)
            return [(group, [.. triangles])];
        if (model.Binding.RigidBoneIndex is int rigid)
            return [(bones.TryGetValue(rigid, out string? name) ? name : group, [.. triangles])];
        Vector4[]? indices = Decode(geometry, RedSemantic.BlendIndices0), weights = Decode(geometry, RedSemantic.BlendWeights0);
        if (indices is null || weights is null)
            return [(group, [.. triangles])];
        string PartOf(uint vertex)
        {
            Vector4 index = indices[vertex], weight = weights[vertex];
            float best = weight.X, slot = index.X;
            if (weight.Y > best) (best, slot) = (weight.Y, index.Y);
            if (weight.Z > best) (best, slot) = (weight.Z, index.Z);
            if (weight.W > best) slot = index.W;
            return bones.TryGetValue((int)MathF.Round(slot), out string? name) ? name : group;
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
        // In the order of their names' numbers (leaves1, leaves2 … leaves16), as the parts panel lists them.
        System.Globalization.CompareInfo compare = System.Globalization.CultureInfo.InvariantCulture.CompareInfo;
        return [.. split.OrderBy(piece => piece.Name, Comparer<string>.Create((a, b) =>
            compare.Compare(a, b, System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.NumericOrdering)))];
    }

    /// <summary>
    /// A level's geometries to draw: all of them, but a mixed tree's duplicates (after RedFury's TreeModelSelection): a
    /// level holding the full tree (render mask 0xE1) and its lod cards (trees_lod) leaves out its reduced copies (0xFA,
    /// 0xE4); one holding the detailed foliage, its cards.
    /// </summary>
    internal static IEnumerable<(int Index, RedGeometry Geometry, RedModel Model)> TreeModels(IReadOnlyList<RedModel> models)
    {
        static bool HasTriangles(RedGeometry g) => g.Vertices.VertexCount > 0 && g.Indices.IndexCount >= 3;
        static bool IsDetail(RedGeometry g) => HasTriangles(g) && g.Material?.ShaderName is "trees_standard" or "trees_standard_frag" or "trees_normal" or "trees_normal_spec";
        static bool IsProxy(RedGeometry g) => g.Material?.ShaderName == "trees_lod";
        static bool IsTree(RedGeometry g) => IsDetail(g) || IsProxy(g)
            || g.Material?.ShaderName is "trunk_nodpm" or "trunk_nodpm_frag" or "trunk_nodpm_decal_blend" or "trunk_nodpm_decal_tnt";
        bool detail = models.Any(m => m.Geometries.Any(IsDetail));
        bool proxy = models.Any(m => m.Geometries.Any(IsProxy));
        bool full = proxy && models.Any(m => m.RenderMask == 0xE1 && m.Geometries.Any(IsDetail));
        int index = 0;
        foreach (RedModel model in models)
        {
            bool alternate = full && model.RenderMask is 0xFA or 0xE4 && model.Geometries.All(IsTree);
            foreach (RedGeometry geometry in model.Geometries)
            {
                int at = index++;
                if (alternate || detail && IsProxy(geometry))
                    continue;
                yield return (at, geometry, model);
            }
        }
    }

    // A drawable model's name: the bone it's bound to, "skinned" for one weighted to the skeleton, else its number.
    private static string ModelName(RedDrawable drawable, RedModel model, int index) =>
        model.Binding.IsSkinned ? "skinned"
        : model.Binding.RigidBoneIndex is int bone && drawable.Skeleton is { } skeleton && bone < skeleton.Bones.Count
            && skeleton.Bones[bone].Name is { Length: > 0 } name ? name
        : $"model {index + 1}";

    /// <summary>
    /// A geometry's vertices as rdr2.wgsl reads them, placed and relative to the scene's origin, as RedFury's
    /// MeshPreparation makes them: a missing normal is the faces' around it, a missing tangent runs along X, missing
    /// second and third UV sets are the first. The first colour is the one its material's program reads (COLOR1 for the
    /// terrain programs that take it, white when missing), the second's red the tint palette's coordinate (its blue, or
    /// nothing without one); river water's COLOR1 rides in the second and third UV sets, as RedFury's viewer has it.
    /// </summary>
    private static void Read(RedGeometry geometry, Vector3[] positions, Matrix4x4 transform, Vector3 origin, Rdr2Material? material,
        List<SurfaceVertex> into)
    {
        Vector4[]? normals = Decode(geometry, RedSemantic.Normal), tangents = Decode(geometry, RedSemantic.Tangent0);
        Vector4[]? uv0 = Decode(geometry, RedSemantic.TexCoord0), uv1 = Decode(geometry, RedSemantic.TexCoord1);
        Vector4[]? uv2 = Decode(geometry, RedSemantic.TexCoord2);
        Vector4[]? colours = Decode(geometry, RedSemantic.Colour0);
        // These terrain programs read COLOR1, even where COLOR0 is there; without it, nothing painted: the lookup mask.
        if (material?.TerrainColour1 == true)
            colours = Decode(geometry, RedSemantic.Colour1) ?? new Vector4[positions.Length];
        Vector4[]? flow = material?.WaterVertexFlow == true ? Decode(geometry, RedSemantic.Colour1) : null;
        Vector3[]? faces = normals is null ? FaceNormals(geometry, positions) : null;
        Matrix4x4 normalTransform = Matrix4x4.Invert(transform, out Matrix4x4 inverse) ? Matrix4x4.Transpose(inverse) : transform;
        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 normal = normals is not null ? normals[i].AsVector3() : faces![i];
            normal = Vector3.TransformNormal(normal, normalTransform);
            normal = normal.LengthSquared() > 1e-12f && float.IsFinite(normal.X) ? Vector3.Normalize(normal) : Vector3.UnitZ;
            Vector4 along = tangents?[i] ?? new Vector4(1, 0, 0, 1);
            Vector3 tangent = Vector3.TransformNormal(along.AsVector3(), transform);
            Vector4 t0 = uv0?[i] ?? default, t1 = uv1?[i] ?? t0, t2 = uv2?[i] ?? t0;
            if (material?.WaterVertexFlow == true)
            {
                Vector4 c = flow?[i] ?? new Vector4(.5f, .5f, 1, 1);
                (t1, t2) = (new Vector4(c.X, c.Y, 0, 0), new Vector4(c.Z, c.W, 0, 0));
            }
            Vector4 colour = colours?[i] ?? Vector4.One;
            into.Add(new SurfaceVertex(Vector3.Transform(positions[i], transform) - origin, normal,
                new Vector4(tangent, along.W < 0 ? -1 : 1), new Vector2(t0.X, t0.Y), new Vector2(t1.X, t1.Y),
                SurfaceVertex.Pack(colour), SurfaceVertex.Pack(new Vector4(colours?[i].Z ?? 0, 0, 0, 0)), new Vector2(t2.X, t2.Y)));
        }
    }

    // A channel's values, one per vertex; none when it's missing or in a format RedFury doesn't decode.
    private static Vector4[]? Decode(RedGeometry geometry, RedSemantic semantic)
    {
        try
        {
            return geometry.Vertices.Decode(semantic) is { } values && values.Length >= geometry.Vertices.VertexCount ? values : null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static Vector3[] FaceNormals(RedGeometry geometry, Vector3[] positions)
    {
        var sums = new Vector3[positions.Length];
        uint[] corners = geometry.Indices.Decode();
        for (int i = 0; i + 2 < corners.Length; i += 3)
        {
            uint a = corners[i], b = corners[i + 1], c = corners[i + 2];
            if (a >= positions.Length || b >= positions.Length || c >= positions.Length)
                continue;
            Vector3 n = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
            sums[a] += n;
            sums[b] += n;
            sums[c] += n;
        }
        return sums;
    }

    // Each drawable's skeleton, once, each bone to its parent.
    private static DrawableOverlays Overlays(IReadOnlyList<Placed> models, Vector3 origin, float radius)
    {
        var drawn = new HashSet<RedFury.Drawables.Skeleton>(ReferenceEqualityComparer.Instance);
        var bones = new List<(Vector3 Joint, Vector3? Parent)>();
        foreach (Placed model in models)
        {
            if (model.Drawable.Skeleton is not { HasBindPose: true } skeleton || !drawn.Add(skeleton))
                continue;
            Vector3[] points = skeleton.BonePositions();
            for (int i = 0; i < points.Length; i++)
            {
                int parent = i < skeleton.ParentIndices.Count ? skeleton.ParentIndices[i] : -1;
                bones.Add((Vector3.Transform(points[i], model.Transform),
                    parent >= 0 && parent < points.Length ? Vector3.Transform(points[parent], model.Transform) : null));
            }
        }
        return DrawableOverlays.Bones(bones, origin, radius);
    }
}
