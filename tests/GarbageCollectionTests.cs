using System.Text.Json;

namespace HikariHopper.Tests;

public sealed class GarbageCollectionTests
{
    // The app's own runtime settings, built next to the tests (and baked into its Native AOT build alike).
    [Fact]
    public void The_app_allocates_arrays_up_to_4_MB_young()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "HikariHopper.runtimeconfig.json");
        using JsonDocument config = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement properties = config.RootElement.GetProperty("runtimeOptions").GetProperty("configProperties");
        Assert.Equal(4L * 1024 * 1024, properties.GetProperty("System.GC.LOHThreshold").GetInt64());
    }
}
