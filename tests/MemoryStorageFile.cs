using System.Reflection;
using Avalonia.Platform.Storage;

namespace HikariHopper.Tests;

/// <summary>
/// A dragged file with no path on disk, like an image dragged out of a browser or a zip. Avalonia's
/// reference assemblies keep <see cref="IStorageFile"/> from being implemented directly, so this is a proxy.
/// </summary>
internal class MemoryStorageFile : DispatchProxy
{
    private string name = "";
    private byte[] contents = [];

    public static IStorageFile Create(string name, byte[] contents)
    {
        IStorageFile file = Create<IStorageFile, MemoryStorageFile>();
        var proxy = (MemoryStorageFile)(object)file;
        proxy.name = name;
        proxy.contents = contents;
        return file;
    }

    protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
    {
        "get_Name" => name,
        "get_Path" => new Uri("virtual-file:///" + Uri.EscapeDataString(name)),
        "get_CanBookmark" => false,
        "OpenReadAsync" => Task.FromResult<Stream>(new MemoryStream(contents, writable: false)),
        "GetBasicPropertiesAsync" => Task.FromResult(new StorageItemProperties((ulong)contents.Length, null, null)),
        "GetParentAsync" => Task.FromResult<IStorageFolder?>(null),
        "Dispose" => null,
        _ => throw new NotSupportedException(method?.Name),
    };
}
