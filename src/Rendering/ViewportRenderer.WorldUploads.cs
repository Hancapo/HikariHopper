using System.Diagnostics;
using System.Runtime.InteropServices;
using HikariHopper.Rendering.World;
using RedFury.World;
using WebGpuSharp;
using Buffer = WebGpuSharp.Buffer;
using GameTexture = FiveFury.Textures.Texture;

namespace HikariHopper.Rendering;

// An admitted asset goes to the GPU over several frames (after RedFury's WebGpuRenderer.Uploads): its textures a mip
// level at a time, its vertices and indexes in chunks, within a byte budget and a soft time limit a frame, so a big
// asset arriving doesn't stall one. It's drawn once all of it is there; one is uploaded at a time.
internal sealed unsafe partial class ViewportRenderer
{
    // A frame's upload, as RedFury's UploadBudget has it: its bytes, its writes, its soft time limit (checked after each
    // write), and the largest write at once.
    private const long UploadBytes = 8L * 1024 * 1024;
    private const int UploadWrites = 64;
    private const long ChunkBytes = 256L * 1024;
    private static readonly long UploadTicks = Stopwatch.Frequency / 500;

    private sealed class PendingWorldUpload(WorldAsset source)
    {
        internal readonly WorldAsset Source = source;

        // Its surfaces as they'll be drawn, laid out on the worker that read it.
        internal WorldLayout Layout => Source.Layout;

        // The textures still to go up, the one going up (with its mip level), and those it put up (by what they are).
        internal readonly Queue<GameTexture> Textures = new();
        internal GameTexture? Texture;
        internal WebGpuSharp.Texture? GpuTexture;
        internal int Block, Level, Row;
        internal readonly List<object> Registered = [];

        // Every texture its materials bind, as read.
        internal readonly HashSet<GameTexture> Read = new(ReferenceEqualityComparer.Instance);

        internal WorldSlice? VertexBuffer, IndexBuffer;
        internal long VertexOffset, IndexOffset;
    }

    private PendingWorldUpload? pendingWorld;

    /// <summary>Whether an asset is on its way to the GPU.</summary>
    public bool HasPendingWorldUpload => pendingWorld is not null;

    /// <summary>The asset on its way to the GPU.</summary>
    public WorldAssetKey? WorldUploadKey => pendingWorld?.Source.Key;

    // Puts an asset on its way up: its layout (made on the streamer's worker) and the textures not up yet.
    private void BeginWorldUpload(WorldAsset asset)
    {
        var pending = new PendingWorldUpload(asset);
        var queued = new HashSet<object>();
        foreach (var (_, material, rdr2) in asset.Layout.Kept)
            foreach (GameTexture? texture in rdr2 is not null ? rdr2.Textures : material.BoundTextures())
                if (texture is not null && pending.Read.Add(texture) && asset.TextureId(texture) is var id && !worldTextures.ContainsKey(id) && queued.Add(id))
                    pending.Textures.Enqueue(texture);
        pendingWorld = pending;
    }

