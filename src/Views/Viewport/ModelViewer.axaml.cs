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
        StatusText.Text = "Starting the GPU…";
        Location = Locate(file);
        Viewport.Started += adapter =>
        {
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

    /// <summary>What the file holds, where and how big, or why it can't be read: the status bar's line.</summary>
    internal string Location { get; }

    /// <summary>Shows the file's triangles framed in the viewport, and says what and where they are; or why it can't.</summary>
    private string Locate(ModelFile file)
    {
        if (file.Data.Length == 0)
            return "";
        ModelContent content;
        try
        {
            content = ModelContent.Read(file.Name, file.Data);
        }
        catch (Exception error) when (error is InvalidDataException or EndOfStreamException or ArgumentException
            or NotSupportedException or OverflowException)
        {
            return $"Couldn't read {file.Name}: {error.Message}";
        }
        if (content.Box.IsEmpty)
            return "Nothing in it";
        Viewport.Frame(content.Box);
        Viewport.Show(content.Mesh);
        ShowLegend(content.Mesh.Legend);
        System.Numerics.Vector3 size = content.Box.Size, center = content.Box.Center;
        int triangles = content.Mesh.TriangleCount, skipped = content.Mesh.Skipped.Values.Sum();
        return string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{triangles:N0} {(triangles == 1 ? "triangle" : "triangles")}{(skipped > 0 ? $" ({skipped:N0} shapes not drawn)" : "")} · "
            + $"{size.X:0.#} × {size.Y:0.#} × {size.Z:0.#} m at {center.X:0.#}, {center.Y:0.#}, {center.Z:0.#}");
    }

    /// <summary>The most entries the legend lists; the rest are counted.</summary>
    private const int LegendLength = 14;

    /// <summary>The legend's entries as shown, most used first.</summary>
    internal IReadOnlyList<LegendEntry> LegendShown { get; private set; } = [];

    private void ShowLegend(IReadOnlyList<LegendEntry> legend)
    {
        LegendShown = [.. legend.OrderByDescending(entry => entry.Count).Take(LegendLength)];
        LegendPanel.Children.Clear();
        foreach (LegendEntry entry in LegendShown)
        {
            var item = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
            item.Children.Add(new Border
            {
                Width = 10, Height = 10, CornerRadius = new CornerRadius(2), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Background = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(ViewportMesh.Unpack(entry.Colour)),
            });
            item.Children.Add(new TextBlock { Text = entry.Name, Classes = { "status" } });
            ToolTip.SetTip(item, $"{entry.Name}: {entry.Count:N0}");
            LegendPanel.Children.Add(item);
        }
        if (legend.Count > LegendLength)
            LegendPanel.Children.Add(new TextBlock { Text = $"+{legend.Count - LegendLength} more", Classes = { "status" }, Foreground = Ui.Theme.TextFaint });
        LegendBar.IsVisible = LegendShown.Count > 0;
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
