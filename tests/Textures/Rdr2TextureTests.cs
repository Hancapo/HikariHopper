using HikariHopper.Core;
using HikariHopper.Textures;
using HikariHopper.ViewModels;
using RedFury.Textures;
using RedYtd = RedFury.Ytd.Ytd;

namespace HikariHopper.Tests.Textures;

/// <summary>Red Dead Redemption 2's texture dictionaries in the texture editor, read and written through RedFury.</summary>
[Collection(ExplorerCollection.Name)]
public sealed class Rdr2TextureTests
{
    // A 4×4 BC1 texture with two mips: one block, then the 2×2 mip's block.
    private static readonly byte[] Pixels = [.. Enumerable.Range(0, 16).Select(i => (byte)(i * 7))];

    private static byte[] Ytd(params string[] names) =>
        RedYtd.Create(names.Select(name => new TextureImage(name, 4, 4, TextureFormat.Bc1Unorm, 2, Pixels))).Write();

    [Fact]
    public void An_RDR2_dictionary_reads_into_the_editors_textures_as_it_is()
    {
        byte[] data = Ytd("rock_d", "rock_n");
        Assert.True(Rdr2TextureSource.IsRdr2(data));
        TextureSet set = YtdSaving.Read(data);
        Assert.True(set.IsRdr2);
        Assert.Equal(["rock_d", "rock_n"], set.Names.Order());
        FiveFury.Textures.Texture rock = set.Textures.Single(texture => texture.Name == "rock_d");
        Assert.Equal((4, 4, 2), (rock.Width, rock.Height, rock.MipCount));
        Assert.Equal(CommonFury.Textures.TextureFormat.BC1Unorm, rock.Format);
        Assert.Equal(Pixels, rock.Data.ToArray());
        // Its checks are RDR2's: a texture whose name's hash repeats another's is refused.
        Assert.True(set.Validate().IsValid);
        TextureSet twice = set.WithTextures([.. set.Textures, set.Textures[0]]);
        Assert.True(twice.IsRdr2);
        Assert.False(twice.Validate().IsValid);
    }

    [Fact]
    public void Saving_keeps_each_textures_own_fields()
    {
        // A dictionary whose textures carry a usage and tile mode other than RedFury's defaults.
        byte[] data = RedYtd.Create(new[] { "rock_d", "rock_n", "rock_s" }.Select(name =>
            new TextureImage(name, 4, 4, TextureFormat.Bc1Unorm, 2, Pixels) { Usage = 5, TileMode = 7 })).Write();
        TextureSet set = YtdSaving.Read(data);
        // rock_n's pixels changed under its own name, rock_s removed, a new one added.
        byte[] changed = [.. Pixels.Reverse()];
        FiveFury.Textures.Texture rockN = set.Textures.Single(texture => texture.Name == "rock_n");
        var edited = new FiveFury.Textures.Texture("rock_n", 4, 4, rockN.Format, 2, changed);
        var added = new FiveFury.Textures.Texture("moss_d", 4, 4, rockN.Format, 2, Pixels);
        TextureSet saved = set.WithTextures([set.Textures.Single(texture => texture.Name == "rock_d"), edited, added]);

        RedYtd reread = RedYtd.Read(YtdSaving.Serialize(saved));
        Assert.Equal(["moss_d", "rock_d", "rock_n"], reread.Textures.Select(texture => texture.Name).Order());
        // Untouched: as it was. Changed: its own pixels, its original's fields. New: RedFury's defaults.
        Assert.Equal((5, 7), ((int)reread.Find("rock_d")!.Usage, (int)reread.Find("rock_d")!.TileMode));
        Assert.Equal(Pixels, reread.Find("rock_d")!.Data.ToArray());
        Assert.Equal((5, 7), ((int)reread.Find("rock_n")!.Usage, (int)reread.Find("rock_n")!.TileMode));
        Assert.Equal(changed, reread.Find("rock_n")!.Data.ToArray());
        Assert.Equal(((int)TextureImage.DefaultUsage, (int)TextureImage.DefaultTileMode),
            ((int)reread.Find("moss_d")!.Usage, (int)reread.Find("moss_d")!.TileMode));
    }

