using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;

namespace HikariHopper.Views.Controls;

/// <summary>
/// A setting that is on or off: a <see cref="SegmentedSwitch"/> with Off and On sides, its raised segment on the
/// one in use. Clicking or the keyboard moves it, and <see cref="Toggled"/> says so.
/// </summary>
public sealed class OnOffSwitch : Decorator
{
    public static readonly StyledProperty<bool> IsOnProperty =
        AvaloniaProperty.Register<OnOffSwitch, bool>(nameof(IsOn));

    private readonly SegmentedSwitch segments = new() { Height = 26 };

    public OnOffSwitch()
    {
        segments.Sides.Add(new SwitchSide { Text = "Off", Width = 40, Label = "Off" });
        segments.Sides.Add(new SwitchSide { Text = "On", Width = 40, Label = "On" });
        segments.SelectedIndex = 0;
        segments.SideRequested += (_, side) => IsOn = side == 1;
        Child = segments;
    }

    /// <summary>Raised when <see cref="IsOn"/> changes.</summary>
    public event EventHandler? Toggled;

    public bool IsOn
    {
        get => GetValue(IsOnProperty);
        set => SetValue(IsOnProperty, value);
    }

    /// <summary>The switch itself, which takes focus.</summary>
    internal SegmentedSwitch Segments => segments;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsOnProperty)
        {
            segments.SelectedIndex = IsOn ? 1 : 0;
            Toggled?.Invoke(this, EventArgs.Empty);
        }
        else if (change.Property == AutomationProperties.NameProperty)
            AutomationProperties.SetName(segments, AutomationProperties.GetName(this));
    }
}
