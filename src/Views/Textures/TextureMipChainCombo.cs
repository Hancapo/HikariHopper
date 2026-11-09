using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>
/// What a tool does with a texture's mipmaps, in one dropdown: none, or the full chain down to a smallest level,
/// 4 × 4 by default. <see cref="FlatComboBox.CurrentValue"/> is that smallest size as an int, 0 for none.
/// </summary>
public class TextureMipChainCombo : FlatComboBox
{
    public TextureMipChainCombo()
    {
        ItemsSource = new FlatComboOption[]
        {
            new FlatComboOption("None", 0),
            new FlatComboOption("Full chain, down to 1 × 1", 1),
            new FlatComboOption("Full chain, down to 2 × 2", 2),
            new FlatComboOption("Full chain, down to 4 × 4", 4),
            new FlatComboOption("Full chain, down to 8 × 8", 8),
        };
        SelectedIndex = 3;
    }

    /// <summary>Whether a chain is made at all.</summary>
    public bool Generate => CurrentValue is int size && size > 0;

    /// <summary>The smallest level of the chain; 1 when there is none to make.</summary>
    public int Minimum => CurrentValue is int size && size > 0 ? size : 1;

    protected override Type StyleKeyOverride => typeof(FlatComboBox);
}
