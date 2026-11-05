using System.Reflection;

namespace HikariHopper.Infrastructure;

/// <summary>Which kind of build this is.</summary>
internal static class BuildInfo
{
    /// <summary>
    /// True only for the builds tools/release.ps1 makes for a GitHub release, which it marks with the
    /// <c>HikariHopperRelease</c> assembly metadata. Every other build (debug runs, local publishes) is a dev build.
    /// </summary>
    public static bool IsRelease { get; } = typeof(BuildInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Any(attribute => attribute.Key == "HikariHopperRelease" && string.Equals(attribute.Value, "true", StringComparison.OrdinalIgnoreCase));
}
