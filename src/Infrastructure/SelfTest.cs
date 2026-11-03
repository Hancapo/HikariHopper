using FiveFury.Rpf;
using FiveFury.Textures;
using HikariHopper.Core;
using HikariHopper.Textures;

namespace HikariHopper.Infrastructure;

/// <summary>
/// <c>HikariHopper --self-test [game folder]</c>: runs the app's core paths without a window and exits with 0 when they
/// all pass. Its job is the published Native AOT build, where code the unit tests cover under the JIT can still fail
/// (reflection the compiler couldn't see, a missing native library), so the release script runs it on the native binary.
/// </summary>
/// <remarks>Reads the game only; saves go to memory and settings to a scratch registry key that is removed again.</remarks>
public static class SelfTest
{
    public static int Run(string root, TextWriter output)
    {
        if (root.Length == 0)
        {
            output.WriteLine("FAIL no game folder: pass one or configure it in the app");
            return 1;
        }
        int failures = 0;
        LoadedGame? game = null;
        ArchiveNode? archive = null;
        byte[]? ytd = null, ydr = null;
        void Check(string name, Action step)
        {
            try
            {
                step();
                output.WriteLine($"PASS {name}");
            }
            catch (Exception exception)
            {
                failures++;
                output.WriteLine($"FAIL {name}: {exception.GetType().Name}: {exception.Message}");
            }
        }

        Check("game keys", () => game = RpfProvider.LoadGame(root));
        Check("nested archives", () =>
        {
            LoadedGame loaded = game ?? throw new InvalidOperationException("The game didn't load");
            archive = ArchiveNode.OpenRoot(Path.Combine(loaded.Root, "x64c.rpf"), loaded.Crypto);
            foreach (RpfBinaryFileEntry entry in archive.Archive.EnumerateEntries().OfType<RpfBinaryFileEntry>()
                .Where(entry => entry.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase)))
            {
                ArchiveNode nested = archive.OpenNested(entry);
                foreach (RpfResourceFileEntry resource in nested.Archive.EnumerateEntries().OfType<RpfResourceFileEntry>())
                {
                    // Both need at least one texture for the checks below.
                    if (ytd is null && resource.Name.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase))
                    {
                        byte[] data = resource.ReadStandalone();
                        if (YtdSaving.Read(data).Textures.Length > 0)
                            ytd = data;
                    }
                    else if (ydr is null && resource.Name.EndsWith(".ydr", StringComparison.OrdinalIgnoreCase)
                        && YdrTextureDocument.Read(resource.ReadStandalone()) is { Dictionary.Textures.Length: > 0 } document)
                        ydr = document.Original;
                }
                if (ytd is not null && ydr is not null)
                    return;
            }
            throw new InvalidDataException("No nested archive holds both a YTD and a YDR with embedded textures");
        });
        Check("texture dictionary", () =>
        {
            TextureSet dictionary = YtdSaving.Read(ytd ?? throw new InvalidOperationException("No YTD was found"));
            Texture texture = dictionary.Textures[0];
            RgbaImage image = TextureOperations.DecodeImage(texture);
            if (image.Width != texture.Width || image.Height != texture.Height)
                throw new InvalidDataException($"Decoded {image.Width}x{image.Height} from a {texture.Width}x{texture.Height} texture");
            TextureSet reread = YtdSaving.Read(YtdSaving.Serialize(dictionary));
            if (reread.Textures.Length != dictionary.Textures.Length
                || !reread.Textures.Zip(dictionary.Textures).All(pair => TextureComparison.SameContent(pair.First, pair.Second)))
                throw new InvalidDataException("The dictionary changed in a save round trip");
        });
        Check("drawable texture edit", () =>
        {
            YdrTextureDocument document = YdrTextureDocument.Read(ydr ?? throw new InvalidOperationException("No YDR was found"));
            Texture texture = document.Dictionary.Textures[0];
            byte[] pixels = texture.Data.ToArray();
            pixels[0] ^= 0xFF;
            Texture edited = new(texture.Name, texture.Width, texture.Height, texture.Format, texture.MipCount, pixels, texture.Shape)
            {
                Usage = texture.Usage,
                UsageFlags = texture.UsageFlags,
                ReferenceCount = texture.ReferenceCount,
            };
            byte[] saved = document.Serialize(new TextureSet([edited, .. document.Dictionary.Textures.Skip(1)], document.Target), new Dictionary<string, string>());
            if (!YdrTextureDocument.Read(saved).Dictionary.Textures.Single(item => item.Name == texture.Name).Data.SequenceEqual(pixels))
                throw new InvalidDataException("The edited texture did not read back");
        });
        Check("drag source", () =>
        {
            if (!OperatingSystem.IsWindows())
                return;
            // A cursor from rendered pixels, and a real shell drag of a scratch file that cancels at once.
            nint cursor = ShellFileDrag.CreateCursor([.. Enumerable.Repeat((byte)255, 16 * 16 * 4)], 16, 16, 0, 0);
            if (cursor == 0)
                throw new InvalidOperationException("Windows refused the drag cursor");
            ShellFileDrag.DestroyCursor(cursor);
            string scratch = Path.Combine(Path.GetTempPath(), $"hikarihopper-self-test-{Guid.NewGuid():N}.txt");
            File.WriteAllText(scratch, "drag");
            try
            {
                if (!ShellFileDrag.Probe(scratch))
                    throw new InvalidOperationException("The shell drag did not run");
            }
            finally
            {
                File.Delete(scratch);
            }
        });
        Check("settings", () =>
        {
            string previous = AppSettings.RegistryPath, scratch = $@"Software\HikariHopperSelfTest\{Guid.NewGuid():N}";
            AppSettings.RegistryPath = scratch;
            try
            {
                AppSettings.SetString("text", "hikari");
                AppSettings.SetInt("number", 42);
                AppSettings.SetStringList("list", ["a", "b"]);
                if (AppSettings.GetString("text") != "hikari" || AppSettings.GetInt("number") != 42 || !AppSettings.GetStringList("list").SequenceEqual(["a", "b"]))
                    throw new InvalidDataException("Settings did not read back as written");
            }
            finally
            {
                AppSettings.RegistryPath = previous;
                // Only this run's key: another self-test may be running at the same time.
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(scratch, throwOnMissingSubKey: false);
                try
                {
                    using (Microsoft.Win32.RegistryKey? parent = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\HikariHopperSelfTest"))
                        if (parent is { SubKeyCount: 0, ValueCount: 0 })
                            Microsoft.Win32.Registry.CurrentUser.DeleteSubKey(@"Software\HikariHopperSelfTest", throwOnMissingSubKey: false);
                }
                catch (InvalidOperationException)
                {
                    // Another run added its key in the meantime; it removes the parent when it finishes.
                }
            }
        });
        archive?.Close();
        output.WriteLine(failures == 0 ? "Self-test passed" : $"Self-test failed: {failures} check(s)");
        return failures == 0 ? 0 : 1;
    }
}
