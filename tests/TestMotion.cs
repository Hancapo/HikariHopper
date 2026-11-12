using System.Runtime.CompilerServices;
using HikariHopper.Views;

namespace HikariHopper.Tests;

/// <summary>
/// Tests run with the app's animations finished as soon as they start, so a test waits for what it checks, not for
/// a dialog to rise or a tab to slide. A test about an animation sets that animation's duration itself, for as
/// long as it runs, and puts back <see cref="Restore"/>'s values.
/// </summary>
internal static class TestMotion
{
    [ModuleInitializer]
    internal static void Still() => Restore();

    /// <summary>Every animation the tests can set back to none.</summary>
    internal static void Restore()
    {
        ModalOverlay.OpenDuration = TimeSpan.Zero;
        ModalOverlay.CloseDuration = TimeSpan.Zero;
        TabStrip.SlideDuration = TimeSpan.Zero;
        Views.Controls.SegmentedSwitch.SlideDuration = TimeSpan.Zero;
        WorkspaceView.FoldersSlideDuration = TimeSpan.Zero;
        EntryViewMorph.Pace = 0;
        ModelViewer.SettleTime = TimeSpan.Zero;
        // And Windows' own setting for animations doesn't change what a test sees.
        Ui.Motion.ReducedOverride = true;
    }
}
