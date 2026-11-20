using System.ComponentModel;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using HikariHopper.Core;
using HikariHopper.Views.Controls;
using HikariHopper.Rendering.World;
using HikariHopper.ViewModels;

namespace HikariHopper.Views;

/// <summary>
/// A game's world, on its tab's World side (<see cref="ExplorerViewModel.InWorld"/>): the 3D view, under the address row
/// and over the status bar, with a camera that flies (W A S D, Q and E down and up, drag to look, wheel for speed). It
/// starts where the tab's camera was left (<see cref="ExplorerViewModel.WorldSpot"/>) and says where it is as it moves.
/// One view serves every tab, as the files' views do, moving its camera to the tab shown. Until the game's world index is
/// ready, the loading screen shows in its place: the train, what the index is doing, "Preparing the world" and how far
/// it has got; the world opens by itself when it's done. Then it streams in around the camera (<see cref="WorldStreamer"/>):
/// the whole map through its lowest levels of detail, the detail near the camera. On the Files side it pauses, keeping
/// what it streamed.
/// <para>
/// The viewport is a native window, which covers whatever the main window draws over it: it hides while a dialog or a
/// menu drawn in the window is open over it, or the tab's loading cover is up. The title bar's menus and the sheets
/// (Settings among them) open over it in windows of their own (<see cref="MenuRow"/>, <see cref="ModalOverlay"/>), so it
/// keeps drawing under them.
/// </para>
/// </summary>
internal sealed class WorldView : UserControl
{
    /// <summary>How often the camera's spot is told to the tab: the coordinates and the Back and Forward stop.</summary>
    internal static TimeSpan FollowInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>How long the viewport waits to come back once what covered it closed, so its closing isn't cut.</summary>
    internal static TimeSpan SettleTime { get; set; } = TimeSpan.FromMilliseconds(150);

    // What's shown around the camera when it's placed: its fly speed and zoom limits follow it.
    private const float Reach = 20;

    private readonly DispatcherTimer follow;
    private ExplorerViewModel? bridge;
    private TopLevel? top;
    private int shownRequest;
    private WorldStreamer? streamer;
    private WorldIndex? streamed;

    public WorldView()
    {
        Viewport = new ViewportHost { FreeCamera = true, World = true };
        // Settings and the other sheets open over it in a pane of their own, so it keeps drawing under them.
        ModalOverlay.SetKeepsDrawing(Viewport, true);
        Viewport.Drawing += (renderer, seconds) => streamer?.Update(renderer, seconds, bridge?.WorldHours ?? Rendering.DayCycle.DefaultHours);
        Viewport.PixelClicked += PickAt;
        Pattern = new RailPattern { IsRunning = false };
        Loading = new LoadingOverlay { Background = Avalonia.Media.Brushes.Transparent, Detail = "Preparing the world" };
        Cover = new Panel
        {
            IsVisible = false,
            Background = Ui.Theme.WindowBg,
            Children =
            {
                Pattern,
                new Border
                {
                    Width = 640,
                    Height = 320,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    IsHitTestVisible = false,
                    Background = Ui.Theme.RailPatternClearing,
                },
                Loading,
            },
        };
        Content = new Panel { Children = { Viewport, Cover } };
        Background = Ui.Theme.WindowBg;
        Viewport.Started += _ =>
        {
            Place();
            Viewport.SetTime(bridge?.WorldHours ?? Rendering.DayCycle.DefaultHours);
        };
        Viewport.KeyForwarded += key =>
        {
            switch (key)
            {
                case "F6":
                    bridge?.ToggleFace();
                    break;
                case "Alt+Left":
                    bridge?.GoBack();
                    break;
                case "Alt+Right":
                    bridge?.GoForward();
                    break;
            }
        };
        follow = new DispatcherTimer { Interval = FollowInterval };
        follow.Tick += (_, _) => Follow();
        AttachedToVisualTree += (_, _) =>
        {
            top = TopLevel.GetTopLevel(this);
            ModalOverlay.StackChanged += OnCoversChanged;
            Controls.RetroMenu.OpenMenusChanged += OnMenusChanged;
            Refresh();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            ModalOverlay.StackChanged -= OnCoversChanged;
            Controls.RetroMenu.OpenMenusChanged -= OnMenusChanged;
            follow.Stop();
            top = null;
            Stream(null);
        };
    }

