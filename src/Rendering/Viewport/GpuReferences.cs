using System.Runtime.CompilerServices;
using WebGpuSharp;
using WebGpuSharp.FFI;
using WebGpuSharp.Internal;
using WebGpuSharp.Marshalling;

namespace HikariHopper.Rendering;

/// <summary>
/// Frees the world's render bundles, which WebGPUSharp alone never does. Dawn hands over each object it creates with one
/// reference, the caller's; WebGPUSharp 0.5.8 takes another as it wraps the object, and its wrapper gives back only one
/// when it's collected. The object is then never freed. A buffer or a texture's memory still goes when it's destroyed,
/// but a bundle has nothing to destroy: each kept its commands (16 KB at least) for good, and a world records its
/// assets' bundles again whenever their levels of detail change, a dozen a frame. That was most of the memory the
/// process held beyond its heap and the GPU's after a flight (1.1 GB after two minutes over the city).
/// </summary>
/// <remarks>
/// A bundle gives the extra reference back as it's recorded (<see cref="Owned"/>) and is let go where it's dropped
/// (<see cref="Free"/>), on the thread that draws: left to its wrapper's finalizer it would be destroyed on the
/// finalizer's thread while the device is in use on this one, which Dawn doesn't allow. The other objects keep the
/// extra reference for that reason: without it their finalizers would destroy them there too. Only for the version
/// known to take the extra reference and to keep its handle where this reads it; with another, the bundles are left to
/// WebGPUSharp.
/// </remarks>
internal static class GpuReferences
{
    private static readonly bool Known = typeof(Device).Assembly.GetName().Version is { Major: 0, Minor: 5, Build: 8 };

    /// <summary>A bundle just recorded, held by its wrapper alone.</summary>
    internal static RenderBundle Owned(this RenderBundle made)
    {
        if (Known)
            RenderBundleHandle.Release(WebGPUMarshal.GetHandle(made));
        return made;
    }

    /// <summary>Lets go of a bundle that was <see cref="Owned"/>, now: nothing draws it after this.</summary>
    internal static void Free(RenderBundle? bundle)
    {
        if (Known && bundle is not null)
            Wrapper<RenderBundleHandle>.SafeHandle(bundle).Dispose();
    }

    // The handle a wrapper keeps to itself. A field of a generic type is reached from a generic type of the same shape.
    private static class Wrapper<THandle>
        where THandle : unmanaged, IWebGpuHandle<THandle>, IEquatable<THandle>
    {
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_safeHandle")]
        internal static extern ref WebGpuSafeHandle<THandle> SafeHandle(WebGPUManagedHandleBase<THandle> wrapper);
    }
}
