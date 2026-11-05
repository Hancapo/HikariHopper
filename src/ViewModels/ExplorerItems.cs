using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HikariHopper.Core;

namespace HikariHopper.ViewModels;

/// <summary>One breadcrumb segment: <paramref name="Target"/> is a tree key such as <c>game://mods</c>.</summary>
public sealed record PathSegment(string Label, string Target, bool Archive);

/// <summary>A source chosen for "RPF from folder/ZIP"; both strings are empty when cancelled.</summary>
public sealed record RpfSource(string Path, string SuggestedName);

/// <summary>A recently opened GTA V installation shown on the start page.</summary>
public sealed record RecentGame(string Name, string Path, string Edition);

/// <summary>One row of the explorer table/grid (the Qt <c>EntryListModel</c> roles).</summary>
public sealed class EntryItem : ObservableObject
{
    private bool selected;

    public EntryItem(EntryRecord record) => Record = record;

    public EntryRecord Record { get; }

    public string Name => Record.Name;

    public string Path => Record.Path;

    public string Kind => Record.Kind;

    public long Size => Record.Size;

    /// <summary>What the file takes: in an archive that compresses it, its compressed size.</summary>
    public string SizeLabel => Formatting.FormatSize(Record.ShownSize);

    public int ChildCount => Record.Children;

    public bool IsDirectory => Record.IsDirectory;

    /// <summary>
    /// The TYPE column: a folder is recognisable by its glyph, so it shows no type. A file in an archive says
    /// whether it is kept compressed, which is what its size is.
    /// </summary>
    public string TypeText => IsDirectory ? "" : Record.Storage switch
    {
        EntryStorage.Compressed => $"{Kind} (Compressed)",
        EntryStorage.Uncompressed => $"{Kind} (Uncompressed)",
        _ => Kind,
    };

    /// <summary>The SIZE column: a file's size, or how many items a folder holds ("Empty" for none).</summary>
    public string SizeText => !IsDirectory ? SizeLabel
        : ChildCount switch
        {
            0 => "Empty",
            1 => "1 item",
            var count => $"{count} items",
        };

    public bool Selected
    {
        get => selected;
        internal set => SetProperty(ref selected, value);
    }
}

/// <summary>Immutable state of one folder-tree row, compared to publish minimal updates.</summary>
internal sealed record TreeRecord(
    string Label,
    string Path,
    int Depth,
    string Kind,
    bool Expanded = false,
    bool HasChildren = false,
    bool Selected = false);

/// <summary>One visible row of the flattened folder tree (the Qt <c>TreeListModel</c> roles).</summary>
public sealed class TreeItem : ObservableObject
{
    private TreeRecord record;

    internal TreeItem(TreeRecord record) => this.record = record;

    public string Label => record.Label;

    /// <summary>Stable key: <c>game://…</c>, <c>rpf://…</c>, <c>archive://…</c> or <c>nested://…</c>.</summary>
    public string Path => record.Path;

    public int Depth => record.Depth;

    /// <summary><c>root</c>, <c>folder</c> or <c>archive</c>.</summary>
    public string Kind => record.Kind;

    public bool Expanded => record.Expanded;

    public bool HasChildren => record.HasChildren;

    public bool Selected => record.Selected;

    internal TreeRecord Record => record;

    internal void Update(TreeRecord value)
    {
        TreeRecord previous = record;
        if (previous == value)
            return;
        record = value;
        if (previous.Label != value.Label)
            OnPropertyChanged(nameof(Label));
        if (previous.Depth != value.Depth)
            OnPropertyChanged(nameof(Depth));
        if (previous.Kind != value.Kind)
            OnPropertyChanged(nameof(Kind));
        if (previous.Expanded != value.Expanded)
            OnPropertyChanged(nameof(Expanded));
        if (previous.HasChildren != value.HasChildren)
            OnPropertyChanged(nameof(HasChildren));
        if (previous.Selected != value.Selected)
            OnPropertyChanged(nameof(Selected));
    }
}

/// <summary>An <see cref="ObservableCollection{T}"/> that publishes bulk changes as single notifications.</summary>
public sealed class ItemCollection<T> : ObservableCollection<T>
{
    /// <summary>Replaces every item with one <see cref="NotifyCollectionChangedAction.Reset"/> (Qt model reset).</summary>
    internal void ResetItems(IEnumerable<T> items)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (T item in items)
            Items.Add(item);
        RaiseCountAndIndexer();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    internal void InsertRange(int index, IReadOnlyList<T> items)
    {
        if (items.Count == 0)
            return;
        CheckReentrancy();
        for (int offset = 0; offset < items.Count; offset++)
            Items.Insert(index + offset, items[offset]);
        RaiseCountAndIndexer();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Add, items.ToList(), index));
    }

    internal void RemoveRange(int index, int count)
    {
        if (count <= 0)
            return;
        CheckReentrancy();
        var removed = new List<T>(count);
        for (int offset = 0; offset < count; offset++)
        {
            removed.Add(Items[index]);
            Items.RemoveAt(index);
        }
        RaiseCountAndIndexer();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Remove, removed, index));
    }

    private void RaiseCountAndIndexer()
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
    }
}
