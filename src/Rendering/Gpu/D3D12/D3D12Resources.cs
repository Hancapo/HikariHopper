using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.D3D12MemoryAllocator;
using Vortice.Direct3D12;
using Vortice.DXGI;
using D3DAllocation = Vortice.D3D12MemoryAllocator.Allocation;
using DxgiFormat = Vortice.DXGI.Format;

namespace HikariHopper.Gpu;

/// <summary>A buffer's memory: in the GPU's own (a default heap), or memory the CPU writes (upload) or reads (readback).</summary>
internal sealed unsafe class BufferLife : D3D12Life
{
    private readonly Device device;
    private readonly D3DAllocation? allocation;
    private readonly bool counted;

    internal BufferLife(Device device, ID3D12Resource resource, D3DAllocation? allocation, HeapType heap, ulong size, byte* pointer, bool counted = false)
    {
        this.device = device;
        this.allocation = allocation;
        this.counted = counted;
        Resource = resource;
        Heap = heap;
        Size = size;
        Pointer = pointer;
        if (counted)
        {
            device.Counted((long)(allocation?.Size ?? size), 0);
            device.Instance.Count("buffers", 1);
        }
    }

    internal ID3D12Resource Resource { get; }

    internal HeapType Heap { get; }

    internal ulong Size { get; }

    /// <summary>Where the CPU reads or writes it, for upload and readback memory (mapped as long as it lives).</summary>
    internal byte* Pointer { get; }

    internal override void Free()
    {
        if (counted)
        {
            device.Counted(-(long)(allocation?.Size ?? Size), 0);
            device.Instance.Count("buffers", -1);
        }
        Resource.Dispose();
        allocation?.Dispose();
    }
}

/// <summary>
/// A texture's memory and what its state tracking needs: how many subresources it has (mips × layers × planes) and the
/// state it rests in between command lists, the one its usage reads it in most.
/// </summary>
internal sealed class TextureLife : D3D12Life
{
    private readonly Device device;
    private readonly D3DAllocation? allocation;

    internal TextureLife(Device device, ID3D12Resource resource, D3DAllocation? allocation, TextureFormat format, TextureUsage usage,
        TextureDimension dimension, uint width, uint height, uint depthOrLayers, uint mips, uint samples, ResourceStates rest)
    {
        this.device = device;
        this.allocation = allocation;
        Resource = resource;
        Format = format;
        Usage = usage;
        Dimension = dimension;
        Width = width;
        Height = height;
        Depth = dimension == TextureDimension.D3 ? depthOrLayers : 1;
        Layers = dimension == TextureDimension.D3 ? 1 : depthOrLayers;
        Mips = mips;
        Samples = samples;
        Planes = D3D12Formats.HasStencil(format) ? 2u : 1u;
        Rest = rest;
        CopyFormats = new DxgiFormat[Planes];
        ResourceDescription description = resource.Description;
        Span<PlacedSubresourceFootPrint> footprint = stackalloc PlacedSubresourceFootPrint[1];
        Span<uint> rows = stackalloc uint[1];
        Span<ulong> rowBytes = stackalloc ulong[1];
        for (uint plane = 0; plane < Planes; plane++)
        {
            device.Native.GetCopyableFootprints(description, Subresource(0, 0, plane), 1, 0, footprint, rows, rowBytes, out _);
            CopyFormats[plane] = footprint[0].Footprint.Format;
        }
        if (allocation is not null)
        {
            device.Counted(0, (long)allocation.Size);
            device.Instance.Count("textures", 1);
        }
    }

    internal ID3D12Resource Resource { get; }

    internal TextureFormat Format { get; }

    internal TextureUsage Usage { get; }

    internal TextureDimension Dimension { get; }

    internal uint Width { get; }

    internal uint Height { get; }

    internal uint Depth { get; }

    internal uint Layers { get; }

    internal uint Mips { get; }

    internal uint Samples { get; }

    internal uint Planes { get; }

    internal ResourceStates Rest { get; }

    /// <summary>The format each plane's texels are copied as.</summary>
    internal DxgiFormat[] CopyFormats { get; }

    internal uint Subresources => Mips * Layers * Planes;

