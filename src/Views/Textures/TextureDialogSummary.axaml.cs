using System.ComponentModel;
using Avalonia.Controls;
using HikariHopper.ViewModels;

namespace HikariHopper.Views;

/// <summary>
/// The selection summary at the top of the tool dialogs (port of <c>TextureDialogSummary.qml</c>):
/// the texture name, or "N textures selected" for a multi-selection.
/// </summary>
public partial class TextureDialogSummary : UserControl
{
    private TextureViewerViewModel? viewModel;

    public TextureDialogSummary()
    {
        InitializeComponent();
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
        if (e.PropertyName is nameof(TextureViewerViewModel.SelectedCount) or nameof(TextureViewerViewModel.SelectedName)
            or nameof(TextureViewerViewModel.SelectedDimensions) or nameof(TextureViewerViewModel.SelectedFormat)
            or nameof(TextureViewerViewModel.MipCount))
            Refresh();
    }

    private void Refresh()
    {
        if (viewModel is null)
        {
            NameText.Text = DetailText.Text = "";
            return;
        }
        bool multiple = viewModel.SelectedCount > 1;
        NameText.Text = multiple ? $"{viewModel.SelectedCount} textures selected" : viewModel.SelectedName;
        DetailText.Text = multiple
            ? "Applies to all selected textures."
            : $"Original  {viewModel.SelectedDimensions}  ·  {viewModel.SelectedFormat}  ·  {viewModel.MipCount} mips";
    }
}
