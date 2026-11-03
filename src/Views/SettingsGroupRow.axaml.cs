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

    private readonly Border surface;
    private readonly TextBlock labelText;
    private readonly TextBlock noteText;

    static SettingsGroupRow()
    {
        HeightProperty.OverrideDefaultValue<SettingsGroupRow>(34);
    }

    public SettingsGroupRow()
    {
        AvaloniaXamlLoader.Load(this);
        surface = this.FindControl<Border>("Surface")!;
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
        surface.Background = Selected ? Ui.Theme.SelectionWash : Brushes.Transparent;
        surface.BorderBrush = Selected ? Ui.Theme.SelectionRing : Brushes.Transparent;
        labelText.Text = Label;
        labelText.Foreground = Selected ? Ui.Theme.Text : Available ? Ui.Theme.TextRow : Ui.Theme.TextFaint;
        labelText.FontWeight = Selected ? FontWeight.SemiBold : FontWeight.Normal;
        noteText.Text = Note;
    }
}