    [Fact]
    public void A_texture_RedFury_cant_write_back_as_it_was_stops_the_save()
    {
        // A CPU-side texture, as the game's decal pools are: its descriptor's flags word says so.
        RedYtd ordinary = RedYtd.Create([new TextureImage("pool_solid_t", 4, 4, TextureFormat.Bc1Unorm, 2, Pixels),
            new TextureImage("rock_d", 4, 4, TextureFormat.Bc1Unorm, 2, Pixels)]);
        byte[] resource = ordinary.Resource.Data.ToArray();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(resource.AsSpan(ordinary.Find("pool_solid_t")!.SourceOffset + 0x10), 0x18408002);
        byte[] data = new RedFury.Resources.Rsc8Resource(ordinary.Resource.Header, resource).Write();
        TextureSet set = YtdSaving.Read(data);
        var error = Assert.Throws<NotSupportedException>(() => YtdSaving.Serialize(set));
        Assert.StartsWith("pool_solid_t can't be saved back as the game stores it yet", error.Message);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task The_editor_opens_edits_and_saves_it_as_RDR2s()
    {
        string folder = Directory.CreateTempSubdirectory("hh-rdr2-ytd-").FullName;
        try
        {
            var dialogs = new FakeDialogs { SaveFile = Path.Combine(folder, "rocks_edited.ytd") };
            TextureViewerViewModel viewer = TextureTestKit.Opened("rocks.ytd", Ytd("rock_d", "rock_n"), dialogs: dialogs);
            Assert.True(viewer.HasDocument);
            Assert.Equal(2, viewer.TextureCount);
            Assert.True(viewer.CanSaveAs);
            // Edits keep it RDR2's.
            viewer.SelectTexture(0);
            TextureTestKit.Settle(viewer);
            Assert.True(viewer.RenameSelected("rock_albedo"));
            TextureTestKit.Settle(viewer);
            Assert.Contains(viewer.TexturesModel, item => item.Name == "rock_albedo");
            // Saved as RDR2's, with the edit.
            Assert.True(await viewer.SaveYtdAs());
            TextureTestKit.Settle(viewer);
            RedYtd saved = RedYtd.Read(File.ReadAllBytes(Path.Combine(folder, "rocks_edited.ytd")));
            Assert.Contains(saved.Textures, texture => texture.Name == "rock_albedo");
            Assert.Equal(2, saved.Textures.Count);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_dictionary_from_the_installed_game_opens_decodes_and_saves_back_as_it_was()
    {
        string game = TestGames.Rdr2();
        foreach (string archive in new[] { "common_0.rpf", "data_0.rpf", "update_1.rpf" })
        {
            ArchiveNode root = ArchiveNode.OpenRoot(Path.Combine(game, archive), Rdr2Keys.Bundled);
            try
            {
                // In the archive, or one level into the archives nested in it, where RDR2 keeps most.
                static ArchiveEntry? Find(ArchiveNode node) => node.EnumerateEntries().FirstOrDefault(entry => entry.IsResource
                    && entry.Name.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase) && entry.Size < 4_000_000);
                ArchiveEntry? ytd = Find(root);
                foreach (ArchiveEntry nested in root.EnumerateEntries().Where(entry => entry.IsBinaryFile
                    && entry.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase)))
                {
                    if (ytd is not null)
                        break;
                    try
                    {
                        ytd = Find(root.OpenNested(nested));
                    }
                    catch (InvalidDataException)
                    {
                    }
                }
                if (ytd is null)
                    continue;
                byte[] data = ytd.Read();
                TextureSet set = YtdSaving.Read(data);
                Assert.True(set.IsRdr2);
                Assert.NotEmpty(set.Textures);
                CommonFury.Textures.RgbaImage image = TextureOperations.DecodeImage(set.Textures[0]);
                Assert.Equal((set.Textures[0].Width, set.Textures[0].Height), (image.Width, image.Height));
                // Saved untouched, every texture reads back as the game stored it.
                RedYtd original = RedYtd.Read(data), saved = RedYtd.Read(YtdSaving.Serialize(set));
                Assert.Equal(original.Textures.Count, saved.Textures.Count);
                foreach (RedFury.Textures.Texture texture in original.Textures)
                {
                    RedFury.Textures.Texture again = saved.Find(texture.NameHash)!;
                    Assert.Equal((texture.Width, texture.Height, texture.Format, texture.Usage, texture.TileMode, texture.Dimension),
                        (again.Width, again.Height, again.Format, again.Usage, again.TileMode, again.Dimension));
                    Assert.True(texture.Data.Span.SequenceEqual(again.Data.Span));
                }
                return;
            }
            finally
            {
                root.Close();
            }
        }
        Assert.Fail("None of the archives searched holds a texture dictionary");
    }
}
