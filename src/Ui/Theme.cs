using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace HikariHopper.Ui;

/// <summary>
/// Shared design tokens, ported one-to-one from the former <c>theme/Theme.qml</c> singleton.
/// Views reference these through <c>{x:Static ui:Theme.Name}</c>; never inline a literal
/// colour or metric that already has a token here.
/// </summary>
public static class Theme
{
    // Palette. The four base colours come from the reference character's
    // uniform: navy coat, blue piping, white gloves, pale green hair. Brass is
    // the fifth, taken from the cap badge and the coat buttons.
    //   #2C2D43 navy   #FFFBFF white   #4C7ECB blue   #D1F0AA green   #D0A94C brass
    // Every grey below is derived from the navy's hue; none of them is neutral.

    // Surfaces, darkest to lightest.
    public static IImmutableSolidColorBrush WindowBg { get; } = Hex("#121322");      // the base the panels float on, a step below them
    public static IImmutableSolidColorBrush RailPatternInk { get; } = Hex("#202235"); // the start page's railway pattern, just above the base
    public static IImmutableSolidColorBrush WindowChrome { get; } = Hex("#1B1C2C");  // hard seams and sunken wells
    public static IImmutableSolidColorBrush AppBg { get; } = Hex("#1E1F30");         // the list canvas
    public static IImmutableSolidColorBrush PanelBg { get; } = Hex("#1E1F30");
    public static IImmutableSolidColorBrush TabActive { get; } = Hex("#1E1F30");     // active tab merges into the canvas
    public static IImmutableSolidColorBrush TabStripBg { get; } = Hex("#121322");    // the strip the tabs hang from: the base, so tabs stand out
    public static IImmutableSolidColorBrush ListHeaderBg { get; } = Hex("#2A2C44");  // column titles over the list canvas, a step above it
    public static IImmutableSolidColorBrush ChromeBg { get; } = Hex("#2C2D43");      // menu, toolbars, headers, status
    public static IImmutableSolidColorBrush SidebarBg { get; } = Hex("#2C2D43");
    public static IImmutableSolidColorBrush NavigationBg { get; } = Hex("#2C2D43");
    public static IImmutableSolidColorBrush HeaderBg { get; } = Hex("#2C2D43");
    public static IImmutableSolidColorBrush InsetBg { get; } = Hex("#161623");       // sunken fields, below the chrome and the list alike
    public static IImmutableSolidColorBrush ChromeRaised { get; } = Hex("#33354C");  // menus, inactive tabs, raised buttons
    public static IImmutableSolidColorBrush TabBg { get; } = Hex("#33354C");
    public static IImmutableSolidColorBrush HoverBg { get; } = Hex("#2F3048");       // hover inside the list
    public static IImmutableSolidColorBrush HoverChrome { get; } = Hex("#3A3C55");   // hover on chrome buttons

    // Lines. BorderHard is the dark seam every panel sits behind, Border is the
    // visible rule, Guide is the dotted tree guide.
    public static IImmutableSolidColorBrush BorderHard { get; } = Hex("#1B1C2C");
    public static IImmutableSolidColorBrush Border { get; } = Hex("#4D4F6B");
    public static IImmutableSolidColorBrush BorderSoft { get; } = Hex("#33354C");
    public static IImmutableSolidColorBrush Guide { get; } = Hex("#4A4C68");
    public static IImmutableSolidColorBrush Bevel { get; } = Rgba(1, 0.984, 1, 0.07);  // 1px highlight on a top edge

    // Ink.
    public static IImmutableSolidColorBrush Text { get; } = Hex("#FFFBFF");
    public static IImmutableSolidColorBrush TextRow { get; } = Hex("#C6C8DA");       // unselected row text
    public static IImmutableSolidColorBrush TextDim { get; } = Hex("#A8AABF");       // 4.5:1 on every surface, hovered buttons included
    public static IImmutableSolidColorBrush TextFaint { get; } = Hex("#9698AF");     // 4.5:1 on the list, fields, chrome and hovered rows

