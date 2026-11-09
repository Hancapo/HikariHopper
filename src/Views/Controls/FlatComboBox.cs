using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace HikariHopper.Views.Controls;

/// <summary>
/// One entry of a <see cref="FlatComboBox"/> model: QML's <c>{ label, value, supported, note }</c>.
/// An unsupported entry is listed disabled with its <see cref="Note"/> as the reason; a supported one's note says
/// something about it (what a format costs), at the right of its row and of the closed box.
/// </summary>
public sealed record FlatComboOption(string Label, object? Value = null, bool Supported = true, string? Note = null);

/// <summary>
/// A square, sunken combo box in the house style (port of <c>FlatComboBox.qml</c>): an inset well,
/// a drawn caret, and a popup built like <see cref="RetroMenu"/>.
/// </summary>
/// <remarks>
/// Items are <see cref="FlatComboOption"/>s (or any object, shown by <c>ToString()</c>).
/// <see cref="SelectingItemsControl.SelectedIndex"/> is QML <c>currentIndex</c>; <see cref="CurrentValue"/>
/// is <c>currentValue</c> (the option's <see cref="FlatComboOption.Value"/>); <see cref="Activated"/>
/// is raised when the user picks an entry. The mouse wheel never changes the selection, as Qt's
/// ComboBox leaves <c>wheelEnabled</c> off.
/// </remarks>
public class FlatComboBox : ComboBox
{
    public static readonly DirectProperty<FlatComboBox, string> DisplayTextProperty =
        AvaloniaProperty.RegisterDirect<FlatComboBox, string>(nameof(DisplayText), o => o.DisplayText);

    public static readonly DirectProperty<FlatComboBox, bool> CurrentSupportedProperty =
        AvaloniaProperty.RegisterDirect<FlatComboBox, bool>(nameof(CurrentSupported), o => o.CurrentSupported);

    public static readonly DirectProperty<FlatComboBox, string> CurrentNoteProperty =
        AvaloniaProperty.RegisterDirect<FlatComboBox, string>(nameof(CurrentNote), o => o.CurrentNote);

    public static readonly DirectProperty<FlatComboBox, object?> CurrentValueProperty =
        AvaloniaProperty.RegisterDirect<FlatComboBox, object?>(nameof(CurrentValue), o => o.CurrentValue);

    public static readonly DirectProperty<FlatComboBox, int> HighlightedIndexProperty =
        AvaloniaProperty.RegisterDirect<FlatComboBox, int>(nameof(HighlightedIndex), o => o.HighlightedIndex);

    private static readonly IImmutableSolidColorBrush HoverWell = new ImmutableSolidColorBrush(Ui.Theme.Lighter(Ui.Theme.InsetBg.Color, 1.35));

    private string displayText = "";
    private bool currentSupported = true;
    private string currentNote = "";
    private object? currentValue;
    private int highlightedIndex = -1;
    private Border? background;
    private TextBlock? display;
    private ChromeIcon? caret;
    private Border? popupFrame;

    public event EventHandler<int>? Activated;

    public string DisplayText
    {
        get => displayText;
        private set => SetAndRaise(DisplayTextProperty, ref displayText, value);
    }

    /// <summary>Whether the entry the user is currently sitting on can actually be opened.</summary>
    public bool CurrentSupported
    {
        get => currentSupported;
        private set => SetAndRaise(CurrentSupportedProperty, ref currentSupported, value);
    }

    public string CurrentNote
    {
        get => currentNote;
        private set => SetAndRaise(CurrentNoteProperty, ref currentNote, value);
    }

    public object? CurrentValue
    {
        get => currentValue;
        private set => SetAndRaise(CurrentValueProperty, ref currentValue, value);
    }

    /// <summary>Qt <c>highlightedIndex</c>: the popup row under the pointer or keyboard.</summary>
    public int HighlightedIndex
    {
        get => highlightedIndex;
        private set
        {
            if (SetAndRaise(HighlightedIndexProperty, ref highlightedIndex, value))
                RefreshHighlight();
        }
    }

