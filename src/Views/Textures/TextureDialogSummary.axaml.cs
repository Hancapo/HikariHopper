using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using HikariHopper.ViewModels;

namespace HikariHopper.Views;

/// <summary>The one thing about a selection that a tool dialog's header says, beside its name: what the tool changes.</summary>
public enum TextureFact
{
    Size,
    Format,
    Mips,
}

/// <summary>
/// The header of a tool dialog that changes something measurable: the texture's name, or the count of a
/// multi-selection, and the <see cref="Fact"/> the tool changes, as it is now.
/// </summary>
public partial class TextureDialogSummary : UserControl
{
    public static readonly StyledProperty<TextureFact> FactProperty =
        AvaloniaProperty.Register<TextureDialogSummary, TextureFact>(nameof(Fact));

    private TextureViewerViewModel? viewModel;

    public TextureDialogSummary()
    {
        InitializeComponent();
    }

    public TextureFact Fact
    {
        get => GetValue(FactProperty);
        set => SetValue(FactProperty, value);
    }

    /// <summary>The fact shown at the right.</summary>
    internal string FactShown => FactText.Text ?? "";

    /// <summary>The name shown.</summary>
    internal string NameShown => NameText.Text ?? "";

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FactProperty && NameText is not null)
            Refresh();
    }

    /// <summary>Sizes in a few words: the one they share, or the smallest to the largest.</summary>
    internal static string Sizes(IEnumerable<(int Width, int Height)> sizes)
    {
        (int Width, int Height)[] distinct = [.. sizes.Distinct().OrderBy(size => (long)size.Width * size.Height).ThenBy(size => size.Width)];
        return distinct.Length switch
        {
            0 => "—",
            1 => $"{distinct[0].Width} × {distinct[0].Height}",
            _ => $"{distinct[0].Width} × {distinct[0].Height} to {distinct[^1].Width} × {distinct[^1].Height}",
        };
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
            NameText.Text = FactText.Text = "";
            return;
        }
        bool multiple = viewModel.SelectedCount > 1;
        IReadOnlyList<TextureFacts> facts = viewModel.SelectionFacts;
        NameText.Text = multiple ? $"{viewModel.SelectedCount} textures" : viewModel.SelectedName;
        FactText.Text = Fact switch
        {
            TextureFact.Size => Sizes(facts.Select(each => (each.Width, each.Height))),
            TextureFact.Format when multiple && facts.Select(each => each.Format).Distinct().Count() > 1
                => $"{facts.Select(each => each.Format).Distinct().Count()} formats",
            TextureFact.Format => viewModel.SelectedFormat,
            _ => TextureMipmapsDialog.Chains(facts.Select(each => each.MipCount)),
        };
    }
}