    // Blue: what a package is, plus the piping on the window frame and the
    // active tab. Deliberately never the selection.
    public static IImmutableSolidColorBrush Accent { get; } = Hex("#5E8BD0");     // 3:1 as a mark on raised and hovered chrome
    public static IImmutableSolidColorBrush BorderAccent { get; } = Hex("#5E8BD0");
    public static IImmutableSolidColorBrush AccentWash { get; } = Rgba(0.369, 0.545, 0.816, 0.30);
    public static IImmutableSolidColorBrush AccentMuted { get; } = Rgba(0.369, 0.545, 0.816, 0.55);

    // Green: what is selected, and the action on it.
    public static IImmutableSolidColorBrush Selection { get; } = Hex("#D1F0AA");
    public static IImmutableSolidColorBrush SelectionText { get; } = Hex("#1B1C2C");
    public static IImmutableSolidColorBrush SelectionInk { get; } = Rgba(0.106, 0.110, 0.173, 0.74);
    public static IImmutableSolidColorBrush SelectionFaint { get; } = Rgba(0.106, 0.110, 0.173, 0.66);
    public static IImmutableSolidColorBrush SelectionWash { get; } = Rgba(0.819, 0.941, 0.667, 0.13);  // unfocused pane
    public static IImmutableSolidColorBrush SelectionRing { get; } = Rgba(0.819, 0.941, 0.667, 0.55);
    public static IImmutableSolidColorBrush SelectionHalo { get; } = Rgba(0.819, 0.941, 0.667, 0.16);  // the status lamp's halo

    // Neutral marquee: it is the selection instrument, not selected state.
    // Green appears only on rows that the marquee has actually included.
    // Modals: the dim over the blurred window behind them.
    public static IImmutableSolidColorBrush ModalDim { get; } = Rgba(0.090, 0.094, 0.165, 0.38);
    // The loading overlay: a deeper dim, so the train and its caption stand clear of what is behind.
    public static IImmutableSolidColorBrush LoadingDim { get; } = Rgba(0.090, 0.094, 0.165, 0.78);

    public static IImmutableSolidColorBrush MarqueeFill { get; } = Rgba(0.173, 0.176, 0.263, 0.24);
    public static IImmutableSolidColorBrush MarqueeBorder { get; } = Rgba(0.776, 0.784, 0.855, 0.82);
    public static IImmutableSolidColorBrush MarqueeHatch { get; } = Rgba(0.604, 0.612, 0.706, 0.42);

    // Type inks — the identity of a FILE TYPE, carried on the icon only.
    //
    // A separate channel from the state colours above: row fills and text keep
    // their meanings, and an icon's tint says what a thing is. Blue already set
    // that precedent by marking packages, so these extend it rather than
    // contradict it.
    //
    // Every one sits at least 75 degrees from the accent blue in hue, because
    // blue is the only state colour ever painted on an icon and it must stay
    // unmistakable. All clear 4.8:1 on both the list canvas and the sidebar.
    public static IImmutableSolidColorBrush InkCode { get; } = Hex("#C98C79");   // exe, dll, asi   — hue 14
    public static IImmutableSolidColorBrush InkAsset { get; } = Hex("#8FB29B");  // textures, models, audio, maps — hue 141
    public static IImmutableSolidColorBrush InkData { get; } = Hex("#B88FB6");   // metadata, xml, text — hue 303

    // Brass: the app emblem, and search matches.
    public static IImmutableSolidColorBrush Brass { get; } = Hex("#D0A94C");

    // Kept for archive-parsing failures surfaced in the status bar. Outside the
    // character palette on purpose — nothing else in the UI should be red.
    public static IImmutableSolidColorBrush Error { get; } = Hex("#DE8481");
    public static IImmutableSolidColorBrush Warning { get; } = Hex("#E0A458");   // something to fix, not a failure
    public static IImmutableSolidColorBrush Ok { get; } = Hex("#A8D58A");        // a check that passed