    internal uint Subresource(uint mip, uint layer, uint plane) => mip + layer * Mips + plane * Mips * Layers;

    internal (uint Width, uint Height, uint Depth) Extent(uint mip) =>
        (Math.Max(1, Width >> (int)mip), Math.Max(1, Height >> (int)mip), Math.Max(1, Depth >> (int)mip));

    internal override void Free()
    {
        if (allocation is not null)
        {
            device.Counted(0, -(long)allocation.Size);
            device.Instance.Count("textures", -1);
        }
        Resource.Dispose();
        allocation?.Dispose();
    }

    /// <summary>
    /// The state a texture rests in: read by shaders if it's sampled, else drawn into, else written by shaders; else the
    /// common state, for copies.
    /// </summary>
    internal static ResourceStates Resting(TextureUsage usage, bool depth) =>
        (usage & TextureUsage.TextureBinding) != 0 ? ResourceStates.AllShaderResource
        : (usage & TextureUsage.RenderAttachment) != 0 ? depth ? ResourceStates.DepthWrite : ResourceStates.RenderTarget
        : (usage & TextureUsage.StorageBinding) != 0 ? ResourceStates.UnorderedAccess
        : ResourceStates.Common;
}

public sealed unsafe class Buffer : GpuObject
{
    private readonly Device device;
    private readonly ulong size;
    private BufferLife? staging;

    internal Buffer(Device device, in BufferDescriptor descriptor)
    {
        this.device = device;
        size = descriptor.Size;
        Usage = descriptor.Usage;
        HeapType heap = (descriptor.Usage & BufferUsage.MapRead) != 0 ? HeapType.Readback
            : (descriptor.Usage & BufferUsage.MapWrite) != 0 ? HeapType.Upload
            : HeapType.Default;
        // Whole constant-buffer views of it: sized in their 256-byte steps.
        ulong bytes = Queue.Align(Math.Max(size, 4), D3D12.ConstantBufferDataPlacementAlignment);
        ResourceFlags flags = (descriptor.Usage & BufferUsage.Storage) != 0 ? ResourceFlags.AllowUnorderedAccess : ResourceFlags.None;
        ResourceStates state = heap switch
        {
            HeapType.Readback => ResourceStates.CopyDest,
            HeapType.Upload => ResourceStates.GenericRead,
            _ => ResourceStates.Common,
        };
        D3DAllocation allocation;
        ID3D12Resource resource;
        try
        {
            allocation = device.Memory.CreateResource(new AllocationDescription(heap), ResourceDescription.Buffer(bytes, flags), state, out resource);
        }
        catch (SharpGenException error)
        {
            throw new GpuException($"A buffer of {size} bytes couldn't be made: {error.Message}");
        }
        if (descriptor.Label is { } label)
            resource.Name = label;
        byte* pointer = null;
        if (heap != HeapType.Default)
        {
            void* mapped;
            resource.Map(0, null, &mapped).CheckError();
            pointer = (byte*)mapped;
            NativeMemory.Clear(pointer, (nuint)bytes);
        }
        Life = new BufferLife(device, resource, allocation, heap, bytes, pointer, counted: true);
        if (descriptor.MappedAtCreation)
        {
            if (heap == HeapType.Default)
            {
                // Written through memory of its own the CPU writes, copied in when it's unmapped.
                staging = device.CreateUpload(Queue.Align(Math.Max(size, 4), 4), "mapped at creation", out byte* stagingPointer);
                NativeMemory.Clear(stagingPointer, (nuint)staging.Size);
            }
            Mapped = true;
        }
        else if (heap == HeapType.Default)
        {
            // Zeros, as WebGPU makes buffers.
            lock (device.Sync)
                device.Pending().ClearBuffer(Life, 0, bytes);
        }
    }

    internal BufferLife Life { get; }

    internal BufferUsage Usage { get; }

    internal bool Mapped { get; set; }

    private protected override void Release()
    {
        if (staging is not null)
            device.Retire(staging);
        device.Retire(Life);
    }

    public ulong GetSize() => size;

    public void Destroy() => Dispose();

    public void Unmap()
    {
        if (staging is { } written)
        {
            staging = null;
            lock (device.Sync)
                device.Pending().CopyBuffer(written, 0, Life, 0, written.Size);
            device.Retire(written);
        }
        Mapped = false;
    }

