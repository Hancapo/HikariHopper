namespace HikariHopper.Tests;

/// <summary>Kept so texture tests read as before; <c>[AvaloniaFact]</c> already runs them on the headless UI thread.</summary>
internal static class Headless
{
    public static Task Run(Action test)
    {
        test();
        return Task.CompletedTask;
    }

    public static Task Run(Func<Task> test) => test();
}
