using HikariHopper.Rendering.World;

namespace HikariHopper.Tests;

public sealed class WorldInstanceIndicesTests
{
    // What the renderer does with a list each update: it writes the live values only when something changed.
    private static void Upload(WorldInstanceIndices list, uint[] gpu)
    {
        if (!list.Changed || list.Count == 0)
            return;
        list.Values.AsSpan(0, list.Count).CopyTo(gpu);
        list.Written();
    }

    private static void Fill(WorldInstanceIndices list, params uint[] values)
    {
        list.Begin();
        foreach (uint value in values)
            list.Add(value);
    }

    [Fact]
    public void A_list_on_a_range_another_asset_left_writes_values_it_never_wrote_there()
    {
        // The range held another asset's indexes, and the list's own copy starts zeroed.
        uint[] gpu = [37, 37, 37, 37];
        var list = new WorldInstanceIndices();
        Fill(list, 0);
        Upload(list, gpu);
        // Another cascade's casters come first now: the list's entries move up, onto bytes never written.
        Fill(list, 0, 0);
        Assert.True(list.Changed);
        Upload(list, gpu);
        Assert.Equal([0u, 0u], gpu[..2]);
    }

    [Fact]
    public void A_list_moved_to_a_new_range_writes_everything_again()
    {
        uint[] gpu = new uint[4];
        var list = new WorldInstanceIndices();
        Fill(list, 1, 2);
        Upload(list, gpu);
        list.Unwritten();
        uint[] moved = [9, 9, 9, 9];
        Fill(list, 1, 2);
        Assert.True(list.Changed);
        Upload(list, moved);
        Assert.Equal([1u, 2u], moved[..2]);
    }

    [Fact]
    public void Values_the_range_holds_already_are_not_written_again()
    {
        uint[] gpu = new uint[4];
        var list = new WorldInstanceIndices();
        Fill(list, 1, 2, 3);
        Upload(list, gpu);
        Fill(list, 1, 2, 3);
        Assert.False(list.Changed);
        // Shorter, then as long again: the tail is still on the GPU from before.
        Fill(list, 1);
        Assert.False(list.Changed);
        Fill(list, 1, 2, 3);
        Assert.False(list.Changed);
    }
}