    /// <summary>
    /// Puts the next part of the asset on its way up within a frame's budget (its bytes and a soft two milliseconds; at
    /// least one step, however big), and draws it once all of it is up.
    /// </summary>
    /// <exception cref="NotSupportedException">The asset can't be drawn; it's let go.</exception>
    public void ProcessWorldUploads()
    {
        if (pendingWorld is not { } pending)
            return;
        long deadline = Stopwatch.GetTimestamp() + UploadTicks;
        long bytes = 0;
        int writes = 0;
        bool Spent() => bytes > 0 && (bytes >= UploadBytes || writes >= UploadWrites || Stopwatch.GetTimestamp() >= deadline);
        try
        {
            while (!Spent())
            {
                if (pending.Texture is null && !pending.Textures.TryDequeue(out pending.Texture))
                    break;
                GameTexture texture = pending.Texture!;
                if (pending.GpuTexture is null)
                {
                    // Another asset's upload may have put it up meanwhile; one that can't be drawn reads as missing.
                    object id = pending.Source.TextureId(texture);
                    var made = worldTextures.ContainsKey(id) ? null : CreateTexture(texture);
                    if (made is not var (created, block))
                    {
                        if (worldTextures.TryAdd(id, new WorldTexture(null)))
                            pending.Registered.Add(id);
                        pending.Texture = null;
                        continue;
                    }
                    (pending.GpuTexture, pending.Block, pending.Level, pending.Row) = (created, block, 0, 0);
                }
                // A big level a few rows at a time, a write at most ChunkBytes (a row at least).
                int rows = (int)Math.Max(1, ChunkBytes / Math.Max(1, RowBytes(texture, pending.Level)));
                var (written, done) = WriteRows(texture, pending.GpuTexture, pending.Block, pending.Level, pending.Row, rows);
                bytes += Math.Max(1, written);
                writes++;
                pending.Row += rows;
                if (done)
                    (pending.Level, pending.Row) = (pending.Level + 1, 0);
                if (pending.Level == texture.MipCount)
                {
                    object id = pending.Source.TextureId(texture);
                    worldTextures[id] = new WorldTexture(pending.GpuTexture);
                    pending.Registered.Add(id);
                    (pending.Texture, pending.GpuTexture) = (null, null);
                }
            }
            if (pending.Texture is not null || pending.Textures.Count > 0)
                return;
            if (pending.Layout.Vertices.Length > 0)
            {
                pending.VertexBuffer ??= VertexPages.Allocate(((ulong)pending.Layout.Vertices.Length * SurfaceVertex.Size + 3) & ~3UL);
                pending.IndexBuffer ??= IndexPages.Allocate((ulong)pending.Layout.Indices.Length * sizeof(uint));
                WorldSlice vertexSlice = pending.VertexBuffer.Value, indexSlice = pending.IndexBuffer.Value;
                ReadOnlySpan<byte> vertices = MemoryMarshal.AsBytes(pending.Layout.Vertices.AsSpan());
                ReadOnlySpan<byte> indices = MemoryMarshal.AsBytes(pending.Layout.Indices.AsSpan());
                // Whole vertices and indexes a chunk, as writes go in fours.
                long vertexChunk = ChunkBytes / SurfaceVertex.Size * SurfaceVertex.Size;
                while (pending.VertexOffset < vertices.Length && !Spent())
                {
                    int count = (int)Math.Min(vertexChunk, vertices.Length - pending.VertexOffset);
                    queue.WriteBuffer(vertexSlice.Buffer, vertexSlice.Offset + (ulong)pending.VertexOffset, vertices.Slice((int)pending.VertexOffset, count));
                    pending.VertexOffset += count;
                    bytes += count;
                    writes++;
                }
                while (pending.VertexOffset == vertices.Length && pending.IndexOffset < indices.Length && !Spent())
                {
                    int count = (int)Math.Min(ChunkBytes, indices.Length - pending.IndexOffset);
                    queue.WriteBuffer(indexSlice.Buffer, indexSlice.Offset + (ulong)pending.IndexOffset, indices.Slice((int)pending.IndexOffset, count));
                    pending.IndexOffset += count;
                    bytes += count;
                    writes++;
                }
                if (pending.VertexOffset < vertices.Length || pending.IndexOffset < indices.Length)
                    return;
            }
            FinishWorldUpload(pending);
        }
        catch
        {
            CancelWorldUpload();
            throw;
        }
    }

    // All of it up: its materials and surfaces, and it's drawn from now on.
    private void FinishWorldUpload(PendingWorldUpload pending)
    {
        var uploaded = new WorldGpuAsset(pending.Source) { Vertices = pending.VertexBuffer, Indices = pending.IndexBuffer };
        LendWorldTextures(pending.Source, pending.Read);
        try
        {
            if (uploaded.Vertices is not null)
                AddWorldMeshes(pending, uploaded);
        }
        finally
        {
            ReturnWorldTextures(pending.Source, pending.Read);
        }
        foreach (object id in uploaded.Textures)
            if (worldTextures.TryGetValue(id, out WorldTexture? texture))
                texture.Users++;
        uploaded.Used = worldClock;
        uploaded.Id = ++worldAssetIds;
        worldAssets.Add(pending.Source.Key, uploaded);
        pendingWorld = null;
        worldNodeAssetsDirty = worldSourcesDirty = worldVisibilityDirty = true;
    }

