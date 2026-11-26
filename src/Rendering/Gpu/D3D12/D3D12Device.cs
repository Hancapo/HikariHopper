using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.D3D12MemoryAllocator;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.Direct3D12.Debug;
using Vortice.DXGI;
using D3DAllocation = Vortice.D3D12MemoryAllocator.Allocation;
using DxgiFormat = Vortice.DXGI.Format;

namespace HikariHopper.Gpu;

/// <summary>
/// An object of the GPU layer this app holds: let go of once, when disposed, or by its finalizer when it's no longer
/// reachable. What the GPU may still be using isn't freed then but retired to its device, which frees it once the GPU is
/// done with it (see <see cref="D3D12Life"/>).
/// </summary>
public abstract class GpuObject : IDisposable
{
    private int released;

    private protected GpuObject()
    {
    }

    private protected abstract void Release();

    public void Dispose()
    {
        Free();
        GC.SuppressFinalize(this);
    }

    ~GpuObject() => Free();

    private void Free()
    {
        if (Interlocked.Exchange(ref released, 1) == 0)
            Release();
    }
}

/// <summary>
/// Something the GPU may use, freed only when nothing can any more: it's been let go of, no command list still being
/// recorded (or recorded and not yet submitted) uses it, and the GPU has finished the last submit that did.
/// </summary>
internal abstract class D3D12Life
{
    /// <summary>The serial of the last submit that used it.</summary>
    internal ulong LastUse;

    /// <summary>Command lists that use it and haven't been submitted.</summary>
    internal int Recording;

    internal bool Released;

    internal abstract void Free();
}

/// <summary>A COM object kept until the GPU is done with it.</summary>
internal sealed class ComLife(IDisposable item) : D3D12Life
{
    internal override void Free() => item.Dispose();
}

public sealed class Instance : GpuObject
{
    private static int pixLoaded;
    private readonly ConcurrentDictionary<string, int> live = new();
    private readonly List<Device> devices = [];

    private Instance(IDXGIFactory6 factory, bool debug)
    {
        Factory = factory;
        Debug = debug;
        Tearing = factory.PresentAllowTearing;
    }

    internal IDXGIFactory6 Factory { get; }

    /// <summary>Whether Direct3D 12's debug layer checks what's done (HIKARI_D3D12_DEBUG=1).</summary>
    internal bool Debug { get; }

    /// <summary>Whether the display can show frames as soon as they're presented, tearing.</summary>
    internal bool Tearing { get; }

    /// <summary>
    /// An instance of Direct3D 12. With <paramref name="dred"/>, or HIKARI_D3D12_DRED=1, Direct3D 12's Device Removed
    /// Extended Data is kept, for a lost device to say what the GPU was doing. HIKARI_D3D12_DEBUG=1 turns on the debug
    /// layer (HIKARI_D3D12_GBV=1 its GPU-based validation too), and HIKARI_PIX=1 loads PIX's GPU capturer, so PIX for
    /// Windows can capture frames of a running app.
    /// </summary>
    public static Instance Create(bool dred = false)
    {
        bool debug = Environment.GetEnvironmentVariable("HIKARI_D3D12_DEBUG") == "1";
        if (Environment.GetEnvironmentVariable("HIKARI_PIX") == "1")
            LoadPix();
        if (debug && D3D12.D3D12GetDebugInterface(out ID3D12Debug1? layer).Success && layer is not null)
        {
            layer.EnableDebugLayer();
            if (Environment.GetEnvironmentVariable("HIKARI_D3D12_GBV") == "1")
                layer.SetEnableGPUBasedValidation(true);
            layer.Dispose();
        }
        if ((dred || Environment.GetEnvironmentVariable("HIKARI_D3D12_DRED") == "1")
            && D3D12.D3D12GetDebugInterface(out ID3D12DeviceRemovedExtendedDataSettings? settings).Success && settings is not null)
        {
            settings.SetAutoBreadcrumbsEnablement(DredEnablement.ForcedOn);
            settings.SetPageFaultEnablement(DredEnablement.ForcedOn);
            settings.Dispose();
        }
        // DXGI's own debug layer comes with Windows' Graphics Tools, which may not be installed.
        IDXGIFactory6 factory = debug && DXGI.CreateDXGIFactory2(true, out IDXGIFactory6? checking).Success ? checking! : DXGI.CreateDXGIFactory2<IDXGIFactory6>(false);
        return new Instance(factory, debug);
    }

