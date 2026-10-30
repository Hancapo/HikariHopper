using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace HikariHopper.Views;

/// <summary>A group in the Settings rail (port of <c>SettingsGroupRow.qml</c>).</summary>
public partial class SettingsGroupRow : UserControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<SettingsGroupRow, string>(nameof(Label), "");

    public static readonly StyledProperty<string> NoteProperty =
        AvaloniaProperty.Register<SettingsGroupRow, string>(nameof(Note), "");

    public static readonly StyledProperty<bool> SelectedProperty =
        AvaloniaProperty.Register<SettingsGroupRow, bool>(nameof(Selected));

    public static readonly StyledProperty<bool> AvailableProperty =
        AvaloniaProperty.Register<SettingsGroupRow, bool>(nameof(Available), true);

    private readonly Panel surface;
    private readonly Border marker;
    private readonly TextBlock labelText;
    private readonly TextBlock noteText;

    static SettingsGroupRow()
    {
        HeightProperty.OverrideDefaultValue<SettingsGroupRow>(44);
    }

    public SettingsGroupRow()
    {
        AvaloniaXamlLoader.Load(this);
        surface = this.FindControl<Panel>("Surface")!;
        marker = this.FindControl<Border>("Marker")!;
        labelText = this.FindControl<TextBlock>("LabelText")!;
        noteText = this.FindControl<TextBlock>("NoteText")!;
        Refresh();
    }

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Note
    {
        get => GetValue(NoteProperty);
        set => SetValue(NoteProperty, value);
    }

    public bool Selected
    {
        get => GetValue(SelectedProperty);
        set => SetValue(SelectedProperty, value);
    }

    public bool Available
    {
        get => GetValue(AvailableProperty);
        set => SetValue(AvailableProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LabelProperty || change.Property == NoteProperty
            || change.Property == SelectedProperty || change.Property == AvailableProperty)
            Refresh();
    }

    private void Refresh()
    {
        if (surface is null)
            return;
        surface.Background = Selected ? Ui.Theme.Selection : Brushes.Transparent;
        marker.IsVisible = Selected;
        labelText.Text = Label;
        labelText.Foreground = Selected ? Ui.Theme.SelectionText : Ui.Theme.TextFaint;
        labelText.FontWeight = Selected ? FontWeight.Bold : FontWeight.Normal;
        labelText.Opacity = Available ? 1 : 0.7;
        noteText.Text = Note;
        noteText.Foreground = Selected ? Ui.Theme.SelectionInk : Ui.Theme.TextFaint;
        noteText.Opacity = Available ? 1 : 0.7;
    }
}
