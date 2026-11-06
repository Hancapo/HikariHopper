using System.Runtime.InteropServices;

namespace HikariHopper.Ui;

/// <summary>
/// Whether the UI animates: not when Windows' "Animation effects" is off (Settings, Accessibility, Visual
/// effects), which asks apps to show changes at once.
/// </summary>
public static class Motion
{
    private const uint GetClientAreaAnimation = 0x1042;

    /// <summary>Set by tests to decide for themselves; null follows Windows.</summary>
    internal static bool? ReducedOverride { get; set; }

    /// <summary>Whether animations are turned off; read each time, so a change in Windows applies at once.</summary>
    public static bool Reduced => ReducedOverride ?? (OperatingSystem.IsWindows() && !ClientAreaAnimation());

    private static bool ClientAreaAnimation() =>
        SystemParametersInfo(GetClientAreaAnimation, 0, out int enabled, 0) == 0 || enabled != 0;

    [DllImport("user32.dll")]
    private static extern int SystemParametersInfo(uint action, uint parameter, out int value, uint update);
}
