using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HikariHopper.Tests;
using Xunit.v3;

[assembly: TestWindows.CloseWhatsLeftOpen]

namespace HikariHopper.Tests;

/// <summary>
/// The windows tests open, closed after each test that left them open. Every test shares one headless app, and a
/// render draws every window it has: one left open by each test before made every later test slower.
/// </summary>
internal static class TestWindows
{
    private static readonly HashSet<Window> Open = [];
    private static bool watching;

    private static void Watch()
    {
        if (watching)
            return;
        watching = true;
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => Open.Add(window), handledEventsToo: true);
        Window.WindowClosedEvent.AddClassHandler<Window>((window, _) => Open.Remove(window), handledEventsToo: true);
    }

    private static void CloseAll()
    {
        foreach (Window window in Open.ToArray())
        {
            Open.Remove(window);
            try
            {
                window.Close();
            }
            catch (InvalidOperationException)
            {
            }
        }
        Dispatcher.UIThread.RunJobs();
    }

    [AttributeUsage(AttributeTargets.Assembly)]
    internal sealed class CloseWhatsLeftOpenAttribute : BeforeAfterTestAttribute
    {
        public override void Before(MethodInfo methodUnderTest, IXunitTest test) => OnUiThread(Watch);

        public override void After(MethodInfo methodUnderTest, IXunitTest test) => OnUiThread(CloseAll);

        private static void OnUiThread(Action action)
        {
            if (Dispatcher.UIThread.CheckAccess())
                action();
        }
    }
}
