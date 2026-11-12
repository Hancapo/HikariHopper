using HikariHopper.Textures;
using HikariHopper.ViewModels;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>
/// Makes the mip chain of the selection again, down to a smallest level, or drops it; the outcome says how many levels
/// each texture has now and would have, and the memory they take.
/// </summary>
public partial class TextureMipmapsDialog : TextureToolDialog
{
    public TextureMipmapsDialog()
    {
        InitializeComponent();
        ApplyAction = () =>
        {
            TextureFilterCombo.Remember(FilterCombo.CurrentValue as string);
            return ViewModel?.RecalculateSelectedMipmaps((string)FilterCombo.CurrentValue!, MipCombo.Minimum, MipCombo.Generate) == true;
        };
        MipCombo.PropertyChanged += (_, e) =>
        {
            if (e.Property == FlatComboBox.CurrentValueProperty)
                Refresh();
        };
    }

    protected override void OnOpened()
    {
        base.OnOpened();
        Refresh();
        MipCombo.Focus();
    }

    protected override void OnViewModelChanged(string? property)
    {
        base.OnViewModelChanged(property);
        Refresh();
    }

    /// <summary>Counts of mips in a few words: the one they share, or the fewest to the most.</summary>
    internal static string Chains(IEnumerable<int> counts)
    {
        int[] distinct = [.. counts.Distinct().Order()];
        return distinct.Length switch
        {
            0 => "—",
            1 => TextureItem.Mips(distinct[0]),
            _ => $"{distinct[0]} to {TextureItem.Mips(distinct[^1])}",
        };
    }

    private void Refresh()
    {
        if (Outcome is null || MipCombo is null || ViewModel is not { } vm)
            return;
        IReadOnlyList<TextureFacts> facts = vm.SelectionFacts;
        int[] chains = [.. facts.Select(each => MipCombo.Generate ? vm.EstimatedMipCount(each.Width, each.Height, MipCombo.Minimum) : 1)];
        long now = facts.Sum(each => each.DataSize), after = 0;
        for (int index = 0; index < facts.Count; index++)
        {
            long bytes = ResizePlan.EstimatedSize(facts[index].Width, facts[index].Height, facts[index].Format, chains[index]);
            after = bytes < 0 || after < 0 ? -1 : after + bytes;
        }
        Outcome.Show(Chains(facts.Select(each => each.MipCount)), now, Chains(chains), after);
        FitBody();
    }
}
