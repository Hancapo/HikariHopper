using Avalonia.Controls;
using HikariHopper.ViewModels;

namespace HikariHopper.Views;

/// <summary>
/// A file open with changes not saved: its name, where it is open ("Texture editor · tab 2"), the tab that has it,
/// how its own editor saves it, and how that editor is brought forward to show why it couldn't.
/// </summary>
public sealed record UnsavedFile(string Name, string Where, ExplorerViewModel? Tab, Func<Task<bool>> Save, Action BringForward);

/// <summary>
/// What the session has unsaved, everywhere or in one tab: the text viewer's edits, the 3D viewer's shader values and
/// each tab's texture document. It knows which saves are being written, waits for them, and saves the files as their
/// own editors do.
/// </summary>
internal sealed class UnsavedWork
{
    private static readonly TimeSpan IdleCheck = TimeSpan.FromMilliseconds(25);

    private readonly ExplorerSession session;

    public UnsavedWork(ExplorerSession session) => this.session = session;

    /// <summary>The files unsaved: in <paramref name="tab"/> and the viewers it opened, or, without one, anywhere.</summary>
    public IReadOnlyList<UnsavedFile> Files(ExplorerViewModel? tab = null)
    {
        var files = new List<UnsavedFile>();
        if (session.TextViewer is { IsOpen: true, IsModified: true } text && Of(session.TextViewerTab, tab))
        {
            ExplorerViewModel? owner = session.TextViewerTab;
            files.Add(new UnsavedFile(text.DocumentName, Where("Text editor", owner), owner, text.SaveBeforeClosing,
                () => BringForward(owner, text)));
        }
        if (session.ModelViewer is { IsOpen: true, HasUnsavedChanges: true } model && Of(session.ModelViewerTab, tab))
        {
            ExplorerViewModel? owner = session.ModelViewerTab;
            files.Add(new UnsavedFile(model.DocumentName, Where("3D viewer · shader values", owner), owner, model.SaveBeforeClosing,
                () => BringForward(owner, model)));
        }
        foreach (ExplorerViewModel each in session.Tabs.Tabs)
        {
            TextureViewerViewModel textures = each.TextureViewer;
            if (!Of(each, tab) || !textures.HasDocument || !textures.Modified)
                continue;
            files.Add(new UnsavedFile(textures.SourceName, Where("Texture editor", each), each, textures.SaveDocument,
                () => BringForward(each, session.TextureEditors.GetValueOrDefault(each))));
        }
        return files;
    }

    /// <summary>Whether a save is being written (or a texture operation runs) in <paramref name="tab"/>, or anywhere.</summary>
    public bool Busy(ExplorerViewModel? tab = null) =>
        session.TextViewer is { IsSaving: true } && Of(session.TextViewerTab, tab)
        || session.ModelViewer is { IsSaving: true } && Of(session.ModelViewerTab, tab)
        || session.Tabs.Tabs.Any(each => Of(each, tab) && each.TextureViewer.OperationBusy);

    /// <summary>Done once nothing is being saved, there or anywhere: at once when nothing is.</summary>
    public async Task WhenIdle(ExplorerViewModel? tab = null)
    {
        while (Busy(tab))
            await Task.Delay(IdleCheck);
    }

    /// <summary>Saves the files one after another, as their own editors do; the first that isn't saved, or null.</summary>
    public static async Task<UnsavedFile?> SaveAll(IReadOnlyList<UnsavedFile> files)
    {
        foreach (UnsavedFile file in files)
        {
            if (!await file.Save())
                return file;
        }
        return null;
    }

    // A file in no tab of its own (a viewer opened without one) goes with every close.
    private static bool Of(ExplorerViewModel? owner, ExplorerViewModel? tab) => tab is null || owner == tab;

    private string Where(string editor, ExplorerViewModel? owner)
    {
        int index = owner is null ? -1 : session.Tabs.Tabs.IndexOf(owner);
        return session.Tabs.Tabs.Count > 1 && index >= 0 ? $"{editor} · tab {index + 1}" : editor;
    }

    private void BringForward(ExplorerViewModel? owner, Control? editor)
    {
        if (owner is not null && session.Tabs.Tabs.IndexOf(owner) is var index and >= 0)
            session.Tabs.ActivateTab(index);
        if (editor is TextureEditor { IsOpen: false } textures)
            textures.Present();
        if (editor is not null)
            ModalOverlay.BringToFront(editor);
    }
}
