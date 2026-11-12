using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommonFury.Textures;
using FiveFury.Textures;
using HikariHopper.Core;
using HikariHopper.Textures;

namespace HikariHopper.ViewModels;

/// <summary>One row of <see cref="TextureViewerViewModel.TexturesModel"/> (Python <c>TextureListModel</c> roles).</summary>
public sealed partial class TextureItem : ObservableObject
{
    internal TextureItem(Texture texture)
    {
        Texture = texture;
        Name = texture.Name ?? "";
        Dimensions = $"{texture.Width} × {texture.Height}";
        ExactFormatName = TextureFormatNames.Name(texture.Format);
        FormatName = Formatting.FormatTextureFormat(ExactFormatName);
        Usage = TextureFormatNames.Usage(texture.Usage);
        DataSize = texture.Data.Length;
        DataSizeLabel = Formatting.FormatSize(DataSize);
        MipCount = texture.MipCount;
    }

    /// <summary>The texture this row shows; never modified.</summary>
    internal Texture Texture { get; }

    /// <summary>Storage format name (<c>BC1</c>, <c>A8R8G8B8</c>…).</summary>
    internal string ExactFormatName { get; }

    public string Name { get; }

    /// <summary><c>"512 × 256"</c>.</summary>
    public string Dimensions { get; }

    /// <summary>Friendly format label (<c>DXT1</c>, <c>ARGB 32-bit</c>…).</summary>
    public string FormatName { get; }

    /// <summary>Usage name (<c>DEFAULT</c>, <c>DIFFUSE</c>…).</summary>
    public string Usage { get; }

    /// <summary>Encoded mip chain size in bytes.</summary>
    public long DataSize { get; }

    /// <summary><c>"1.3 MB"</c>.</summary>
    public string DataSizeLabel { get; }

    public int MipCount { get; }

    /// <summary>Thumbnail key, empty until the thumbnail is decoded (QML <c>thumbnailUrl</c>).</summary>
    [ObservableProperty]
    public partial string ThumbnailUrl { get; internal set; } = "";

    /// <summary>The decoded thumbnail (premultiplied BGRA, at most 96×96), or null while <see cref="ThumbnailUrl"/> is empty.</summary>
    [ObservableProperty]
    public partial Bitmap? Thumbnail { get; internal set; }

    /// <summary>Whether this row is part of the multi-selection (QML <c>textureSelected</c>).</summary>
    [ObservableProperty]
    public partial bool TextureSelected { get; internal set; }

    internal void SetThumbnail(string url, Bitmap? bitmap)
    {
        Bitmap? previous = Thumbnail;
        ThumbnailUrl = url;
        Thumbnail = bitmap;
        if (previous is not null && !ReferenceEquals(previous, bitmap))
            TextureBitmaps.DisposeLater(previous);
    }
}
