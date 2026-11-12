using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using HikariHopper.ViewModels;

namespace HikariHopper.Views;

/// <summary>The one thing about a selection that a tool dialog's header says, beside its name: what the tool changes.</summary>
public enum TextureFact
{
    /// <summary>The name over its dimensions, format and mip count, as before a dialog picks one.</summary>
    None,
    Size,
    Format,
    Mips,
}

/// <summary>
/// The selection summary at the top of the tool dialogs (port of <c>TextureDialogSummary.qml</c>):
/// the texture name, or the count of a multi-selection, and with a <see cref="Fact"/> the one thing the tool changes.
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

    /// <summary>The fact shown at the right; empty without one.</summary>
    internal string FactShown => FactText.IsVisible ? FactText.Text ?? "" : "";

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
            NameText.Text = DetailText.Text = FactText.Text = "";
            return;
        }
        bool multiple = viewModel.SelectedCount > 1;
        bool fact = Fact != TextureFact.None;
        FactText.IsVisible = fact;
        DetailText.IsVisible = !fact;
        Glyph.Width = Glyph.Height = fact ? 16 : 20;
        if (fact)
        {
            IReadOnlyList<TextureFacts> facts = viewModel.SelectionFacts;
            NameText.Text = multiple ? $"{viewModel.SelectedCount} textures" : viewModel.SelectedName;
            FactText.Text = Fact switch
            {
                TextureFact.Size => Sizes(facts.Select(each => (each.Width, each.Height))),
                TextureFact.Format when multiple && facts.Select(each => each.Format).Distinct().Count() > 1
                    => $"{facts.Select(each => each.Format).Distinct().Count()} formats",
                TextureFact.Format => viewModel.SelectedFormat,
                _ when multiple && facts.Select(each => each.MipCount).Distinct().Count() > 1
                    => $"{facts.Min(each => each.MipCount)} to {TextureItem.Mips(facts.Max(each => each.MipCount))}",
                _ => TextureItem.Mips(viewModel.MipCount),
            };
            return;
        }
        NameText.Text = multiple ? $"{viewModel.SelectedCount} textures selected" : viewModel.SelectedName;
        DetailText.Text = multiple
            ? "Applies to all selected textures."
            : $"Original  {viewModel.SelectedDimensions}  ·  {viewModel.SelectedFormat}  ·  {TextureItem.Mips(viewModel.MipCount)}";
    }
}
