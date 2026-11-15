using System.Diagnostics;
using System.Numerics;
using FiveFury.Cryptography;
using HikariHopper.Core;
using RedFury.World;

namespace HikariHopper.Tests;

/// <summary>GTA V's world index, made from a real Legacy installation (<see cref="TestGames"/>); skipped without one.</summary>
public sealed class GtaWorldIndexTests : IDisposable
{
    private readonly string previousCache = GameTextures.CacheFolder;
    private readonly string scratch = Directory.CreateTempSubdirectory("hh-gta-world-").FullName;

    public GtaWorldIndexTests() => GameTextures.CacheFolder = Path.Combine(scratch, "cache");

    public void Dispose()
    {
        GameTextures.CacheFolder = previousCache;
        try
        {
            Directory.Delete(scratch, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void The_base_game_s_maps_are_placed_around_Legion_Square_their_files_found_and_cached()
    {
        string root = TestGames.Legacy();
        // HIKARI_TEST_LEGACY may name an Enhanced folder: its index is made the same way.
        string executable = Path.Combine(root, File.Exists(Path.Combine(root, "GTA5.exe")) ? "GTA5.exe" : "GTA5_Enhanced.exe");
        var keys = new Rpf7Keys(GameKeys.Load(executable));
        var made = Stopwatch.StartNew();
        GtaWorldIndex index = GtaWorldIndex.Build(root, keys, _ => { }, CancellationToken.None);
        made.Stop();
        try
        {
            Assert.True(index.MapCount > 1000, $"{index.MapCount} maps");
            Assert.True(index.Lod.Nodes.Count > 100_000, $"{index.Lod.Nodes.Count} placements");
            // Legion Square's landmark has the city around it, at every level of detail.
            WorldLandmark landmark = GameCatalog.Games.Single(game => game.Id == GameCatalog.GtaV).Landmark!;
            WorldLodNode[] near = [.. index.Lod.Nodes.Where(node => Vector2.Distance(new(node.Center.X, node.Center.Y), new(landmark.Eye.X, landmark.Eye.Y)) < 150)];
            Assert.True(near.Length > 200, $"{near.Length} placements near Legion Square");
            Assert.Contains(near, node => node.Parent is not null);
            // A placement's model and map are among the game's files.
            WorldLodNode placed = near.First(node => node.Asset.Extension == 2);
            Assert.NotNull(index.Locate(placed.Asset.Hash, placed.Asset.Extension));
            Assert.EndsWith(".ymap", index.Locate(placed.Id.Map, 73)!.Name);
            Assert.DoesNotContain("0x", index.NameOf(placed.Id.Map));
            // Its roads: their own dictionary holds only some of their textures, the rest are its gtxd parents'
            // (downtownrd, then globalroads); all of them are found, and the surfaces are drawn.
            WorldLodNode road = near.First(node => node.Asset.TextureDictionary == new CommonFury.Hashing.Hash32("dt1_rd1_1").Value);
            Rendering.World.WorldAsset asset;
            lock (index.Gate)
                asset = index.LoadAsset(road.Asset);
            Assert.NotEmpty(asset.Layout.Kept);
            Assert.DoesNotContain(asset.Diagnostics, line => line.StartsWith("Texture not found", StringComparison.Ordinal));
            Assert.All(asset.Layout.Kept, kept => Assert.Null(kept.Rdr2));
            // Grass batches are indexed unread; one expands into its plants, each with its colour and fade.
            WorldLodNode grass = index.Lod.Nodes.First(node => node.Grass is { InstanceCount: > 16 });
            WorldLodNode plants;
            lock (index.Gate)
                plants = index.CreateGrass()!(grass, CancellationToken.None);
            Assert.InRange(plants.Instances.Count, 1, grass.Grass!.InstanceCount);
            Assert.All(plants.Instances, plant => Assert.Equal(grass.Distance, plant.Grass!.Value.FadeEnd));
            Assert.True(grass.Distance <= GtaWorldCatalog.GrassReach);
            // Interiors: Michael's house's contents are placed, each in a room taking its ambient from the game's
            // timecycle modifiers (v_michael lets a fifth of the natural ambient in).
            WorldLodNode armchair = index.Lod.Nodes.First(node => node.Asset.Hash == new CommonFury.Hashing.Hash32("v_res_m_armchair").Value
                && Vector3.Distance(node.Center, new Vector3(-804.8f, 175.1f, 72.4f)) < 2);
            Assert.Contains("interior_v_michael", index.NameOf(armchair.Id.Map));
            Assert.Equal(.2f, index.AmbientOf(armchair)!.Value.Natural, 3);
            Assert.Null(index.AmbientOf(placed));
        }
        finally
        {
            index.Game.Dispose();
        }

        // The second time reads the cache, the same placements.
        var read = Stopwatch.StartNew();
        GtaWorldIndex cached = GtaWorldIndex.Build(root, keys, _ => { }, CancellationToken.None);
        read.Stop();
        try
        {
            Assert.Equal(index.Lod.Nodes.Count, cached.Lod.Nodes.Count);
            Assert.Equal(index.Lod.Nodes[^1].Transform, cached.Lod.Nodes[^1].Transform);
            Assert.Equal(index.Lod.Nodes[^1].Parent, cached.Lod.Nodes[^1].Parent);
            TestContext.Current.SendDiagnosticMessage($"GTA world: {index.MapCount} maps, {index.Lod.Nodes.Count} placements, made in {made.Elapsed.TotalSeconds:0.0} s, read back in {read.Elapsed.TotalSeconds:0.0} s");
        }
        finally
        {
            cached.Game.Dispose();
        }
    }
}