    // Its surfaces' material groups (each material's values in the uniform pages) and meshes, and its depth runs.
    private void AddWorldMeshes(PendingWorldUpload pending, WorldGpuAsset uploaded)
    {
        var groups = new Dictionary<int, (BindGroup Group, WorldSlice Uniforms)>();
        foreach (int i in pending.Layout.Order)
        {
            var (part, material, rdr2) = pending.Layout.Kept[i];
            WorldFamily family = rdr2 is not null ? rdr2.Water ? WorldFamily.Rdr2Water : WorldFamily.Rdr2
                : material.Water is not null ? WorldFamily.GtaWater : material.Terrain ? WorldFamily.GtaTerrain : WorldFamily.Gta;
            if (!groups.TryGetValue(part.Material, out var group))
            {
                CreateWorldFamily(family);
                WorldSlice uniforms;
                if (rdr2 is not null)
                {
                    uniforms = UniformPages.Allocate(Rdr2Material.UniformBytes);
                    queue.WriteBuffer(uniforms.Buffer, uniforms.Offset, (ReadOnlySpan<byte>)rdr2.Uniforms);
                    group = (Rdr2Group(rdr2, uniforms.Buffer, uniforms.Offset), uniforms);
                }
                else if (material.Water is { } water)
                {
                    uniforms = UniformPages.Allocate(GtaWater.UniformBytes);
                    queue.WriteBuffer(uniforms.Buffer, uniforms.Offset, (ReadOnlySpan<float>)water.Uniforms());
                    group = (GtaWaterGroup(water, uniforms.Buffer, uniforms.Offset), uniforms);
                }
                else
                {
                    uniforms = UniformPages.Allocate(SurfaceMaterial.UniformBytes);
                    queue.WriteBuffer(uniforms.Buffer, uniforms.Offset, (ReadOnlySpan<float>)material.Uniforms);
                    group = (material.Terrain ? TerrainGroup(material, uniforms.Buffer, uniforms.Offset)
                        : MaterialGroup(material, material.Uniforms, uniforms.Buffer, uniforms.Offset), uniforms);
                }
                groups.Add(part.Material, group);
                foreach (GameTexture? texture in rdr2 is not null ? rdr2.Textures : material.BoundTextures())
                    if (texture is not null && pending.Source.TextureId(texture) is var id && !uploaded.Textures.Contains(id))
                        uploaded.Textures.Add(id);
            }
            // Terrain is solid ground, as the model viewer draws it; water is blended over the scene behind it.
            AlphaMode mode = family is WorldFamily.Rdr2Water or WorldFamily.GtaWater ? AlphaMode.Blend
                : material.Terrain ? AlphaMode.Opaque : material.Decal ? AlphaMode.Blend : material.AlphaMode;
            uploaded.Meshes.Add(new WorldMesh(pending.Layout.Placed[i], group.Group, group.Uniforms, mode, rdr2?.DoubleSided ?? false,
                material.Decal && family is not (WorldFamily.Rdr2Water or WorldFamily.GtaWater), family));
        }
        // RedFury's depth runs: adjacent casters of a level, one draw each.
        foreach (WorldMesh mesh in uploaded.Meshes.Where(m => m.Caster))
        {
            int last = uploaded.DepthRuns.Count - 1;
            if (last >= 0 && uploaded.DepthRuns[last] is var run && run.Level == mesh.Level && run.FirstIndex + run.IndexCount == mesh.Part.FirstIndex)
                uploaded.DepthRuns[last] = run with { IndexCount = run.IndexCount + mesh.Part.IndexCount };
            else
                uploaded.DepthRuns.Add(new WorldDepthRun(mesh.Level, mesh.Part.FirstIndex, mesh.Part.IndexCount));
        }
        uploaded.CastsShadows = uploaded.Meshes.Exists(mesh => mesh.Caster || mesh.Mode == AlphaMode.Mask);
    }

    /// <summary>Lets go of the asset on its way up, and what it put up that nothing else uses.</summary>
    public void CancelWorldUpload()
    {
        if (pendingWorld is not { } pending)
            return;
        pendingWorld = null;
        pending.GpuTexture?.Destroy();
        FreeSlice(VertexPages, pending.VertexBuffer);
        FreeSlice(IndexPages, pending.IndexBuffer);
        foreach (object id in pending.Registered)
            if (worldTextures.TryGetValue(id, out WorldTexture? texture) && texture.Users == 0)
                ForgetWorldTexture(id);
        ReleaseWorldMemory(pending.Source.MemoryCost);
    }
}
