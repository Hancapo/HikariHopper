using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace HikariHopper.Views;

/// <summary>A Settings panel header strip (port of <c>SettingsPanelHeader.qml</c>).</summary>
public partial class SettingsPanelHeader : UserControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<SettingsPanelHeader, string>(nameof(Label), "");

    private readonly TextBlock labelText;

    static SettingsPanelHeader()
    {
        HeightProperty.OverrideDefaultValue<SettingsPanelHeader>(Ui.Theme.HeaderHeight);
    }

    public SettingsPanelHeader()
    {
        AvaloniaXamlLoader.Load(this);
        labelText = this.FindControl<TextBlock>("LabelText")!;
    }

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LabelProperty)
            labelText.Text = Label;
    }
}
