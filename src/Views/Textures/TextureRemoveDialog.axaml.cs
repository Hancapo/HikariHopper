using HikariHopper.ViewModels;

namespace HikariHopper.Views;

/// <summary>
/// Confirms removing the selected textures: which ones, by name, and what the dictionary holds now and would hold.
/// </summary>
public partial class TextureRemoveDialog : TextureToolDialog
{
    public TextureRemoveDialog()
    {
        InitializeComponent();
        ApplyAction = () => ViewModel?.RemoveSelected() == true;
        Refresh();
    }

    protected override void OnOpened()
    {
        base.OnOpened();
        Refresh();
    }

    protected override void OnViewModelChanged(string? property)
    {
        base.OnViewModelChanged(property);
        Refresh();
    }

    /// <summary>A count of textures in words: "1 texture", "9 textures".</summary>
    internal static string Textures(int count) => count == 1 ? "1 texture" : $"{count} textures";

    private void Refresh()
    {
        if (NamesText is null || Outcome is null)
            return;
        int count = ViewModel?.SelectedCount ?? 0;
        Heading = count > 1 ? $"REMOVE {count} TEXTURES" : "REMOVE TEXTURE";
        ApplyEnabled = ViewModel?.CanRemoveSelection ?? false;
        if (ViewModel is not { } vm)
            return;
        IReadOnlyList<TextureFacts> facts = vm.SelectionFacts;
        NamesText.Text = TextureOutcome.Names([.. facts.Select(each => each.Name)], most: 6);
        long all = vm.DictionaryDataSize;
        Outcome.Show(Textures(vm.TextureCount), all, Textures(vm.TextureCount - facts.Count), all - facts.Sum(each => each.DataSize));
        FitBody();
    }
}
