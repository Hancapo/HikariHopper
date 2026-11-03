using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using HikariHopper.ViewModels;

namespace HikariHopper.Views;

/// <summary>
/// The texture window's status bar (port of <c>TextureViewerStatusBar.qml</c>). The middle cell
/// shows the view model's status only while an operation runs or after an error, in the error
/// colour when there is one.
/// </summary>
public partial class TextureViewerStatusBar : UserControl
{
    private TextureViewerViewModel? viewModel;

    public TextureViewerStatusBar()
    {
        InitializeComponent();
        // statusHintInset, then the row's 14px spacing before the trailing statusHintInset spacer.
        Hints.Margin = new Thickness(Ui.Theme.StatusHintInset, 0, 14 + Ui.Theme.StatusHintInset, 0);
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
        if (e.PropertyName is nameof(TextureViewerViewModel.Status) or nameof(TextureViewerViewModel.Error)
            or nameof(TextureViewerViewModel.OperationBusy))
            Refresh();
    }

    private void Refresh()
    {
        if (viewModel is null)
        {
            StatusText.Text = "";
            return;
        }
        StatusText.Text = viewModel.OperationBusy || viewModel.Error.Length > 0 ? viewModel.Status : "";
        StatusText.Foreground = viewModel.Error.Length == 0 ? Ui.Theme.TextFaint : Ui.Theme.Error;
    }
}