    /// <summary>Maps the whole buffer, waiting for the GPU to be done with it.</summary>
    public void MapSync(MapMode mode) => MapSync(mode, 0, (nuint)size);

    public void MapSync(MapMode mode, nuint offset, nuint length)
    {
        if (Life.Pointer == null)
            throw new GpuException("The buffer can't be mapped: it wasn't made to be.");
        device.WaitFor(Life.LastUse);
        if (device.CheckLost())
            throw new GpuException("The buffer couldn't be mapped: the device is lost.");
        Mapped = true;
    }

    /// <summary>
    /// Maps the whole buffer without waiting: <paramref name="mapped"/> is called once it is, from a later device poll
    /// (each frame polls).
    /// </summary>
    public void Map(MapMode mode, Action<MapAsyncStatus, string> mapped)
    {
        if (Life.Pointer == null)
        {
            mapped(MapAsyncStatus.Error, "The buffer wasn't made to be mapped.");
            return;
        }
        device.Map(this, mapped);
    }

    /// <summary>The mapped range, <paramref name="count"/> values from value <paramref name="first"/>, read by <paramref name="read"/>.</summary>
    public TResult GetConstMappedRange<TResult, T>(nuint first, nuint count, Func<ReadOnlySpan<T>, TResult> read) where T : unmanaged
    {
        byte* range = Range(first * (nuint)sizeof(T), count * (nuint)sizeof(T));
        return read(new ReadOnlySpan<T>(range, checked((int)count)));
    }

    /// <summary>The mapped range for writing, <paramref name="count"/> values from value <paramref name="first"/>.</summary>
    public Span<T> GetMappedRange<T>(nuint first, nuint count) where T : unmanaged =>
        new(Range(first * (nuint)sizeof(T), count * (nuint)sizeof(T)), checked((int)count));

    private byte* Range(nuint offset, nuint length)
    {
        byte* pointer = staging is null ? Life.Pointer : staging.Pointer;
        if (!Mapped || pointer == null || offset + length > (nuint)size)
            throw new GpuException("The buffer isn't mapped there.");
        return pointer + offset;
    }
}

public sealed class Texture : GpuObject
{
    private readonly Device device;
    private readonly bool owned;

    internal Texture(Device device, in TextureDescriptor descriptor)
    {
        this.device = device;
        owned = true;
        TextureFormat format = descriptor.Format;
        bool depth = D3D12Formats.IsDepth(format);
        TextureUsage usage = descriptor.Usage;
        bool sampled = (usage & TextureUsage.TextureBinding) != 0;
        bool castable = descriptor.ViewFormats?.Any(view => view != format) == true;
        DxgiFormat made = D3D12Formats.Resource(format, depth && sampled, castable);
        ResourceFlags flags = ResourceFlags.None;
        if ((usage & TextureUsage.RenderAttachment) != 0)
            flags |= depth ? ResourceFlags.AllowDepthStencil : ResourceFlags.AllowRenderTarget;
        if ((usage & TextureUsage.StorageBinding) != 0)
            flags |= ResourceFlags.AllowUnorderedAccess;
        if (depth && !sampled)
            flags |= ResourceFlags.DenyShaderResource;
        Extent3D size = descriptor.Size;
        ResourceDescription description = descriptor.Dimension switch
        {
            TextureDimension.D1 => ResourceDescription.Texture1D(made, size.Width, (ushort)size.DepthOrArrayLayers, (ushort)descriptor.MipLevelCount, flags),
            TextureDimension.D3 => ResourceDescription.Texture3D(made, size.Width, size.Height, (ushort)size.DepthOrArrayLayers, (ushort)descriptor.MipLevelCount, flags),
            _ => ResourceDescription.Texture2D(made, size.Width, size.Height, (ushort)size.DepthOrArrayLayers, (ushort)descriptor.MipLevelCount,
                descriptor.SampleCount, 0, flags),
        };
        ResourceStates rest = TextureLife.Resting(usage, depth);
        D3DAllocation allocation;
        ID3D12Resource resource;
        try
        {
            allocation = device.Memory.CreateResource(new AllocationDescription(HeapType.Default), description, rest, out resource);
        }
        catch (SharpGenException error)
        {
            throw new GpuException($"A {size.Width}×{size.Height} {format} texture couldn't be made: {error.Message}");
        }
        if (descriptor.Label is { } label)
            resource.Name = label;
        Life = new TextureLife(device, resource, allocation, format, usage, descriptor.Dimension, size.Width, size.Height,
            size.DepthOrArrayLayers, descriptor.MipLevelCount, descriptor.SampleCount, rest);
        // What's drawn into starts cleared, as WebGPU makes textures.
        if ((usage & TextureUsage.RenderAttachment) != 0)
            lock (device.Sync)
                device.Pending().ClearTexture(Life);
    }

