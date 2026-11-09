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
        };
        Viewport.Failed += error =>
        {
            StatusText.Text = $"The 3D view couldn't start: {error.Message}";
            StatusText.Foreground = Ui.Theme.Error;
        };
        Viewport.EscapePressed += Close;
        SunSlider.Value = Math.Round(DayCycle.DefaultHours * 4);
        SunText.Text = TimeButton.Text = Clock(DayCycle.DefaultHours);
        SunSlider.Moved += (_, quarters) => SetHours(quarters / 4f);
        timeBody = TimeBody();
        TimeButton.Click += (_, _) => ToggleTime();
        ThemedToolTip.Set(TimeButton, "Time of day");
        ShadowsButton.Click += (_, _) => SetShadows(!Viewport.Shadows);
        PartsArrow.Click += (_, _) => SetPartsShown(!PartsShown);
        PartsHandle.PointerPressed += OnHandlePressed;
        PartsHandle.PointerMoved += OnHandleMoved;
        PartsHandle.PointerReleased += (_, e) => EndResize(e.Pointer);
        PartsHandle.PointerCaptureLost += (_, _) => EndResize(null);
        Parts.Width = partsWidth;
        LodSwitch.SideRequested += (_, index) =>
        {
            if (visibility is { } shown && index >= 0 && index < shown.Levels.Count)
                SetLevel(shown.Levels[index]);
        };
        TexturesButton.Click += (_, _) => OpenTextures();
        DamagedButton.Click += (_, _) => SetDamaged(visibility?.Damaged != true);
        this.file = file;
        Parts.Changed += ShowParts;
        FrameButton.Click += (_, _) => Reframe();
        SkeletonButton.Click += (_, _) => SetOverlay(Overlay.Skeleton, !skeletonShown);
        BoundsButton.Click += (_, _) => SetOverlay(Overlay.Bounds, !boundsShown);
        SolidButton.Click += (_, _) => SetOverlay(Overlay.Solid, !solidShown);
        LightsButton.Click += (_, _) => SetOverlay(Overlay.Lights, !lightsShown);
        OrbitButton.Click += (_, _) => SetFreeCamera(false);
        FreeButton.Click += (_, _) => SetFreeCamera(true);
        Viewport.SpeedChanged += ShowSpeed;
        ThemedToolTip.Set(OrbitButton, "Orbit camera  (V)");
        ThemedToolTip.Set(FreeButton, "Free camera  (V)\nW A S D to move, Q and E down and up, Shift faster\nDrag to look, wheel for speed");
        ThemedToolTip.Set(SpeedStat, "Free camera speed");
        ThemedToolTip.Set(ShadowsButton, "Shadows  (H)");
        ThemedToolTip.Set(FrameButton, "Frame the model, orbiting it  (F)");
        ThemedToolTip.Set(TrianglesStat, "Triangles");
        ThemedToolTip.Set(VerticesStat, "Vertices");
        ThemedToolTip.Set(EdgesStat, "Edges");
        RefreshToggles();
        Viewport.ShortcutPressed += key => Shortcut(key);
    }

    /// <summary>Whether the sun casts shadows on the model and the ground.</summary>
    internal void SetShadows(bool on)
    {
        Viewport.Shadows = on;
        RefreshToggles();
    }

    /// <summary>A toggle that is on sits raised in its well in full ink; one that is off is flat and dimmer.</summary>
    private void RefreshToggles()
    {
        // One that's off (nothing to show) keeps the button's own faint ink.
        static void Set(ChromeToolButton button, bool on)
        {
            button.Sunken = on && button.IsEnabled;
            if (button.IsEnabled)
                button.Foreground = on ? Ui.Theme.Text : Ui.Theme.TextDim;
            else
                button.ClearValue(ForegroundProperty);
        }
        Set(ShadowsButton, Viewport.Shadows);
        Set(DamagedButton, visibility?.Damaged == true);
        Set(SkeletonButton, skeletonShown);
        Set(BoundsButton, boundsShown);
        Set(SolidButton, solidShown);
        Set(LightsButton, lightsShown);
        Set(OrbitButton, !Viewport.FreeCamera);
        Set(FreeButton, Viewport.FreeCamera);
    }

    /// <summary>What can be drawn over the model.</summary>
    internal enum Overlay { Skeleton, Bounds, Lights, Solid }

    private bool skeletonShown, boundsShown, lightsShown, solidShown;
    private DrawableOverlays overlays = DrawableOverlays.None;

    /// <summary>Whether an overlay is drawn over the model.</summary>
    internal bool OverlayShown(Overlay overlay) => overlay switch
    {
        Overlay.Skeleton => skeletonShown,
        Overlay.Bounds => boundsShown,
        Overlay.Solid => solidShown,
        _ => lightsShown,
    };

    /// <summary>Draws the model's skeleton, bounds or lights over it, or stops; one it doesn't have stays off.</summary>
    internal bool SetOverlay(Overlay overlay, bool on)
    {
        // The solid collision is the bounds' triangles, drawn filled over the model.
        if (overlay == Overlay.Solid)
        {
            if (overlays.Solid.Length == 0)
                return false;
            solidShown = on;
            Viewport.ShowSolid(on ? overlays.Solid : []);
            RefreshToggles();
            return true;
        }
        ViewportSegment[] segments = Segments(overlay);
        if (segments.Length == 0)
            return false;
        switch (overlay)
        {
            case Overlay.Skeleton:
                skeletonShown = on;
                break;
            case Overlay.Bounds:
                boundsShown = on;
                break;
            default:
                lightsShown = on;
                break;
        }
        Viewport.ShowOverlays([.. (skeletonShown ? overlays.Skeleton : []), .. (boundsShown ? overlays.Bounds : []), .. (lightsShown ? overlays.Lights : [])]);
        RefreshToggles();
        return true;
    }

    private ViewportSegment[] Segments(Overlay overlay) => overlay switch
    {
        Overlay.Skeleton => overlays.Skeleton,
        Overlay.Bounds => overlays.Bounds,
        _ => overlays.Lights,
    };

    /// <summary>The overlays the model has, each button off when it has none.</summary>
    private void ShowOverlayButtons(DrawableScene scene)
    {
        overlays = scene.Overlays;
        void Offer(ChromeToolButton button, Overlay overlay, string name, char key)
        {
            bool has = Segments(overlay).Length > 0;
            button.IsEnabled = has;
            ThemedToolTip.Set(button, has ? $"{name}  ({key})" : $"No {name.ToLowerInvariant()}");
        }
        Offer(SkeletonButton, Overlay.Skeleton, "Skeleton", 'B');
        Offer(BoundsButton, Overlay.Bounds, "Bounds", 'C');
        SolidButton.IsEnabled = overlays.Solid.Length > 0;
        ThemedToolTip.Set(SolidButton, SolidButton.IsEnabled ? "Solid collision  (K)" : "No collision");
        Offer(LightsButton, Overlay.Lights, "Lights", 'L');
    }

    /// <summary>
    /// Orbits the model or flies: flying, the viewport takes the keyboard for W, A, S, D, Q and E, and the status bar
    /// says how fast it goes.
    /// </summary>
    internal void SetFreeCamera(bool on)
    {
        Viewport.FreeCamera = on;
        SpeedStat.IsVisible = on;
        if (on)
        {
            ShowSpeed(Viewport.FreeSpeed);
            Viewport.FocusViewport();
        }
        RefreshToggles();
    }

    /// <summary>Points the camera back at the model, orbiting it.</summary>
    internal void Reframe()
    {
        Viewport.Reframe();
        SetFreeCamera(false);
    }

    private void ShowSpeed(float speed) =>
        SpeedText.Text = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{speed:0.#} m/s");

    private readonly ModelFile file;
    private PartVisibility? visibility;

    /// <summary>Opens the drawable's embedded textures in the texture viewer, over this viewer.</summary>
    internal bool OpenTextures()
    {
        if (!TexturesButton.IsEnabled || file.OpenTextures is not { } open)
            return false;
        open();
        return true;
    }

    /// <summary>Shows a fragment damaged, its damaged drawables in place of their intact ones, or intact; one with no damaged state stays intact.</summary>
    internal bool SetDamaged(bool damaged)
    {
        if (visibility is null || surfaces is not { HasDamaged: true })
            return false;
        visibility.Damaged = damaged;
        Parts.Refresh();
        ShowParts();
        RefreshToggles();
        return true;
    }

    private DrawableScene? surfaces;

    /// <summary>The textures button, off when there are none or they can't be opened; its tooltip says how many.</summary>
    private void ShowTextures(DrawableScene scene)
    {
        int count = scene.EmbeddedTextureCount;
        TexturesButton.IsEnabled = count > 0 && file.OpenTextures is not null;
        ThemedToolTip.Set(TexturesButton, count switch
        {
            0 => "No embedded textures",
            1 => "1 embedded texture  (T)",
            _ => $"{count} embedded textures  (T)",
        });
    }

    /// <summary>The drawable's parts and what of them is shown, once read.</summary>
    internal PartVisibility? Visibility => visibility;

    // How wide the parts panel is, kept for the next viewer; and how far it can be dragged.
    private static double partsWidth = 236;
    private const double MinPartsWidth = 160, MaxPartsWidth = 520;
    private readonly Avalonia.Animation.Transitions PartsSlide =
    [
        new Avalonia.Animation.DoubleTransition { Property = WidthProperty, Duration = TimeSpan.FromMilliseconds(180), Easing = new Avalonia.Animation.Easings.CubicEaseOut() },
    ];
    private bool partsShown;
    private Point? resizeFrom;
    private double resizeWidth;

    /// <summary>Whether the parts panel is open.</summary>
    internal bool PartsShown => partsShown;

    /// <summary>The parts panel's width when open.</summary>
    internal static double PartsWidth => partsWidth;

    /// <summary>Slides the parts panel open or closed; its edge's arrow points the way it would go.</summary>
    internal void SetPartsShown(bool shown)
    {
        partsShown = shown && visibility is not null;
        PartsHandle.IsVisible = visibility is not null;
        PartsSide.Transitions ??= PartsSlide;
        PartsSide.Width = partsShown ? partsWidth : 0;
        PartsArrow.IconKind = partsShown ? "back" : "forward";
        ThemedToolTip.Set(PartsArrow, partsShown ? "Hide the parts  (P)" : "Show the parts  (P)");
        RefreshToggles();
    }

    /// <summary>Sets the open parts panel's width, as dragging its edge does, within its limits.</summary>
    internal void ResizeParts(double width)
    {
        partsWidth = Math.Clamp(width, MinPartsWidth, MaxPartsWidth);
        Parts.Width = partsWidth;
        if (partsShown)
            PartsSide.Width = partsWidth;
    }

    // A drag on the edge, off its arrow, resizes the open panel; it follows the pointer, without the slide.
    private void OnHandlePressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (!partsShown || !e.GetCurrentPoint(PartsHandle).Properties.IsLeftButtonPressed)
            return;
        resizeFrom = e.GetPosition(this);
        resizeWidth = partsWidth;
        PartsSide.Transitions = null;
        e.Pointer.Capture(PartsHandle);
        e.Handled = true;
    }

    private void OnHandleMoved(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (resizeFrom is { } from)
            ResizeParts(resizeWidth + e.GetPosition(this).X - from.X);
    }

    private void EndResize(Avalonia.Input.IPointer? pointer)
    {
        if (resizeFrom is null)
            return;
        resizeFrom = null;
        pointer?.Capture(null);
        PartsSide.Transitions = PartsSlide;
    }

    /// <summary>Shows a level of detail the drawable has.</summary>
    internal void SetLevel(int lod)
    {
        if (visibility is null || !visibility.Levels.Contains(lod))
            return;
        visibility.Lod = lod;
        LodSwitch.SelectedIndex = IndexOf(visibility.Levels, lod);
        Parts.Refresh();
        ShowParts();
    }

    /// <summary>The levels the drawable has as segments, the one shown raised; none when it has only the one.</summary>
    private void ShowLevels()
    {
        LodSwitch.Sides.Clear();
        IReadOnlyList<int> levels = visibility?.Levels ?? [];
        foreach (int lod in levels)
        {
            string name = PartVisibility.LevelName(lod);
            string shortName = PartVisibility.ShortLevelName(lod);
            LodSwitch.Sides.Add(new SwitchSide { Text = shortName, Width = 14 + 7 * shortName.Length, Label = name, ToolTip = $"{name} detail  ({lod + 1})" });
        }
        LodSwitch.RefreshSides();
        LodSwitch.IsVisible = LodSeparator.IsVisible = levels.Count > 1;
        LodSwitch.SelectedIndex = visibility is null ? -1 : IndexOf(levels, visibility.Lod);
    }

    private static int IndexOf(IReadOnlyList<int> levels, int lod)
    {
        for (int i = 0; i < levels.Count; i++)
            if (levels[i] == lod)
                return i;
        return -1;
    }

    /// <summary>Draws what the parts panel and the level say, and counts it.</summary>
    private void ShowParts()
    {
        if (visibility is null)
            return;
        Viewport.ShowParts(visibility.Mask());
        (int triangles, int vertices, int edges) = visibility.Stats();
        static string Count(int value) => value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        TrianglesText.Text = Count(triangles);
        VerticesText.Text = Count(vertices);
        EdgesText.Text = Count(edges);
    }

    /// <summary>
    /// The toolbar's keys, from the viewport or the viewer: P parts, 1 to 4 the level of detail, H shadows, T textures,
    /// X damaged, B skeleton, C bounds, K solid collision, L lights, V orbit or fly, F frame. None is W, A, S, D, Q or E, which the free camera moves with.
    /// </summary>
    internal bool Shortcut(char key)
    {
        if (!Toolbar.IsVisible)
            return false;
        switch (char.ToUpperInvariant(key))
        {
            case 'H':
                SetShadows(!Viewport.Shadows);
                return true;
            case 'F':
                Reframe();
                return true;
            case 'X':
                return SetDamaged(visibility?.Damaged != true);
            case 'B':
                return SetOverlay(Overlay.Skeleton, !skeletonShown);
            case 'C':
                return SetOverlay(Overlay.Bounds, !boundsShown);
            case 'K':
                return SetOverlay(Overlay.Solid, !solidShown);
            case 'L':
                return SetOverlay(Overlay.Lights, !lightsShown);
            case 'V':
                SetFreeCamera(!Viewport.FreeCamera);
                return true;
            case 'P':
                SetPartsShown(!PartsShown);
                return true;
            case 'T':
                return OpenTextures();
            case >= '1' and <= '4':
                SetLevel(key - '1');
                return true;
        }
        return false;
    }

    protected override void OnKeyDown(Avalonia.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.KeyModifiers != Avalonia.Input.KeyModifiers.None)
            return;
        char? key = e.Key switch
        {
            >= Avalonia.Input.Key.A and <= Avalonia.Input.Key.Z => (char)('A' + (e.Key - Avalonia.Input.Key.A)),
            >= Avalonia.Input.Key.D0 and <= Avalonia.Input.Key.D9 => (char)('0' + (e.Key - Avalonia.Input.Key.D0)),
            _ => null,
        };
        if (key is { } shortcut && Shortcut(shortcut))
            e.Handled = true;
    }

    /// <summary>The time of day in its dropdown: quarter hours, with marks every six hours.</summary>
    internal TextureMipSlider SunSlider { get; } = new() { Maximum = 96, TickCount = 5, Width = 212 };

    /// <summary>The time of day the slider is at, in the dropdown's corner.</summary>
    internal TextBlock SunText { get; } = new()
    {
        FontFamily = Ui.Theme.MonoFont,
        FontSize = Ui.Theme.SmallFontSize,
        Foreground = Ui.Theme.Text,
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
    };

    private readonly Control timeBody;
    private DropdownWindow? timeDropdown;
    private long timeClosedAt;

    /// <summary>Whether the time of day's dropdown is open.</summary>
    internal bool TimeOpen => timeDropdown is not null;

    private Control TimeBody()
    {
        Avalonia.Automation.AutomationProperties.SetName(SunSlider, "Time of day");
        DockPanel.SetDock(SunText, Dock.Right);
        return new StackPanel
        {
            Margin = new Thickness(12, 10, 12, 12),
            Spacing = 10,
            Children =
            {
                new DockPanel
                {
                    Children =
                    {
                        SunText,
                        new TextBlock
                        {
                            Text = "Time of day",
                            FontFamily = Ui.Theme.UiFont,
                            FontSize = Ui.Theme.SmallFontSize,
                            Foreground = Ui.Theme.TextDim,
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        },
                    },
                },
                SunSlider,
            },
        };
    }

    /// <summary>
    /// Opens the time of day's slider under its button, a window of its own so the viewport doesn't cover it, or closes
    /// it; the button sits raised while it is open.
    /// </summary>
    internal void ToggleTime()
    {
        if (timeDropdown is not null)
        {
            timeDropdown.Close();
            return;
        }
        // The press on the button that just closed it, by taking the keyboard from it, doesn't open it again.
        if (Environment.TickCount64 - timeClosedAt < 400)
            return;
        timeDropdown = DropdownWindow.OpenUnder(TimeButton, timeBody, 2);
        if (timeDropdown is null)
            return;
        TimeButton.Sunken = true;
        timeDropdown.Closed += (_, _) =>
        {
            timeDropdown = null;
            timeClosedAt = Environment.TickCount64;
            TimeButton.Sunken = false;
        };
    }

    /// <summary>Lights the model as the game does at <paramref name="hours"/>, 0 to 24.</summary>
    internal void SetHours(float hours)
    {
        Viewport.SetTime(hours);
        SunText.Text = TimeButton.Text = Clock(hours);
        SunSlider.SyncValue((int)Math.Round(hours * 4));
    }

    /// <summary>The time of day the model is lit at, in hours.</summary>
    internal float Hours => Viewport.Hours;

    private static string Clock(float hours)
    {
        int minutes = (int)Math.Round(hours * 60) % (24 * 60);
        return $"{minutes / 60:00}:{minutes % 60:00}";
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
        // The game's dictionaries for it, once the game's texture tables are ready (worked out when the game loads).
        IReadOnlyList<NearbyDictionary> game = [];
        if (file.GameDictionaries is { } pending)
        {
            if (!pending.IsCompleted)
                StatusText.Text = $"Finding the game's textures for {file.Name}…";
            try
            {
                game = await pending;
            }
            catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or ArgumentException
                or NotSupportedException or UnauthorizedAccessException)
            {
            }
            StatusText.Text = $"Reading {file.Name}…";
        }
        (ModelContent? content, string? failure) = await Task.Run(() => Read(file, game));
        Location = content is not null ? Locate(content) : failure!;
        if (started)
            StatusText.Text = Location;
    }

    private static (ModelContent? Content, string? Failure) Read(ModelFile file, IReadOnlyList<NearbyDictionary> game)
    {
        try
        {
            return (ModelContent.Read(file.Name, file.Data, file.Dictionaries, [.. game, .. file.Nearby]), null);
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
            // A drawable is lit by the game's light, and has the toolbar; collisions and navmeshes are drawn flat.
            Toolbar.IsVisible = true;
            ShowStats(surfaces);
            visibility = new PartVisibility(surfaces);
            ShowLevels();
            Parts.Parts = visibility;
            SetPartsShown(true);
            ShowTextures(surfaces);
            ShowOverlayButtons(surfaces);
            this.surfaces = surfaces;
            DamagedButton.IsEnabled = surfaces.HasDamaged;
            ThemedToolTip.Set(DamagedButton, surfaces.HasDamaged ? "Damaged  (X)" : "No damaged state");
            RefreshToggles();
            ShowLegend([]);
            // What isn't shown as the game shows it, under the status line.
            ToolTip.SetTip(StatusText, surfaces.Diagnostics.Count > 0 ? string.Join("\n", surfaces.Diagnostics) : null);
            return Describe(surfaces);
        }
        Viewport.Show(content.Mesh);
        ShowLegend(content.Mesh.Legend);
        int triangles = content.Mesh.TriangleCount, skipped = content.Mesh.Skipped.Values.Sum();
        return string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{triangles:N0} {(triangles == 1 ? "triangle" : "triangles")}{(skipped > 0 ? $" ({skipped:N0} shapes not drawn)" : "")} · "
            + $"{size.X:0.#} × {size.Y:0.#} × {size.Z:0.#} m at {center.X:0.#}, {center.Y:0.#}, {center.Z:0.#}");
    }

    /// <summary>A drawable's triangles, vertices and edges, beside the status line.</summary>
    private void ShowStats(DrawableScene scene)
    {
        static string Count(int value) => value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        TrianglesText.Text = Count(scene.TriangleCount);
        VerticesText.Text = Count(scene.VertexCount);
        EdgesText.Text = Count(scene.EdgeCount);
        StatsPanel.IsVisible = true;
    }

    /// <summary>
    /// A drawable's line, after its triangles, vertices and edges: the textures it names that weren't found, if any; the
    /// status line's tooltip lists those and the rest of <see cref="DrawableScene.Diagnostics"/>.
    /// </summary>
    private static string Describe(DrawableScene scene)
    {
        int missing = scene.Materials.SelectMany(material => material.MissingTextures).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        return missing switch
        {
            0 => "",
            1 => "1 texture not found",
            _ => $"{missing} textures not found",
        };
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
            ModalOverlay.StackChanged += OverlaysChanged;
            overlay.Closed += (_, _) =>
            {
                ModalOverlay.StackChanged -= OverlaysChanged;
                overlay = null;
                Closed?.Invoke(this, EventArgs.Empty);
            };
            DispatcherTimer.RunOnce(() => Viewport.IsVisible = IsOpen, SettleTime);
        }
        Dispatcher.UIThread.Post(() => Focus(), DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Another overlay opened over the viewer (the texture viewer, say) or closed: the viewport, a native window, would
    /// cover it, so it hides while the viewer isn't the top one, and shows once that one has gone.
    /// </summary>
    private void OverlaysChanged(object? sender, TopLevel host)
    {
        if (overlay is not { IsShown: true } own)
            return;
        if (!own.IsTopmost)
            Viewport.IsVisible = false;
        else if (!Viewport.IsVisible)
            DispatcherTimer.RunOnce(() => Viewport.IsVisible = IsOpen && overlay?.IsTopmost == true, SettleTime);
    }

    /// <summary>How long the sheet takes to open, after which the viewport shows.</summary>
    internal static TimeSpan SettleTime { get; set; } = TimeSpan.FromMilliseconds(200);

    public void Close()
    {
        timeDropdown?.Close();
        // Nor the closing one: it goes first, so the sheet leaves as a whole.
        Viewport.IsVisible = false;
        overlay?.Close();
    }

    void IModalContent.RequestClose() => Close();
}