    // PIX attaches to a process that loaded its capturer before making its device: the newest PIX installed.
    private static void LoadPix()
    {
        if (Interlocked.Exchange(ref pixLoaded, 1) != 0)
            return;
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft PIX");
        if (!Directory.Exists(root))
            return;
        string? newest = Directory.GetDirectories(root)
            .Select(folder => Path.Combine(folder, "WinPixGpuCapturer.dll"))
            .Where(File.Exists)
            .OrderByDescending(path => Version.TryParse(Path.GetFileName(Path.GetDirectoryName(path)), out Version? version) ? version : new Version())
            .FirstOrDefault();
        if (newest is not null)
            NativeLibrary.TryLoad(newest, out _);
    }

    private protected override void Release() => Factory.Dispose();

    /// <summary>
    /// How many objects of a kind made from this instance are alive ("render bundles", "buffers", "textures"...): those
    /// let go of and still used by the GPU included until it's done with them.
    /// </summary>
    public int LiveObjects(string type) => live.TryGetValue(type, out int count) ? count : 0;

    internal void Count(string type, int change) => live.AddOrUpdate(type, change, (_, count) => count + change);

    /// <summary>Runs the callbacks of what has completed: buffer maps.</summary>
    public void ProcessEvents()
    {
        Device[] now;
        lock (devices)
            now = [.. devices];
        foreach (Device device in now)
            device.Poll();
    }

    internal void Add(Device device)
    {
        lock (devices)
            devices.Add(device);
    }

    internal void Remove(Device device)
    {
        lock (devices)
            devices.Remove(device);
    }

    public Adapter? RequestAdapterSync(RequestAdapterOptions options)
    {
        try
        {
            if (options.ForceFallbackAdapter)
                return Factory.EnumWarpAdapter(out IDXGIAdapter1? warp).Success && warp is not null ? new Adapter(warp, this) : null;
            GpuPreference preference = options.PowerPreference switch
            {
                PowerPreference.LowPower => GpuPreference.MinimumPower,
                PowerPreference.HighPerformance => GpuPreference.HighPerformance,
                _ => GpuPreference.Unspecified,
            };
            for (uint index = 0; Factory.EnumAdapterByGpuPreference(index, preference, out IDXGIAdapter1? adapter).Success && adapter is not null; index++)
            {
                if ((adapter.Description1.Flags & AdapterFlags.Software) == 0 && D3D12.IsSupported(adapter, FeatureLevel.Level_11_0))
                    return new Adapter(adapter, this);
                adapter.Dispose();
            }
            return null;
        }
        catch (SharpGenException)
        {
            return null;
        }
    }

    /// <summary>A surface for a window, by its HWND (the module isn't needed).</summary>
    public Surface? CreateSurface(nint window, nint module) => window == 0 ? null : new Surface(this, window);
}

public sealed class Adapter : GpuObject
{
    private readonly Instance instance;
    private ID3D12Device? device;

    internal Adapter(IDXGIAdapter1 native, Instance instance)
    {
        Native = native;
        this.instance = instance;
    }

    internal IDXGIAdapter1 Native { get; }

    private protected override void Release() => Native.Dispose();

    /// <summary>
    /// The adapter's device, made once: Direct3D 12 gives one device an adapter in a process, so every <see cref="Device"/>
    /// of this adapter draws with it.
    /// </summary>
    internal ID3D12Device NativeDevice()
    {
        if (device is null)
        {
            D3D12.D3D12CreateDevice(Native, FeatureLevel.Level_11_0, out ID3D12Device? made).CheckError();
            device = made!;
        }
        return device;
    }

    public bool HasFeature(FeatureName feature) => feature switch
    {
        FeatureName.CoreFeaturesAndLimits or FeatureName.DepthClipControl or FeatureName.Depth32FloatStencil8
            or FeatureName.TextureCompressionBC or FeatureName.TextureCompressionBCSliced3D or FeatureName.TimestampQuery
            or FeatureName.IndirectFirstInstance or FeatureName.RG11B10UfloatRenderable or FeatureName.Float32Filterable
            or FeatureName.Float32Blendable or FeatureName.ClipDistances or FeatureName.DualSourceBlending => true,
        _ => false,
    };

