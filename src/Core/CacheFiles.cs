namespace HikariHopper.Core;

/// <summary>Deleting what the app keeps in its caches: a file in use, or gone already, is left to the next time.</summary>
internal static class CacheFiles
{
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