    /// <summary>The 3D view.</summary>
    internal ViewportHost Viewport { get; }

    /// <summary>The loading screen, shown while the world's index is made.</summary>
    internal Panel Cover { get; }

    /// <summary>The loading screen's train and caption.</summary>
    internal LoadingOverlay Loading { get; }

    private RailPattern Pattern { get; }

    private DropdownWindow? menu;

    /// <summary>The right click's menu, while it's open.</summary>
    internal DropdownWindow? Menu => menu;

    /// <summary>
    /// Picks the placement under a pixel of the view and outlines it (nothing there outlines nothing); a right click
    /// opens its menu there.
    /// </summary>
    private void PickAt(int x, int y, bool right)
    {
        if (bridge is not { InWorld: true } model || Viewport.Renderer is not { } renderer)
            return;
        Rendering.WorldPick? pick = renderer.PickWorldAt(x, y);
        renderer.PickWorld(pick);
        if (right && pick is not null && model.DescribePlacement(pick) is { } placement)
        {
            double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            OpenMenu(placement, new Point(x / scale, y / scale));
        }
    }

    /// <summary>
    /// A picked placement's menu at a point of the view (mockup 5A), over the viewport: its name and where it's from,
    /// then opening its model, its model's and map's files, and copying its name and coordinates.
    /// </summary>
    internal void OpenMenu(WorldPlacement placement, Point at)
    {
        menu?.Close();
        if (bridge is not { } model)
            return;
        var items = new StackPanel { Margin = Ui.Theme.MenuInset, MinWidth = 280 };
        items.Children.Add(new StackPanel
        {
            Margin = new Thickness(10, 6, 10, 6),
            Spacing = 2,
            Children =
            {
                new TextBlock
                {
                    Text = placement.Name,
                    FontFamily = Ui.Theme.UiFont,
                    FontSize = Ui.Theme.FontSize,
                    FontWeight = Avalonia.Media.FontWeight.SemiBold,
                    Foreground = Ui.Theme.Text,
                },
                new TextBlock
                {
                    Text = placement.Detail,
                    FontFamily = Ui.Theme.MonoFont,
                    FontSize = Ui.Theme.SmallFontSize,
                    Foreground = Ui.Theme.TextDim,
                },
            },
        });
        items.Children.Add(new RetroMenuSeparator());
        void Item(string icon, string text, Action action, bool enabled = true)
        {
            var row = new PartsPanel.MenuRowItem(icon, text, "") { IsEnabled = enabled };
            row.Triggered += () =>
            {
                menu?.Close();
                action();
            };
            items.Children.Add(row);
        }
        Item("box", "Open model", () => model.OpenWorldModel(placement.Model!), placement.Model is not null);
        Item("folder", "Show model in Files", () => model.ShowWorldFile(placement.Model!), placement.Model is not null);
        Item("map", "Show map file in Files", () => model.ShowWorldFile(placement.Map!), placement.Map is not null);
        items.Children.Add(new RetroMenuSeparator());
        Item("copy", "Copy name", () => model.CopyWorldText(placement.Name, "name"));
        Item("copy", "Copy coordinates", () => model.CopyWorldText(placement.Coordinates, "coordinates"));
        menu = DropdownWindow.OpenAt(this, at, items);
        if (menu is not null)
            menu.Closed += (_, _) => menu = null;
    }

