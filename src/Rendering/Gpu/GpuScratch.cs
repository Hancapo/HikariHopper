using System.Runtime.InteropServices;
using System.Text;
using WGpuFury;

namespace HikariHopper.Gpu;

/// <summary>
/// Unmanaged memory for one call's descriptors (their arrays, strings and nested structs), freed when the call
/// returns: wgpu copies what it keeps.
/// </summary>
internal sealed unsafe class GpuScratch : IDisposable
{
    private readonly List<nint> blocks = [];

    internal T* Alloc<T>(int count = 1) where T : unmanaged
    {
        if (count <= 0)
            return null;
        void* block = NativeMemory.AllocZeroed((nuint)count, (nuint)sizeof(T));
        blocks.Add((nint)block);
        return (T*)block;
    }

    internal T* One<T>(in T value) where T : unmanaged
    {
        T* block = Alloc<T>();
        *block = value;
        return block;
    }

    internal T* Copy<T>(ReadOnlySpan<T> values) where T : unmanaged
    {
        T* block = Alloc<T>(values.Length);
        values.CopyTo(new Span<T>(block, values.Length));
        return block;
    }

    /// <summary>A string as wgpu reads it; null for none (a label not given, an entry point left to the shader).</summary>
    internal WGPUStringView Text(string? text)
    {
        if (text is null)
            return WGPUStringView.Null;
        int length = Encoding.UTF8.GetByteCount(text);
        byte* bytes = Alloc<byte>(Math.Max(1, length));
        Encoding.UTF8.GetBytes(text, new Span<byte>(bytes, length));
        return new WGPUStringView(bytes, (nuint)length);
    }

    public void Dispose()
    {
        foreach (nint block in blocks)
            NativeMemory.Free((void*)block);
        blocks.Clear();
    }
}

/// <summary>A GPU call that failed, or a GPU object that couldn't be made.</summary>
public sealed class GpuException(string message) : Exception(message);
