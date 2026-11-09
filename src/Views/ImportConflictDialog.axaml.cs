using Avalonia.Controls;
using Avalonia.Interactivity;
using HikariHopper.Core;
using HikariHopper.ViewModels;

namespace HikariHopper.Views;

/// <summary>
/// Asks what to do with a drop whose names are already in the folder: replace them (a folder merges), skip them,
/// keep both (the dropped ones as "name (2)"), or cancel the drop. <see cref="Open"/> shows it; it closes itself once
/// answered.
/// </summary>
public partial class ImportConflictDialog : UserControl
{
    /// <summary>The most names listed; the rest are counted.</summary>
    private const int Listed = 4;

    private readonly ExplorerViewModel bridge;
    private readonly ImportConflicts pending;
    private ModalOverlay? overlay;

    public ImportConflictDialog() : this(new ExplorerViewModel(), new ImportConflicts([], []))
    {
    }

    public ImportConflictDialog(ExplorerViewModel bridge, ImportConflicts pending)
    {
        this.bridge = bridge;
        this.pending = pending;
        InitializeComponent();
        QuestionText.Text = Question;
        NamesText.Text = Names;
        NamesText.IsVisible = pending.Names.Count > 1;
    }

    /// <summary>Raised once it has closed, answered or not.</summary>
    public event EventHandler? Closed;

    /// <summary>The question: the one name already here, or how many of the drop's are.</summary>
    public string Question => pending.Names.Count == 1
        ? $"“{pending.Names[0]}” is already here."
        : $"{pending.Names.Count} of the {pending.Sources.Count} items are already here.";

    /// <summary>The names already here, the first few, then how many more.</summary>
    public string Names => string.Join(", ", pending.Names.Take(Listed))
        + (pending.Names.Count > Listed ? $" and {pending.Names.Count - Listed} more" : "");

    public void Open(Control anchor)
    {
        overlay = ModalOverlay.Show(anchor, this);
        if (overlay is not null)
            overlay.Closed += (_, _) => Closed?.Invoke(this, EventArgs.Empty);
    }

    public void Close() => overlay?.Close();

    /// <summary>Carries on with the drop as answered, and closes.</summary>
    public void Answer(ImportConflict choice)
    {
        bridge.ResolveImport(pending, choice);
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void OnKeepBoth(object? sender, RoutedEventArgs e) => Answer(ImportConflict.KeepBoth);

    private void OnSkip(object? sender, RoutedEventArgs e) => Answer(ImportConflict.Skip);

    private void OnReplace(object? sender, RoutedEventArgs e) => Answer(ImportConflict.Replace);
}
