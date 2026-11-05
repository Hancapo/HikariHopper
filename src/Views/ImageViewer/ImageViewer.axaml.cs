using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using HikariHopper.Core;
using HikariHopper.ViewModels;

namespace HikariHopper.Views;

/// <summary>
/// An image file shown over the main window like the text viewer: DDS (with its mips), PNG, JPEG, BMP, TGA
/// and WebP. It uses the texture editor's preview, so the channels, filtering, the transparency grid and the
/// zoom work the same; F fits it, 1 shows it at actual size, and Escape closes.
/// </summary>
public partial class ImageViewer : UserControl, IModalContent
{
    private readonly ImageViewerViewModel image;
    private ModalOverlay? overlay;

    /// <summary>An empty viewer, for the XAML loader and previews.</summary>
    public ImageViewer() : this(new ImageViewerViewModel("", []))
    {
    }

    public ImageViewer(ImageViewerViewModel image)
    {
        this.image = image;
        InitializeComponent();
        TitleText.Text = image.Name;
        Preview.DataContext = image;
        CloseMark.Click += (_, _) => Close();
        image.PropertyChanged += OnImageChanged;
        AddHandler(KeyDownEvent, OnShortcut, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        SizeText.Text = Formatting.FormatSize(image.Size);
        Refresh();
    }

    public ImageViewerViewModel Image => image;

    /// <summary>The control the viewer opens over, in the main window.</summary>
    public Control? Anchor { get; set; }

    public bool IsOpen => overlay?.IsShown == true;

    /// <summary>The viewer closed; the image it held is let go.</summary>
    public event EventHandler? Closed;

    public void Present()
    {
        if (IsOpen)
            return;
        Control? anchor = Anchor
            ?? ((Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainWindow)?.ExplorerScreenHost;
        if (anchor is null)
            return;
        overlay = ModalOverlay.Show(anchor, this);
        if (overlay is not null)
            overlay.Closed += (_, _) =>
            {
                overlay = null;
                image.PropertyChanged -= OnImageChanged;
                image.Dispose();
                Closed?.Invoke(this, EventArgs.Empty);
            };
        Dispatcher.UIThread.Post(() => Focus(), DispatcherPriority.Loaded);
    }

    public void Close() => overlay?.Close();

    void IModalContent.RequestClose() => Close();

    private void OnImageChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        StatusText.Text = image.Status;
        StatusText.Foreground = image.Error.Length > 0 ? Ui.Theme.Error : Ui.Theme.TextFaint;
        Facts.IsVisible = image.SelectedIndex >= 0;
        DimensionsText.Text = $"{image.Width} × {image.Height}";
        MipsText.Text = image.MipCount == 1 ? "1 MIP" : $"{image.MipCount} MIPS";
        MipsText.IsVisible = image.MipCount > 1;
        FormatText.Text = image.FormatName;
        // A mip bar only for images that have mips to step through.
        Preview.MipBar.IsVisible = image.MipCount > 1;
    }

    private void OnShortcut(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None)
            return;
        switch (e.Key)
        {
            case Key.F:
                Preview.FitTexture();
                break;
            case Key.D1 or Key.NumPad1:
                Preview.ShowActualSize();
                break;
            default:
                return;
        }
        e.Handled = true;
    }
}
