using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HikariHopper.ViewModels;

namespace HikariHopper.Views;

/// <summary>
/// Names a new folder, texture dictionary or RPF (port of <c>ExplorerCreateDialog.qml</c>). Open it
/// with <see cref="Begin"/>; it closes itself when the creation starts or is cancelled.
/// </summary>
public partial class ExplorerCreateDialog : UserControl
{
    private readonly ExplorerViewModel bridge;
    private ModalOverlay? overlay;
    private string creationKind = "";
    private string sourcePath = "";
    private string nameError = "";

    public ExplorerCreateDialog() : this(new ExplorerViewModel())
    {
    }

    public ExplorerCreateDialog(ExplorerViewModel bridge)
    {
        this.bridge = bridge;
        InitializeComponent();
        NameField.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
                UpdateNameValidation();
        };
        // Keys.onReturnPressed / onEnterPressed, ahead of the TextBox's own handling.
        NameField.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;
            Submit();
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        SourceBox.SizeChanged += (_, _) => RefreshSource();
    }

    /// <summary>QML <c>closed</c>.</summary>
    public event EventHandler? Closed;

    public string CreationKind => creationKind;

    public string SourcePath => sourcePath;

    public string NameError => nameError;

    public bool HasSource => sourcePath.Length > 0;

    /// <summary><c>ytd</c>, <c>entry</c> (folders) or <c>rpf</c>.</summary>
    public string NameKind => creationKind == "ytd" ? "ytd" : creationKind == "folder" ? "entry" : "rpf";

    public string Heading => creationKind switch
    {
        "folder" => "NEW FOLDER",
        "ytd" => "NEW TEXTURE DICTIONARY",
        "empty-rpf" => "NEW EMPTY RPF",
        "folder-rpf" => "RPF FROM FOLDER",
        "zip-rpf" => "RPF FROM ZIP",
        _ => "NEW ENTRY",
    };

    internal TextBox Field => NameField;

    /// <summary>QML <c>begin(kind, suggestedName, source)</c>: fills the dialog and opens it over <paramref name="anchor"/>'s window.</summary>
    public void Begin(Control anchor, string kind, string suggestedName, string source)
    {
        creationKind = kind;
        sourcePath = source;
        HeadingText.Text = Heading;
        NameField.Text = suggestedName;
        UpdateNameValidation();
        RefreshSource();
        bridge.PropertyChanged += OnBridgePropertyChanged;
        overlay = ModalOverlay.Show(anchor, this);
        if (overlay is null)
            return;
        overlay.Closed += (_, _) =>
        {
            bridge.PropertyChanged -= OnBridgePropertyChanged;
            Closed?.Invoke(this, EventArgs.Empty);
        };
        // onOpened (once the popup is laid out): nameField.forceActiveFocus(); nameField.selectAll()
        NameField.SelectAll();
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            NameField.Focus(NavigationMethod.Pointer);
            NameField.SelectAll();
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>QML <c>reject()</c> / <c>accept()</c>.</summary>
    public void Close() => overlay?.Close();

    private void OnBridgePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExplorerViewModel.EntryOperationBusy))
            RefreshConfirm();
    }

    private void UpdateNameValidation()
    {
        nameError = bridge.CreationNameError(NameField.Text ?? "", NameKind);
        NameErrorText.Text = nameError;
        NameErrorText.IsVisible = nameError.Length > 0;
        NameField.Invalid = nameError.Length > 0;
        Height = Ui.Theme.HeaderHeight + (HasSource ? 164 : 112) + (nameError.Length == 0 ? 0 : 22) + 58;
        RefreshConfirm();
    }

    private void RefreshConfirm() => ConfirmButton.IsEnabled = nameError.Length == 0 && !bridge.EntryOperationBusy;

    private void RefreshSource()
    {
        SourceLabel.IsVisible = HasSource;
        SourceBox.IsVisible = HasSource;
        double width = SourceBox.Bounds.Width - 18;
        SourceText.Text = width > 0
            ? QtElide.Middle(sourcePath, Ui.Theme.MonoFont, Ui.Theme.SmallFontSize, width)
            : sourcePath;
    }

    /// <summary>QML <c>submit()</c>.</summary>
    public void Submit()
    {
        UpdateNameValidation();
        if (nameError.Length > 0 || bridge.EntryOperationBusy)
            return;
        string name = (NameField.Text ?? "").Trim();
        bool started = creationKind switch
        {
            "folder" => bridge.CreateFolder(name),
            "ytd" => bridge.CreateYtd(name),
            "empty-rpf" => bridge.CreateEmptyRpf(name),
            "folder-rpf" => bridge.CreateRpfFromFolder(name, sourcePath),
            "zip-rpf" => bridge.CreateRpfFromZip(name, sourcePath),
            _ => false,
        };
        if (started)
            Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void OnCreate(object? sender, RoutedEventArgs e) => Submit();
}
