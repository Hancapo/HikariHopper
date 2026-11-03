using Avalonia.Input;
using HikariHopper.Textures;

namespace HikariHopper.ViewModels;

public sealed partial class TextureViewerViewModel
{
    /// <summary>Selects a row: plain click selects only it, Ctrl toggles it, Shift selects the range from the anchor
    /// (Ctrl+Shift adds the range).</summary>
    public void SelectTexture(int row, KeyModifiers modifiers = KeyModifiers.None)
    {
        if (OperationBusy || RecordAt(row) is null)
            return;
        bool control = modifiers.HasFlag(KeyModifiers.Control);
        bool shift = modifiers.HasFlag(KeyModifiers.Shift);
        SortedSet<int> rows;
        if (shift)
        {
            int anchor = selectionAnchor >= 0 ? selectionAnchor : row;
            rows = [.. Enumerable.Range(Math.Min(anchor, row), Math.Abs(anchor - row) + 1)];
            if (control)
                rows.UnionWith(selectedRows);
            selectionAnchor = anchor;
        }
        else if (control)
        {
            rows = [.. selectedRows];
            if (!rows.Remove(row))
                rows.Add(row);
            selectionAnchor = row;
        }
        else
        {
            rows = [row];
            selectionAnchor = row;
        }
        int current = rows.Contains(row) ? row : rows.Count > 0 ? rows.Min : -1;
        SetTextureSelection(rows, current);
    }

    /// <summary>Right-click: keeps a selection that already contains <paramref name="row"/>, making it current.</summary>
    public void SelectContextTexture(int row)
    {
        if (!selectedRows.Contains(row))
            SelectTexture(row);
        else if (!OperationBusy)
            SetTextureSelection(selectedRows, row);
    }

    public void SelectAllTextures()
    {
        if (OperationBusy)
            return;
        SortedSet<int> rows = [.. Enumerable.Range(0, TextureCount)];
        selectionAnchor = rows.Count > 0 ? 0 : -1;
        SetTextureSelection(rows, rows.Count > 0 ? Math.Max(0, selectedIndex) : -1);
    }

    public void SetMipLevel(int level)
    {
        if (SelectedRecord() is not { } record)
            return;
        int normalized = Math.Max(0, Math.Min(level, record.MipCount - 1));
        if (normalized == mipLevel)
            return;
        mipLevel = normalized;
        if (DecodedMip(normalized) is { } decoded)
        {
            ShowMip(selectedIndex, channel, normalized, decoded);
            return;
        }
        // Not decoded yet: the current mip stays on screen until this one is.
        previewLoading = true;
        EmitSelectionChanged();
        EmitStateChanged();
        RequestPreview();
    }

    /// <summary>Shows <c>rgba</c>, <c>r</c>, <c>g</c>, <c>b</c> or <c>a</c>; other values are ignored.</summary>
    public void SetChannel(string mode)
    {
        string normalized = mode.ToLowerInvariant();
        if (!TextureOperations.Channels.Contains(normalized) || normalized == channel)
            return;
        channel = normalized;
        ClearPreview();
        previewLoading = SelectedRecord() is not null;
        EmitSelectionChanged();
        EmitStateChanged();
        RequestPreview();
    }

    public int EstimatedMipCount(int width, int height, int minMipSize) =>
        TextureDimensions.MipCountForDimensions(width, height, minMipSize);

    /// <summary><c>"8 × 4"</c>, or <c>"—"</c> for invalid dimensions.</summary>
    public string EstimatedSmallestMipDimensions(int width, int height, int minMipSize)
    {
        var dimensions = TextureDimensions.MipDimensionsForMinimum(width, height, minMipSize);
        if (dimensions.Count == 0)
            return "—";
        (int mipWidth, int mipHeight) = dimensions[^1];
        return $"{mipWidth} × {mipHeight}";
    }

    /// <summary>True when every selected texture rounds within <see cref="MaximumDimension"/>.</summary>
    public bool ValidPowerOfTwoSelection(string mode)
    {
        try
        {
            return selectedRows.Count > 0 && SelectionRecords().All(record =>
            {
                (int width, int height) = TextureDimensions.PowerOfTwoDimensions(record.Item.Texture.Width, record.Item.Texture.Height, mode);
                return Math.Max(width, height) <= MaximumDimension;
            });
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>The current texture rounded with <paramref name="mode"/>, or null without one or for an invalid mode.</summary>
    public TextureSize? PowerOfTwoSize(string mode)
    {
        if (SelectedRecord() is null)
            return null;
        try
        {
            (int width, int height) = TextureDimensions.PowerOfTwoDimensions(SelectedWidth, SelectedHeight, mode);
            return new TextureSize(width, height);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private void SetTextureSelection(IEnumerable<int> rows, int current)
    {
        selectedRows = [.. rows];
        ApplyModelSelection();
        // Still the texture on screen (a right click on it, say): keep its preview and mip rather than decode it again.
        if (current >= 0 && current == selectedIndex && previewImage is not null
            && ReferenceEquals(RecordAt(current)?.Texture, previewTexture))
        {
            EmitSelectionChanged();
            EmitStateChanged();
            return;
        }
        selectedIndex = current;
        mipLevel = 0;
        ClearPreview();
        previewLoading = current >= 0;
        EmitSelectionChanged();
        EmitStateChanged();
        RequestPreview();
    }
}
