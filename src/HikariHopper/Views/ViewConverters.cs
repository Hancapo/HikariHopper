using Avalonia.Data.Converters;

namespace HikariHopper.Views;

/// <summary>Small value converters for the explorer views' QML binding expressions.</summary>
public static class ViewConverters
{
    /// <summary>QML <c>count &gt; 0</c>.</summary>
    public static IValueConverter IsPositive { get; } = new FuncValueConverter<int, bool>(value => value > 0);

    /// <summary>QML <c>qsTr("%1 items").arg(count)</c>.</summary>
    public static IValueConverter ItemCount { get; } = new FuncValueConverter<int, string>(value => $"{value} items");
}
