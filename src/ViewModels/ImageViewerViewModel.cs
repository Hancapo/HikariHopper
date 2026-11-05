using Avalonia.Media.Imaging;
using CommonFury.Textures;
using CommunityToolkit.Mvvm.ComponentModel;
using FiveFury.Textures;
using HikariHopper.Core;
using HikariHopper.Textures;

namespace HikariHopper.ViewModels;

/// <summary>
/// One image file opened from the explorer for viewing: DDS (with its mips) or PNG, JPEG, BMP, TGA and WebP.
/// It is decoded off the UI thread, and each channel and mip it has shown is kept, so going back to one is instant.
/// </summary>
public sealed class ImageViewerViewModel : ObservableObject, ITexturePreviewSource, IDisposable
{
    private static readonly string[] Changes =
    [
        nameof(SelectedIndex), nameof(PreviewImage), nameof(HasPreview), nameof(PreviewLoading), nameof(PreviewWidth),
        nameof(PreviewHeight), nameof(Status), nameof(Error), nameof(Channel), nameof(MipCount), nameof(MipLevel),
        nameof(Width), nameof(Height), nameof(FormatName),
    ];

    private readonly Dictionary<(string Channel, int Mip), Bitmap> views = [];
    private readonly Dictionary<int, RgbaImage> mips = [];
    private Texture? texture;
    private Bitmap? previewImage;
    private string channel = "rgba";
    private int mipLevel;
    private int request;
    private bool loading = true;
    private bool opened;
    private bool disposed;
    private string status;
    private string error = "";

    /// <summary>The largest image file opened for viewing.</summary>
    public const long MaxSize = 256L * 1024 * 1024;

    public ImageViewerViewModel(string name, byte[] data)
    {
        Name = name;
        Size = data.LongLength;
        status = $"Decoding {name}…";
        Loading = LoadAsync(data);
    }

    /// <summary>The file's name, as the explorer lists it.</summary>
    public string Name { get; }

    /// <summary>The file's size in bytes.</summary>
    public long Size { get; }

    /// <summary>Completes once the file is decoded and shown, or failed to.</summary>
    public Task Loading { get; }

    /// <summary>The image's full size, in pixels (0 until it is decoded).</summary>
    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>The file's kind: the DDS's texture format, or PNG, JPEG and so on.</summary>
    public string FormatName { get; private set; } = "";

    public int SelectedIndex => opened ? 0 : -1;

    public Bitmap? PreviewImage => previewImage;

    public bool HasPreview => previewImage is not null;

    public bool PreviewLoading => loading && opened;

    public int PreviewWidth => Math.Max(1, Width >> mipLevel);

    public int PreviewHeight => Math.Max(1, Height >> mipLevel);

    public string Status => status;

    public string Error => error;

    public string Channel => channel;

    public int MipCount => texture?.MipCount ?? (opened ? 1 : 0);

    public int MipLevel => mipLevel;

    public void SetChannel(string mode)
    {
        string normalized = mode.ToLowerInvariant();
        if (!opened || !TextureOperations.Channels.Contains(normalized) || normalized == channel)
            return;
        channel = normalized;
        _ = ShowAsync();
    }

    public void SetMipLevel(int level)
    {
        int normalized = Math.Clamp(level, 0, Math.Max(0, MipCount - 1));
        if (!opened || normalized == mipLevel)
            return;
        mipLevel = normalized;
        _ = ShowAsync();
    }

    public void Dispose()
    {
        disposed = true;
        foreach (Bitmap bitmap in views.Values)
            TextureBitmaps.DisposeLater(bitmap);
        views.Clear();
        mips.Clear();
        previewImage = null;
    }

    private async Task LoadAsync(byte[] data)
    {
        string name = Name;
        try
        {
            (Texture? decodedTexture, RgbaImage first, string format) = await Task.Run(() => Decode(name, data));
            if (disposed)
                return;
            texture = decodedTexture;
            mips[0] = first;
            Width = first.Width;
            Height = first.Height;
            FormatName = format;
            opened = true;
            await ShowAsync();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            error = $"Could not open {name}: {exception.Message}";
            status = error;
            loading = false;
            RaiseAll();
        }
    }

    /// <summary>The file decoded: its texture for a DDS, its first mip as RGBA, and its kind.</summary>
    private static (Texture? Texture, RgbaImage First, string Format) Decode(string name, byte[] data)
    {
        string extension = Path.GetExtension(name).ToLowerInvariant();
        if (extension == ".dds")
        {
            Texture dds = Texture.FromSurface(DdsCodec.Read(data), Path.GetFileNameWithoutExtension(name));
            string format = Formatting.FormatTextureFormat(TextureFormatNames.Name(dds.Format), includeExact: true);
            return (dds, TextureProcessing.Decode(dds, 0), format);
        }
        string kind = extension switch { ".jpg" or ".jpeg" => "JPEG", "" => "Image", _ => extension[1..].ToUpperInvariant() };
        return (null, TextureImport.ReadImageBytes(data, name), kind);
    }

    /// <summary>Shows the current channel and mip: at once if it was made before, otherwise once it is.</summary>
    private async Task ShowAsync()
    {
        int token = ++request;
        (string Channel, int Mip) key = (channel, mipLevel);
        if (!views.TryGetValue(key, out Bitmap? bitmap))
        {
            // The current view stays up while the new one is made.
            loading = true;
            status = $"Decoding {Name} · mip {mipLevel}…";
            RaiseAll();
            Texture? source = texture;
            mips.TryGetValue(key.Mip, out RgbaImage? decoded);
            try
            {
                (bitmap, decoded) = await Task.Run(() =>
                {
                    RgbaImage image = decoded ?? TextureProcessing.Decode(source!, key.Mip);
                    return ((Bitmap)TextureBitmaps.Create(TextureOperations.ChannelView(image, key.Channel)), image);
                });
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                if (token != request || disposed)
                    return;
                error = $"Could not show mip {key.Mip}: {exception.Message}";
                status = error;
                loading = false;
                RaiseAll();
                return;
            }
            if (disposed)
            {
                bitmap.Dispose();
                return;
            }
            mips.TryAdd(key.Mip, decoded);
            if (!views.TryAdd(key, bitmap))
            {
                bitmap.Dispose();
                bitmap = views[key];
            }
            if (token != request)
                return;
        }
        previewImage = bitmap;
        loading = false;
        error = "";
        status = MipCount > 1 ? $"Previewing {Name} · mip {mipLevel}" : $"Previewing {Name}";
        RaiseAll();
    }

    private void RaiseAll()
    {
        foreach (string name in Changes)
            OnPropertyChanged(name);
    }
}