    /// <summary>A swap chain's buffer, which the swap chain keeps.</summary>
    internal Texture(Device device, TextureLife life)
    {
        this.device = device;
        Life = life;
    }

    internal TextureLife Life { get; }

    internal Device Device => device;

    private protected override void Release()
    {
        if (owned)
            device.Retire(Life);
    }

    public void Destroy() => Dispose();

    public uint GetWidth() => Life.Width;

    public uint GetHeight() => Life.Height;

    public uint GetDepthOrArrayLayers() => Life.Dimension == TextureDimension.D3 ? Life.Depth : Life.Layers;

    public uint GetMipLevelCount() => Life.Mips;

    public TextureFormat GetFormat() => Life.Format;

    public TextureUsage GetUsage() => Life.Usage;

    public TextureView CreateView() => CreateView(new TextureViewDescriptor());

    public TextureView CreateView(in TextureViewDescriptor descriptor)
    {
        // WebGPU's defaults: the texture's format, every mip, and its dimension (an array of layers, if it has more than one).
        TextureViewDimension dimension = descriptor.Dimension != TextureViewDimension.Undefined ? descriptor.Dimension
            : Life.Dimension switch
            {
                TextureDimension.D1 => TextureViewDimension.D1,
                TextureDimension.D3 => TextureViewDimension.D3,
                _ => Life.Layers > 1 ? TextureViewDimension.D2Array : TextureViewDimension.D2,
            };
        uint layers = descriptor.ArrayLayerCount ?? dimension switch
        {
            TextureViewDimension.D1 or TextureViewDimension.D2 or TextureViewDimension.D3 => 1,
            TextureViewDimension.Cube => 6,
            _ => Life.Layers - descriptor.BaseArrayLayer,
        };
        return new TextureView(this, descriptor.Format == TextureFormat.Undefined ? Life.Format : descriptor.Format, dimension,
            descriptor.BaseMipLevel, descriptor.MipLevelCount ?? Life.Mips - descriptor.BaseMipLevel, descriptor.BaseArrayLayer, layers,
            descriptor.Aspect);
    }
}

public sealed class TextureView : GpuObject
{
    private readonly Dictionary<uint, CpuDescriptorHandle> targets = [];
    private readonly CpuDescriptorHandle?[] depths = new CpuDescriptorHandle?[4];

    internal TextureView(Texture texture, TextureFormat format, TextureViewDimension dimension, uint baseMip, uint mips, uint baseLayer,
        uint layers, TextureAspect aspect)
    {
        Texture = texture;
        Format = format;
        Dimension = dimension;
        BaseMip = baseMip;
        Mips = mips;
        BaseLayer = baseLayer;
        Layers = layers;
        Aspect = aspect;
    }

    internal Texture Texture { get; }

    internal TextureFormat Format { get; }

    internal TextureViewDimension Dimension { get; }

    internal uint BaseMip { get; }

    internal uint Mips { get; }

    internal uint BaseLayer { get; }

    internal uint Layers { get; }

    internal TextureAspect Aspect { get; }

    private protected override void Release()
    {
        Device device = Texture.Device;
        lock (targets)
        {
            foreach (CpuDescriptorHandle handle in targets.Values)
                device.Rtv.Give(handle);
            targets.Clear();
            for (int i = 0; i < depths.Length; i++)
                if (depths[i] is { } handle)
                {
                    device.Dsv.Give(handle);
                    depths[i] = null;
                }
        }
    }

