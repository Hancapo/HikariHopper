using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;

namespace HikariHopper.Views.Controls;

/// <summary>
/// Row-level marks for entries (port of <c>FileGlyph.qml</c>).
/// </summary>
/// <remarks>
/// Lucide geometry, tinted by family. The tint is the app's type-ink ramp: it says WHAT a thing
/// is, and it is the only channel that carries type. Blue stays the container colour, and a
/// selected row overrides every tint with dark ink.
/// </remarks>
public class FileGlyph : Control
{
    /// <summary>Structural kind from the caller: folder | root | archive | package | file.</summary>
    public static readonly StyledProperty<string> KindProperty =
        AvaloniaProperty.Register<FileGlyph, string>(nameof(Kind), "file");

    /// <summary>The backend's kind string, which refines a plain file into its real type.</summary>
    public static readonly StyledProperty<string> FileKindProperty =
        AvaloniaProperty.Register<FileGlyph, string>(nameof(FileKind), "");

    public static readonly StyledProperty<bool> SelectedProperty =
        AvaloniaProperty.Register<FileGlyph, bool>(nameof(Selected));

    // Backend kind -> (Lucide name, family)
    private static readonly Dictionary<string, (string Icon, string Family)> ByFileKind = new()
    {
        ["RPF Package"] = ("package", "container"),
        ["Application"] = ("app-window", "code"),
        ["Application Extension"] = ("puzzle", "code"),
        ["ASI Plugin"] = ("plug", "code"),
        ["Audio Container"] = ("audio-waveform", "asset"),
        ["Texture Dictionary"] = ("book-image", "asset"),
        ["Drawable"] = ("box", "asset"),
        ["Drawable Dictionary"] = ("boxes", "asset"),
        ["Fragment"] = ("zap", "asset"),
        ["Map Data"] = ("map", "asset"),
        ["Collision Bounds"] = ("triangle-dashed", "asset"),
        ["Archetype Definitions"] = ("library", "asset"),
        ["Map Manifest"] = ("list-tree", "data"),
        ["Node Dictionary"] = ("route", "asset"),
        ["Navigation Mesh"] = ("grid-3x3", "asset"),
        ["Clip Dictionary"] = ("clapperboard", "asset"),
        ["Expression Dictionary"] = ("scan-face", "asset"),
        ["Particle Dictionary"] = ("sparkles", "asset"),
        ["Audio Metadata"] = ("database", "data"),
        ["Text Table"] = ("languages", "data"),
        ["Metadata Container"] = ("file-cog", "data"),
        ["Texture Parent Data"] = ("file-stack", "data"),
        ["Height Map"] = ("mountain", "asset"),
        ["Water Data"] = ("droplets", "asset"),
        ["Cutscene"] = ("film", "asset"),
        ["Vehicle Definitions"] = ("car-front", "data"),
        ["Vehicle Handling"] = ("wrench", "data"),
        ["Vehicle Colors"] = ("palette", "data"),
        ["Vehicle Mod Colors"] = ("palette", "data"),
        ["Vehicle Variations"] = ("shuffle", "data"),
        ["Vehicle Layouts"] = ("layout-template", "data"),
        ["Ped Definitions"] = ("users", "data"),
        ["Metadata"] = ("file-cog", "data"),
        ["XML Document"] = ("file-code", "data"),
        ["JSON Document"] = ("file-code", "data"),
        ["Text Document"] = ("file-text", "data"),
        ["DDS Texture"] = ("image", "asset"),
        ["Data File"] = ("file", "data"),
        ["File"] = ("file", "data"),
    };

    static FileGlyph()
    {
        AffectsRender<FileGlyph>(KindProperty, FileKindProperty, SelectedProperty);
        AutomationProperties.AccessibilityViewProperty.OverrideDefaultValue<FileGlyph>(AccessibilityView.Raw);
    }

    public string Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public string FileKind
    {
        get => GetValue(FileKindProperty);
        set => SetValue(FileKindProperty, value);
    }

    public bool Selected
    {
        get => GetValue(SelectedProperty);
        set => SetValue(SelectedProperty, value);
    }

    public bool IsFolder => Kind is "folder" or "root";

    public bool IsPackage => Kind is "archive" or "package";

    /// <summary>The Lucide icon name and family (folder | container | code | asset | data).</summary>
    public (string Icon, string Family) Resolved => Resolve(Kind, FileKind);

    /// <summary>The icon tint for the current kind and selection.</summary>
    public IBrush Tint => TintFor(Resolved.Family, Selected);

    public static (string Icon, string Family) Resolve(string kind, string fileKind)
    {
        if (kind is "folder" or "root")
            return ("folder", "folder");
        if (kind is "archive" or "package")
            return ("package", "container");
        if (ByFileKind.TryGetValue(fileKind, out var hit))
            return hit;
        // PNG Image, JPEG Image and the rest.
        return fileKind.EndsWith(" Image", StringComparison.Ordinal) ? ("image", "asset") : ("file", "data");
    }

    public static IBrush TintFor(string family, bool selected)
    {
        if (selected)
            return Ui.Theme.SelectionText;
        return family switch
        {
            "container" => Ui.Theme.Accent,
            "code" => Ui.Theme.InkCode,
            "asset" => Ui.Theme.InkAsset,
            "data" => Ui.Theme.InkData,
            _ => Ui.Theme.TextDim,
        };
    }

    /// <summary>The QML implicit size: 16x16.</summary>
    protected override Size MeasureOverride(Size availableSize) => new(16, 16);

    public override void Render(DrawingContext context) =>
        LucideIcon.DrawIcon(context, Resolved.Icon, Tint, 1.8, Math.Min(Bounds.Width, Bounds.Height));
}
