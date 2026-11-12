using System.Numerics;
using Avalonia.Headless.XUnit;
using FiveFury.Drawables;
using FiveFury.Rpf;
using HikariHopper.Core;
using HikariHopper.Tests.Textures;
using HikariHopper.Rendering;
using HikariHopper.ViewModels;

namespace HikariHopper.Tests;

public sealed class ShaderSaveTests
{
    private static Views.ModelViewer Open(byte[] ydr, ModelSaveTarget? target)
    {
        var viewer = new Views.ModelViewer(new ModelFile("tri.ydr", ydr) { SaveTarget = target });
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!viewer.Reading.IsCompleted && clock.ElapsedMilliseconds < 10_000)
            ExplorerStateRenders.Pump(10);
        viewer.PickAt(new Vector3(.2f, .2f, 3), -Vector3.UnitZ);
        return viewer;
    }

    private static void Wait(Task task)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted && clock.ElapsedMilliseconds < 10_000)
            ExplorerStateRenders.Pump(10);
        Assert.True(task.IsCompleted);
    }

    // The triangle's bumpiness, typed on the card.
    private static void Bump(Views.ModelViewer viewer, string value)
    {
        Views.ScrubField field = viewer.Card.Field("bumpiness")!;
        field.StartTyping();
        field.Editor!.Text = value;
        field.FinishTyping(keep: true);
    }

    private static float BumpinessOf(byte[] ydr) =>
        ShaderEdits.Current(FiveFury.Ydr.Ydr.Read(ydr).Drawable.Materials[0], "bumpiness")[0];

    [AvaloniaFact]
    public void Ctrl_S_writes_the_file_again_with_the_values_changed()
    {
        byte[]? written = null;
        var finished = new List<bool>();
        bool prepared = false;
        Views.ModelViewer viewer = Open(ShaderValuesTests.Triangle(),
            new ModelSaveTarget(bytes => written = bytes, () => prepared = true, finished.Add));
        // Nothing changed, nothing to save.
        Assert.False(viewer.Command('S'));
        Assert.Equal("tri.ydr", viewer.TitleText.Text);

        Bump(viewer, "2.5");
        Assert.Equal("tri.ydr *", viewer.TitleText.Text);
        Assert.True(viewer.Command('S'));
        Wait(viewer.Saving);
        Assert.True(viewer.Saving.Result);
        Assert.True(prepared);
        Assert.Equal([true], finished);
        // The file as written has the new value, and is the file's now: no reset, no asterisk.
        Assert.Equal(2.5f, BumpinessOf(written!));
        Assert.False(viewer.ShaderEdits.Dirty);
        Assert.False(viewer.Card.ResetShown("bumpiness"));
        Assert.Equal("tri.ydr", viewer.TitleText.Text);
        Assert.Equal("Saved tri.ydr", viewer.StatusText.Text);
        viewer.Close();
    }

    [AvaloniaFact]
    public void A_save_that_fails_says_why_and_keeps_the_changes()
    {
        var finished = new List<bool>();
        Views.ModelViewer viewer = Open(ShaderValuesTests.Triangle(),
            new ModelSaveTarget(_ => throw new IOException("The file is in use."), Finished: finished.Add));
        Bump(viewer, "3");
        Assert.True(viewer.Save());
        Wait(viewer.Saving);
        Assert.False(viewer.Saving.Result);
        Assert.Equal([false], finished);
        Assert.Equal("Couldn't save tri.ydr: The file is in use.", viewer.StatusText.Text);
        Assert.True(viewer.ShaderEdits.Dirty);
        Assert.Equal("tri.ydr *", viewer.TitleText.Text);
    }

    [AvaloniaFact]
    public void Closing_with_unsaved_values_asks_first()
    {
        byte[]? written = null;
        Views.ModelViewer viewer = Open(ShaderValuesTests.Triangle(), new ModelSaveTarget(bytes => written = bytes));
        Bump(viewer, "2");

        // Asked: Cancel stays open, with the change.
        viewer.Close();
        Views.TextureUnsavedChangesDialog dialog = viewer.UnsavedDialog!;
        Assert.Equal("Save changes to tri.ydr?", dialog.QuestionText.Text);
        Assert.False(viewer.Viewport.IsVisible);
        dialog.CancelButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Assert.Null(viewer.UnsavedDialog);
        Assert.True(viewer.ShaderEdits.Dirty);

        // Asked again: Save writes it, then closes.
        viewer.Close();
        viewer.UnsavedDialog!.SaveButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Wait(viewer.Saving);
        ExplorerStateRenders.Pump(20);
        Assert.Equal(2, BumpinessOf(written!));
        Assert.Null(viewer.UnsavedDialog);
    }

    [AvaloniaFact]
    public void Dont_save_closes_without_writing()
    {
        bool written = false;
        Views.ModelViewer viewer = Open(ShaderValuesTests.Triangle(), new ModelSaveTarget(_ => written = true));
        Bump(viewer, "2");
        viewer.Close();
        viewer.UnsavedDialog!.DiscardButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Assert.Null(viewer.UnsavedDialog);
        Assert.False(written);
    }

    [AvaloniaFact]
    public void A_file_with_nowhere_to_save_closes_without_asking()
    {
        Views.ModelViewer viewer = Open(ShaderValuesTests.Triangle(), target: null);
        Bump(viewer, "2");
        Assert.False(viewer.Save());
        viewer.Close();
        Assert.Null(viewer.UnsavedDialog);
    }

    [AvaloniaFact]
    public void A_drawable_in_an_rpf_is_saved_back_into_it()
    {
        using var folder = new TempFolder();
        string archivePath = folder.File("props.rpf");
        var archive = new RpfArchive("props.rpf");
        ArchiveWriter.AddStandalone(archive, "tri.ydr", ShaderValuesTests.Triangle());
        ArchiveWriter.AddStandalone(archive, "keep.txt", "untouched"u8.ToArray());
        archive.Save(archivePath);
        ArchiveNode source = ArchiveNode.OpenRoot(archivePath, null);
        var target = new ArchiveEntryTarget(archivePath, "tri.ydr", null, source);
        byte[] data = ((RpfResourceFileEntry)source.Archive.Find("tri.ydr")!).ReadStandalone();
        Views.ModelViewer viewer = Open(data, new ModelSaveTarget(target.Save, target.Prepare));
        Bump(viewer, "1.75");
        Assert.True(viewer.Save());
        Wait(viewer.Saving);
        Assert.True(viewer.Saving.Result, viewer.StatusText.Text);
        ArchiveNode saved = ArchiveNode.OpenRoot(archivePath, null);
        try
        {
            Assert.Equal("untouched"u8.ToArray(), ((RpfBinaryFileEntry)saved.Archive.Find("keep.txt")!).ReadStandalone());
            Assert.Equal(1.75f, BumpinessOf(((RpfResourceFileEntry)saved.Archive.Find("tri.ydr")!).ReadStandalone()));
        }
        finally
        {
            saved.Close();
        }
    }

    [Fact]
    public void Edits_to_one_rpf_wait_for_each_other()
    {
        using var folder = new TempFolder();
        string archivePath = folder.File("props.rpf");
        var archive = new RpfArchive("props.rpf");
        ArchiveWriter.AddStandalone(archive, "tri.ydr", ShaderValuesTests.Triangle());
        archive.Save(archivePath);
        byte[] model = ShaderValuesTests.Triangle("default");
        Task? save = null;
        // A model save asked for while an explorer change is being made waits for it, and keeps it.
        ArchiveWriter.Mutate(archivePath, null, "", edited =>
        {
            save = Task.Run(() => ArchiveWriter.ReplaceEntry(archivePath, null, "tri.ydr", model));
            Assert.False(save.Wait(300));
            ArchiveWriter.AddStandalone(edited, "added.txt", "added"u8.ToArray());
        });
        Assert.True(save!.Wait(10_000));
        ArchiveNode saved = ArchiveNode.OpenRoot(archivePath, null);
        try
        {
            Assert.Equal("added"u8.ToArray(), ((RpfBinaryFileEntry)saved.Archive.Find("added.txt")!).ReadStandalone());
            byte[] written = ((RpfResourceFileEntry)saved.Archive.Find("tri.ydr")!).ReadStandalone();
            Assert.Equal(FiveFury.Ydr.Ydr.Read(model).Drawable.Materials[0].ShaderHash, FiveFury.Ydr.Ydr.Read(written).Drawable.Materials[0].ShaderHash);
        }
        finally
        {
            saved.Close();
        }
    }
}

[Collection(ExplorerCollection.Name)]
public sealed class ModelSaveTargetTests : ExplorerTestBase
{
    [AvaloniaFact]
    public void A_loose_drawable_opened_from_the_explorer_is_saved_over_its_file()
    {
        MakeGame();
        string path = Path.Combine(Work, "tri.ydr");
        File.WriteAllBytes(path, ShaderValuesTests.Triangle());
        var bridge = new ExplorerViewModel();
        bridge.OpenGame(Work);
        Settle(bridge);
        ModelFile? opened = null;
        bridge.ModelFileOpened += (_, file) => opened = file;
        bridge.ActivateEntry(Enumerable.Range(0, bridge.EntriesModel.Count).Single(row => bridge.EntriesModel[row].Name == "tri.ydr"));

        ModelSaveTarget target = opened!.SaveTarget!;
        target.Prepare?.Invoke();
        target.Write([1, 2, 3, 4]);
        target.Finished?.Invoke(true);
        Assert.Equal([(byte)1, 2, 3, 4], File.ReadAllBytes(path));
        Assert.Equal("Saved tri.ydr", bridge.Status);
    }
}