    public static string LabelOf(object? item) => item switch
    {
        null => "",
        FlatComboOption option => option.Label,
        _ => item.ToString() ?? "",
    };

    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey) => new FlatComboBoxItem();

    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey) =>
        NeedsContainer<FlatComboBoxItem>(item, out recycleKey);

    protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        base.PrepareContainerForItemOverride(container, item, index);
        if (container is FlatComboBoxItem option)
        {
            option.Label = LabelOf(item);
            option.Supported = item is not FlatComboOption { Supported: false };
            option.Note = item is FlatComboOption { Note: { } note } ? note : "";
            option.IsEnabled = option.Supported;
            option.IsHighlighted = index == HighlightedIndex;
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        background = e.NameScope.Find<Border>("PART_Background");
        display = e.NameScope.Find<TextBlock>("PART_Display");
        caret = e.NameScope.Find<ChromeIcon>("PART_Caret");
        popupFrame = e.NameScope.Find<Border>("PART_PopupFrame");
        UpdateCurrent();
        Refresh();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedIndexProperty || change.Property == SelectedItemProperty
            || change.Property == ItemsSourceProperty)
            UpdateCurrent();
        if (change.Property == IsDropDownOpenProperty)
        {
            if (IsDropDownOpen)
            {
                if (popupFrame is not null)
                    popupFrame.Width = Bounds.Width;
                HighlightedIndex = SelectedIndex;
            }
        }
        if (change.Property == IsPointerOverProperty || change.Property == IsFocusedProperty
            || change.Property == IsDropDownOpenProperty || change.Property == SelectedIndexProperty)
            Refresh();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (IsDropDownOpen)
            base.OnPointerWheelChanged(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        int before = SelectedIndex;
        base.OnKeyDown(e);
        if (!IsDropDownOpen && SelectedIndex != before && SelectedIndex >= 0)
            Activated?.Invoke(this, SelectedIndex);
    }

    public override bool UpdateSelectionFromEvent(Control container, RoutedEventArgs eventArgs)
    {
        bool updated = base.UpdateSelectionFromEvent(container, eventArgs);
        if (updated && IndexFromContainer(container) is var index and >= 0)
            Activated?.Invoke(this, index);
        return updated;
    }

    internal void Highlight(FlatComboBoxItem item)
    {
        int index = IndexFromContainer(item);
        if (index >= 0)
            HighlightedIndex = index;
    }

    private void RefreshHighlight()
    {
        foreach (Control container in GetRealizedContainers())
        {
            if (container is FlatComboBoxItem option)
                option.IsHighlighted = IndexFromContainer(option) == HighlightedIndex;
        }
    }

    private void UpdateCurrent()
    {
        object? item = SelectedIndex >= 0 ? ItemAt(SelectedIndex) : null;
        DisplayText = LabelOf(item);
        CurrentSupported = SelectedIndex < 0 || item is not FlatComboOption { Supported: false };
        CurrentNote = item is FlatComboOption { Note: { } note } ? note : "";
        CurrentValue = item is FlatComboOption option ? option.Value : item;
    }

    private object? ItemAt(int index)
    {
        if (ItemsSource is IList list)
            return index < list.Count ? list[index] : null;
        return index < Items.Count ? Items[index] : null;
    }

    private void Refresh()
    {
        if (background is null || display is null || caret is null)
            return;
        bool open = IsDropDownOpen;
        background.Background = IsPointerOver && !open ? HoverWell : Ui.Theme.InsetBg;
        background.BorderBrush = IsFocused || open
            ? Ui.Theme.Accent
            : IsPointerOver ? Ui.Theme.Border : Ui.Theme.BorderHard;
        display.Text = DisplayText;
        display.Foreground = CurrentSupported ? Ui.Theme.Text : Ui.Theme.TextFaint;
        caret.Stroke = IsPointerOver || open ? Ui.Theme.Text : Ui.Theme.TextDim;
    }
}

/// <summary>A row of the <see cref="FlatComboBox"/> popup: 24px, accent fill while highlighted.</summary>
public class FlatComboBoxItem : ComboBoxItem
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<FlatComboBoxItem, string>(nameof(Label), "");

    public static readonly StyledProperty<string> NoteProperty =
        AvaloniaProperty.Register<FlatComboBoxItem, string>(nameof(Note), "");

    public static readonly StyledProperty<bool> SupportedProperty =
        AvaloniaProperty.Register<FlatComboBoxItem, bool>(nameof(Supported), true);

    public static readonly StyledProperty<bool> IsHighlightedProperty =
        AvaloniaProperty.Register<FlatComboBoxItem, bool>(nameof(IsHighlighted));

    private Border? background;
    private TextBlock? label;
    private TextBlock? note;

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

    public bool Supported
    {
        get => GetValue(SupportedProperty);
        set => SetValue(SupportedProperty, value);
    }

    public bool IsHighlighted
    {
        get => GetValue(IsHighlightedProperty);
        set => SetValue(IsHighlightedProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        background = e.NameScope.Find<Border>("PART_Background");
        label = e.NameScope.Find<TextBlock>("PART_Label");
        note = e.NameScope.Find<TextBlock>("PART_Note");
        Refresh();
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        if (Supported)
            Owner?.Highlight(this);
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        Owner?.Highlight(this);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsHighlightedProperty || change.Property == SupportedProperty
            || change.Property == NoteProperty)
            Refresh();
    }

    private FlatComboBox? Owner => ItemsControl.ItemsControlFromItemContainer(this) as FlatComboBox;

    /// <summary>Whether the row shows its note.</summary>
    internal bool NoteShown => note?.IsVisible == true;

    private void Refresh()
    {
        if (background is null || label is null || note is null)
            return;
        background.Background = IsHighlighted && Supported ? Ui.Theme.Accent : Brushes.Transparent;
        label.Foreground = !Supported ? Ui.Theme.TextFaint : IsHighlighted ? Ui.Theme.Text : Ui.Theme.TextRow;
        // A note shows for any option that has one: why a disabled one is, or what a supported one costs.
        note.IsVisible = Note.Length > 0;
        note.Foreground = IsHighlighted && Supported ? Ui.Theme.Text : Ui.Theme.TextDim;
    }
}
