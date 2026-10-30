namespace HikariHopper.Tests;

public sealed class SmokeTests
{
    [Fact]
    public void ThemeMatchesQmlPalette() =>
        Assert.Equal("#ff1e1f30", Ui.Theme.AppBg.Color.ToString());
}
