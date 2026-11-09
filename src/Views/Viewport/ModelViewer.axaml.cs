using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using HikariHopper.Core;
using HikariHopper.Rendering;
using HikariHopper.ViewModels;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>
/// A 3D file shown over the main window like the image viewer: a collision's shapes in their materials' colours, or a
/// navmesh's polygons, where they are in the world, over a grid. A left drag orbits, the wheel zooms, and Escape closes.
/// </summary>
public partial class ModelViewer : UserControl, IModalContent
{
    private ModalOverlay? overlay;

    /// <summary>An empty viewer, for the XAML loader and previews.</summary>
    public ModelViewer() : this(new ModelFile("", []))
    {
    }

    public ModelViewer(ModelFile file)
    {
        InitializeComponent();
        TitleText.Text = file.Name;
        TitleGlyph.FileKind = RpfProvider.FileKind(file.Name);
        CloseMark.Click += (_, _) => Close();
        StatusText.Text = file.Data.Length > 0 ? $"Reading {file.Name}…" : "Starting the GPU…";
        // Read in the background while the window opens and the GPU starts, then shown.
        Reading = Load(file);
        Viewport.Started += adapter =>
        {
            started = true;
            if (Reading.IsCompleted)
                StatusText.Text = Location;
            AdapterText.Text = adapter;
        };
        Viewport.Failed += error =>
        {
            StatusText.Text = $"The 3D view couldn't start: {error.Message}";
            StatusText.Foreground = Ui.Theme.Error;
        };
        Viewport.EscapePressed += Close;
    }

    private bool started;

    /// <summary>What the file holds, where and how big, or why it can't be read: the status bar's line, once read.</summary>
    internal string Location { get; private set; } = "";

    /// <summary>Done once the file is read and shown, or found unreadable.</summary>
    internal Task Reading { get; }

    private async Task Load(ModelFile file)
    {
        if (file.Data.Length == 0)
            return;
        (ModelContent? content, string? failure) = await Task.Run(() => Read(file));
        Location = content is not null ? Locate(content) : failure!;
        if (started)
            StatusText.Text = Location;
    }

    private static (ModelContent? Content, string? Failure) Read(ModelFile file)
    {
        try
        {
            return (ModelContent.Read(file.Name, file.Data, file.Dictionaries, file.Nearby), null);
        }
        catch (Exception error) when (error is InvalidDataException or EndOfStreamException or ArgumentException
            or NotSupportedException or OverflowException)
        {
            return (null, $"Couldn't read {file.Name}: {error.Message}");
        }
    }

