using CommonFury.Hashing;
using FiveFury.Assets;
using FiveFury.Authoring;
using HikariHopper.Core;

namespace HikariHopper.Tests;

// The archives GTA V's level registers, and a name two archives hold read from the one the game registers.
public sealed class GameImagesTests
{
    private static GameInstallation Legacy()
    {
        string root = TestGames.Legacy();
        var keys = new Rpf7Keys(GameKeys.Load(Path.Combine(root, "GTA5.exe")));
        return GameInstallation.Open(root, FiveFury.GameTarget.Legacy, keys.Crypto, new GameInstallationOptions { DlcLevel = DlcLevel.Base });
    }

    [Fact]
    public void A_dictionary_in_an_archive_the_level_never_registers_is_read_from_the_one_it_does()
    {
        using GameInstallation game = Legacy();
        GameFileSystem files = game.FileSystem;
        // gta5.meta's data files register the level's archives, over eight hundred of them.
        Assert.InRange(GameImages.Of(files).Count, 800, 2000);
        // id2_props_lod.ytd is in indust_02's id2_props.rpf, which images.meta doesn't list, and its id2_rd.rpf, which it
        // does: FiveFury mounts both at the same rank and reads neither; the game reads id2_rd's, which has the LOD
        // pylons' prop_pylon_L2.
        var name = new Hash32("id2_props_lod");
        Assert.Equal(ResolutionStatus.Ambiguous, files.Resolve(name, GameFileType.Ytd).Status);
        GameFile registered = files.ResolveRegistered(name, GameFileType.Ytd)!;
        Assert.Contains("/indust_02/id2_rd.rpf/", registered.VirtualPath, StringComparison.OrdinalIgnoreCase);
        var dictionary = FiveFury.Ytd.Ytd.Read(registered.ReadStandalone()).Dictionary;
        Assert.Contains(dictionary.Textures, texture => string.Equals(texture.Name, "prop_pylon_L2", StringComparison.OrdinalIgnoreCase));
        // A name with one copy is read as FiveFury reads it.
        var single = new Hash32("mapdetail");
        Assert.Equal(files.Resolve(single, GameFileType.Ytd).Value, files.ResolveRegistered(single, GameFileType.Ytd));
    }
}
