namespace HikariHopper.Tests;

/// <summary>Kept so control tests read as before; <c>[AvaloniaFact]</c> already runs them on the headless UI thread.</summary>
internal static class UiThread
{
    public static void Run(Action body) => body();

    public static T Run<T>(Func<T> body) => body();
}