    /// <summary>Shows what was read framed in the viewport, and says what and where it is.</summary>
    private string Locate(ModelContent content)
    {
        if (content.Box.IsEmpty)
            return "Nothing in it";
        Viewport.Frame(content.Box);
        System.Numerics.Vector3 size = content.Box.Size, center = content.Box.Center;
        if (content.Surfaces is { } surfaces)
        {
            Viewport.Show(surfaces);
            ShowLegend([]);
            // What isn't shown as the game shows it, under the status line.
            ToolTip.SetTip(StatusText, surfaces.Diagnostics.Count > 0 ? string.Join("\n", surfaces.Diagnostics) : null);
            return Describe(surfaces, size);
        }
        Viewport.Show(content.Mesh);
        ShowLegend(content.Mesh.Legend);
        int triangles = content.Mesh.TriangleCount, skipped = content.Mesh.Skipped.Values.Sum();
        return string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{triangles:N0} {(triangles == 1 ? "triangle" : "triangles")}{(skipped > 0 ? $" ({skipped:N0} shapes not drawn)" : "")} · "
            + $"{size.X:0.#} × {size.Y:0.#} × {size.Z:0.#} m at {center.X:0.#}, {center.Y:0.#}, {center.Z:0.#}");
    }

    /// <summary>
    /// A drawable's line: its triangles, materials and size, and the textures it names that weren't found; the status
    /// line's tooltip lists those and the rest of <see cref="DrawableScene.Diagnostics"/>.
    /// </summary>
    private static string Describe(DrawableScene scene, System.Numerics.Vector3 size)
    {
        int triangles = scene.TriangleCount, materials = scene.Materials.Count;
        int missing = scene.Materials.SelectMany(material => material.MissingTextures).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        string found = missing > 0 ? $" · {missing} {(missing == 1 ? "texture" : "textures")} not found" : "";
        return string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{triangles:N0} {(triangles == 1 ? "triangle" : "triangles")} · {materials} {(materials == 1 ? "material" : "materials")} · {size.X:0.##} × {size.Y:0.##} × {size.Z:0.##} m{found}");
    }

    /// <summary>The most entries the legend lists; the rest are counted.</summary>
    private const int LegendLength = 14;

    /// <summary>The legend's entries as shown, most used first.</summary>
    internal IReadOnlyList<LegendEntry> LegendShown { get; private set; } = [];

    private void ShowLegend(IReadOnlyList<LegendEntry> legend)
    {
        LegendEntry[] ordered = [.. legend.OrderByDescending(entry => entry.Count)];
        LegendShown = [.. ordered.Take(LegendLength)];
        LegendHidden = [.. ordered.Skip(LegendLength)];
        LegendPanel.Children.Clear();
        foreach (LegendEntry entry in LegendShown)
        {
            Control item = LegendItem(entry);
            ToolTip.SetTip(item, $"{entry.Name}: {entry.Count:N0}");
            LegendPanel.Children.Add(item);
        }
        if (LegendHidden.Count > 0)
        {
            // The rest, swatches and all, in the tooltip of their count.
            var rest = new StackPanel { Spacing = 4 };
            foreach (LegendEntry entry in LegendHidden)
                rest.Children.Add(LegendItem(entry, $"{entry.Name}: {entry.Count:N0}"));
            var more = new TextBlock { Text = $"+{LegendHidden.Count} more", Classes = { "status" }, Foreground = Ui.Theme.TextFaint };
            ToolTip.SetTip(more, rest);
            LegendPanel.Children.Add(more);
        }
        LegendBar.IsVisible = LegendShown.Count > 0;
    }

    /// <summary>The legend's entries left out of the bar, listed in the tooltip of its "+N more".</summary>
    internal IReadOnlyList<LegendEntry> LegendHidden { get; private set; } = [];

    private static Control LegendItem(LegendEntry entry, string? text = null)
    {
        var item = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
        item.Children.Add(new Border
        {
            Width = 10, Height = 10, CornerRadius = new CornerRadius(2), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Background = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(ViewportMesh.Unpack(entry.Colour)),
        });
        item.Children.Add(new TextBlock { Text = text ?? entry.Name, Classes = { "status" } });
        return item;
    }

    /// <summary>The control the viewer opens over, in the main window.</summary>
    public Control? Anchor { get; set; }

    public bool IsOpen => overlay?.IsShown == true;

    /// <summary>The viewer closed; its GPU resources are let go.</summary>
    public event EventHandler? Closed;

    public void Present()
    {
        if (IsOpen)
            return;
        Control? anchor = Anchor
            ?? ((Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainWindow)?.ExplorerScreenHost;
        if (anchor is null)
            return;
        // The viewport is a native window: it can't follow the sheet's opening animation (a scale), so it shows
        // once the sheet has settled, where it then is.
        Viewport.IsVisible = false;
        overlay = ModalOverlay.Show(anchor, this);
        if (overlay is not null)
        {
            overlay.Closed += (_, _) =>
            {
                overlay = null;
                Closed?.Invoke(this, EventArgs.Empty);
            };
            DispatcherTimer.RunOnce(() => Viewport.IsVisible = IsOpen, SettleTime);
        }
        Dispatcher.UIThread.Post(() => Focus(), DispatcherPriority.Loaded);
    }

    /// <summary>How long the sheet takes to open, after which the viewport shows.</summary>
    internal static TimeSpan SettleTime { get; set; } = TimeSpan.FromMilliseconds(200);

    public void Close()
    {
        // Nor the closing one: it goes first, so the sheet leaves as a whole.
        Viewport.IsVisible = false;
        overlay?.Close();
    }

    void IModalContent.RequestClose() => Close();
}