    public void GetLimits(out Limits limits)
    {
        // Bind groups are descriptor tables in one shader-visible heap: as many textures and buffers as the hardware's
        // binding tier lets a table hold, and samplers in tables of their own.
        ResourceBindingTier tier = NativeDevice().Options.ResourceBindingTier;
        limits = new Limits
        {
            MaxSampledTexturesPerShaderStage = tier >= ResourceBindingTier.Tier2 ? 1_000_000u : 128u,
            MaxSamplersPerShaderStage = tier >= ResourceBindingTier.Tier2 ? 2048u : 16u,
            MaxStorageBuffersPerShaderStage = tier >= ResourceBindingTier.Tier3 ? 1_000_000u : 64u,
            MaxBindGroups = 8,
            MaxBufferSize = 1ul << 31,
            MaxTextureDimension2D = 16384,
        };
    }

    public AdapterInfo GetInfo()
    {
        AdapterDescription1 description = Native.Description1;
        string vendor = description.VendorId switch
        {
            0x10DE => "nvidia",
            0x1002 => "amd",
            0x8086 => "intel",
            0x1414 => "microsoft",
            0x5143 => "qualcomm",
            _ => $"0x{description.VendorId:x4}",
        };
        return new AdapterInfo(vendor, "", description.Description, description.Description, BackendType.D3D12, description.VendorId, description.DeviceId);
    }

    public Device? RequestDeviceSync(DeviceDescriptor descriptor)
    {
        try
        {
            return new Device(this, instance, descriptor);
        }
        catch (SharpGenException)
        {
            return null;
        }
    }
}

public sealed unsafe class Device : GpuObject
{
    private const ulong UploadPage = 8 << 20;

    private readonly Instance instance;
    private readonly Adapter adapter;
    private readonly HashSet<FeatureName> features;
    private readonly Action<DeviceLostReason, string>? lostCallback;
    private readonly Action<ErrorType, string>? uncaptured;
    private readonly ID3D12Fence fence;
    private readonly ID3D12InfoQueue? messages;
    private readonly List<D3D12Life> retired = [];
    private readonly List<(Buffer Buffer, Action<MapAsyncStatus, string> Mapped)> maps = [];
    private readonly Stack<ID3D12GraphicsCommandList> lists = new();
    private readonly Queue<(ID3D12CommandAllocator Allocator, ulong Serial)> allocators = new();
    private readonly List<(ErrorFilter Filter, ErrorType Type, string Message)> scopes = [];
    private readonly List<UploadPageLife> freePages = [];
    private readonly List<UploadPageLife> dirtyPages = [];
    private UploadPageLife? page;
    private ulong pageOffset;
    private Recorder? pending;
    private ulong submitted;
    private int lost;
    private long bufferBytes, textureBytes, allocations;

    internal Device(Adapter adapter, Instance instance, in DeviceDescriptor descriptor)
    {
        this.instance = instance;
        this.adapter = adapter;
        Native = adapter.NativeDevice();
        features = [.. descriptor.RequiredFeatures ?? []];
        lostCallback = descriptor.DeviceLostCallback;
        uncaptured = descriptor.UncapturedErrorCallback;
        Direct = Native.CreateCommandQueue(CommandListType.Direct);
        Direct.Name = "HikariHopper";
        fence = Native.CreateFence<ID3D12Fence>(0);
        Memory = D3D12MA.CreateAllocator(new AllocatorDescription(Native, adapter.Native));
        Rtv = new CpuDescriptors(Native, DescriptorHeapType.RenderTargetView);
        Dsv = new CpuDescriptors(Native, DescriptorHeapType.DepthStencilView);
        if (instance.Debug)
            messages = Native.QueryInterfaceOrNull<ID3D12InfoQueue>();
        Zeros = CreateUpload(1 << 20, "zeros", out byte* zeros);
        NativeMemory.Clear(zeros, 1 << 20);
        Queue = new Queue(this);
        instance.Add(this);
    }

    internal ID3D12Device Native { get; }

    internal ID3D12CommandQueue Direct { get; }

    internal Allocator Memory { get; }

