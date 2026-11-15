using FiveFury;
using FiveFury.Rpf;
using FiveFury.Textures;
using FiveFury.Ytd;
using HikariHopper.Core;
using GameFormat = CommonFury.Textures.TextureFormat;

namespace HikariHopper.Tests;

/// <summary>An archive read through the format-neutral layer alone (<see cref="ArchiveNode"/>, <see cref="ArchiveEntry"/>).</summary>
public sealed class ArchiveLayerTests : IDisposable
{
    private readonly string scratch = Directory.CreateTempSubdirectory("hh-archives-").FullName;

    public void Dispose() => Directory.Delete(scratch, recursive: true);

    private static byte[] Ytd()
    {
        var dictionary = new TextureDictionary();
        dictionary.Textures.Add(new Texture("one", 4, 4, GameFormat.BC1Unorm, 1, new byte[8]));
        return new Ytd(dictionary, GameTarget.Legacy).Write();
    }

    // x.rpf: a folder with a resource and a binary file, and an RPF nested in it holding one file.
    private string Archive()
    {
        var nested = new RpfArchive("inner.rpf");
        nested.AddBinary("deep/note.txt", "inside"u8.ToArray());
        var archive = new RpfArchive("x.rpf");
        archive.AddResource("props/box.ytd", Ytd());
        archive.AddBinary("props/readme.txt", "hello"u8.ToArray());
        archive.AddBinary("inner.rpf", nested.Write());
        string path = Path.Combine(scratch, "x.rpf");
        archive.Save(path);
        return path;
    }

    [Fact]
    public void An_archive_lists_reads_and_nests_through_the_neutral_layer()
    {
        ArchiveNode root = ArchiveNode.OpenRoot(Archive(), null);
        try
        {
            Assert.Equal("RPF7", root.Format);
            Assert.Equal("RSC7", root.ResourceFormat);
            Assert.Equal("", root.RootDirectory.Path);
            Assert.True(root.RootDirectory.IsDirectory);

            // Every entry but the root, the folder's two files as listed, the same objects each time.
            Assert.Equal(["inner.rpf", "props", "props/box.ytd", "props/readme.txt"],
                root.EnumerateEntries().Select(entry => entry.Path).Order(StringComparer.Ordinal));
            Assert.DoesNotContain(root.RootDirectory, root.EnumerateEntries());
            ArchiveEntry props = root.DirectoryAt("props")!;
            Assert.Same(props, root.DirectoryAt("props/"));
            Assert.Equal(["box.ytd", "readme.txt"], props.Children.Select(entry => entry.Name).Order());
            Assert.Null(root.DirectoryAt("props/readme.txt"));

            ArchiveEntry ytd = root.EntryAt("props/box.ytd")!;
            Assert.True(ytd.IsResource);
            Assert.False(ytd.IsBinaryFile);
            Assert.True(ytd.Compressed);
            Assert.NotNull(ytd.ResourceVersion);
            Assert.Equal(Ytd(), ytd.Read());
            ArchiveEntry readme = root.EntryAt("props/readme.txt")!;
            Assert.True(readme.IsBinaryFile);
            Assert.Null(readme.ResourceVersion);
            Assert.Equal("hello"u8.ToArray(), readme.Read());
            Assert.Equal(5, readme.Size);

            // Into the nested archive, by its entry or by a path through it.
            ArchiveNode inner = root.OpenNested(root.EntryAt("inner.rpf")!);
            Assert.Same(inner, root.OpenNested(root.EntryAt("inner.rpf")!));
            Assert.Equal("inner.rpf", inner.Prefix);
            Assert.Same(root, inner.Root);
            var (owner, note) = root.FindEntry("inner.rpf/deep/note.txt")!.Value;
            Assert.Same(inner, owner);
            Assert.Equal("inner.rpf/deep/note.txt", owner.FullPath(note));
            Assert.Equal("inside"u8.ToArray(), note.Read());
            Assert.Throws<ArgumentException>(() => inner.OpenNested(readme));
            Assert.Throws<InvalidDataException>(() => root.OpenNested(ytd));
        }
        finally
        {
            root.Close();
        }
        Assert.True(root.IsClosed);
        Assert.True(root.Children.All(child => child.IsClosed));
    }

    [Fact]
    public void GTA_Vs_keys_pass_as_archive_keys()
    {
        var crypto = new FiveFury.Cryptography.GameCrypto(new byte[32]);
        ArchiveKeys? keys = crypto;
        Assert.Same(crypto, Assert.IsType<Rpf7Keys>(keys).Crypto);
        ArchiveKeys? none = (FiveFury.Cryptography.GameCrypto?)null;
        Assert.Null(none);
    }
}
