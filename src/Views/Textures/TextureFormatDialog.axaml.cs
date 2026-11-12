using CommonFury.Textures;
using HikariHopper.Core;
using HikariHopper.Textures;
using HikariHopper.ViewModels;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>
/// Converts the selection to another storage format. It opens on the current texture's own format; each format says
/// what it costs and keeps, and the outcome what the selection takes now and would take, with a line when the format
/// picked loses something the selection has.
/// </summary>
/// <remarks>
/// Quality is how hard a block-compressed format is compressed: there is none to pick for an uncompressed one. The
/// resampling filter is the one last applied with in Resize or Mipmaps.
/// </remarks>
public partial class TextureFormatDialog : TextureToolDialog
{
    private static readonly double[] Qualities = [0.45, 0.7, 1.0];

    // Whether the selection has pixels less than opaque: read once as the dialog opens, for the warning.
    private bool usesAlpha;

    public TextureFormatDialog()
    {
        InitializeComponent();
        QualitySwitch.Sides.Add(new SwitchSide { Text = "Fast", Width = 54, Label = "Fast" });
        QualitySwitch.Sides.Add(new SwitchSide { Text = "Balanced", Width = 78, Label = "Balanced" });
        QualitySwitch.Sides.Add(new SwitchSide { Text = "High", Width = 54, Label = "High" });
        QualitySwitch.SelectedIndex = 1;
        QualitySwitch.SideRequested += (_, side) => QualitySwitch.SelectedIndex = side;
        ApplyAction = () => ViewModel?.ChangeSelectedFormat(TargetName, Quality, TextureFilterCombo.Remembered,
            MipCombo.Minimum, MipCombo.Generate) == true;
        foreach (FlatComboBox combo in new FlatComboBox[] { FormatCombo, MipCombo })
            combo.PropertyChanged += (_, e) =>
            {
                if (e.Property == FlatComboBox.CurrentValueProperty)
                    Refresh();
            };
    }

    /// <summary>The format picked, as the view model names it: <c>BC1</c>, <c>A8R8G8B8</c>.</summary>
    internal string TargetName => FormatCombo.CurrentValue as string ?? "";

    /// <summary>How hard a compressed format is compressed, 0 to 1.</summary>
    internal double Quality => Qualities[Math.Clamp(QualitySwitch.SelectedIndex, 0, Qualities.Length - 1)];

    /// <summary>What a format costs and keeps, beside its name: "4 bpp · no alpha".</summary>
    internal static string Describe(string name)
    {
        string kept = name switch
        {
            "BC1" => "no alpha",
            "BC2" => "sharp alpha",
            "BC4" => "one channel",
            "BC5" => "two channels",
            _ => "alpha",
        };
        try
        {
            TextureFormat format = TextureFormatNames.FromEditable(name, TextureFormat.Unknown);
            return TextureFormats.TryUnit(format, out int bytes, out int dimension) && dimension > 0
                ? $"{bytes * 8 / (dimension * dimension)} bpp · {kept}"
                : kept;
        }
        catch (ArgumentException)
        {
            return kept;
        }
    }

    protected override void OnOpened()
    {
        base.OnOpened();
        usesAlpha = ViewModel?.SelectionUsesAlpha() == true;
        // On the current texture's own format, by its exact name.
        string exact = ViewModel is { SelectionFacts: [var first, ..] } ? TextureFormatNames.Name(first.Format) : "";
        for (int index = 0; index < FormatCombo.ItemCount; index++)
        {
            if (FormatCombo.Items[index] is FlatComboOption { Value: string value } && value == exact)
            {
                FormatCombo.SelectedIndex = index;
                break;
            }
        }
        Refresh();
        FormatCombo.Focus();
    }

    protected override void OnViewModelChanged(string? property)
    {
        base.OnViewModelChanged(property);
        if (property is null && ViewModel is { } vm)
        {
            FormatCombo.ItemsSource = vm.FormatOptions
                .Select(option => new FlatComboOption(option.Label, option.Value, Note: Describe(option.Value))).ToArray();
            FormatCombo.SelectedIndex = 0;
        }
        Refresh();
    }

    private void Refresh()
    {
        if (Outcome is null || ViewModel is not { } vm || TargetName.Length == 0)
            return;
        IReadOnlyList<TextureFacts> facts = vm.SelectionFacts;
        string label = Formatting.FormatTextureFormat(TargetName, includeExact: true);
        bool blocks = false;
        long now = facts.Sum(each => each.DataSize), after = 0;
        foreach (TextureFacts each in facts)
        {
            TextureFormat target = TextureFormatNames.FromEditable(TargetName, each.Format);
            blocks = ResizePlan.InBlocks(target);
            long bytes = ResizePlan.EstimatedSize(each.Width, each.Height, target,
                MipCombo.Generate ? vm.EstimatedMipCount(each.Width, each.Height, MipCombo.Minimum) : 1);
            after = bytes < 0 || after < 0 ? -1 : after + bytes;
        }
        // Only a block-compressed format has a quality to pick.
        QualityLabel.IsVisible = QualitySwitch.IsVisible = blocks;
        string[] formats = [.. facts.Select(each => Formatting.FormatTextureFormat(TextureFormatNames.Name(each.Format), includeExact: true)).Distinct()];
        Outcome.Show(formats.Length == 1 ? formats[0] : $"{formats.Length} formats", now, label, after, Warnings(label));
        FitBody();
    }

    // What the format picked loses of what the selection has.
    private IReadOnlyList<string> Warnings(string label) => TargetName switch
    {
        "BC1" when usesAlpha => [$"{label} has no alpha here, and {(ViewModel is { SelectedCount: > 1 } ? "the selection" : "this texture")} uses it."],
        "BC4" => [$"{label} keeps the red channel only."],
        "BC5" => [$"{label} keeps red and green only, as a normal map needs."],
        _ => [],
    };
}
