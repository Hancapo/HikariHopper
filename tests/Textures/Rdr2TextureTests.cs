using HikariHopper.Core;
using HikariHopper.Textures;
using HikariHopper.ViewModels;
using RedFury.Textures;
using RedYtd = RedFury.Ytd.Ytd;

namespace HikariHopper.Tests.Textures;

/// <summary>Red Dead Redemption 2's texture dictionaries in the texture editor, read through RedFury.</summary>
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
        // Its checks are RDR2's: a texture the edit leaves without a name, or whose name's hash repeats, is refused.
        Assert.True(set.Validate().IsValid);
        TextureSet renamed = set.WithTextures([.. set.Textures, set.Textures[0]]);
        Assert.True(renamed.IsRdr2);
        Assert.False(renamed.Validate().IsValid);
        // Saving waits for what keeps each texture's own fields; it says so rather than losing them.
        Assert.Throws<NotSupportedException>(() => YtdSaving.Serialize(set));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void The_editor_opens_it_to_view_edit_and_extract_but_not_yet_to_save()
    {
        TextureViewerViewModel viewer = TextureTestKit.Opened("rocks.ytd", Ytd("rock_d", "rock_n"), @"C:\mods\rocks.ytd");
        Assert.True(viewer.HasDocument);
        Assert.Equal(2, viewer.TextureCount);
        Assert.False(viewer.CanSave);
        Assert.False(viewer.CanSaveAs);
        // Edits keep it RDR2's.
        viewer.SelectTexture(0);
        TextureTestKit.Settle(viewer);
        Assert.True(viewer.RenameSelected("rock_albedo"));
        TextureTestKit.Settle(viewer);
        Assert.Contains(viewer.TexturesModel, item => item.Name == "rock_albedo");
        Assert.False(viewer.CanSaveAs);
    }

    [Fact]
    public void A_dictionary_from_the_installed_game_opens_and_decodes()
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
                TextureSet set = YtdSaving.Read(ytd.Read());
                Assert.True(set.IsRdr2);
                Assert.NotEmpty(set.Textures);
                CommonFury.Textures.RgbaImage image = TextureOperations.DecodeImage(set.Textures[0]);
                Assert.Equal((set.Textures[0].Width, set.Textures[0].Height), (image.Width, image.Height));
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
