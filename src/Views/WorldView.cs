using System.ComponentModel;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using HikariHopper.ViewModels;

namespace HikariHopper.Views;

/// <summary>
/// A game's world, on its tab's World side (<see cref="ExplorerViewModel.InWorld"/>): the 3D view, under the address row
/// and over the status bar, with a camera that flies (W A S D, Q and E down and up, drag to look, wheel for speed). It
/// starts where the tab's camera was left (<see cref="ExplorerViewModel.WorldSpot"/>) and says where it is as it moves.
/// One view serves every tab, as the files' views do, moving its camera to the tab shown.
/// <para>
/// The viewport is a native window, which covers whatever the main window draws over it: it hides while a dialog or a
/// menu is open over the window, or the tab's loading cover is up.
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

    public WorldView()
    {
        Viewport = new ViewportHost { FreeCamera = true };
        Content = Viewport;
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
        };
    }

    /// <summary>The 3D view.</summary>
    internal ViewportHost Viewport { get; }

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

    // Tells the tab where the camera is, while the world is shown.
    private void Follow()
    {
        if (bridge is { InWorld: true } model && Viewport.CameraSpot is { } camera)
            model.RememberWorldSpot(new WorldSpot(camera.Eye, camera.Yaw, camera.Pitch));
    }

    /// <summary>Whether something the main window draws covers the viewport now: a dialog, a menu, the loading cover.</summary>
    internal bool Covered => top is not null && (ModalOverlay.IsOpen(top) || Controls.RetroMenu.AnyOpenOver(top)) || bridge?.GameLoading == true;

    private void Refresh()
    {
        bool shown = bridge is { InWorld: true } && !Covered;
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
        if (bridge is { InWorld: true } world)
            world.SetWorldStatus("No map streamed yet");
    }
}