    public static FontFamily UiFont { get; } = new("avares://HikariHopper/Assets/Fonts#Archivo");
    public static FontFamily MonoFont { get; } = new("avares://HikariHopper/Assets/Fonts#Space Mono");

    // Corner radii, largest to smallest. The window and its floating panels carry the largest;
    // anything that sits inside a well steps down so the inner curve stays parallel to the outer.
    public const double WindowRadius = 7;      // Windows 11 rounds by 8
    public const double PanelRadius = 6;       // the folder and entry panels
    public const double TabRadius = 6;         // active tab top corners, its concave foot, menus
    public const double ControlRadius = 5;     // fields, combo boxes, segmented groups
    public const double ItemRadius = 4;        // rows, menu titles, tabs on hover, buttons
    public const double InnerRadius = 4;       // a button inside a group or a field, menu highlights
    public const double KeyCapRadius = 3;      // key caps, check boxes, scrollbar thumbs
    public static readonly CornerRadius WindowCorner = new(WindowRadius);
    public static readonly CornerRadius PanelCorner = new(PanelRadius);
    public static readonly CornerRadius TabCorner = new(TabRadius);
    public static readonly CornerRadius ControlCorner = new(ControlRadius);
    public static readonly CornerRadius ItemCorner = new(ItemRadius);
    public static readonly CornerRadius InnerCorner = new(InnerRadius);
    public static readonly CornerRadius KeyCapCorner = new(KeyCapRadius);
    public static readonly Thickness MenuInset = new(MenuPadding);

    public const double WindowTitleHeight = 30;
    public const double MainTitleHeight = 38;  // the explorer's title row, which carries the menus
    public const double MainCaptionWidth = 46;
    public const double MenuHeight = 26;
    public const double MenuItemHeight = 26;   // the list's row rhythm
    public const double MenuSeparatorHeight = 9;
    public const double MenuSectionHeight = 24;
    public const double MenuPadding = 4;       // the frame's inset around its rows
    public const double MenuMinimumWidth = 230;
    public const double MenuWidth = MenuMinimumWidth + 2 * MenuPadding;

    /// <summary>A menu's soft shadow: a wide falloff and a tight contact shade, both cast downwards.</summary>
    public static readonly BoxShadows MenuShadow = BoxShadows.Parse("0 6 16 0 #8C080812, 0 1 4 0 #66080812");

    /// <summary>The transparent room a menu leaves around itself for its shadow; none above, where it meets its title.</summary>
    public static readonly Thickness MenuShadowMargin = new(16, 0, 16, 24);

    /// <summary>A modal card: rounder than a panel, and lifted well clear of the blurred window.</summary>
    public const double SheetRadius = 10;
    public static readonly CornerRadius SheetCorner = new(SheetRadius);
    public const double SheetInnerRadius = SheetRadius - 1;
    public static readonly CornerRadius SheetInnerCorner = new(SheetInnerRadius);
    // Spread pulled in, so the dense part of the shadow stays under the card and only its soft falloff
    // shows past the rounded corners.
    public static readonly BoxShadows SheetShadow = BoxShadows.Parse("0 16 44 -14 #80080812, 0 2 6 -2 #40080812");
    public const double SheetHeaderHeight = 40;

    /// <summary>A dialog's footer, with its buttons: the sheets' and the confirmation and tool dialogs'.</summary>
    public const double DialogFooterHeight = 48;

    /// <summary>Small-caps section headings and field labels (mono, bold, spaced 1.2).</summary>
    public const double CapsFontSize = 9;
    public const double TabStripHeight = 36;
    public const double TabHeight = 32;        // every tab; the active one merges into the toolbar below
    public const double TabWidth = 220;
    public const double NavigationHeight = 46;
    public const double ToolbarControlHeight = 30;
    public const double PanelGutter = 8;
    public const double HeaderHeight = 24;
    public const double ListHeaderHeight = 30;
    public const double ListInset = 6;         // rows sit this far inside the panel edge
    public const double StatusHeight = 24;
    public const double ExplorerStatusHeight = 32;
    public const double StatusHintInset = 10;
    public const double SplitHandleHitWidth = 8;
    public const double GridCellWidth = 148;
    public const double GridCellHeight = 112;
    public const double GridCellInset = 3;
    public const double GridContentInset = 10;
    public const double GridGlyphSize = 34;
    public const double RowHeight = 26;
    public const double ScrollbarWidth = 12;
    public const double ScrollbarIdleThumbWidth = 4;
    public const double ScrollbarActiveThumbWidth = 8;
    public const double ScrollbarMinThumbLength = 28;
    public const double FontSize = 12;
    public const double SmallFontSize = 10;

