using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using HikariHopper.Core;

namespace HikariHopper.Views.Controls;

/// <summary>
/// The mark of a game's edition (<see cref="GameCatalog"/>), its numeral in a flat slab serif: GTA V's V, sand for
/// Legacy and green for Enhanced; Red Dead Redemption 2's II, in rust.
/// </summary>
public sealed class EditionMark : Control
{
    public static readonly StyledProperty<string> EditionProperty =
        AvaloniaProperty.Register<EditionMark, string>(nameof(Edition), "");

    // A 16-unit V with serifs on both arms.
    private static readonly Geometry V = Geometry.Parse(
        "M0.8 2 H7.4 V3.5 H6.3 L8.7 10.4 L11.3 3.5 H10.2 V2 H15.2 V3.5 H14.1 L9.4 14.8 H7.1 L2.2 3.5 H0.8 Z");

    // A 16-unit II: two stems under a serif bar along the top and along the foot, as wide as the V.
    private static readonly Geometry II = Geometry.Parse(
        "F1 M1.2 2 H14.8 V3.5 H11.9 V13.3 H14.8 V14.8 H1.2 V13.3 H4.1 V3.5 H1.2 Z M6.5 3.5 V13.3 H9.5 V3.5 Z");

    // The box both are fitted into, a little margin around them.
    private static readonly Rect Frame = new(0.6, 1.8, 14.8, 13.2);

    static EditionMark()
    {
        AffectsRender<EditionMark>(EditionProperty);
        AutomationProperties.AccessibilityViewProperty.OverrideDefaultValue<EditionMark>(AccessibilityView.Raw);
    }

    /// <summary>The edition's id; GTA V Legacy's look when it's none of them.</summary>
    public string Edition
    {
        get => GetValue(EditionProperty);
        set => SetValue(EditionProperty, value);
    }

    /// <summary>Whether it's RDR2's II rather than GTA V's V.</summary>
    public bool IsRdr2 => Edition == GameSettings.Rdr2Edition;

    public IBrush Fill => Edition switch
    {
        GameSettings.EnhancedEdition => Ui.Theme.Selection,
        GameSettings.Rdr2Edition => Ui.Theme.Rdr2Mark,
        _ => Ui.Theme.LegacyMark,
    };

    protected override Size MeasureOverride(Size availableSize) => new(16, 16);

    public override void Render(DrawingContext context)
    {
        double scale = Math.Min(Bounds.Width / Frame.Width, Bounds.Height / Frame.Height);
        double left = (Bounds.Width - Frame.Width * scale) / 2 - Frame.X * scale;
        double top = (Bounds.Height - Frame.Height * scale) / 2 - Frame.Y * scale;
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(left, top)))
            context.DrawGeometry(Fill, null, IsRdr2 ? II : V);
    }
}
