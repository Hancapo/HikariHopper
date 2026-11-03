using System.ComponentModel;
using Avalonia.Controls;
using HikariHopper.ViewModels;

namespace HikariHopper.Views;

/// <summary>
/// The mip level bar under the texture preview (port of <c>TextureMipBar.qml</c>). The slider is
/// enabled when the current texture has more than one mip; moving it calls
/// <see cref="TextureViewerViewModel.SetMipLevel"/>.
/// </summary>
public partial class TextureMipBar : UserControl
{
    private TextureViewerViewModel? viewModel;

    public TextureMipBar()
    {
        InitializeComponent();
        Slider.Moved += (_, level) => viewModel?.SetMipLevel(level);
        // Qt restores the value binding when the slider is released.
        Slider.Released += (_, _) => Slider.SyncValue(viewModel?.MipLevel ?? 0);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (viewModel is not null)
            viewModel.PropertyChanged -= ViewModelChanged;
        viewModel = DataContext as TextureViewerViewModel;
        if (viewModel is not null)
            viewModel.PropertyChanged += ViewModelChanged;
        Refresh();
    }

    private void ViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TextureViewerViewModel.MipLevel) or nameof(TextureViewerViewModel.MipCount)
            or nameof(TextureViewerViewModel.PreviewWidth) or nameof(TextureViewerViewModel.PreviewHeight))
            Refresh();
    }

    private void Refresh()
    {
        int mipLevel = viewModel?.MipLevel ?? 0;
        int mipCount = viewModel?.MipCount ?? 0;
        MipText.Text = $"MIP {mipLevel} / {Math.Max(0, mipCount - 1)}";
        SizeText.Text = $"{viewModel?.PreviewWidth ?? 0} × {viewModel?.PreviewHeight ?? 0}";
        Slider.Maximum = Math.Max(0, mipCount - 1);
        Slider.TickCount = mipCount;
        Slider.IsEnabled = mipCount > 1;
        Slider.SyncValue(mipLevel);
    }
}
