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
    // The parts arrow and the tool strip, over the viewport.
    private readonly ViewportOverlay partsTab;
    private readonly ViewportOverlay toolStrip;

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
        toolStrip = new ViewportOverlay(Viewport, ToolStrip, left: Ui.Theme.ToolStripInset);
        WindowToolTip.SetIsEnabled(PartsTab, true);
        WindowToolTip.SetIsEnabled(ToolStrip, true);
        TitleText.Text = file.Name;
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
        this.file = file;
        Parts.Changed += ShowParts;
        Parts.FrameRequested += FrameParts;
        Parts.TexturesRequested += () => OpenTextures();
        FrameButton.Click += (_, _) => Reframe();
        SkeletonButton.Click += (_, _) => SetOverlay(Overlay.Skeleton, !skeletonShown);
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
        Set(OrbitButton, !Viewport.FreeCamera);
        Set(FreeButton, Viewport.FreeCamera);
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
    /// Orbits the model or flies: flying, the viewport takes the keyboard for W, A, S, D, Q and E, and the status bar
    /// says how fast it goes.
    /// </summary>
    internal void SetFreeCamera(bool on)
    {
        Viewport.FreeCamera = on;
        SpeedStat.IsVisible = on;
        // A collision or a navmesh has no counts beside the status line: the speed is there alone, while it flies.
        if (surfaces is null)
            StatsPanel.IsVisible = on;
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
        if (closed || surfaces is null)
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
        // A collision or a navmesh is drawn again without the marks hidden; the camera stays where it is.
        if (marks is not null)
            Viewport.Show(marks.Mesh);
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
        if (e.Handled || e.KeyModifiers != Avalonia.Input.KeyModifiers.None || e.Source is TextBox)
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
        // Shown at once with the textures at hand (its own, the ones beside it, and the game's if its texture tables are
        // ready); while the tables are still being worked out, the game's are added when they come.
        Task<IReadOnlyList<NearbyDictionary>>? pending = file.GameDictionaries;
        IReadOnlyList<NearbyDictionary> game = pending is { IsCompletedSuccessfully: true } ? pending.Result : [];
        data = file.Data;
        gameDictionaries = game;
        (ModelContent? content, string? failure) = await Task.Run(() => Read(file, file.Data, game, null));
        Location = content is not null ? Locate(content) : failure!;
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
        if (game.Count > 0 && !closed && surfaces is { } before)
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
    private void ShowAgain(DrawableScene after)
    {
        PartVisibility? before = visibility;
        Viewport.Show(after);
        surfaces = after;
        visibility = new PartVisibility(after);
        if (before is not null)
            visibility.KeepFrom(before);
        Parts.Parts = visibility;
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
        System.Numerics.Vector3 size = content.Box.Size, center = content.Box.Center;
        if (content.Surfaces is { } surfaces)
        {
            Viewport.Show(surfaces);
            // A drawable is lit by the game's light, and has the whole toolbar.
            Toolbar.IsVisible = ModelTools.IsVisible = TimeWell.IsVisible = true;
            partsTab.IsWanted = toolStrip.IsWanted = true;
            ShowStats(surfaces);
            visibility = new PartVisibility(surfaces);
            ShowLevels();
            Parts.Parts = visibility;
            SetPartsShown(true);
            ShowTextures(surfaces);
            ShowOverlayButtons(surfaces);
            this.surfaces = surfaces;
            RefreshToggles();
            // What isn't shown as the game shows it, under the status line.
            ToolTip.SetTip(StatusText, surfaces.Diagnostics.Count > 0 ? string.Join("\n", surfaces.Diagnostics) : null);
            return Describe(surfaces);
        }
        Viewport.Show(content.Mesh);
        // Collisions and navmeshes are drawn flat: of the toolbar, they have the camera's buttons, and the parts panel
        // lists what their colours mean (a collision's materials), to show, hide and find each.
        Toolbar.IsVisible = true;
        ModelTools.IsVisible = TimeWell.IsVisible = false;
        TrianglesStat.IsVisible = VerticesStat.IsVisible = EdgesStat.IsVisible = false;
        var listed = new MeshParts(content.Mesh);
        marks = listed.Any ? listed : null;
        Parts.Parts = marks;
        Parts.ShowTextures(0);
        partsTab.IsWanted = marks is not null;
        SetPartsShown(marks is not null);
        RefreshToggles();
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

    /// <summary>What the parts panel lists for a collision or a navmesh: what its colours mean. None for a drawable.</summary>
    internal MeshParts? Marks => marks;

    /// <summary>The control the viewer opens over, in the main window.</summary>
    public Control? Anchor { get; set; }

    public bool IsOpen => overlay?.IsShown == true;

    /// <summary>The parts arrow, drawn over the viewport's top left in a window of its own.</summary>
    internal ViewportOverlay PartsTabOverlay => partsTab;

    /// <summary>The tool strip, drawn over the viewport's left edge, centred on its height, in a window of its own.</summary>
    internal ViewportOverlay ToolStripOverlay => toolStrip;

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
        closed = true;
        timeDropdown?.Close();
        // Nor the closing one: it goes first, so the sheet leaves as a whole.
        Viewport.IsVisible = false;
        overlay?.Close();
    }

    void IModalContent.RequestClose() => Close();
}
