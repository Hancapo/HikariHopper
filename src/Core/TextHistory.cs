namespace HikariHopper.Core;

/// <summary>What kind of edit a step is, which says whether the next edit can join it.</summary>
public enum TextEditKind
{
    /// <summary>Typed characters: a run of them is one step.</summary>
    Typing,

    /// <summary>Backspace: a run of them is one step.</summary>
    DeleteBack,

    /// <summary>Delete: a run of them is one step.</summary>
    DeleteForward,

    /// <summary>Anything else (a paste, a cut, a new line), always a step of its own.</summary>
    Other,
}

/// <summary>
/// One edit, in the file's own text: from <see cref="Start"/>, <see cref="Removed"/> (which ended at
/// <see cref="RemovedEnd"/>) was replaced by <see cref="Inserted"/> (which ends at <see cref="InsertedEnd"/>), and
/// where the selection was before it. Positions before the edit are as the text was then; the rest as it is after.
/// </summary>
public sealed record TextEdit(RawPosition Start, TextRun Removed, RawPosition RemovedEnd, TextRun Inserted, RawPosition InsertedEnd,
    RawPosition AnchorBefore, RawPosition CaretBefore, TextEditKind Kind)
{
    /// <summary>Which state of the text this step leads to; a step joined by another leads to a new one.</summary>
    internal int Id { get; init; }
}

/// <summary>
/// The text viewer's undo and redo: the edits made, in order, a run of typing (or of Backspace, or of Delete) joined
/// into one step. Each state the text has been in has an id, so it can tell whether the text is as it was last saved.
/// </summary>
public sealed class TextHistory
{
    private readonly List<TextEdit> undo = [];
    private readonly List<TextEdit> redo = [];
    private int lastId;
    private int savedId;
    // Set when the caret moves on its own, or the text is saved: the next edit starts a step of its own.
    private bool sealedTop;

    public bool CanUndo => undo.Count > 0;

    public bool CanRedo => redo.Count > 0;

    /// <summary>Which state the text is in now: 0 as it was read.</summary>
    public int State => undo.Count == 0 ? 0 : undo[^1].Id;

    /// <summary>Whether the text differs from how it was last saved (or read).</summary>
    public bool IsModified => State != savedId;

    /// <summary>The text in <paramref name="state"/> was saved.</summary>
    public void MarkSaved(int state)
    {
        savedId = state;
        sealedTop = true;
    }

    /// <summary>The next edit starts a step of its own, whatever it is.</summary>
    public void Seal() => sealedTop = true;

    /// <summary>Adds an edit just made, joined to the last step when it carries a run on; anything undone is gone.</summary>
    public void Record(TextEdit edit)
    {
        redo.Clear();
        if (!sealedTop && undo.Count > 0 && Join(undo[^1], edit) is { } joined)
            undo[^1] = joined with { Id = ++lastId };
        else
            undo.Add(edit with { Id = ++lastId });
        sealedTop = edit.Kind == TextEditKind.Other;
    }

    /// <summary>The last step, taken off to be undone; null when there is none.</summary>
    public TextEdit? Undo()
    {
        if (undo.Count == 0)
            return null;
        TextEdit step = undo[^1];
        undo.RemoveAt(undo.Count - 1);
        redo.Add(step);
        sealedTop = true;
        return step;
    }

    /// <summary>The last step undone, put back to be done again; null when there is none.</summary>
    public TextEdit? Redo()
    {
        if (redo.Count == 0)
            return null;
        TextEdit step = redo[^1];
        redo.RemoveAt(redo.Count - 1);
        undo.Add(step);
        sealedTop = true;
        return step;
    }

    /// <summary>The two steps as one, when <paramref name="next"/> carries on the run <paramref name="last"/> is; else null.</summary>
    private static TextEdit? Join(TextEdit last, TextEdit next)
    {
        if (last.Kind != next.Kind)
            return null;
        switch (next.Kind)
        {
            // A character typed where the last one ended, on the same line.
            case TextEditKind.Typing when next.Removed.IsEmpty && next.Start == last.InsertedEnd
                && last.Inserted.Lines.Count == 1 && next.Inserted.Lines.Count == 1:
                return last with { Inserted = last.Inserted.Then(next.Inserted), InsertedEnd = next.InsertedEnd };
            // Backspace again, just before what the last one took.
            case TextEditKind.DeleteBack when next.Inserted.IsEmpty && last.Inserted.IsEmpty && next.RemovedEnd == last.Start:
                return last with { Start = next.Start, Removed = next.Removed.Then(last.Removed), InsertedEnd = next.Start };
            // Delete again, where the last one did.
            case TextEditKind.DeleteForward when next.Inserted.IsEmpty && last.Inserted.IsEmpty && next.Start == last.Start:
            {
                TextRun removed = last.Removed.Then(next.Removed);
                return last with { Removed = removed, RemovedEnd = removed.EndFrom(last.Start) };
            }
            default:
                return null;
        }
    }
}