    /// <summary>The view as a render target: of one slice of a 3D texture, or of its mip and layers otherwise.</summary>
    internal CpuDescriptorHandle Target(uint slice)
    {
        lock (targets)
        {
            if (targets.TryGetValue(slice, out CpuDescriptorHandle handle))
                return handle;
            handle = Texture.Device.Rtv.Take();
            Texture.Device.Native.CreateRenderTargetView(Texture.Life.Resource, D3D12Views.Target(Texture.Life, Format, BaseMip, BaseLayer, Layers, slice, 1), handle);
            targets[slice] = handle;
            return handle;
        }
    }

    /// <summary>The view as a depth target, its depth or stencil (or both) only read.</summary>
    internal CpuDescriptorHandle DepthTarget(DepthStencilViewFlags flags)
    {
        lock (targets)
        {
            if (depths[(int)flags] is { } handle)
                return handle;
            handle = Texture.Device.Dsv.Take();
            Texture.Device.Native.CreateDepthStencilView(Texture.Life.Resource, D3D12Views.Depth(Texture.Life, Format, BaseMip, BaseLayer, Layers, flags), handle);
            depths[(int)flags] = handle;
            return handle;
        }
    }
}

/// <summary>How Direct3D 12 sees a texture's mip and layers as a render or depth target.</summary>
internal static class D3D12Views
{
    internal static RenderTargetViewDescription Target(TextureLife texture, TextureFormat format, uint mip, uint baseLayer, uint layers, uint slice, uint slices)
    {
        var description = new RenderTargetViewDescription { Format = D3D12Formats.View(format) };
        if (texture.Dimension == TextureDimension.D3)
        {
            description.ViewDimension = RenderTargetViewDimension.Texture3D;
            description.Texture3D = new Texture3DRenderTargetView { MipSlice = mip, FirstWSlice = slice, WSize = slices };
        }
        else if (texture.Samples > 1)
        {
            description.ViewDimension = texture.Layers > 1 ? RenderTargetViewDimension.Texture2DMultisampledArray : RenderTargetViewDimension.Texture2DMultisampled;
            description.Texture2DMSArray = new Texture2DMultisampledArrayRenderTargetView { FirstArraySlice = baseLayer, ArraySize = layers };
        }
        else if (texture.Layers > 1)
        {
            description.ViewDimension = RenderTargetViewDimension.Texture2DArray;
            description.Texture2DArray = new Texture2DArrayRenderTargetView { MipSlice = mip, FirstArraySlice = baseLayer, ArraySize = layers };
        }
        else
        {
            description.ViewDimension = RenderTargetViewDimension.Texture2D;
            description.Texture2D = new Texture2DRenderTargetView { MipSlice = mip };
        }
        return description;
    }

    internal static DepthStencilViewDescription Depth(TextureLife texture, TextureFormat format, uint mip, uint baseLayer, uint layers, DepthStencilViewFlags flags)
    {
        var description = new DepthStencilViewDescription { Format = D3D12Formats.View(format), Flags = flags };
        if (texture.Samples > 1)
        {
            description.ViewDimension = texture.Layers > 1 ? DepthStencilViewDimension.Texture2DMultisampledArray : DepthStencilViewDimension.Texture2DMultisampled;
            description.Texture2DMSArray = new Texture2DMultisampledArrayDepthStencilView { FirstArraySlice = baseLayer, ArraySize = layers };
        }
        else if (texture.Layers > 1)
        {
            description.ViewDimension = DepthStencilViewDimension.Texture2DArray;
            description.Texture2DArray = new Texture2DArrayDepthStencilView { MipSlice = mip, FirstArraySlice = baseLayer, ArraySize = layers };
        }
        else
        {
            description.ViewDimension = DepthStencilViewDimension.Texture2D;
            description.Texture2D = new Texture2DDepthStencilView { MipSlice = mip };
        }
        return description;
    }
}

/// <summary>A window's swap chain: three buffers flipped to the screen, drawn into through an sRGB view if asked.</summary>
public sealed class Surface : GpuObject
{
    private const uint Buffers = 3;

    private readonly Instance instance;
    private readonly nint window;
    private Device? device;
    private IDXGISwapChain3? chain;
    private TextureLife[] buffers = [];
    private PresentMode mode;
    private SwapChainFlags flags;