    internal CpuDescriptors Rtv { get; }

    internal CpuDescriptors Dsv { get; }

    /// <summary>A megabyte of zeros, copied into what's made to clear it.</summary>
    internal BufferLife Zeros { get; }

    internal Instance Instance => instance;

    /// <summary>The shader-visible heaps bind groups and samplers go into, made with the first that does.</summary>
    internal ShaderHeaps Heaps => LazyInitializer.EnsureInitialized(ref heaps, ref heapsMade, ref heapsLock, () => new ShaderHeaps(this))!;

    private ShaderHeaps? heaps;
    private bool heapsMade;
    private object? heapsLock;

    /// <summary>What submits and retires hold.</summary>
    internal object Sync { get; } = new();

    internal bool IsLost => Volatile.Read(ref lost) != 0;

    private protected override void Release()
    {
        instance.Remove(this);
        if (IsLost)
            return;
        WaitQueue();
        lock (Sync)
            FreeRetired(fence.CompletedValue);
    }

    public Queue Queue { get; }

    public Queue GetQueue() => Queue;

    public void GetLimits(out Limits limits) => adapter.GetLimits(out limits);

    public bool HasFeature(FeatureName feature) => features.Contains(feature);

    /// <summary>Lets go of the device: everything submitted is waited out, and it reports itself lost as destroyed.</summary>
    public void Destroy()
    {
        WaitQueue();
        if (Interlocked.Exchange(ref lost, 1) != 0)
            return;
        Report(() => lostCallback?.Invoke(DeviceLostReason.Destroyed, "The device was destroyed."));
    }

    /// <summary>The GPU memory the device's buffers and textures take, and how many allocations hold them.</summary>
    public (long Buffers, long Textures, long Allocations) MemoryCounters() =>
        (Interlocked.Read(ref bufferBytes), Interlocked.Read(ref textureBytes), Interlocked.Read(ref allocations));

    internal void Counted(long buffer, long texture)
    {
        Interlocked.Add(ref bufferBytes, buffer);
        Interlocked.Add(ref textureBytes, texture);
        Interlocked.Add(ref allocations, Math.Sign(buffer + texture));
    }

    /// <summary>
    /// Frees what the GPU is done with and calls back the buffer maps that are ready. With <paramref name="wait"/>, until
    /// the GPU has done everything submitted. False when the device is lost.
    /// </summary>
    public bool Poll(bool wait = false)
    {
        if (IsLost)
            return false;
        if (wait)
            WaitIdle();
        List<(Action<MapAsyncStatus, string> Mapped, MapAsyncStatus Status)>? ready = null;
        lock (Sync)
        {
            ulong completed = fence.CompletedValue;
            FreeRetired(completed);
            for (int i = maps.Count - 1; i >= 0; i--)
            {
                (Buffer buffer, Action<MapAsyncStatus, string> mapped) = maps[i];
                if (buffer.Life.LastUse > completed)
                    continue;
                maps.RemoveAt(i);
                buffer.Mapped = true;
                (ready ??= []).Add((mapped, MapAsyncStatus.Success));
            }
        }
        if (ready is not null)
            foreach ((Action<MapAsyncStatus, string> mapped, MapAsyncStatus status) in ready)
                Report(() => mapped(status, ""));
        Drain(uncaughtOnly: true);
        return !CheckLost();
    }

    internal ulong Completed => fence.CompletedValue;

    /// <summary>Waits for the GPU to finish the submit of the given serial (all submitted, by default).</summary>
    internal void WaitFor(ulong serial)
    {
        if (fence.CompletedValue < serial)
            fence.SetEventOnCompletion(serial, 0).CheckError();
    }

    internal void WaitIdle() => WaitFor(Volatile.Read(ref submitted));

    /// <summary>
    /// Waits for everything on the queue, presents included: they're queued after the last submit's signal, so the
    /// queue is signalled once more and that waited for.
    /// </summary>
    internal void WaitQueue()
    {
        if (IsLost)
            return;
        ulong serial;
        lock (Sync)
        {
            serial = ++submitted;
            Direct.Signal(fence, serial).CheckError();
        }
        WaitFor(serial);
    }