    /// <summary>What streams the world in, once its index is ready; none before, or without a world.</summary>
    internal WorldStreamer? Streamer => streamer;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (bridge is not null)
        {
            Follow();
            bridge.PropertyChanged -= OnBridgePropertyChanged;
            bridge.WorldSpotRequested -= OnWorldSpotRequested;
        }
        bridge = DataContext as ExplorerViewModel;
        if (bridge is not null)
        {
            bridge.PropertyChanged += OnBridgePropertyChanged;
            bridge.WorldSpotRequested += OnWorldSpotRequested;
            // Another tab: its camera where it left it, at its time of day.
            Place();
            Viewport.SetTime(bridge.WorldHours);
        }
        Refresh();
    }

    private void OnBridgePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ExplorerViewModel.InWorld):
                if (bridge?.InWorld == true)
                    Place();
                Refresh();
                break;
            case nameof(ExplorerViewModel.GameLoading):
            case nameof(ExplorerViewModel.WorldReady):
            case nameof(ExplorerViewModel.WorldPreparing):
            case nameof(ExplorerViewModel.WorldStage):
            case nameof(ExplorerViewModel.WorldProgress):
            case nameof(ExplorerViewModel.WorldFailure):
                Refresh();
                break;
            case nameof(ExplorerViewModel.WorldHours):
                Viewport.SetTime(bridge!.WorldHours);
                break;
        }
    }

    private void OnWorldSpotRequested(object? sender, EventArgs e) => Place();

    private void OnCoversChanged(object? sender, TopLevel window)
    {
        if (window == top)
            Refresh();
    }

    private void OnMenusChanged(object? sender, EventArgs e) => Refresh();

    /// <summary>Puts the camera at the tab's spot: where it was left, or the game's landmark.</summary>
    private void Place()
    {
        if (bridge is not { HasWorld: true } model)
            return;
        WorldSpot spot = model.WorldSpot;
        Viewport.Frame(new CommonFury.Numerics.Aabb3(spot.Eye - new Vector3(Reach), spot.Eye + new Vector3(Reach)));
        Viewport.Place(spot.Eye, spot.Yaw, spot.Pitch);
    }

    // Tells the tab where the camera is, and what's streamed, while the world is shown.
    private void Follow()
    {
        if (bridge is { InWorld: true } model && Viewport.CameraSpot is { } camera)
        {
            model.RememberWorldSpot(new WorldSpot(camera.Eye, camera.Yaw, camera.Pitch));
            if (streamer is { Status.Length: > 0 } streaming)
                model.SetWorldStatus(streaming.Status);
        }
    }

    /// <summary>
    /// Streams the world whose index this is, or none: what another world streamed goes. Tabs of the same game share
    /// one index, and what it streamed.
    /// </summary>
    private void Stream(WorldIndex? index)
    {
        if (ReferenceEquals(index, streamed))
            return;
        streamer?.Dispose();
        streamer = null;
        streamed = index;
        Viewport.Renderer?.ClearWorld();
        if (index is not null)
            streamer = new WorldStreamer(index);
    }

    /// <summary>Whether something the main window draws covers the viewport now: a dialog, a menu, the loading cover.</summary>
    internal bool Covered => top is not null && (ModalOverlay.IsDrawnIn(top) || Controls.RetroMenu.AnyOpenOver(top)) || bridge?.GameLoading == true;

    private void Refresh()
    {
        Stream(top is null ? null : bridge?.WorldIndex);
        // The loading screen while the index is made: the stage, small and capitalised, and how far, when known.
        bool preparing = bridge is { InWorld: true, WorldPreparing: true };
        Cover.IsVisible = preparing;
        Pattern.IsRunning = Loading.IsRunning = preparing;
        if (bridge?.WorldFailure is { } failure)
        {
            // Why it couldn't be made, the train standing still.
            Loading.Title = "COULDN'T PREPARE THE WORLD";
            Loading.Detail = failure;
            Loading.Progress = null;
            Pattern.IsRunning = Loading.IsRunning = false;
        }
        else if (bridge is not null)
        {
            Loading.Title = bridge.WorldStage.ToUpperInvariant();
            Loading.Detail = "Preparing the world";
            Loading.Progress = bridge.WorldProgress;
            Loading.ProgressText = bridge.WorldProgress is { } share
                ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{share * 100:0}%") : null;
        }
        bool shown = bridge is { InWorld: true, WorldReady: true } && !Covered;
        int request = ++shownRequest;
        if (!shown)
        {
            Viewport.IsVisible = false;
            follow.Stop();
        }
        else if (!Viewport.IsVisible)
        {
            // Back once the cover has gone, unless something covered it again meanwhile.
            DispatcherTimer.RunOnce(() =>
            {
                if (request != shownRequest)
                    return;
                Viewport.IsVisible = true;
                follow.Start();
                Dispatcher.UIThread.Post(Viewport.FocusViewport, DispatcherPriority.Input);
            }, SettleTime);
        }
        else
        {
            follow.Start();
        }
    }
}
