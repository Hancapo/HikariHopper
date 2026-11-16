using System.Diagnostics;
using System.Runtime.InteropServices;
using HikariHopper.Views;

namespace HikariHopper.Tests;

// The 3D viewport's pacer on a hidden window: it ticks at every first, second, third or fourth vertical blank, by how
// long frames take. Skipped where no display reports its refresh rate.
public sealed class DisplayPacerTests
{
    [Fact]
    public void Slow_frames_slow_the_ticks_to_a_steady_whole_number_of_refreshes_and_fast_ones_bring_them_back()
    {
        nint window = CreateWindowExW(0, "STATIC", "pacer", 0x80000000 /* WS_POPUP, never shown */, 0, 0, 320, 180, 0, 0, GetModuleHandleW(null), 0);
        Assert.NotEqual(0, window);
        var ticks = new List<long>();
        var pacer = new DisplayPacer(window, () =>
        {
            lock (ticks)
                ticks.Add(Stopwatch.GetTimestamp());
        });
        try
        {
            var wait = Stopwatch.StartNew();
            while (pacer.RefreshPeriod <= 0 && wait.Elapsed < TimeSpan.FromSeconds(1))
                Thread.Sleep(10);
            double refresh = pacer.RefreshPeriod;
            if (refresh <= 0)
                Assert.Skip("No display reports its refresh rate.");
            Assert.Equal(1, pacer.Divisor);
            // Frames a little slower than two refreshes, a few in a row: three refreshes a tick, at once.
            for (int i = 0; i < 20 && pacer.Divisor < 3; i++)
                pacer.FrameTook(refresh * 2.2);
            Assert.Equal(3, pacer.Divisor);
            Thread.Sleep(100);
            lock (ticks)
                ticks.Clear();
            Thread.Sleep(500);
            double[] intervals;
            lock (ticks)
                intervals = [.. ticks.Zip(ticks.Skip(1), (a, b) => Stopwatch.GetElapsedTime(a, b).TotalSeconds).Order()];
            Assert.InRange(intervals[intervals.Length / 2], refresh * 2.9, refresh * 3.1);
            // Two seconds of frames with room to spare at one refresh fewer: back down to two.
            var fast = Stopwatch.StartNew();
            while (fast.Elapsed < TimeSpan.FromSeconds(2.3) && pacer.Divisor == 3)
            {
                pacer.FrameTook(refresh * .5);
                Thread.Sleep(5);
            }
            Assert.Equal(2, pacer.Divisor);
        }
        finally
        {
            pacer.Dispose();
            DestroyWindow(window);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowExW(int exStyle, string className, string title, uint style, int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint hwnd);
}