    /// <summary>Hands the device something the GPU may be using, to free once it's done with it.</summary>
    internal void Retire(D3D12Life life)
    {
        lock (Sync)
        {
            life.Released = true;
            retired.Add(life);
        }
    }

    private void FreeRetired(ulong completed)
    {
        for (int i = retired.Count - 1; i >= 0; i--)
        {
            D3D12Life life = retired[i];
            if (Volatile.Read(ref life.Recording) != 0 || life.LastUse > completed)
                continue;
            retired[i] = retired[^1];
            retired.RemoveAt(retired.Count - 1);
            life.Free();
        }
    }

    public Buffer CreateBuffer(in BufferDescriptor descriptor) => new(this, descriptor);

    public Texture CreateTexture(in TextureDescriptor descriptor) => new(this, descriptor);

    public Sampler CreateSampler() => CreateSampler(new SamplerDescriptor());

    public Sampler CreateSampler(in SamplerDescriptor descriptor) => new(this, descriptor);

    public BindGroupLayout CreateBindGroupLayout(in BindGroupLayoutDescriptor descriptor) => new(descriptor);

    public BindGroup CreateBindGroup(in BindGroupDescriptor descriptor) => new(this, descriptor);

    public PipelineLayout CreatePipelineLayout(in PipelineLayoutDescriptor descriptor) => new(this, descriptor);

    public ShaderModule CreateShaderModuleWGSL(in ShaderModuleWGSLDescriptor descriptor) => new(this, descriptor);

    public RenderPipeline CreateRenderPipelineSync(in RenderPipelineDescriptor descriptor) => new(this, descriptor);

    public Task<RenderPipeline> CreateRenderPipelineAsync(RenderPipelineDescriptor descriptor) => Task.Run(() => CreateRenderPipelineSync(descriptor));

    public ComputePipeline CreateComputePipelineSync(in ComputePipelineDescriptor descriptor) => new(this, descriptor);

    public CommandEncoder CreateCommandEncoder() => new(new Recorder(this));

    public RenderBundleEncoder CreateRenderBundleEncoder(in RenderBundleEncoderDescriptor descriptor) => throw D3D12Pending.Bundles();

    public void PushErrorScope(ErrorFilter filter)
    {
        lock (scopes)
            scopes.Add((filter, ErrorType.NoError, ""));
    }

    /// <summary>The first error the innermost scope caught (NoError if none).</summary>
    public (ErrorType Type, string Message) PopErrorScopeSync(ulong timeoutNanoseconds = ulong.MaxValue)
    {
        Drain(uncaughtOnly: false);
        lock (scopes)
        {
            if (scopes.Count == 0)
                return (ErrorType.Unknown, "There's no error scope to pop.");
            (_, ErrorType type, string message) = scopes[^1];
            scopes.RemoveAt(scopes.Count - 1);
            return (type, message);
        }
    }

    /// <summary>
    /// An error the GPU layer met, as wgpu's validation would report it: caught by the innermost scope that filters it,
    /// or the uncaptured error callback's.
    /// </summary>
    internal void Error(ErrorType type, string message)
    {
        ErrorFilter filter = type switch
        {
            ErrorType.OutOfMemory => ErrorFilter.OutOfMemory,
            ErrorType.Internal => ErrorFilter.Internal,
            _ => ErrorFilter.Validation,
        };
        lock (scopes)
        {
            for (int i = scopes.Count - 1; i >= 0; i--)
            {
                if (scopes[i].Filter != filter)
                    continue;
                if (scopes[i].Type == ErrorType.NoError)
                    scopes[i] = (filter, type, message);
                return;
            }
        }
        Report(() => uncaptured?.Invoke(type, message));
    }

    // What the debug layer found wrong, as validation errors.
    private void Drain(bool uncaughtOnly)
    {
        if (messages is null)
            return;
        List<string>? errors = null;
        lock (messages)
        {
            ulong count = messages.NumStoredMessages;
            for (ulong i = 0; i < count; i++)
            {
                Message message = messages.GetMessage(i);
                if (message.Severity is MessageSeverity.Corruption or MessageSeverity.Error)
                    (errors ??= []).Add(message.Description);
            }
            messages.ClearStoredMessages();
        }
        if (errors is null)
            return;
        foreach (string error in errors)
        {
            if (uncaughtOnly)
                Report(() => uncaptured?.Invoke(ErrorType.Validation, error));
            else
                Error(ErrorType.Validation, error);
        }
    }

