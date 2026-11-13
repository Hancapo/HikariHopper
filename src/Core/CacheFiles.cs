namespace HikariHopper.Core;

/// <summary>
/// What the app keeps in its caches: how much of it there is, and deleting it, where a file in use, or gone already,
/// is left to the next time.
/// </summary>
internal static class CacheFiles
{
    /// <summary>The bytes of the files in <paramref name="folder"/> that match <paramref name="pattern"/>, and in its folders with <paramref name="deep"/>.</summary>
    public static long Size(string folder, string pattern, bool deep = false)
    {
        long total = 0;
        try
        {
            if (!Directory.Exists(folder))
                return 0;
            foreach (FileInfo file in new DirectoryInfo(folder).EnumerateFiles(pattern, deep ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
            {
                try
                {
                    total += file.Length;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    // Gone or locked since it was listed: not counted.
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A folder that can't be read counts as what was read of it.
        }
        return total;
    }

    /// <summary>Deletes the files in <paramref name="folder"/> that match <paramref name="pattern"/>; false when one stayed.</summary>
    public static bool Delete(string folder, string pattern)
    {
        if (!Directory.Exists(folder))
            return true;
        bool all = true;
        foreach (string file in Directory.EnumerateFiles(folder, pattern))
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                all = false;
            }
        }
        return all;
    }

    /// <summary>Deletes <paramref name="folder"/> and everything in it; false when something stayed.</summary>
    public static bool DeleteFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