    // Texture dictionary viewer metrics. Its rows are taller only because the
    // thumbnail is the primary recognition aid rather than an entry glyph.
    public const double TextureRailWidth = 340;
    public const double TextureRowHeight = 62;
    public const double TextureThumbnailSize = 50;
    public const double TextureToolbarHeight = 38;

    /// <summary>
    /// The 3D viewer's footer: the level of detail, the counts, the status line, the camera and the time. Tall enough for
    /// its controls to have 8 px on every side, as far as they sit from the window's edge, so they sit evenly in its
    /// rounded corner.
    /// </summary>
    public const double ModelFooterHeight = 40;

    /// <summary>The controls in it, as the explorer's view switch in a shorter bar.</summary>
    public const double ModelFooterControlHeight = 24;

    /// <summary>The 3D viewer's parts panel's edge, dragged to resize it.</summary>
    public const double PartsEdgeWidth = 5;

    /// <summary>How far the 3D viewer's parts arrow sits below the viewport's top.</summary>
    public const double PartsTabTop = 8;

    /// <summary>How far the 3D viewer's axes gizmo sits in from the viewport's top right corner.</summary>
    public const double GizmoInset = 6;

    /// <summary>How far the 3D viewer's overlay row sits below the viewport's top, centred across it.</summary>
    public const double ToolStripInset = 8;

    /// <summary>How far the 3D viewer's shader values card sits in from the viewport's left, clear of the parts arrow.</summary>
    public const double ShaderCardLeft = 24;
    public const double TextureMipBarHeight = 32;
    public const double TextureCheckerCellSize = 16;

    // Fixed identity-window metrics. The splash is an expressive surface, but
    // its geometry remains part of the shared design contract.
    public const double SplashWidth = 720;
    public const double SplashHeight = 400;

    /// <summary>Equivalent of QML <c>Qt.rgba</c>: channels in 0..1.</summary>
    public static Color RgbaColor(double r, double g, double b, double a) =>
        Color.FromArgb(Channel(a), Channel(r), Channel(g), Channel(b));

    /// <summary>Equivalent of QML <c>Qt.darker(color, factor)</c> (HSV value divided by factor).</summary>
    public static Color Darker(Color color, double factor = 2.0) => ScaleValue(color, 1.0 / factor);

    /// <summary>Equivalent of QML <c>Qt.lighter(color, factor)</c> (HSV value multiplied by factor).</summary>
    public static Color Lighter(Color color, double factor = 1.5) => ScaleValue(color, factor);

    /// <summary>Equivalent of QML <c>Qt.alpha(color, alpha)</c>.</summary>
    public static Color WithAlpha(Color color, double alpha) => Color.FromArgb(Channel(alpha), color.R, color.G, color.B);

    private static IImmutableSolidColorBrush Hex(string value) => new ImmutableSolidColorBrush(Color.Parse(value));

    private static IImmutableSolidColorBrush Rgba(double r, double g, double b, double a) =>
        new ImmutableSolidColorBrush(RgbaColor(r, g, b, a));

    private static byte Channel(double value) => (byte)Math.Clamp(Math.Round(value * 255.0), 0, 255);

    private static Color ScaleValue(Color color, double factor)
    {
        HsvColor hsv = color.ToHsv();
        double value = Math.Clamp(hsv.V * factor, 0, 1);
        return HsvColor.FromAhsv(hsv.A, hsv.H, hsv.S, value).ToRgb();
    }
}