    /// <summary>Whether the device is lost, telling the lost callback the first time it's found to be.</summary>
    internal bool CheckLost()
    {
        if (IsLost)
            return true;
        Result reason = Native.DeviceRemovedReason;
        if (reason.Success || Interlocked.Exchange(ref lost, 1) != 0)
            return IsLost;
        string message = $"Direct3D 12 removed the device ({reason.Description.Trim()}, 0x{reason.Code:X8}).{D3D12Dred.Describe(Native)}";
        Report(() => lostCallback?.Invoke(DeviceLostReason.Unknown, message));
        return true;
    }

    // Never unwinding into what called back.
    private static void Report(Action call)
    {
        try
        {
            call();
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.WriteLine($"A GPU callback failed: {error}");
        }
    }

    internal void Map(Buffer buffer, Action<MapAsyncStatus, string> mapped)
    {
        lock (Sync)
            maps.Add((buffer, mapped));
    }

    // Command lists and their allocators, used again once the GPU is done with them.

    internal (ID3D12CommandAllocator Allocator, ID3D12GraphicsCommandList List) OpenList()
    {
        ID3D12CommandAllocator allocator;
        ID3D12GraphicsCommandList? list;
        lock (Sync)
        {
            if (allocators.TryPeek(out var oldest) && oldest.Serial <= fence.CompletedValue)
            {
                allocator = allocators.Dequeue().Allocator;
                allocator.Reset();
            }
            else
            {
                allocator = Native.CreateCommandAllocator(CommandListType.Direct);
            }
            lists.TryPop(out list);
        }
        if (list is null)
            list = Native.CreateCommandList<ID3D12GraphicsCommandList>(CommandListType.Direct, allocator, null);
        else
            list.Reset(allocator, null);
        return (allocator, list);
    }

    /// <summary>A recorded list given back unsubmitted: its allocator is free at once.</summary>
    internal void Recycle(ID3D12CommandAllocator allocator, ID3D12GraphicsCommandList list, ulong serial)
    {
        lock (Sync)
        {
            allocators.Enqueue((allocator, serial));
            lists.Push(list);
        }
    }

    internal void Submit(ReadOnlySpan<CommandBuffer> commands)
    {
        if (IsLost)
            return;
        lock (Sync)
        {
            ulong serial = submitted + 1;
            if (pending is { } writes)
            {
                pending = null;
                writes.Close();
                Direct.ExecuteCommandList(writes.List);
                writes.Submitted(serial);
            }
            foreach (CommandBuffer command in commands)
            {
                Recorder recorder = command.Take();
                Direct.ExecuteCommandList(recorder.List);
                recorder.Submitted(serial);
            }
            Direct.Signal(fence, serial).CheckError();
            Volatile.Write(ref submitted, serial);
            foreach (UploadPageLife used in dirtyPages)
                used.LastUse = serial;
            dirtyPages.Clear();
        }
        CheckLost();
    }

    /// <summary>The command list the queue's writes are recorded into, run before what's submitted next. Under <see cref="Sync"/>.</summary>
    internal Recorder Pending() => pending ??= new Recorder(this);

    /// <summary>
    /// Room in the upload pages for <paramref name="size"/> bytes the CPU writes for a copy, aligned to
    /// <paramref name="alignment"/>: in use until the next submit has run. Under <see cref="Sync"/>.
    /// </summary>
    internal BufferLife Upload(ulong size, ulong alignment, out ulong at, out byte* pointer)
    {
        if (size > UploadPage / 2)
        {
            // Too big to share a page: one of its own, freed once copied.
            BufferLife own = CreateUpload(size, "upload", out pointer);
            Retire(own);
            own.LastUse = submitted + 1;
            at = 0;
            return own;
        }
        ulong offset = (pageOffset + alignment - 1) / alignment * alignment;
        if (page is null || offset + size > UploadPage)
        {
            page = TakePage();
            offset = 0;
        }
        pageOffset = offset + size;
        if (!page.Dirty)
        {
            page.Dirty = true;
            dirtyPages.Add(page);
        }
        at = offset;
        pointer = page.Pointer + offset;
        return page.Buffer;
    }

