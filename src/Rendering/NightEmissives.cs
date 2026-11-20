namespace HikariHopper.Rendering;

/// <summary>
/// When GTA V's night emissives are on: the day/night balance the game works out each frame from the clock and
/// visualsettings.dat's <c>emissive.night.start.time</c> and <c>emissive.night.end.time</c>, which its shaders read as
/// globalScalars2.y. Nought by day, one by night, fading over the hour before the start and the hour before the end.
/// Read from the game once one is open (<see cref="Use"/>); until then, with the defaults the game's code reads the
/// settings with.
/// </summary>
internal static class NightEmissives
{
    /// <summary>The hours the game's code takes where visualsettings.dat hasn't them.</summary>
    internal const float DefaultStart = 20, DefaultEnd = 7;

    private static volatile Hours window = new(DefaultStart, DefaultEnd);

    private sealed record Hours(float Start, float End);

    /// <summary>The start and end in use: the open game's, or the code's defaults.</summary>
    internal static (float Start, float End) Window => (window.Start, window.End);

    /// <summary>Takes the game's settings from now on; null goes back to the code's defaults.</summary>
    internal static void Use(Core.GameVisualSettings? file) =>
        window = new(file?.Get("emissive.night.start.time", DefaultStart) ?? DefaultStart, file?.Get("emissive.night.end.time", DefaultEnd) ?? DefaultEnd);

    /// <summary>The balance at <paramref name="hours"/> after midnight, with the settings in use.</summary>
    internal static float Balance(float hours)
    {
        Hours current = window;
        return Balance(hours, current.Start, current.End);
    }

    /// <summary>
    /// The balance at <paramref name="hours"/> after midnight for night emissives on from the hour
    /// <paramref name="start"/> to the hour <paramref name="end"/> (both taken as whole hours, as the game does): the
    /// hours they're on, from the start round midnight to the end, flagged; within an hour whose next one turns them on,
    /// they fade in over its minutes, and out over those of the last hour before they turn off.
    /// </summary>
    internal static float Balance(float hours, float start, float end)
    {
        if (!float.IsFinite(hours))
            return 0;
        hours %= 24;
        if (hours < 0)
            hours += 24;
        uint on = (uint)Math.Max(0, start), off = (uint)Math.Max(0, end);
        // The game's loop runs for the hours from the start to midnight and on to the end, each a bit of a day's.
        long count = 24L - on + off;
        uint flags = 0;
        for (long i = 0; i < Math.Min(Math.Max(count, 0), 24); i++)
            flags |= 1u << (int)((i + on) % 24);
        int hour = Math.Min((int)hours, 23), next = (hour + 1) % 24;
        float fade = Math.Clamp(hours - hour, 0, 1);
        bool now = (flags & (1u << hour)) != 0, soon = (flags & (1u << next)) != 0;
        if (!now && soon)
            return fade;
        if (now && !soon)
            return 1 - fade;
        return now ? 1 : 0;
    }
}
