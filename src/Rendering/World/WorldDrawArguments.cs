using System.Runtime.InteropServices;

namespace HikariHopper.Rendering.World;

/// <summary>One indirect indexed draw's arguments, as the GPU reads them (RedFury's, copied).</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct WorldDrawArguments(uint IndexCount, uint InstanceCount, uint FirstIndex, int BaseVertex, uint FirstInstance);

/// <summary>
/// Where each asset's arguments are in the shared buffer (RedFury's WorldArgumentAllocator, copied): offsets stay put
/// while other assets come and go, and freed ranges are joined up and reused.
/// </summary>
internal sealed class WorldArgumentAllocator
{
    private readonly List<(int Start, int Count)> free = [];

    internal int Length { get; private set; }

    internal int Allocate(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        for (int i = 0; i < free.Count; i++)
        {
            var range = free[i];
            if (range.Count < count)
                continue;
            if (range.Count == count)
                free.RemoveAt(i);
            else
                free[i] = (range.Start + count, range.Count - count);
            return range.Start;
        }
        int start = Length;
        Length = checked(Length + count);
        return start;
    }

    internal void Free(int start, int count)
    {
        free.Add((start, count));
        free.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        for (int i = free.Count - 2; i >= 0; i--)
            if (free[i].Start + free[i].Count == free[i + 1].Start)
            {
                free[i] = (free[i].Start, free[i].Count + free[i + 1].Count);
                free.RemoveAt(i + 1);
            }
    }
}