    private UploadPageLife TakePage()
    {
        if (page is not null)
            freePages.Add(page);
        ulong completed = fence.CompletedValue;
        for (int i = 0; i < freePages.Count; i++)
        {
            UploadPageLife free = freePages[i];
            if (free.Dirty || free.LastUse > completed)
                continue;
            freePages.RemoveAt(i);
            return free;
        }
        BufferLife buffer = CreateUpload(UploadPage, "upload page", out byte* pointer);
        return new UploadPageLife(buffer, pointer);
    }

    /// <summary>A buffer in memory the CPU writes and the GPU reads, mapped for as long as it lives.</summary>
    internal BufferLife CreateUpload(ulong size, string name, out byte* pointer)
    {
        D3DAllocation allocation = Memory.CreateResource(new AllocationDescription(HeapType.Upload), ResourceDescription.Buffer(size),
            ResourceStates.GenericRead, out ID3D12Resource resource);
        resource.Name = name;
        void* mapped;
        resource.Map(0, null, &mapped).CheckError();
        pointer = (byte*)mapped;
        return new BufferLife(this, resource, allocation, HeapType.Upload, size, pointer);
    }

    /// <summary>A buffer of the GPU's own memory a copy goes through; retired by who made it, once recorded.</summary>
    internal BufferLife CreateScratch(ulong size)
    {
        D3DAllocation allocation = Memory.CreateResource(new AllocationDescription(HeapType.Default), ResourceDescription.Buffer(size),
            ResourceStates.Common, out ID3D12Resource resource);
        resource.Name = "copy scratch";
        return new BufferLife(this, resource, allocation, HeapType.Default, size, null);
    }

    private sealed class UploadPageLife(BufferLife buffer, byte* pointer)
    {
        internal readonly BufferLife Buffer = buffer;
        internal readonly byte* Pointer = pointer;
        internal ulong LastUse;
        internal bool Dirty;
    }
}

public sealed unsafe class Queue : GpuObject
{
    private readonly Device device;

    internal Queue(Device device) => this.device = device;

    private protected override void Release()
    {
    }

    /// <summary>Submits the command buffers, which are used up: each is let go once submitted.</summary>
    public void Submit(params ReadOnlySpan<CommandBuffer> commands)
    {
        device.Submit(commands);
        foreach (CommandBuffer command in commands)
            command.Dispose();
    }

    /// <summary>Writes the data into the buffer before what's submitted next runs.</summary>
    public void WriteBuffer<T>(Buffer buffer, ulong offset, ReadOnlySpan<T> data) where T : unmanaged
    {
        if (data.IsEmpty)
            return;
        ulong size = (ulong)data.Length * (ulong)sizeof(T);
        if (offset + size > buffer.GetSize())
        {
            device.Error(ErrorType.Validation, $"A write of {size} bytes at {offset} overruns a buffer of {buffer.GetSize()}.");
            return;
        }
        lock (device.Sync)
        {
            BufferLife staging = device.Upload(size, 4, out ulong at, out byte* pointer);
            fixed (T* source = data)
                System.Buffer.MemoryCopy(source, pointer, size, size);
            device.Pending().CopyBuffer(staging, at, buffer.Life, offset, size);
        }
    }

    public void WriteBuffer<T>(Buffer buffer, ulong offset, T[] data) where T : unmanaged => WriteBuffer(buffer, offset, (ReadOnlySpan<T>)data);

