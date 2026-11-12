using Avalonia.Automation;
using Avalonia.Controls;

namespace HikariHopper.Views;

/// <summary>
/// Confirms importing images whose names collide with existing textures: how many are new and how many replace a
/// texture, each with their names. Cancel or Esc declines; the dialog closes by itself once the view model no longer
/// has a pending import.
/// </summary>
public partial class TextureImportDialog : TextureToolDialog
{
    public TextureImportDialog()
    {
        InitializeComponent();
        ApplyAction = () => ViewModel?.ConfirmImageImport(true) == true;
        Rejected += (_, _) => ViewModel?.ConfirmImageImport(false);
    }

    /// <summary>The names of the images that would be new textures, as listed.</summary>
    internal IReadOnlyList<string> NewNames { get; private set; } = [];

    /// <summary>The names of the textures the import would replace, as listed.</summary>
    internal IReadOnlyList<string> ReplacedNames { get; private set; } = [];

    /// <summary>The lines under the cells, as shown.</summary>
    internal IReadOnlyList<string> NotesShown { get; private set; } = [];

    protected override void OnOpened()
    {
        base.OnOpened();
        Refresh();
    }

    protected override void OnViewModelChanged(string? property)
    {
        base.OnViewModelChanged(property);
        Refresh();
        // QML Connections { onStateChanged: if (!importConfirmationPending) close() }.
        if (ViewModel is { } vm && property == nameof(vm.ImportConfirmationPending) && !vm.ImportConfirmationPending && IsOpen)
            Close();
    }

    private void Refresh()
    {
        if (Cells is null || Notes is null || ViewModel is not { } vm)
            return;
        ApplyEnabled = vm.ImportConfirmationPending;
        if (!vm.ImportConfirmationPending)
            return;
        Heading = vm.ImportFileCount == 1 ? "IMPORT 1 IMAGE" : $"IMPORT {vm.ImportFileCount} IMAGES";
        NewNames = vm.ImportNewNames;
        ReplacedNames = vm.ImportReplacedNames;
        Cells.Children.Clear();
        Cells.Children.Add(TextureOutcome.Count("NEW", NewNames.Count, NewNames, outlined: false));
        Border replaces = TextureOutcome.Count("REPLACES", ReplacedNames.Count, ReplacedNames, outlined: true);
        Grid.SetColumn(replaces, 2);
        Cells.Children.Add(replaces);
        IReadOnlyList<string> shared = vm.ImportSharedNames;
        NotesShown = shared.Count switch
        {
            0 => [],
            1 => [$"More than one file is named {shared[0]}: the last one wins."],
            _ => [$"{shared.Count} names are shared by more than one file: the last file of each wins."],
        };
        Notes.Children.Clear();
        foreach (string note in NotesShown)
            Notes.Children.Add(TextureOutcome.Warning(note));
        Notes.IsVisible = NotesShown.Count > 0;
        AutomationProperties.SetName(Cells, $"{NewNames.Count} new, {ReplacedNames.Count} replace a texture");
        FitBody();
    }
}
