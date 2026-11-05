using Avalonia.Headless;
using Avalonia.Input;
using FiveFury.Rpf;
using HikariHopper.ViewModels;
using HikariHopper.Views;

namespace HikariHopper.Tests;

/// <summary>Renaming a file or folder, on disk or in an RPF.</summary>
[Collection(ExplorerCollection.Name)]
public sealed class RenameTests : ExplorerTestBase
{
    private static int Row(ExplorerViewModel bridge, string name) =>
        Enumerable.Range(0, bridge.EntriesModel.Count).Single(row => bridge.EntriesModel[row].Name == name);

    private ExplorerViewModel Game()
    {
        MakeGame();
        Write(Path.Combine(Work, "notes.txt"), "n");
        Write(Path.Combine(Work, "pack", "a.txt"), "a");
        var bridge = new ExplorerViewModel();
        bridge.OpenGame(Work);
        Settle(bridge);
        return bridge;
    }

    [AvaloniaFact]
    public void RenamingALooseFileMovesItAndSelectsIt()
    {
        ExplorerViewModel bridge = Game();
        bridge.SelectEntry(Row(bridge, "notes.txt"));
        Assert.True(bridge.CanRenameSelection);
        Assert.Equal("notes.txt", bridge.RenameSource);
        Assert.True(bridge.RenameSelected("readme.txt"));
        Settle(bridge);
        Assert.False(File.Exists(Path.Combine(Work, "notes.txt")));
        Assert.Equal("n", File.ReadAllText(Path.Combine(Work, "readme.txt")));
        Assert.Equal("Renamed notes.txt to readme.txt", bridge.Status);
        Assert.Equal("readme.txt", bridge.EntriesModel.Single(item => item.Selected).Name);
    }

    [AvaloniaFact]
    public void AFolderCanChangeOnlyTheCaseOfItsName()
    {
        ExplorerViewModel bridge = Game();
        bridge.SelectEntry(Row(bridge, "pack"));
        Assert.Equal("", bridge.RenameNameError("Pack"));
        Assert.True(bridge.RenameSelected("Pack"));
        Settle(bridge);
        Assert.Equal(["Pack"], new DirectoryInfo(Work).GetDirectories("pack").Select(folder => folder.Name));
        Assert.Equal("a", File.ReadAllText(Path.Combine(Work, "Pack", "a.txt")));
    }

    [AvaloniaFact]
    public void ATakenNameIsRefusedAndTheSameNameDoesNothing()
    {
        ExplorerViewModel bridge = Game();
        bridge.SelectEntry(Row(bridge, "notes.txt"));
        Assert.Equal("An entry with this name already exists", bridge.RenameNameError("PACK"));
        Assert.False(bridge.RenameSelected("pack"));
        Assert.True(bridge.RenameSelected("notes.txt"));
        Assert.False(bridge.EntryOperationBusy);
        Assert.True(File.Exists(Path.Combine(Work, "notes.txt")));
        // Several selected: nothing to rename.
        bridge.SelectEntry(Row(bridge, "pack"), KeyModifiers.Control);
        Assert.False(bridge.CanRenameSelection);
    }

    [AvaloniaFact]
    public void RenamingInsideAnArchiveKeepsTheContent()
    {
        MakeGame();
        string path = SaveArchive(Path.Combine(Work, "mods.rpf"), ("data/carcols.meta", "<Item />"), ("readme.txt", "hello"));
        var bridge = new ExplorerViewModel();
        bridge.OpenArchive(path);
        bridge.SelectEntry(Row(bridge, "readme.txt"));
        Assert.True(bridge.RenameSelected("install.txt"));
        Settle(bridge);
        bridge.SelectEntry(Row(bridge, "data"));
        Assert.True(bridge.RenameSelected("meta"));
        Settle(bridge);
        Assert.Equal(["install.txt", "meta"], bridge.EntriesModel.Select(item => item.Name).Order());
        RpfArchive saved = ReadArchive(path);
        Assert.Null(saved.Find("readme.txt"));
        Assert.Equal("hello", System.Text.Encoding.UTF8.GetString(((RpfFileEntry)saved.Find("install.txt")!).ReadLogical()));
        Assert.Equal("<Item />", System.Text.Encoding.UTF8.GetString(((RpfFileEntry)saved.Find("meta/carcols.meta")!).ReadLogical()));
    }

    [AvaloniaFact]
    public void F2OpensTheRenameDialogWithTheNameBeforeItsExtensionPicked()
    {
        ExplorerViewModel bridgeForSetup = Game();
        _ = bridgeForSetup;
        var session = new ExplorerSession();
        var window = new MainWindow { Width = 1100, Height = 600 };
        session.Attach(window);
        window.Show();
        ExplorerViewModel bridge = session.Tabs.ActiveBridge;
        bridge.OpenGame(Work);
        Settle(bridge);
        ExplorerStateRenders.Pump(300);
        bridge.SelectEntry(Row(bridge, "notes.txt"));
        window.KeyPress(Key.F2, RawInputModifiers.None, PhysicalKey.F2, null);
        window.KeyRelease(Key.F2, RawInputModifiers.None, PhysicalKey.F2, null);
        ExplorerStateRenders.Pump(300);

        WorkspaceView workspace = window.GetVisualDescendantsOf<WorkspaceView>();
        ExplorerCreateDialog dialog = Assert.IsType<ExplorerCreateDialog>(workspace.CreateDialog);
        Assert.Equal("RENAME", dialog.Heading);
        Assert.Equal("Rename", dialog.ConfirmButton.Text);
        Assert.Equal("notes.txt", dialog.Field.Text);
        Assert.Equal((0, 5), (dialog.Field.SelectionStart, dialog.Field.SelectionEnd));

        dialog.Field.Text = "todo.txt";
        dialog.Submit();
        Settle(bridge);
        ExplorerStateRenders.Pump(300);
        Assert.True(File.Exists(Path.Combine(Work, "todo.txt")));
        window.Close();
    }
}

internal static class VisualFinding
{
    public static T GetVisualDescendantsOf<T>(this Avalonia.Visual visual) where T : Avalonia.Visual =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(visual).OfType<T>().First();
}
