using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using HikariHopper.Core;
using HikariHopper.Infrastructure;
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
    // The parts arrow and the tool strip, over the viewport.
    private readonly ViewportOverlay partsTab;
    private readonly ViewportOverlay toolStrip;
    private readonly ViewportOverlay gizmo;
    // The picked geometry's shader values, at the view's top left, and what has been changed in them.
    private readonly ViewportOverlay shaderCard;
    private readonly ShaderEdits shaderEdits = new();
    private readonly ShaderValuesCard card;

    /// <summary>An empty viewer, for the XAML loader and previews.</summary>
    public ModelViewer() : this(new ModelFile("", []))
    {
    }

    public ModelViewer(ModelFile file)
    {
        InitializeComponent();
        // Its tooltips are drawn over the viewport, which covers the app's in-window popups.
        WindowToolTip.SetIsEnabled(this, true);
        ToolsHolder.Children.Clear();
        partsTab = new ViewportOverlay(Viewport, PartsTab, top: Ui.Theme.PartsTabTop);
        // Centred across the view's top.
        toolStrip = new ViewportOverlay(Viewport, ToolStrip, left: null, top: Ui.Theme.ToolStripInset);
        gizmo = new ViewportOverlay(Viewport, Gizmo, top: Ui.Theme.GizmoInset, right: Ui.Theme.GizmoInset);
        // An axis looks at the model from that side, orbiting it: flying stops, as framing it does.
        Gizmo.AxisPicked += axis =>
        {
            SetFreeCamera(false);
            Viewport.LookFrom(axis);
        };
        Gizmo.Dragged += (dx, dy) => Viewport.Turn(dx, dy);
        Viewport.CameraTurned += () => Gizmo.Basis = Viewport.CameraBasis;
        Gizmo.Basis = Viewport.CameraBasis;
        card = new ShaderValuesCard(shaderEdits);
        WindowToolTip.SetIsEnabled(card, true);
        shaderCard = new ViewportOverlay(Viewport, card, left: Ui.Theme.ShaderCardLeft, top: Ui.Theme.ToolStripInset);
        // It points at the row it shows, and follows it as the rows scroll and the card grows.
        Parts.RowsMoved += PlaceCard;
        card.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty)
                PlaceCard();
        };
        card.TypingStarted += shaderCard.TakeKeyboard;
        card.TypingEnded += shaderCard.ReturnKeyboard;
        shaderEdits.Changed += ShowEdited;
        shaderEdits.StructureChanged += Rebuild;
        card.SplitRequested = SplitPicked;
        // Closed, the card goes; the pick stays.
        card.CloseRequested += CloseShaderValues;
        // The parts panel's menu offers the picked geometry's shader values, for a drawable.
        Parts.ShaderValues = () => surfaces is null ? null : PickedMaterial() is not null;
        Parts.ShaderValuesRequested += () => OpenShaderValues();
        shaderEdits.Saved += RefreshTitle;
        Viewport.CommandPressed += (key, shift) => Command(key, shift);
        Viewport.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty)
                card.Room = Viewport.Bounds.Height - 2 * Ui.Theme.ToolStripInset;
        };
        WindowToolTip.SetIsEnabled(PartsTab, true);
        WindowToolTip.SetIsEnabled(ToolStrip, true);
        documentName = file.Name;
        saveTarget = file.SaveTarget;
        TitleText.Text = file.Name;
        Menus = BuildMenus();
        MenuHolder.Children.Add(Menus);
        TitleGlyph.FileKind = RpfProvider.FileKind(file.Name);
        CloseMark.Click += (_, _) => Close();
        StatusText.Text = file.Data.Length > 0 ? $"Reading {file.Name}…" : "Starting the GPU…";
        // Read in the background while the window opens and the GPU starts, then shown.
        Reading = Load(file);
        // The sun and moon move along the game's own paths, once its time.xml is read.
        SkyRead = file.GameSky is { } sky ? ShowSky(sky) : Task.CompletedTask;
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
        Viewport.EscapePressed += Escape;
        TimeButton.Text = TimeDial.Clock(DayCycle.DefaultHours);
        Dial.Moved += SetHours;
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
        LodWell.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(LodWell).Properties.IsLeftButtonPressed)
            {
                ToggleLevels();
                e.Handled = true;
            }
        };
        PaintWell.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(PaintWell).Properties.IsLeftButtonPressed)
            {
                TogglePaint();
                e.Handled = true;
            }
        };
        PaintWell.PointerEntered += (_, _) => PaintPaintWell();
        PaintWell.PointerExited += (_, _) => PaintPaintWell();
        PaintWell.KeyDown += (_, e) =>
        {
            if (e.Key is Avalonia.Input.Key.Enter or Avalonia.Input.Key.Space)
            {
                TogglePaint();
                e.Handled = true;
            }
        };
        LodWell.PointerEntered += (_, _) => PaintLevelWell();
        LodWell.PointerExited += (_, _) => PaintLevelWell();
        LodWell.KeyDown += (_, e) =>
        {
            if (e.Key is Avalonia.Input.Key.Enter or Avalonia.Input.Key.Space)
            {
                ToggleLevels();
                e.Handled = true;
            }
        };
        StatsButton.Click += (_, _) => SetStatsShown(!statsShown);
        CameraSwitch.Sides.Add(new SwitchSide { Icon = "orbit", Label = "Orbit", ToolTip = "Orbit  (V)" });
        CameraSwitch.Sides.Add(new SwitchSide
        {
            Icon = "fly",
            Label = "Fly",
            ToolTip = "Fly  (V)\nW A S D to move, Q and E down and up, Shift faster\nDrag to look, wheel for speed",
        });
        CameraSwitch.RefreshSides();
        CameraSwitch.SideRequested += (_, index) => SetFreeCamera(index == 1);
        Avalonia.Automation.AutomationProperties.SetName(CameraSwitch, "Camera");
        this.file = file;
        Parts.Changed += ShowParts;
        Parts.SelectionChanged += ShowPicked;
        Viewport.Clicked += (from, direction) => PickAt(from, direction);
        Parts.FrameRequested += FrameParts;
        Parts.TexturesRequested += () => OpenTextures();
        SkeletonButton.Click += (_, _) => SetOverlay(Overlay.Skeleton, !skeletonShown);
        SolidButton.Click += (_, _) => SetOverlay(Overlay.Solid, !solidShown);
        LightsButton.Click += (_, _) => SetOverlay(Overlay.Lights, !lightsShown);
        Viewport.SpeedChanged += ShowSpeed;
        ThemedToolTip.Set(SpeedStat, "How fast the camera flies");
        ThemedToolTip.Set(ShadowsButton, "Shadows  (H)");
        ThemedToolTip.Set(StatsButton, "Triangles, vertices and edges drawn");
        ThemedToolTip.Set(LodWell, "Level of detail  (1 to 4)");
        PaintLevelWell();
        RefreshToggles();
        Viewport.ShortcutPressed += key => Shortcut(key);
    }

    /// <summary>The game's sky paths being read and put on the viewport; done at once outside a game.</summary>
    internal Task SkyRead { get; }

    private async Task ShowSky(Task<SkyPath> pending)
    {
        SkyPath sky;
        try
        {
            sky = await pending;
        }
        catch (Exception error) when (ViewModels.OperationErrors.IsExpected(error))
        {
            return;
        }
        if (!closed)
            Viewport.Sky = sky;
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
        Set(SkeletonButton, skeletonShown);
        Set(SolidButton, solidShown);
        Set(LightsButton, lightsShown);
        Set(StatsButton, statsShown);
        CameraSwitch.SelectedIndex = Viewport.FreeCamera ? 1 : 0;
    }

    // Whether the footer shows what is drawn counted, kept for the next viewer.
    private static bool statsShown;

    /// <summary>Whether the footer counts what is drawn: its triangles, vertices and edges.</summary>
    internal static bool StatsShown => statsShown;

    /// <summary>Shows the counts beside their button, or only the button; the next viewer opens the same way.</summary>
    internal void SetStatsShown(bool shown)
    {
        statsShown = shown;
        StatsPanel.IsVisible = shown;
        RefreshToggles();
    }

    /// <summary>What can be drawn over the model.</summary>
    internal enum Overlay { Skeleton, Lights, Solid }

    private bool skeletonShown, lightsShown, solidShown;
    private DrawableOverlays overlays = DrawableOverlays.None;

    /// <summary>Whether an overlay is drawn over the model.</summary>
    internal bool OverlayShown(Overlay overlay) => overlay switch
    {
        Overlay.Skeleton => skeletonShown,
        Overlay.Solid => solidShown,
        _ => lightsShown,
    };

    /// <summary>Draws the model's skeleton, collision or lights over it, or stops; one it doesn't have stays off.</summary>
    internal bool SetOverlay(Overlay overlay, bool on)
    {
        // The collision is its triangles in their materials' colours, with their edges, drawn over the model.
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
        if (overlay == Overlay.Skeleton)
            skeletonShown = on;
        else
            lightsShown = on;
        Viewport.ShowOverlays([.. (skeletonShown ? overlays.Skeleton : []), .. (lightsShown ? overlays.Lights : [])]);
        RefreshToggles();
        return true;
    }

    private ViewportSegment[] Segments(Overlay overlay) => overlay == Overlay.Skeleton ? overlays.Skeleton : overlays.Lights;

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
        SolidButton.IsEnabled = overlays.Solid.Length > 0;
        ThemedToolTip.Set(SolidButton, SolidButton.IsEnabled ? "Collision  (K)" : "No collision");
        Offer(LightsButton, Overlay.Lights, "Lights", 'L');
    }

    /// <summary>
    /// Orbits the model or flies: flying, the viewport takes the keyboard for W, A, S, D, Q and E, and the footer says
    /// how fast it goes, beside the switch.
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
    // A collision's or a navmesh's marks, which the parts panel lists in place of a drawable's parts.
    private MeshParts? marks;
    // What the drawable is read from: the file's bytes, until its embedded textures are saved into it; the textures
    // saved for a fragment or dictionary, which can't take them back, over its own; and the game's dictionaries, once found.
    private byte[] data = [];
    private IReadOnlyList<FiveFury.Textures.Texture>? savedTextures;
    private IReadOnlyList<NearbyDictionary> gameDictionaries = [];

    /// <summary>
    /// Opens the drawable's embedded textures in the texture viewer, over this viewer; once it closes after saving,
    /// the drawable is shown with them as saved.
    /// </summary>
    internal bool OpenTextures()
    {
        if (!texturesAvailable || file.OpenTextures is not { } open)
            return false;
        open(saved => TexturesSaved = ShowSaved(saved));
        return true;
    }

    /// <summary>Done once the drawable is shown with the textures last saved in the texture viewer.</summary>
    internal Task TexturesSaved { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// The drawable read again with its textures as saved, shown in place of the one shown, as it was: a .ydr from
    /// the file it was saved as; a fragment's or dictionary's textures over its own.
    /// </summary>
    private async Task ShowSaved(SavedDocument saved)
    {
        if (closed || surfaces is null || shaderEdits.HasUnsavedSplits)
            return;
        if (file.Name.EndsWith(".ydr", StringComparison.OrdinalIgnoreCase))
            data = saved.Data;
        else
            savedTextures = saved.Textures;
        (ModelContent? again, string? failure) = await Task.Run(() => Read(file, data, gameDictionaries, savedTextures));
        if (closed)
            return;
        if (again?.Surfaces is { } after)
        {
            ShowAgain(after);
            ShowTextures(after);
            ShowStats(after);
        }
        if (started)
            StatusText.Text = again?.Surfaces is not null ? Location : failure ?? Location;
    }

    /// <summary>Shows a fragment damaged, its damaged drawables in place of their intact ones, or intact; one with no damaged state stays intact.</summary>
    internal bool SetDamaged(bool damaged)
    {
        if (visibility is null || surfaces is not { HasDamaged: true })
            return false;
        // Through the panel, which opens the state's row and asks for the parts to be drawn again.
        Parts.PickState(damaged);
        return true;
    }

    private DrawableScene? surfaces;

    /// <summary>The drawable shown, once read.</summary>
    internal DrawableScene? Surfaces => surfaces;

    // Whether the drawable has embedded textures that can be opened.
    private bool texturesAvailable;

    /// <summary>The embedded textures' row at the foot of the parts panel, with how many; none when there are none or they can't be opened.</summary>
    private void ShowTextures(DrawableScene scene)
    {
        int count = scene.EmbeddedTextureCount;
        texturesAvailable = count > 0 && file.OpenTextures is not null;
        Parts.ShowTextures(texturesAvailable ? count : 0);
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
        partsShown = shown && Parts.Parts is not null;
        PartsHandle.IsVisible = partsShown;
        PartsSide.Transitions ??= PartsSlide;
        PartsSide.Width = partsShown ? partsWidth : 0;
        PartsArrow.IconKind = partsShown ? "back" : "forward";
        PlaceCard();
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

    /// <summary>
    /// Picks the geometry a ray from the world meets first, as a click on the model does: its row is selected in the parts
    /// panel, opened to it, and it is outlined; a click on nothing picks nothing.
    /// </summary>
    internal bool PickAt(System.Numerics.Vector3 from, System.Numerics.Vector3 direction)
    {
        if (visibility is null)
            return false;
        if (visibility.PartAt(from, direction) is int part
            && Parts.Reveal(node => node.Kind == PartKind.Geometry && node.Parts.Count == 1 && node.Parts[0] == part))
            return true;
        Parts.ClearSelection();
        return false;
    }

    // What is selected in the parts panel is outlined on the model: a drawable's geometries under the rows. One geometry
    // picked has its shader values in the card.
    private void ShowPicked()
    {
        if (visibility is null)
            return;
        Viewport.Pick(Parts.Selection.SelectMany(node => node.Parts).Distinct());
        ShowCard();
    }

    /// <summary>The shader values card, while it shows.</summary>
    internal ShaderValuesCard Card => card;

    /// <summary>The shader values card's window over the viewport.</summary>
    internal ViewportOverlay CardOverlay => shaderCard;

    /// <summary>What has been changed in the drawable's shader values.</summary>
    internal ShaderEdits ShaderEdits => shaderEdits;

    /// <summary>
    /// Shows the shader values of the geometry picked, one geometry at the level shown, in the card, under the name of
    /// what it belongs to; with anything else picked, or nothing, there is no card.
    /// </summary>
    private void ShowCard()
    {
        FiveFury.Drawables.DrawableMaterial? material = PickedMaterial();
        // Not asked for, or nothing to show for what's picked now: no card, and it stays closed until asked for again.
        if (!cardOpen || material is null)
        {
            cardOpen = false;
            shaderCard.IsWanted = false;
            return;
        }
        string owner = "";
        int geometries = 1;
        if (surfaces is { } scene && Parts.Selection is [{ Kind: PartKind.Geometry } node])
        {
            geometries = scene.GeometriesUsing(material);
            owner = Parts.Parts is PartVisibility parts ? OwnerOf(parts, node) ?? file.Name : "";
        }
        // The same entry, built again (for a geometry given one of its own): the card keeps its rows, and a drag on them.
        if (material is not null && material == card.Material && owner == card.Title)
            card.SetShared(geometries);
        else
            card.Show(owner, material, geometries);
        card.SetAlsoOn(material is not null && surfaces is { } drawn && Parts.Selection is [{ Parts: [int picked, ..] }] ? AlsoDrawnOn(drawn, picked) : []);
        shaderCard.IsWanted = material is not null;
        PlaceCard();
    }

    private bool cardOpen;

    /// <summary>
    /// Whether the shader values card is open: opened from the parts panel's menu or with M, for the one geometry
    /// picked; while open it follows the pick, and it closes when the pick has no one geometry's values to show.
    /// </summary>
    internal bool ShaderValuesOpen => cardOpen;

    /// <summary>The shader entry of the one geometry picked, at the level shown; none for anything else.</summary>
    private FiveFury.Drawables.DrawableMaterial? PickedMaterial() =>
        surfaces is { } scene && Parts.Selection is [{ Kind: PartKind.Geometry } node]
            && node.Parts.Select(part => scene.Parts[part].Material).Distinct().ToArray() is [int index] && index < scene.Sources.Count
            ? scene.Sources[index] : null;

    /// <summary>Opens the shader values card for the geometry picked; false when there's none to show.</summary>
    internal bool OpenShaderValues()
    {
        if (PickedMaterial() is null)
            return false;
        cardOpen = true;
        ShowCard();
        return true;
    }

    /// <summary>Closes the shader values card; the pick stays.</summary>
    internal void CloseShaderValues()
    {
        cardOpen = false;
        shaderCard.IsWanted = false;
    }

    // Escape closes the card first, then the viewer.
    private void Escape()
    {
        if (cardOpen)
            CloseShaderValues();
        else
            Close();
    }

    /// <summary>
    /// The other parts a picked part's geometry is drawn as, by name: those its bones split it into (a vehicle's body
    /// panels), and where else its drawable is placed (a wheel at each of its bones).
    /// </summary>
    internal static List<string> AlsoDrawnOn(DrawableScene scene, int picked)
    {
        string NameOf(int part) => scene.Parts[part].Group is { Length: > 0 } group ? group
            : scene.Parts[part].Model < scene.Models.Count ? scene.Models[scene.Parts[part].Model].Name : "";
        string own = NameOf(picked);
        var names = new List<string>();
        for (int i = 0; i < scene.Parts.Count && i < scene.Geometries.Count; i++)
            if (scene.Geometries[i] == scene.Geometries[picked] && NameOf(i) is { Length: > 0 } name && name != own && !names.Contains(name))
                names.Add(name);
        return names;
    }

    /// <summary>
    /// Puts the card beside the row it shows, out of the parts panel's edge, its notch at the row's middle and the
    /// whole of it in the view, below the parts arrow; with the panel closed, or the row scrolled away, at the view's
    /// top left without a notch.
    /// </summary>
    private void PlaceCard()
    {
        if (!shaderCard.IsWanted)
            return;
        double inset = Ui.Theme.ToolStripInset, height = Viewport.Bounds.Height;
        double cardHeight = card.Bounds.Height > 0 ? card.Bounds.Height : card.DesiredSize.Height;
        if (partsShown && Parts.SelectedRow is { IsEffectivelyVisible: true } row
            && row.TranslatePoint(new Point(0, row.Bounds.Height / 2), Viewport) is { } middle && middle.Y >= 0 && middle.Y <= height)
        {
            double first = Ui.Theme.PartsTabTop + PartsTab.Bounds.Height + 6;
            double top = Math.Clamp(middle.Y - 20, first, Math.Max(first, height - cardHeight - inset));
            shaderCard.MoveTo(0, Math.Round(top));
            card.PointAt(middle.Y - Math.Round(top));
        }
        else
        {
            shaderCard.MoveTo(Ui.Theme.ShaderCardLeft, inset);
            card.PointAt(null);
        }
    }

    /// <summary>Where the card is put, from the view's left and top, and where its notch points (none when it doesn't).</summary>
    internal (double? Left, double? Top, double? Notch) CardPlace => (shaderCard.Left, shaderCard.Top, card.PointedAt);

    // The name of the row a geometry's row is under: its model, or its drawable.
    private static string? OwnerOf(PartVisibility parts, PartNode node)
    {
        string? Find(IEnumerable<PartNode> nodes, string? above)
        {
            foreach (PartNode candidate in nodes)
            {
                if (candidate.Key == node.Key && candidate.Kind == PartKind.Geometry)
                    return above;
                if (Find(candidate.Children, candidate.Name) is { } found)
                    return found;
            }
            return null;
        }
        return parts.Sections().Select(section => Find(section.Nodes, null)).FirstOrDefault(found => found is not null);
    }

    /// <summary>
    /// Gives the picked geometry a copy of the entry it shares, as This one asks at its first change: the card takes the
    /// copy, and the drawables are drawn again with it.
    /// </summary>
    private void SplitPicked()
    {
        if (surfaces is not { } scene || Parts.Selection is not [{ Kind: PartKind.Geometry, Parts: [int part, ..] }] || card.Material is not { } shared)
            return;
        int index = scene.Parts[part].Material;
        if (index >= scene.Sources.Count || scene.Sources[index] != shared)
            return;
        // Taken before the scene is built again, so the card keeps its rows.
        card.Adopt(shaderEdits.Split(scene.Geometries[part], scene.SourceLists[index], shared));
        Rebuild();
    }

    /// <summary>
    /// The drawables drawn again as they are now, after a geometry was given an entry of its own or moved back: as they
    /// were shown, the camera and what is picked kept.
    /// </summary>
    private void Rebuild()
    {
        if (surfaces?.Rebuild() is { } after)
            ShowAgain(after, carry: false);
    }

    /// <summary>An entry's values changed: every material drawn with it is drawn again with them.</summary>
    private void ShowEdited(FiveFury.Drawables.DrawableMaterial material)
    {
        // A drag changes values at the pointer's rate: they are drawn, and the title marked, once a frame.
        edited.Add(material);
        if (frameAsked)
            return;
        frameAsked = true;
        if (TopLevel.GetTopLevel(this) is { } top)
            top.RequestAnimationFrame(_ => FlushEdits());
        else
            Dispatcher.UIThread.Post(FlushEdits, DispatcherPriority.Render);
    }

    private readonly HashSet<FiveFury.Drawables.DrawableMaterial> edited = new(ReferenceEqualityComparer.Instance);
    private bool frameAsked;

    /// <summary>Draws the entries changed since the last frame with their values now, and marks the title.</summary>
    internal void FlushEdits()
    {
        frameAsked = false;
        if (surfaces is { } scene && edited.Count > 0)
            for (int i = 0; i < scene.Sources.Count; i++)
                if (edited.Contains(scene.Sources[i]) && scene.Remake(i))
                    Viewport.UpdateMaterial(i);
        edited.Clear();
        RefreshTitle();
    }

    /// <summary>
    /// The viewer's Ctrl keys: Z undoes the last change to the shader values, Y (or Shift+Z) redoes it, S saves them,
    /// Shift+S saves them as another file, and W closes the viewer.
    /// </summary>
    internal bool Command(char key, bool shift = false)
    {
        if (char.ToUpperInvariant(key) == 'W')
        {
            Close();
            return true;
        }
        if (!contentShown || surfaces is null)
            return false;
        switch (char.ToUpperInvariant(key), shift)
        {
            case ('Z', false):
                return Undo();
            case ('Y', _) or ('Z', true):
                return Redo();
            case ('S', false):
                return Save();
            case ('S', true):
                _ = SaveAs();
                return true;
        }
        return false;
    }

    /// <summary>Takes the last change to the shader values back, and draws it at once.</summary>
    internal bool Undo()
    {
        bool undone = shaderEdits.Undo();
        FlushEdits();
        return undone;
    }

    /// <summary>Does the last change undone again, and draws it at once.</summary>
    internal bool Redo()
    {
        bool redone = shaderEdits.Redo();
        FlushEdits();
        return redone;
    }

    // The file's name as shown, and where Save puts it: as opened, until it is saved as another file.
    private string documentName;
    private ModelSaveTarget? saveTarget;
    private bool savedElsewhere;

    /// <summary>The file's name in the title, marked with an asterisk while it has unsaved changes.</summary>
    private void RefreshTitle() => TitleText.Text = shaderEdits.Dirty ? $"{documentName} *" : documentName;

    /// <summary>Whether the file can be saved where it lives: it was opened from one, and can be written again.</summary>
    internal bool CanSave => saveTarget is not null && surfaces?.Writer is not null;

    private bool saving;

    /// <summary>The last save, done once it is written (true) or failed (false).</summary>
    internal Task<bool> Saving { get; private set; } = Task.FromResult(false);

    /// <summary>
    /// Saves the shader values changed (Ctrl+S): the whole file is written again from what was read, as it is now, and
    /// put where it lives, into its RPF or over the loose file, off the UI thread. False when nothing is changed, a
    /// save is under way, or the file can't be saved.
    /// </summary>
    internal bool Save()
    {
        if (saving || !shaderEdits.Dirty || saveTarget is not { } target)
            return false;
        return SaveTo(target, documentName, null);
    }

    /// <summary>
    /// Saves the drawable as it is now, its shader values included, as a loose file picked for it (Ctrl+Shift+S), which
    /// Save then saves to; changed or not, so a file in an RPF can be saved out as it is. False when it can't be written
    /// or nothing was picked.
    /// </summary>
    internal async Task<bool> SaveAs()
    {
        if (saving || surfaces?.Writer is null)
            return false;
        string extension = Path.GetExtension(file.Name);
        string kind = extension.ToLowerInvariant() switch { ".yft" => "Fragment", ".ydd" => "Drawable dictionary", _ => "Drawable" };
        string? path = await Dialogs.PickSaveFileAsync($"Save {documentName} as", documentName,
            [new FileFilter(kind, [$"*{extension}"])]);
        if (string.IsNullOrEmpty(path) || closed)
            return false;
        string destination = string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase) ? path : Path.ChangeExtension(path, extension);
        var target = new ModelSaveTarget(bytes => CommonFury.IO.AtomicFile.Write(destination, bytes));
        string name = Path.GetFileName(destination);
        if (!SaveTo(target, name, () =>
            {
                // From now on it is that file: Save saves there, and the title says so.
                saveTarget = target;
                documentName = name;
                savedElsewhere = true;
                RefreshTitle();
            }))
            return false;
        return await Saving;
    }

    // Writes the file as it is now through a target, off the UI thread; then, once it is written, runs afterwards.
    private bool SaveTo(ModelSaveTarget target, string name, Action? afterwards)
    {
        if (surfaces?.Writer is not { } write)
            return false;
        // What is written is what the values are now; a change made while it is put in place stays a change.
        ShaderSnapshot saved = shaderEdits.Snapshot();
        byte[] bytes;
        try
        {
            bytes = write();
            target.Prepare?.Invoke();
        }
        catch (Exception error) when (OperationErrors.IsExpected(error))
        {
            ShowSaveFailure(name, error.Message);
            return false;
        }
        saving = true;
        StatusText.Foreground = Ui.Theme.TextFaint;
        StatusText.Text = $"Saving {name}…";
        Saving = Finish();
        return true;

        async Task<bool> Finish()
        {
            string? failure = null;
            try
            {
                await Task.Run(() => target.Write(bytes));
            }
            catch (Exception error) when (OperationErrors.IsExpected(error))
            {
                failure = error.Message;
            }
            saving = false;
            if (failure is null)
            {
                // Read again from what was saved, from now on.
                data = bytes;
                afterwards?.Invoke();
                shaderEdits.MarkSaved(saved);
            }
            target.Finished?.Invoke(failure is null);
            if (failure is not null)
                ShowSaveFailure(name, failure);
            else if (!closed)
                StatusText.Text = $"Saved {name}";
            return failure is null;
        }
    }

    private void ShowSaveFailure(string name, string message)
    {
        StatusText.Text = $"Couldn't save {name}: {message}";
        StatusText.Foreground = Ui.Theme.Error;
    }

    /// <summary>The file pickers Save as and Extract ask; the app's, unless a test gives its own.</summary>
    internal IFileDialogs Dialogs { get; set; } = AppServices.Dialogs;

    /// <summary>Whether the drawable has textures of its own to extract.</summary>
    internal bool HasEmbeddedTextures => surfaces is { EmbeddedTextureCount: > 0 };

    /// <summary>
    /// Extracts the drawable's own textures, as they are now, to DDS files in a folder picked for them, as the texture
    /// editor's Extract all does; false when there are none or no folder was picked.
    /// </summary>
    internal async Task<bool> ExtractTextures()
    {
        if (!HasEmbeddedTextures)
            return false;
        string? folder = await Dialogs.PickFolderAsync("Extract embedded textures");
        if (string.IsNullOrEmpty(folder) || closed)
            return false;
        int count;
        try
        {
            (IReadOnlyList<FiveFury.Textures.Texture> own, _) = ModelContent.EmbeddedTextures(file.Name, data);
            // A fragment's or dictionary's textures as last saved in the texture editor, over its own.
            IReadOnlyList<FiveFury.Textures.Texture> textures = savedTextures is { Count: > 0 } saved ? saved : own;
            var dictionary = new FiveFury.Textures.TextureDictionary();
            dictionary.Textures.AddRange(textures);
            count = FiveFury.Textures.TextureDirectory.ExportDds(dictionary, folder).Count;
        }
        catch (Exception error) when (OperationErrors.IsExpected(error))
        {
            StatusText.Text = $"Couldn't extract the textures: {error.Message}";
            StatusText.Foreground = Ui.Theme.Error;
            return false;
        }
        StatusText.Foreground = Ui.Theme.TextFaint;
        StatusText.Text = count == 1 ? "Extracted 1 texture" : $"Extracted {count} textures";
        return true;
    }

    /// <summary>The menu bar under the title: File and Edit.</summary>
    internal ViewerMenuRow Menus { get; }

    private ViewerMenuRow BuildMenus() => new(
        ("File",
        [
            new("Save", "Ctrl+S", () => Save(), () => CanSave && shaderEdits.Dirty && !saving),
            new("Save as…", "Ctrl+Shift+S", () => _ = SaveAs(), () => surfaces?.Writer is not null && !saving),
            null,
            new("Embedded textures…", "T", () => OpenTextures(), () => texturesAvailable && !savedElsewhere),
            new("Extract embedded textures…", "", () => _ = ExtractTextures(), () => HasEmbeddedTextures),
            null,
            new("Close", "Ctrl+W", Close),
        ]),
        ("Edit",
        [
            new("Undo", "Ctrl+Z", () => Undo(), () => shaderEdits.CanUndo),
            new("Redo", "Ctrl+Y", () => Redo(), () => shaderEdits.CanRedo),
            null,
            new("Reset the picked shader's values", "", () => { if (card.Material is { } material) shaderEdits.ResetAll(material); },
                () => card.Material is { } material && ShaderEdits.ValuesOf(material).Any(value => shaderEdits.IsChanged(material, value.Name))),
            new("Reset every shader value", "", shaderEdits.ResetEverything, () => shaderEdits.AnyChanged),
        ]));

    // Whether the viewer may close with its changes unsaved: they were saved, or the dialog was told not to.
    private bool closeApproved;
    private TextureUnsavedChangesDialog? unsavedDialog;

    /// <summary>The dialog asking whether to save before the viewer closes, while it is open.</summary>
    internal TextureUnsavedChangesDialog? UnsavedDialog => unsavedDialog is { IsOpen: true } ? unsavedDialog : null;

    /// <summary>
    /// Asks whether to save the changed shader values before the viewer closes: Save closes it once they are saved,
    /// Don't save closes it, Cancel stays. The viewport, a native window that would cover the dialog, hides meanwhile.
    /// </summary>
    private void AskToSave()
    {
        if (UnsavedDialog is not null)
            return;
        Viewport.IsVisible = false;
        var dialog = new TextureUnsavedChangesDialog { DocumentName = documentName };
        dialog.SaveRequested += async (_, _) =>
        {
            if (Save() && await Saving)
            {
                closeApproved = true;
                Close();
            }
            else
                ShowViewportAgain();
        };
        dialog.DiscardRequested += (_, _) =>
        {
            closeApproved = true;
            Close();
        };
        dialog.Rejected += (_, _) => ShowViewportAgain();
        unsavedDialog = dialog;
        dialog.Open(DialogLayer);
    }

    private void ShowViewportAgain() => Viewport.IsVisible = IsOpen && overlay?.IsTopmost != false && !closed;

    /// <summary>Turns the camera to the parts under these rows of the parts panel.</summary>
    internal void FrameParts(IReadOnlyList<PartNode> nodes)
    {
        if (Parts.Parts?.Bounds(nodes) is { } box)
            Viewport.Focus(box);
    }

    /// <summary>Shows a level of detail the drawable has.</summary>
    internal void SetLevel(int lod)
    {
        if (visibility is null || !visibility.Levels.Contains(lod))
            return;
        visibility.Lod = lod;
        levelsDropdown?.Close();
        Parts.Refresh();
        ShowParts();
    }

    private CarPaints? carPaints;
    private int?[] paintIndices = new int?[VehiclePaint.Slots.Length];
    private DropdownWindow? paintDropdown;
    private long paintClosedAt;

    /// <summary>The game's colours for the vehicle shown and its combinations of them; none for anything else.</summary>
    internal CarPaints? CarPaints => carPaints;

    /// <summary>The six paint slots' colour numbers the vehicle is drawn in.</summary>
    internal IReadOnlyList<int?> PaintIndices => paintIndices;

    /// <summary>The paint's dropdown, while it's open.</summary>
    internal PaintPanel? PaintPanel { get; private set; }

    private static int?[] Slots(IReadOnlyList<int?> indices) =>
        [.. Enumerable.Range(0, VehiclePaint.Slots.Length).Select(slot => slot < indices.Count ? indices[slot] : null)];

    /// <summary>The paint's well, for a vehicle the game has colours for: its primary over its secondary.</summary>
    private void ShowPaint()
    {
        PaintWell.IsVisible = carPaints is not null;
        if (carPaints is null)
            return;
        Avalonia.Media.IBrush Chip(int slot) => carPaints.Colour(paintIndices[slot]) is { } colour
            ? new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Avalonia.Media.Color.FromRgb((byte)(colour.Argb >> 16), (byte)(colour.Argb >> 8), (byte)colour.Argb))
            : new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Avalonia.Media.Color.FromRgb(150, 150, 150));
        PaintChipPrimary.Background = Chip(0);
        PaintChipSecondary.Background = Chip(1);
        string Named(int slot) => carPaints.Colour(paintIndices[slot])?.Name ?? "the game's grey";
        ThemedToolTip.Set(PaintWell, $"Paint: {Named(0)} over {Named(1)}");
        PaintPaintWell();
    }

    // The well brightens under the pointer, and while its dropdown is open.
    private void PaintPaintWell() =>
        PaintWell.Background = PaintWell.IsPointerOver || paintDropdown is not null ? HoverWell : Ui.Theme.InsetBg;

    /// <summary>Paints the vehicle with the six slots' colour numbers <paramref name="indices"/>.</summary>
    internal void SetPaint(IReadOnlyList<int?> indices)
    {
        if (carPaints is null || surfaces is not { } scene)
            return;
        paintIndices = Slots(indices);
        foreach (int index in scene.Repaint(VehiclePaint.Of(carPaints, paintIndices)))
            Viewport.UpdateMaterial(index);
        ShowPaint();
    }

    /// <summary>Opens the paint above its well, or closes it.</summary>
    internal void TogglePaint()
    {
        if (paintDropdown is not null)
        {
            paintDropdown.Close();
            return;
        }
        // The press on the well that just closed it, by taking the keyboard from it, doesn't open it again.
        if (carPaints is null || Environment.TickCount64 - paintClosedAt < 400)
            return;
        var panel = new PaintPanel(carPaints, paintIndices);
        panel.Changed += SetPaint;
        paintDropdown = DropdownWindow.OpenAbove(PaintWell, panel, 4);
        if (paintDropdown is null)
            return;
        PaintPanel = panel;
        panel.Focus();
        PaintPaintWell();
        paintDropdown.Closed += (_, _) =>
        {
            paintDropdown = null;
            PaintPanel = null;
            paintClosedAt = Environment.TickCount64;
            PaintPaintWell();
        };
    }

    private DropdownWindow? levelsDropdown;
    private long levelsClosedAt;

    /// <summary>Whether the list of levels is open.</summary>
    internal bool LevelsOpen => levelsDropdown is not null;

    /// <summary>The rows of the open list of levels: each level's key, name and triangles, and whether the drawable has it.</summary>
    internal IReadOnlyList<LevelRow> LevelRows { get; private set; } = [];

    /// <summary>Whether the drawable has more than one level, so its well opens a list of them.</summary>
    private bool LevelsChoosable => visibility is { Levels.Count: > 1 };

    /// <summary>The level shown, with its triangles, in the footer; the well opens a list only when there is a choice.</summary>
    private void ShowLevels()
    {
        if (visibility is null)
            return;
        LodText.Text = PartVisibility.LevelName(visibility.Lod);
        LodCount.Text = PartsPanel.Compact(visibility.Stats().Triangles);
        LodCaret.IsVisible = LevelsChoosable;
        LodWell.Cursor = LevelsChoosable ? new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) : null;
        LodWell.Focusable = LevelsChoosable;
        PaintLevelWell();
    }

    // A dropdown's well under the pointer, as the app's comboboxes have it.
    private static readonly Avalonia.Media.Immutable.ImmutableSolidColorBrush HoverWell = new(Ui.Theme.Lighter(Ui.Theme.InsetBg.Color, 1.35));

    // The well brightens under the pointer, and while its list is open, when it has one.
    private void PaintLevelWell() =>
        LodWell.Background = LevelsChoosable && (LodWell.IsPointerOver || levelsDropdown is not null) ? HoverWell : Ui.Theme.InsetBg;

    /// <summary>Opens the list of the four levels above the footer, or closes it; a level the drawable lacks is faded.</summary>
    internal void ToggleLevels()
    {
        if (levelsDropdown is not null)
        {
            levelsDropdown.Close();
            return;
        }
        if (!LevelsChoosable || Environment.TickCount64 - levelsClosedAt < 400)
            return;
        var rows = new StackPanel { Margin = Ui.Theme.MenuInset, MinWidth = 196 };
        var listed = new List<LevelRow>();
        for (int lod = 0; lod < 4; lod++)
        {
            int level = lod;
            bool has = visibility!.Levels.Contains(lod);
            var row = new LevelRow(lod, has ? visibility.TrianglesAt(lod).ToString("N0", System.Globalization.CultureInfo.InvariantCulture) : "none", has, lod == visibility.Lod);
            row.Triggered += () => SetLevel(level);
            rows.Children.Add(row);
            listed.Add(row);
        }
        LevelRows = listed;
        levelsDropdown = DropdownWindow.OpenAbove(LodWell, rows, 4);
        if (levelsDropdown is null)
            return;
        // From the keyboard: the level shown marked to start with, Up and Down to another, Enter or Space to pick it.
        foreach (LevelRow row in listed)
            row.Highlighted = row.Lod == visibility!.Lod;
        levelsDropdown.KeyDown += (_, e) => e.Handled |= LevelsKey(e.Key);
        PaintLevelWell();
        levelsDropdown.Closed += (_, _) =>
        {
            levelsDropdown = null;
            levelsClosedAt = Environment.TickCount64;
            LevelRows = [];
            PaintLevelWell();
        };
    }

    /// <summary>
    /// A key in the open list of levels: Up and Down move the mark over the levels the drawable has, Enter or Space picks
    /// the one marked, 1 to 4 a level. False for any other key.
    /// </summary>
    internal bool LevelsKey(Avalonia.Input.Key key)
    {
        if (LevelRows.Count == 0)
            return false;
        int marked = LevelRows.ToList().FindIndex(row => row.Highlighted);
        switch (key)
        {
            case Avalonia.Input.Key.Up or Avalonia.Input.Key.Down:
                int step = key == Avalonia.Input.Key.Down ? 1 : -1;
                for (int at = marked + step; at >= 0 && at < LevelRows.Count; at += step)
                    if (LevelRows[at].Available)
                    {
                        foreach (LevelRow row in LevelRows)
                            row.Highlighted = row == LevelRows[at];
                        break;
                    }
                return true;
            case Avalonia.Input.Key.Enter or Avalonia.Input.Key.Space:
                if (marked >= 0)
                    LevelRows[marked].Trigger();
                return true;
            case >= Avalonia.Input.Key.D1 and <= Avalonia.Input.Key.D4:
                LevelRows[key - Avalonia.Input.Key.D1].Trigger();
                return true;
        }
        return false;
    }

    /// <summary>A level in the footer's list: its key, its name, its triangles; faded, and not picked, when the drawable lacks it.</summary>
    internal sealed class LevelRow : Border
    {
        private readonly TextBlock name;
        private bool highlighted;

        public LevelRow(int lod, string triangles, bool available, bool current)
        {
            Lod = lod;
            Available = available;
            Height = Ui.Theme.MenuItemHeight;
            CornerRadius = Ui.Theme.InnerCorner;
            Background = Avalonia.Media.Brushes.Transparent;
            Opacity = available ? 1 : Views.Controls.ChromeToolButton.DisabledOpacity;
            if (available)
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
            var key = new TextBlock { Text = (lod + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), Width = 22, Margin = new Thickness(9, 0, 0, 0), FontFamily = Ui.Theme.MonoFont, FontSize = Ui.Theme.SmallFontSize, Foreground = Ui.Theme.TextFaint, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            var count = new TextBlock { Text = triangles, Margin = new Thickness(18, 0, 10, 0), FontFamily = Ui.Theme.MonoFont, FontSize = Ui.Theme.SmallFontSize, Foreground = Ui.Theme.TextDim, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            name = new TextBlock { Text = PartVisibility.LevelName(lod), FontFamily = Ui.Theme.UiFont, FontSize = Ui.Theme.FontSize, Foreground = current ? Ui.Theme.Text : Ui.Theme.TextRow, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            DockPanel.SetDock(key, Dock.Left);
            DockPanel.SetDock(count, Dock.Right);
            Child = new DockPanel { Children = { key, count, name } };
            Triangles = triangles;
            PointerEntered += (_, _) => Paint();
            PointerExited += (_, _) => Paint();
            PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton == Avalonia.Input.MouseButton.Left)
                    Trigger();
            };
        }

        public int Lod { get; }

        public bool Available { get; }

        /// <summary>Whether the keyboard's mark is on it: it fills as under the pointer.</summary>
        public bool Highlighted
        {
            get => highlighted;
            set
            {
                highlighted = value;
                Paint();
            }
        }

        private void Paint() =>
            Background = highlighted || (Available && IsPointerOver) ? Ui.Theme.HoverChrome : Avalonia.Media.Brushes.Transparent;

        /// <summary>Its triangles as listed, or "none".</summary>
        public string Triangles { get; }

        public event Action? Triggered;

        /// <summary>Picks the level, as a click does, when the drawable has it.</summary>
        internal void Trigger()
        {
            if (Available)
                Triggered?.Invoke();
        }
    }

    /// <summary>Draws what the parts panel and the level say, and counts it.</summary>
    private void ShowParts()
    {
        // A collision or a navmesh is drawn again without the marks hidden; the camera stays where it is.
        if (marks is not null)
            Viewport.Show(marks.Mesh);
        if (visibility is null)
            return;
        Viewport.ShowParts(visibility.Mask());
        // What is picked, again: another level's rows keep their keys but not their parts.
        ShowPicked();
        (int triangles, int vertices, int edges) = visibility.Stats();
        static string Count(int value) => value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        TrianglesText.Text = Count(triangles);
        VerticesText.Text = Count(vertices);
        EdgesText.Text = Count(edges);
        ShowLevels();
    }

    /// <summary>
    /// The viewer's keys, from the viewport or the viewer: P parts, 1 to 4 the level of detail, H shadows, T textures,
    /// X damaged, B skeleton, K collision, L lights, V orbit or fly, F frame. None is W, A, S, D, Q or E, which the free
    /// camera moves with.
    /// </summary>
    internal bool Shortcut(char key)
    {
        if (!contentShown)
            return false;
        // A collision or a navmesh has the camera's keys, and the parts panel's when it has something to list.
        if (surfaces is null && char.ToUpperInvariant(key) is not ('V' or 'F') && !(marks is not null && char.ToUpperInvariant(key) == 'P'))
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
            case 'M':
                if (cardOpen)
                {
                    CloseShaderValues();
                    return true;
                }
                return OpenShaderValues();
            case >= '1' and <= '4':
                SetLevel(key - '1');
                return true;
        }
        return false;
    }

    protected override void OnKeyDown(Avalonia.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        // Typing in a field (the parts filter) isn't a shortcut.
        if (e.Handled || e.Source is TextBox)
            return;
        if (e.KeyModifiers == Avalonia.Input.KeyModifiers.Control && e.Key is >= Avalonia.Input.Key.A and <= Avalonia.Input.Key.Z)
        {
            if (Command((char)('A' + (e.Key - Avalonia.Input.Key.A))))
                e.Handled = true;
            return;
        }
        if (e.KeyModifiers == (Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Shift) && e.Key is >= Avalonia.Input.Key.A and <= Avalonia.Input.Key.Z)
        {
            if (Command((char)('A' + (e.Key - Avalonia.Input.Key.A)), shift: true))
                e.Handled = true;
            return;
        }
        if (e.KeyModifiers != Avalonia.Input.KeyModifiers.None)
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

    /// <summary>The time of day in its dropdown: a dial of the day, the sun or moon on it, in quarter hours.</summary>
    internal TimeDial Dial { get; } = new() { Width = 152, Height = 152 };

    private readonly Control timeBody;
    private DropdownWindow? timeDropdown;
    private long timeClosedAt;

    /// <summary>Whether the time of day's dropdown is open.</summary>
    internal bool TimeOpen => timeDropdown is not null;

    private Control TimeBody()
    {
        Avalonia.Automation.AutomationProperties.SetName(Dial, "Time of day");
        return new Border { Padding = new Thickness(12, 10, 12, 12), Child = Dial };
    }

    /// <summary>
    /// Opens the time of day's dial above its button, a window of its own so the viewport doesn't cover it, or closes
    /// it; the button sits raised while it is open, and the dial takes the keyboard.
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
        timeDropdown = DropdownWindow.OpenAbove(TimeWell, timeBody, 4, right: true);
        if (timeDropdown is null)
            return;
        TimeButton.Sunken = true;
        Dial.Focus();
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
        TimeButton.Text = TimeDial.Clock(hours);
        Dial.Hours = TimeGlyph.Hours = hours;
    }

    /// <summary>The time of day the model is lit at, in hours.</summary>
    internal float Hours => Viewport.Hours;

    private bool started;
    // Whether something was read to show, which the keys act on.
    private bool contentShown;

    /// <summary>What the file holds, where and how big, or why it can't be read: the status bar's line, once read.</summary>
    internal string Location { get; private set; } = "";

    /// <summary>Done once the file is read and shown, or found unreadable.</summary>
    internal Task Reading { get; }

    private async Task Load(ModelFile file)
    {
        if (file.Data.Length == 0)
            return;
        // Shown at once with the textures at hand (its own, the ones beside it, and the game's if its texture tables are
        // ready); while the tables are still being worked out, the game's are added when they come.
        Task<IReadOnlyList<NearbyDictionary>>? pending = file.GameDictionaries;
        IReadOnlyList<NearbyDictionary> game = pending is { IsCompletedSuccessfully: true } ? pending.Result : [];
        data = file.Data;
        gameDictionaries = game;
        (ModelContent? content, string? failure) = await Task.Run(() => Read(file, file.Data, game, null));
        // A vehicle from a game is painted in the first of its colour combinations, as the game spawns it, from the first
        // frame on; read with the game's texture tables, so at hand by now.
        if (content?.Surfaces is { Paintable: true } painted && file.GamePaints is { } asked && await asked is { } paints)
        {
            carPaints = paints;
            paintIndices = paints.Combinations.Count > 0 ? Slots(paints.Combinations[0]) : new int?[VehiclePaint.Slots.Length];
            painted.Repaint(VehiclePaint.Of(paints, paintIndices));
        }
        Location = content is not null ? Locate(content) : failure!;
        ShowPaint();
        if (started)
            StatusText.Text = Location;
        if (content?.Surfaces is { } first && pending is { IsCompleted: false } && Missing(first) > 0)
            GameTexturesAdded = AddGameTextures(file, pending);
    }

    /// <summary>Done once the game's textures have been added to the drawable shown, or there were none to add.</summary>
    internal Task GameTexturesAdded { get; private set; } = Task.CompletedTask;

    private bool closed;

    /// <summary>
    /// Waits for the game's dictionaries for the drawable shown, then reads it again with them and shows that in its
    /// place, as it was: the camera, the level, the damaged state, the parts hidden and the overlays stay.
    /// </summary>
    private async Task AddGameTextures(ModelFile file, Task<IReadOnlyList<NearbyDictionary>> pending)
    {
        string shown = Location;
        StatusText.Text = shown.Length > 0 ? $"{shown} · finding the game's textures…" : "Finding the game's textures…";
        IReadOnlyList<NearbyDictionary> game = [];
        try
        {
            game = await pending;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or ArgumentException
            or NotSupportedException or UnauthorizedAccessException)
        {
        }
        // A geometry given an entry of its own isn't in the file until it's saved, so it isn't read again until then.
        if (game.Count > 0 && !closed && surfaces is { } before && !shaderEdits.HasUnsavedSplits)
        {
            byte[] current = data;
            IReadOnlyList<FiveFury.Textures.Texture>? edited = savedTextures;
            (ModelContent? again, _) = await Task.Run(() => Read(file, current, game, edited));
            if (!closed && again?.Surfaces is { } after && Missing(after) < Missing(before))
            {
                gameDictionaries = game;
                ShowAgain(after);
            }
        }
        if (!closed && started)
            StatusText.Text = Location;
    }

    /// <summary>How many of the textures a drawable names weren't found.</summary>
    private static int Missing(DrawableScene scene) =>
        scene.Materials.SelectMany(material => material.MissingTextures).Distinct(StringComparer.OrdinalIgnoreCase).Count();

    /// <summary>The same drawable read again (with more of its textures), shown in place of the one shown, as it was.</summary>
    private void ShowAgain(DrawableScene after, bool carry = true)
    {
        PartVisibility? before = visibility;
        // The shader values changed so far go into the drawables read again, which are drawn with them.
        if (carry && surfaces is { } shown)
            foreach (int index in shaderEdits.Carry(shown.Sources, after.Sources))
                after.Remake(index);
        // In the paint it had.
        if (carPaints is not null)
            after.Repaint(VehiclePaint.Of(carPaints, paintIndices));
        Viewport.Show(after);
        surfaces = after;
        visibility = new PartVisibility(after);
        if (before is not null)
            visibility.KeepFrom(before);
        Parts.Replace(visibility);
        ShowParts();
        overlays = after.Overlays;
        if (skeletonShown || lightsShown)
            SetOverlay(skeletonShown ? Overlay.Skeleton : Overlay.Lights, true);
        if (solidShown)
            SetOverlay(Overlay.Solid, true);
        ToolTip.SetTip(StatusText, after.Diagnostics.Count > 0 ? string.Join("\n", after.Diagnostics) : null);
        Location = Describe(after);
    }

    private static (ModelContent? Content, string? Failure) Read(ModelFile file, byte[] data, IReadOnlyList<NearbyDictionary> game,
        IReadOnlyList<FiveFury.Textures.Texture>? savedTextures)
    {
        try
        {
            return (ModelContent.Read(file.Name, data, file.Dictionaries, [.. game, .. file.Nearby], savedTextures), null);
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
        contentShown = true;
        gizmo.IsWanted = true;
        System.Numerics.Vector3 size = content.Box.Size, center = content.Box.Center;
        if (content.Surfaces is { } surfaces)
        {
            Viewport.Show(surfaces);
            // A drawable is lit by the game's light, and has the whole footer and the overlays; its parts panel opens
            // when asked for.
            ModelTools.IsVisible = TimeWell.IsVisible = true;
            partsTab.IsWanted = toolStrip.IsWanted = true;
            ShowStats(surfaces);
            visibility = new PartVisibility(surfaces);
            ShowLevels();
            Parts.Parts = visibility;
            SetPartsShown(false);
            ShowTextures(surfaces);
            ShowOverlayButtons(surfaces);
            this.surfaces = surfaces;
            RefreshToggles();
            // What isn't shown as the game shows it, under the status line.
            ToolTip.SetTip(StatusText, surfaces.Diagnostics.Count > 0 ? string.Join("\n", surfaces.Diagnostics) : null);
            return Describe(surfaces);
        }
        Viewport.Show(content.Mesh);
        // Collisions and navmeshes are drawn flat: of the footer, they have the camera, and the parts panel lists what
        // their colours mean (a collision's materials), to show, hide and find each, when asked for.
        ModelTools.IsVisible = TimeWell.IsVisible = false;
        var listed = new MeshParts(content.Mesh);
        marks = listed.Any ? listed : null;
        Parts.Parts = marks;
        Parts.ShowTextures(0);
        partsTab.IsWanted = marks is not null;
        SetPartsShown(false);
        RefreshToggles();
        int triangles = content.Mesh.TriangleCount, skipped = content.Mesh.Skipped.Values.Sum();
        return string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{triangles:N0} {(triangles == 1 ? "triangle" : "triangles")}{(skipped > 0 ? $" ({skipped:N0} shapes not drawn)" : "")} · "
            + $"{size.X:0.#} × {size.Y:0.#} × {size.Z:0.#} m at {center.X:0.#}, {center.Y:0.#}, {center.Z:0.#}");
    }

    /// <summary>A drawable's triangles, vertices and edges, beside their button while it is on.</summary>
    private void ShowStats(DrawableScene scene)
    {
        static string Count(int value) => value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        TrianglesText.Text = Count(scene.TriangleCount);
        VerticesText.Text = Count(scene.VertexCount);
        EdgesText.Text = Count(scene.EdgeCount);
        StatsPanel.IsVisible = statsShown;
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

    /// <summary>What the parts panel lists for a collision or a navmesh: what its colours mean. None for a drawable.</summary>
    internal MeshParts? Marks => marks;

    /// <summary>The control the viewer opens over, in the main window.</summary>
    public Control? Anchor { get; set; }

    public bool IsOpen => overlay?.IsShown == true;

    /// <summary>The parts arrow, drawn over the viewport's top left in a window of its own.</summary>
    internal ViewportOverlay PartsTabOverlay => partsTab;

    /// <summary>The overlay row, drawn over the viewport's top, centred across it, in a window of its own.</summary>
    internal ViewportOverlay ToolStripOverlay => toolStrip;

    /// <summary>The axes gizmo, drawn over the viewport's top right in a window of its own.</summary>
    internal ViewportOverlay GizmoOverlay => gizmo;

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
                partsTab.Close();
                toolStrip.Close();
                gizmo.Close();
                shaderCard.Close();
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
        if (!closeApproved && !closed && shaderEdits.Dirty && CanSave)
        {
            AskToSave();
            return;
        }
        closed = true;
        timeDropdown?.Close();
        levelsDropdown?.Close();
        paintDropdown?.Close();
        // Nor the closing one: it goes first, so the sheet leaves as a whole.
        Viewport.IsVisible = false;
        overlay?.Close();
    }

    void IModalContent.RequestClose() => Escape();
}