    internal Surface(Instance instance, nint window)
    {
        this.instance = instance;
        this.window = window;
    }

    private protected override void Release() => Unconfigure();

    public SurfaceCapabilities? GetCapabilities(Adapter adapter) => new(
        [TextureFormat.BGRA8Unorm, TextureFormat.RGBA8Unorm, TextureFormat.RGBA16Float],
        instance.Tearing ? [PresentMode.Fifo, PresentMode.Immediate, PresentMode.Mailbox] : [PresentMode.Fifo, PresentMode.Mailbox],
        [CompositeAlphaMode.Opaque]);

    public void Configure(in SurfaceConfiguration configuration)
    {
        Device on = configuration.Device;
        DxgiFormat format = configuration.Format switch
        {
            TextureFormat.BGRA8Unorm or TextureFormat.RGBA8Unorm or TextureFormat.RGBA16Float => D3D12Formats.View(configuration.Format),
            _ => DxgiFormat.Unknown,
        };
        if (format == DxgiFormat.Unknown)
        {
            on.Error(ErrorType.Validation, $"A window's swap chain can't be {configuration.Format}.");
            return;
        }
        uint width = Math.Max(1, configuration.Width), height = Math.Max(1, configuration.Height);
        try
        {
            if (chain is not null && device == on)
            {
                // Resized: the old buffers let go of first, once the GPU (and the screen) are done with them.
                on.WaitQueue();
                FreeBuffers();
                chain.ResizeBuffers(Buffers, width, height, format, flags).CheckError();
            }
            else
            {
                Unconfigure();
                flags = instance.Tearing ? SwapChainFlags.AllowTearing : SwapChainFlags.None;
                Usage usage = Usage.RenderTargetOutput | ((configuration.Usage & TextureUsage.TextureBinding) != 0 ? Usage.ShaderInput : 0);
                var description = new SwapChainDescription1(width, height, format, false, usage, Buffers, Scaling.Stretch, SwapEffect.FlipDiscard,
                    AlphaMode.Ignore, flags);
                using IDXGISwapChain1 made = instance.Factory.CreateSwapChainForHwnd(on.Direct, window, description);
                chain = made.QueryInterface<IDXGISwapChain3>();
                instance.Factory.MakeWindowAssociation(window, WindowAssociationFlags.IgnoreAltEnter);
                device = on;
            }
        }
        catch (SharpGenException error)
        {
            on.Error(ErrorType.Validation, $"The window's swap chain couldn't be made: {error.Message}");
            return;
        }
        mode = configuration.PresentMode;
        buffers = new TextureLife[Buffers];
        for (uint i = 0; i < Buffers; i++)
            buffers[i] = new TextureLife(on, chain!.GetBuffer<ID3D12Resource>(i), null, configuration.Format, configuration.Usage,
                TextureDimension.D2, width, height, 1, 1, 1, ResourceStates.Present);
    }

    public void Unconfigure()
    {
        if (chain is null)
            return;
        device!.WaitQueue();
        FreeBuffers();
        chain.Dispose();
        chain = null;
        device = null;
    }

    private void FreeBuffers()
    {
        foreach (TextureLife buffer in buffers)
            buffer.Free();
        buffers = [];
    }

    public SurfaceTexture GetCurrentTexture()
    {
        if (chain is null || device is null || buffers.Length == 0)
            return new SurfaceTexture(SurfaceGetCurrentTextureStatus.Error, null);
        if (device.IsLost)
            return new SurfaceTexture(SurfaceGetCurrentTextureStatus.Lost, null);
        return new SurfaceTexture(SurfaceGetCurrentTextureStatus.SuccessOptimal, new Texture(device, buffers[chain.CurrentBackBufferIndex]));
    }

    public bool Present()
    {
        if (chain is null)
            return false;
        bool now = mode is PresentMode.Immediate or PresentMode.Mailbox;
        PresentFlags presentFlags = mode == PresentMode.Immediate && (flags & SwapChainFlags.AllowTearing) != 0 ? PresentFlags.AllowTearing : PresentFlags.None;
        Result result = chain.Present(now ? 0u : 1u, presentFlags);
        if (result.Failure)
        {
            device?.CheckLost();
            return false;
        }
        return true;
    }
}