    /// <summary>Writes the texels into the texture before what's submitted next runs.</summary>
    public void WriteTexture<T>(in TexelCopyTextureInfo destination, ReadOnlySpan<T> data, in TexelCopyBufferLayout layout, in Extent3D size) where T : unmanaged
    {
        if (size.Width == 0 || size.Height == 0 || size.DepthOrArrayLayers == 0)
            return;
        Texture texture = destination.Texture;
        (uint block, uint blockBytes) = D3D12Formats.Layout(texture.GetFormat());
        uint blocksWide = (size.Width + block - 1) / block, blocksHigh = (size.Height + block - 1) / block;
        ulong rowBytes = (ulong)blocksWide * blockBytes;
        ulong pitch = Align(rowBytes, D3D12.TextureDataPitchAlignment);
        ulong sourcePitch = layout.BytesPerRow == uint.MaxValue ? rowBytes : layout.BytesPerRow;
        ulong sourceRows = layout.RowsPerImage == uint.MaxValue ? blocksHigh : layout.RowsPerImage;
        ulong needed = layout.Offset + sourcePitch * sourceRows * (size.DepthOrArrayLayers - 1) + sourcePitch * (blocksHigh - 1) + rowBytes;
        ulong bytes = (ulong)data.Length * (ulong)sizeof(T);
        if (needed > bytes)
        {
            device.Error(ErrorType.Validation, $"A texture write needs {needed} bytes and was given {bytes}.");
            return;
        }
        lock (device.Sync)
        {
            ulong image = pitch * blocksHigh;
            BufferLife staging = device.Upload(image * size.DepthOrArrayLayers, D3D12.TextureDataPlacementAlignment, out ulong at, out byte* pointer);
            fixed (T* source = data)
            {
                byte* from = (byte*)source + layout.Offset;
                for (uint layer = 0; layer < size.DepthOrArrayLayers; layer++)
                    for (uint row = 0; row < blocksHigh; row++)
                        System.Buffer.MemoryCopy(from + layer * sourcePitch * sourceRows + row * sourcePitch, pointer + layer * image + row * pitch, rowBytes, rowBytes);
            }
            device.Pending().CopyToTexture(staging, at, (uint)pitch, blocksHigh, destination, size);
        }
    }

    public void WriteTexture<T>(in TexelCopyTextureInfo destination, T[] data, in TexelCopyBufferLayout layout, in Extent3D size) where T : unmanaged =>
        WriteTexture(destination, (ReadOnlySpan<T>)data, layout, size);

    internal static ulong Align(ulong value, ulong alignment) => (value + alignment - 1) / alignment * alignment;
}

/// <summary>
/// CPU descriptors of one kind (render target or depth views), in heaps of 256 handed out one at a time. A render pass
/// reads them as it's recorded, so one is free again as soon as its view is let go of.
/// </summary>
internal sealed class CpuDescriptors(ID3D12Device device, DescriptorHeapType type)
{
    private const int HeapSize = 256;
    private readonly List<ID3D12DescriptorHeap> heaps = [];
    private readonly Stack<CpuDescriptorHandle> free = new();
    private readonly uint increment = device.GetDescriptorHandleIncrementSize(type);

    internal CpuDescriptorHandle Take()
    {
        lock (free)
        {
            if (free.Count == 0)
            {
                ID3D12DescriptorHeap heap = device.CreateDescriptorHeap(new DescriptorHeapDescription(type, HeapSize, DescriptorHeapFlags.None));
                heaps.Add(heap);
                CpuDescriptorHandle start = heap.GetCPUDescriptorHandleForHeapStart();
                for (int i = HeapSize - 1; i >= 0; i--)
                    free.Push(new CpuDescriptorHandle(start, i, increment));
            }
            return free.Pop();
        }
    }

    internal void Give(CpuDescriptorHandle handle)
    {
        lock (free)
            free.Push(handle);
    }
}

/// <summary>What isn't made on Direct3D 12 yet, while it's built in steps.</summary>
internal static class D3D12Pending
{
    internal static NotSupportedException Bundles() => new("Direct3D 12 doesn't record render bundles yet.");

    internal static NotSupportedException Draws() => new("Direct3D 12 doesn't draw or dispatch yet.");
}

/// <summary>What Device Removed Extended Data says the GPU was doing when it lost the device, when it's kept.</summary>
internal static unsafe class D3D12Dred
{
    internal static string Describe(ID3D12Device device)
    {
        using ID3D12DeviceRemovedExtendedData? dred = device.QueryInterfaceOrNull<ID3D12DeviceRemovedExtendedData>();
        if (dred is null)
            return " Set HIKARI_D3D12_DRED=1 to learn what the GPU was doing.";
        var text = new System.Text.StringBuilder();
        if (dred.GetPageFaultAllocationOutput(out DredPageFaultOutput fault).Success && fault.PageFaultVA != 0)
            text.Append($" The GPU faulted reading 0x{fault.PageFaultVA:X}.");
        return text.Length == 0 ? " Device Removed Extended Data has nothing to add." : text.ToString();
    }
}
