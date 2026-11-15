namespace HikariHopper.Tests;

public sealed class GameLibrariesTests
{
    [Fact]
    public void FiveFury_and_RedFury_share_one_CommonFury()
    {
        // GTA V's library and RDR2's, each touched so both are loaded; both read files through CommonFury.
        System.Reflection.Assembly gta = typeof(FiveFury.Rpf.RpfArchive).Assembly;
        System.Reflection.Assembly rdr = typeof(RedFury.Rpf.RpfArchive).Assembly;
        string? Common(System.Reflection.Assembly library) =>
            library.GetReferencedAssemblies().FirstOrDefault(name => name.Name == "CommonFury")?.FullName;
        Assert.NotNull(Common(gta));
        Assert.Equal(Common(gta), Common(rdr));
        // One CommonFury in the process, the one both were built against.
        Assert.Equal(typeof(CommonFury.Hashing.Hash32).Assembly.FullName, Common(gta));
        Assert.Single(AppDomain.CurrentDomain.GetAssemblies(), assembly => assembly.GetName().Name == "CommonFury");
    }
}
