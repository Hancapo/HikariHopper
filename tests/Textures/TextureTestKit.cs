using Avalonia.Threading;
using CommonFury.Textures;
using FiveFury;
using FiveFury.Textures;
using HikariHopper.Infrastructure;
using HikariHopper.Textures;
using HikariHopper.ViewModels;

namespace HikariHopper.Tests.Textures;

/// <summary>Synthetic textures, fakes and settling helpers shared by the texture tests.</summary>
internal static class TextureTestKit
{
    public static byte[] Repeat(byte[] pattern, int count)
    {
        var result = new byte[pattern.Length * count];
        for (int index = 0; index < count; index++)
            pattern.CopyTo(result, index * pattern.Length);
        return result;
    }

    /// <summary>A 2×2 A8R8G8B8 (BGRA in memory) red texture, like the Python fixture.</summary>
    public static Texture Red(string name = "red") =>
        new(name, 2, 2, TextureFormat.B8G8R8A8Unorm, 1, Repeat([0, 0, 255, 255], 4));

    public static Texture Bgra(string name, int width, int height, byte[] pixel) =>
        new(name, width, height, TextureFormat.B8G8R8A8Unorm, 1, Repeat(pixel, width * height));

    public static TextureSet Dictionary(params string[] names) => Dictionary(GameTarget.Legacy, names);

    public static TextureSet Dictionary(GameTarget target, params string[] names) => new(names.Select(name => Red(name)), target);

    public static byte[] Bytes(TextureSet set) => set.ToYtd().Write(allowEmpty: true);

    public static byte[] EmptyYtd(GameTarget target) => new FiveFury.Ytd.Ytd(target).Write(allowEmpty: true);

    /// <summary>Waits for background work and runs the resulting UI-thread continuations, several rounds deep.</summary>
    public static void Settle(TextureViewerViewModel viewer)
    {
        for (int round = 0; round < 4; round++)
        {
            Assert.True(viewer.WaitForBackgroundWork(TimeSpan.FromSeconds(10)));
            Dispatcher.UIThread.RunJobs();
        }
    }

    public static TextureViewerViewModel Viewer(FakeClipboard? clipboard = null, FakeDialogs? dialogs = null)
    {
        var viewer = new TextureViewerViewModel(clipboard ?? new FakeClipboard());
        viewer.Dialogs = dialogs ?? new FakeDialogs();
        return viewer;
    }

    public static TextureViewerViewModel Loaded(TextureSet set, FakeClipboard? clipboard = null, FakeDialogs? dialogs = null)
    {
        TextureViewerViewModel viewer = Viewer(clipboard, dialogs);
        viewer.DictionaryLoaded(viewer.Generation, set);
        Settle(viewer);
        return viewer;
    }

    public static TextureViewerViewModel Opened(string name, byte[] data, string path = "", FakeClipboard? clipboard = null,
        FakeDialogs? dialogs = null)
    {
        TextureViewerViewModel viewer = Viewer(clipboard, dialogs);
        viewer.OpenDictionary(name, path, data);
        Settle(viewer);
        return viewer;
    }

    public static RgbaImage Solid(int width, int height, byte red, byte green, byte blue, byte alpha) =>
        new(width, height, Repeat([red, green, blue, alpha], width * height));

    /// <summary>Writes a solid image file whose format follows the extension.</summary>
    public static string ImageFile(string path, int width = 8, int height = 4, byte alpha = 100)
    {
        RgbaImage image = Solid(width, height, 230, 40, 70, alpha);
        WriteImage(path, image);
        return path;
    }

    public static void WriteImage(string path, RgbaImage image)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        byte[] data = extension switch
        {
            ".png" => image.Write(ImageFileFormat.Png),
            ".jpg" or ".jpeg" => image.Write(ImageFileFormat.Jpeg, 95),
            ".bmp" => image.Write(ImageFileFormat.Bmp),
            ".tga" => image.Write(ImageFileFormat.Tga),
            ".webp" => EncodeWebp(image),
            ".dds" => DdsCodec.Write(TextureProcessing.Encode(image,
                new TextureEncodingOptions { Format = TextureFormat.B8G8R8A8Unorm, GenerateMipmaps = false }).ToSurface()),
            _ => throw new ArgumentException(extension),
        };
        File.WriteAllBytes(path, data);
    }

    private static byte[] EncodeWebp(RgbaImage image)
    {
        var info = new SkiaSharp.SKImageInfo(image.Width, image.Height, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Unpremul);
        using var bitmap = new SkiaSharp.SKBitmap(info);
        System.Runtime.InteropServices.Marshal.Copy(image.Pixels.ToArray(), 0, bitmap.GetPixels(), image.Pixels.Length);
        using SkiaSharp.SKData encoded = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Webp, 100);
        return encoded.ToArray();
    }

    /// <summary>Records every status message, since later preview updates replace the final one.</summary>
    public static List<string> StatusHistory(TextureViewerViewModel viewer)
    {
        var history = new List<string>();
        viewer.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(TextureViewerViewModel.Status))
                history.Add(viewer.Status);
        };
        return history;
    }

    public static IReadOnlyList<string> Names(TextureViewerViewModel viewer) => viewer.Dictionary!.Names;

    public static HashSet<int> Selected(TextureViewerViewModel viewer) =>
        [.. viewer.TexturesModel.Select((item, row) => (item, row)).Where(pair => pair.item.TextureSelected).Select(pair => pair.row)];
}

/// <summary>A process-local clipboard, so tests never touch the user's clipboard.</summary>
internal sealed class FakeClipboard : IClipboardService
{
    public string? Text { get; set; }
    public RgbaImage? Image { get; private set; }
    public int ImageWrites { get; private set; }

    public bool HasImage => Image is not null;

    public event EventHandler? Changed;

    public Task<string?> GetTextAsync() => Task.FromResult(Text);

    public Task SetTextAsync(string text)
    {
        Text = text;
        Image = null;
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task<RgbaImage?> GetImageAsync() => Task.FromResult(Image);

    public Task<bool> SetImageAsync(RgbaImage image)
    {
        Image = image;
        Text = null;
        ImageWrites++;
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(true);
    }
}

/// <summary>Scripted file pickers that record their titles.</summary>
internal sealed class FakeDialogs : IFileDialogs
{
    public string? Folder { get; set; }
    public string? SaveFile { get; set; }
    public List<string> OpenFiles { get; } = [];
    public List<string> Titles { get; } = [];

    public Task<string?> PickFolderAsync(string title, string? startPath = null)
    {
        Titles.Add(title);
        return Task.FromResult(Folder);
    }

    public Task<IReadOnlyList<string>> PickOpenFilesAsync(string title, IReadOnlyList<FileFilter> filters,
        bool allowMultiple = false, string? startPath = null)
    {
        Titles.Add(title);
        return Task.FromResult<IReadOnlyList<string>>([.. OpenFiles]);
    }

    public Task<string?> PickSaveFileAsync(string title, string suggestedName, IReadOnlyList<FileFilter> filters,
        string? startPath = null)
    {
        Titles.Add(title);
        return Task.FromResult(SaveFile);
    }
}

/// <summary>A scratch folder deleted after the test.</summary>
internal sealed class TempFolder : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("hikari-texture-").FullName;

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        { Directory.Delete(Path, recursive: true); }
        catch (IOException)
        { }
    }
}
