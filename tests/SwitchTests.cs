using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using HikariHopper.Views.Controls;

namespace HikariHopper.Tests;

public sealed class SwitchTests
{
    [AvaloniaFact]
    public void ASegmentedSwitchIsOneWellWhoseRaisedSegmentSlidesToTheSideAskedFor()
    {
        var choice = new SegmentedSwitch
        {
            Sides =
            {
                new SwitchSide { Text = "RGBA", Width = 46, Label = "All channels" },
                new SwitchSide { Text = "R", Width = 26, Label = "Red channel" },
                new SwitchSide { Text = "A", Width = 26, Label = "Alpha channel" },
            },
        };
        var window = new Window { Width = 400, Height = 120, Content = choice };
        window.Show();
        TestWait.Pump(50);
        var asked = new List<int>();
        choice.SideRequested += (_, side) =>
        {
            asked.Add(side);
            choice.SelectedIndex = side;
        };
        void Click(int side)
        {
            Point point = choice.TranslatePoint(new Point(choice.SideStart(side) + 6, choice.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            TestWait.Settle();
        }
        double r = Ui.Theme.InnerRadius;

        // One well holding every side. The raised segment is as wide as the side in use, and rounded only
        // where it reaches an end of the well: straight wherever it meets a neighbour.
        Assert.Equal(46 + 26 + 26 + 4, choice.Bounds.Width, 3);
        Assert.Equal(46, choice.Segment.Bounds.Width, 3);
        Assert.Equal(new CornerRadius(r, 0, 0, r), choice.Segment.CornerRadius);
        Assert.Equal("All channels", Avalonia.Automation.AutomationProperties.GetName(choice));

        // A click on the side in use changes nothing; a click on another asks for it, and the segment
        // slides there.
        Click(0);
        Assert.Empty(asked);
        Click(1);
        Assert.Equal([1], asked);
        Assert.Equal(46, choice.SegmentOffset, 3);
        Assert.Equal(26, choice.Segment.Bounds.Width, 3);
        Assert.Equal(new CornerRadius(0), choice.Segment.CornerRadius);
        Click(2);
        Assert.Equal(new CornerRadius(0, r, r, 0), choice.Segment.CornerRadius);
        Assert.Equal("Alpha channel", Avalonia.Automation.AutomationProperties.GetName(choice));

        // Keys: Right stops at the end, Left steps back, Space moves on, wrapping round.
        choice.Focus();
        window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Assert.Equal([1, 2, 1, 2, 0], asked);

        // With no side in use the segment is hidden, and a click asks for the side clicked.
        choice.SelectedIndex = -1;
        Assert.False(choice.Segment.IsVisible);
        Click(0);
        Assert.Equal([1, 2, 1, 2, 0, 0], asked);

        // Screen readers see a list of the sides, the one in use selected, and can select another.
        var peer = Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(choice);
        Assert.Equal(Avalonia.Automation.Peers.AutomationControlType.List, peer.GetAutomationControlType());
        var list = Assert.IsAssignableFrom<Avalonia.Automation.Provider.ISelectionProvider>(peer);
        IReadOnlyList<Avalonia.Automation.Peers.AutomationPeer> items = peer.GetChildren();
        Assert.Equal(["All channels", "Red channel", "Alpha channel"], items.Select(item => item.GetName()));
        Assert.All(items, item => Assert.Equal(Avalonia.Automation.Peers.AutomationControlType.ListItem, item.GetAutomationControlType()));
        Assert.Same(items[0], Assert.Single(list.GetSelection()));
        Assert.IsAssignableFrom<Avalonia.Automation.Provider.ISelectionItemProvider>(items[2]).Select();
        Assert.Equal(2, choice.SelectedIndex);
        Assert.True(Assert.IsAssignableFrom<Avalonia.Automation.Provider.ISelectionItemProvider>(items[2]).IsSelected);
        Assert.Same(items[2], Assert.Single(list.GetSelection()));
        window.Close();
    }
}
