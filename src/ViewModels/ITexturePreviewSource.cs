using System.ComponentModel;
using Avalonia.Media.Imaging;

namespace HikariHopper.ViewModels;

/// <summary>
/// What the texture preview (<see cref="Views.TexturePreview"/>: its toolbar, viewport and mip bar) shows: one
/// image, in a channel and at a mip level. The texture editor's view model is one, the image viewer's another.
/// Changes are raised by property name.
/// </summary>
public interface ITexturePreviewSource : INotifyPropertyChanged
{
    /// <summary>The image shown, or -1 while there is none (nothing opened yet, or it couldn't be read).</summary>
    int SelectedIndex { get; }

    Bitmap? PreviewImage { get; }

    bool HasPreview { get; }

    /// <summary>A view is being made; the current one stays up meanwhile.</summary>
    bool PreviewLoading { get; }

    /// <summary>The size of the mip shown, in pixels.</summary>
    int PreviewWidth { get; }

    int PreviewHeight { get; }

    /// <summary>What the preview says when there is no image to show.</summary>
    string Status { get; }

    /// <summary>Non-empty when <see cref="Status"/> reports an error.</summary>
    string Error { get; }

    /// <summary><c>rgba</c>, <c>r</c>, <c>g</c>, <c>b</c> or <c>a</c>.</summary>
    string Channel { get; }

    void SetChannel(string mode);

    int MipCount { get; }

    int MipLevel { get; }

    void SetMipLevel(int level);
}
