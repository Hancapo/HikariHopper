using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CommonFury.Textures;
using FiveFury.Textures;
using HikariHopper.Tests.Textures;
using HikariHopper.Textures;
using HikariHopper.ViewModels;
using HikariHopper.Views;

namespace HikariHopper.Tests;

/// <summary>The image viewer: decoding image files, its channels and mips, and opening it from the explorer.</summary>
[Collection(ExplorerCollection.Name)]
public sealed class ImageViewerTests : ExplorerTestBase
{
    /// <summary>Runs the UI thread until the image is decoded and its view is up.</summary>
    internal static void Settle(ImageViewerViewModel image)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        do
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        while ((!image.Loading.IsCompleted || image.PreviewLoading) && DateTime.UtcNow < deadline);
        Dispatcher.UIThread.RunJobs();
        Assert.True(image.Loading.IsCompleted && !image.PreviewLoading, "the image did not finish decoding");
    }

    private static byte[] Png(int width, int height, byte alpha = 100)
    {
        using var folder = new TempFolder();
        return File.ReadAllBytes(TextureTestKit.ImageFile(folder.File("image.png"), width, height, alpha));
    }

    /// <summary>A BC3 DDS with a full mip chain, from an image of the given size.</summary>
    private static byte[] Dds(int width, int height)
    {
        Texture texture = TextureImport.NormalizeTextureImage(TextureTestKit.Solid(width, height, 200, 40, 20, 128), "paint");
        return DdsCodec.Write(texture.ToSurface());
    }

    [AvaloniaFact]
    public void APngIsDecodedAndEachChannelCanBeShown()
    {
        var image = new ImageViewerViewModel("logo.png", Png(8, 4));
        Assert.Equal(-1, image.SelectedIndex);
        Assert.Equal("Decoding logo.png…", image.Status);
        Settle(image);
        Assert.Equal(0, image.SelectedIndex);
        Assert.True(image.HasPreview);
        Assert.Equal((8, 4, 1, "PNG"), (image.Width, image.Height, image.MipCount, image.FormatName));
        Assert.Equal((8, 4), (image.PreviewImage!.PixelSize.Width, image.PreviewImage.PixelSize.Height));
        Avalonia.Media.Imaging.Bitmap rgba = image.PreviewImage;

        // Another channel is a view of its own, and going back to one already made is instant.
        image.SetChannel("a");
        Settle(image);
        Assert.Equal("a", image.Channel);
        Assert.NotSame(rgba, image.PreviewImage);
        image.SetChannel("rgba");
        Assert.False(image.PreviewLoading);
        Assert.Same(rgba, image.PreviewImage);
        image.Dispose();
    }

    [AvaloniaFact]
    public void ADdsShowsItsFormatAndEveryMip()
    {
        var image = new ImageViewerViewModel("paint.dds", Dds(64, 32));
        Settle(image);
        Assert.Equal((64, 32), (image.Width, image.Height));
        Assert.True(image.MipCount > 1);
        Assert.Contains("BC3", image.FormatName);
        image.SetMipLevel(2);
        Settle(image);
        Assert.Equal(2, image.MipLevel);
        Assert.Equal((16, 8), (image.PreviewWidth, image.PreviewHeight));
        Assert.Equal((16, 8), (image.PreviewImage!.PixelSize.Width, image.PreviewImage.PixelSize.Height));
        Assert.Equal("", image.Status);
        image.Dispose();
    }

    [AvaloniaFact]
    public void AFileThatIsNotAnImageSaysSo()
    {
        var image = new ImageViewerViewModel("broken.png", [1, 2, 3, 4]);
        Settle(image);
        Assert.Equal(-1, image.SelectedIndex);
        Assert.False(image.HasPreview);
        Assert.StartsWith("Could not open broken.png", image.Error);
        Assert.Equal(image.Error, image.Status);
    }

    [AvaloniaFact]
    public void DoubleClickingAnImageInTheExplorerOpensTheViewer()
    {
        MakeGame();
        File.WriteAllBytes(Path.Combine(Work, "photo.png"), Png(40, 20, alpha: 255));
        var session = new ExplorerSession();
        var window = new MainWindow { Width = 1360, Height = 840 };
        session.Attach(window);
        window.Show();
        ExplorerViewModel bridge = session.Tabs.ActiveBridge;
        bridge.OpenGame(Work);
        Settle(bridge);
        ExplorerStateRenders.Pump(200);
        int row = Enumerable.Range(0, bridge.EntriesModel.Count).Single(index => bridge.EntriesModel[index].Name == "photo.png");
        bridge.ActivateEntry(row);
        ImageViewer viewer = Assert.IsType<ImageViewer>(session.ImageViewer);
        Settle(viewer.Image);
        ExplorerStateRenders.Pump(300);
        Assert.True(viewer.IsOpen);
        Assert.Equal("Opened photo.png", bridge.Status);
        Assert.Equal((40, 20), (viewer.Image.Width, viewer.Image.Height));
        Assert.Same(viewer.Image, viewer.Preview.DataContext);
        Assert.Equal("40 × 20", viewer.DimensionsText.Text);
        Assert.Equal("PNG", viewer.FormatText.Text);
        Assert.False(viewer.Preview.MipBar.IsVisible);

        // Escape closes it and the session lets it go.
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        ExplorerStateRenders.Pump(300);
        Assert.False(viewer.IsOpen);
        Assert.Null(session.ImageViewer);
        window.Close();
    }
}
