using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using WGpuFury;

namespace HikariHopper.Gpu;

/// <summary>
/// A wgpu object this app holds: released once, when disposed, or by its finalizer when it's no longer reachable.
/// wgpu's objects are thread-safe and reference-counted inside, so a release on the finalizer's thread is fine, and an
/// object still used by a recorded pass or bundle stays alive in wgpu until that's done.
/// </summary>
public abstract class GpuObject : IDisposable
{
    private nint handle;

    private protected GpuObject(nint handle)
    {
        if (handle == 0)
            throw new GpuException($"{GetType().Name} couldn't be made.");
        this.handle = handle;
    }

    /// <summary>The native handle; zero once released.</summary>
    internal nint Handle => handle;

    private protected abstract void Release(nint handle);

    public void Dispose()
    {
        Free();
        GC.SuppressFinalize(this);
    }

    ~GpuObject() => Free();

    private void Free()
    {
        nint released = Interlocked.Exchange(ref handle, 0);
        if (released != 0)
            Release(released);
    }
}

public sealed unsafe class Instance : GpuObject
{
    private Instance(nint handle) : base(handle)
    {
    }

    /// <summary>
    /// An instance with only the given backends. With <paramref name="dred"/>, Direct3D 12's Device Removed Extended Data
    /// is kept, for a lost device to say what the GPU was doing; WGPU_D3D12_DRED=1 does the same at run time.
    /// </summary>
    public static Instance Create(WGPUInstanceBackend backends, bool dred = false)
    {
        var extras = new WGPUInstanceExtras
        {
            Chain = new WGPUChainedStruct { SType = WGPUSType.InstanceExtras },
            Backends = backends,
            Flags = WGPUInstanceFlag.WithEnv | (dred ? WGPUInstanceFlag.D3D12DRED : 0),
        };
        var descriptor = new WGPUInstanceDescriptor { NextInChain = &extras.Chain };
        return new Instance(WGPU.wgpuCreateInstance(&descriptor).Handle);
    }

    private protected override void Release(nint handle) => WGPU.wgpuInstanceRelease(new WGPUInstance(handle));

    internal WGPUInstance Native => new(Handle);

    /// <summary>
    /// How many objects of a kind made from this instance are alive ("render bundles", "buffers", "textures"...), as
    /// wgpu counts them: those released and still used by the GPU included until it's done with them.
    /// </summary>
    public int LiveObjects(string type)
    {
        foreach (var (kind, count) in Native.GetLiveObjects().Counts)
            if (kind == type)
                return count;
        return 0;
    }

    /// <summary>Runs the callbacks of what has completed.</summary>
    public void ProcessEvents() => WGPU.wgpuInstanceProcessEvents(Native);

    public Adapter? RequestAdapterSync(RequestAdapterOptions options)
    {
        var native = new WGPURequestAdapterOptions
        {
            PowerPreference = (WGPUPowerPreference)options.PowerPreference,
            BackendType = (WGPUBackendType)options.BackendType,
            ForceFallbackAdapter = options.ForceFallbackAdapter,
            CompatibleSurface = new WGPUSurface(options.CompatibleSurface?.Handle ?? 0),
        };
        try
        {
            WGPUAdapter adapter = Native.RequestAdapterSync(native);
            return adapter.IsNull ? null : new Adapter(adapter.Handle, this);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>A surface for a window, by its HWND and module.</summary>
    public Surface? CreateSurface(nint window, nint module)
    {
        var source = new WGPUSurfaceSourceWindowsHWND
        {
            Chain = new WGPUChainedStruct { SType = WGPUSType.SurfaceSourceWindowsHWND },
            Hwnd = (void*)window,
            Hinstance = (void*)module,
        };
        var descriptor = new WGPUSurfaceDescriptor { NextInChain = &source.Chain, Label = WGPUStringView.Null };
        WGPUSurface surface = WGPU.wgpuInstanceCreateSurface(Native, &descriptor);
        return surface.IsNull ? null : new Surface(surface.Handle);
    }
}

public sealed unsafe class Adapter : GpuObject
{
    private readonly Instance instance;

    internal Adapter(nint handle, Instance instance) : base(handle) => this.instance = instance;

    private protected override void Release(nint handle) => WGPU.wgpuAdapterRelease(new WGPUAdapter(handle));

    internal WGPUAdapter Native => new(Handle);

    public bool HasFeature(FeatureName feature) => WGPU.wgpuAdapterHasFeature(Native, (WGPUFeatureName)feature);

    public void GetLimits(out WGPULimits limits)
    {
        limits = new WGPULimits();
        fixed (WGPULimits* pointer = &limits)
            WGPU.wgpuAdapterGetLimits(Native, pointer);
    }

    public AdapterInfo GetInfo()
    {
        var info = new WGPUAdapterInfo();
        WGPU.wgpuAdapterGetInfo(Native, &info);
        try
        {
            return new AdapterInfo(info.Vendor.ToString(), info.Architecture.ToString(), info.Device.ToString(), info.Description.ToString(),
                (BackendType)info.BackendType, info.VendorID, info.DeviceID);
        }
        finally
        {
            info.FreeMembers();
        }
    }

    public Device? RequestDeviceSync(DeviceDescriptor descriptor)
    {
        using var scratch = new GpuScratch();
        var callbacks = new DeviceCallbacks(descriptor.DeviceLostCallback, descriptor.UncapturedErrorCallback);
        // Kept for the device's life: wgpu calls back with it as long as the device is.
        GCHandle kept = GCHandle.Alloc(callbacks);
        WGPULimits limits = descriptor.RequiredLimits ?? default;
        var native = new WGPUDeviceDescriptor
        {
            Label = scratch.Text(descriptor.Label),
            RequiredFeatureCount = (nuint)(descriptor.RequiredFeatures?.Length ?? 0),
            RequiredFeatures = (WGPUFeatureName*)scratch.Copy<FeatureName>(descriptor.RequiredFeatures ?? []),
            RequiredLimits = descriptor.RequiredLimits is null ? null : &limits,
            DeviceLostCallbackInfo = new WGPUDeviceLostCallbackInfo
            {
                Mode = WGPUCallbackMode.AllowSpontaneous,
                Callback = &DeviceCallbacks.Lost,
                Userdata1 = (void*)GCHandle.ToIntPtr(kept),
            },
            UncapturedErrorCallbackInfo = new WGPUUncapturedErrorCallbackInfo
            {
                Callback = &DeviceCallbacks.Uncaptured,
                Userdata1 = (void*)GCHandle.ToIntPtr(kept),
            },
        };
        try
        {
            WGPUDevice device = Native.RequestDeviceSync(instance.Native, native);
            if (device.IsNull)
            {
                kept.Free();
                return null;
            }
            return new Device(device.Handle, instance, kept);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            kept.Free();
            return null;
        }
    }

    private sealed class DeviceCallbacks(Action<DeviceLostReason, string>? lost, Action<ErrorType, string>? uncaptured)
    {
        private readonly Action<DeviceLostReason, string>? lost = lost;
        private readonly Action<ErrorType, string>? uncaptured = uncaptured;

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        internal static void Lost(WGPUDevice* device, WGPUDeviceLostReason reason, WGPUStringView message, void* user1, void* user2)
        {
            if (GCHandle.FromIntPtr((nint)user1).Target is DeviceCallbacks { lost: { } callback })
                Run(() => callback((DeviceLostReason)reason, message.ToString()));
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        internal static void Uncaptured(WGPUDevice* device, WGPUErrorType type, WGPUStringView message, void* user1, void* user2)
        {
            if (GCHandle.FromIntPtr((nint)user1).Target is DeviceCallbacks { uncaptured: { } callback })
                Run(() => callback((ErrorType)type, message.ToString()));
        }

        // Never unwinding into wgpu.
        private static void Run(Action call)
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
    }
}

public sealed unsafe class Device : GpuObject
{
    private readonly Instance instance;
    private readonly GCHandle callbacks;

    internal Device(nint handle, Instance instance, GCHandle callbacks) : base(handle)
    {
        this.instance = instance;
        this.callbacks = callbacks;
        Queue = new Queue(WGPU.wgpuDeviceGetQueue(Native).Handle, this);
    }

    private protected override void Release(nint handle) => WGPU.wgpuDeviceRelease(new WGPUDevice(handle));

    internal WGPUDevice Native => new(Handle);

    internal Instance Instance => instance;

    public Queue Queue { get; }

    public Queue GetQueue() => Queue;

    public void GetLimits(ref WGPULimits limits)
    {
        fixed (WGPULimits* pointer = &limits)
            WGPU.wgpuDeviceGetLimits(Native, pointer);
    }

    public bool HasFeature(FeatureName feature) => WGPU.wgpuDeviceHasFeature(Native, (WGPUFeatureName)feature);

    public void Destroy() => WGPU.wgpuDeviceDestroy(Native);

    /// <summary>The GPU memory wgpu counts its buffers and textures holding (zero in a wgpu built without its counters).</summary>
    public (long Buffers, long Textures, long Allocations) MemoryCounters()
    {
        WGPUInternalCounters counters = WGPU.wgpuDeviceGetInternalCounters(Native);
        return (counters.Hal.BufferMemory, counters.Hal.TextureMemory, counters.Hal.MemoryAllocations);
    }

    /// <summary>
    /// Lets wgpu finish what's done: buffer maps call back, and what's no longer used is freed. With <paramref name="wait"/>,
    /// until the GPU has done everything submitted. False when the device is lost.
    /// </summary>
    public bool Poll(bool wait = false) => WGPU.wgpuDevicePoll(Native, wait, null, ulong.MaxValue) != WGPUNativePollStatus.Error;

    public Buffer CreateBuffer(in BufferDescriptor descriptor)
    {
        using var scratch = new GpuScratch();
        var native = new WGPUBufferDescriptor
        {
            Label = scratch.Text(descriptor.Label),
            Usage = (WGPUBufferUsage)descriptor.Usage,
            Size = descriptor.Size,
            MappedAtCreation = descriptor.MappedAtCreation,
        };
        return new Buffer(WGPU.wgpuDeviceCreateBuffer(Native, &native).Handle, descriptor.Size, this);
    }

    public Texture CreateTexture(in TextureDescriptor descriptor)
    {
        using var scratch = new GpuScratch();
        var native = new WGPUTextureDescriptor
        {
            Label = scratch.Text(descriptor.Label),
            Usage = (WGPUTextureUsage)descriptor.Usage,
            Dimension = (WGPUTextureDimension)descriptor.Dimension,
            Size = Native3D(descriptor.Size),
            Format = (WGPUTextureFormat)descriptor.Format,
            MipLevelCount = descriptor.MipLevelCount,
            SampleCount = descriptor.SampleCount,
            ViewFormatCount = (nuint)(descriptor.ViewFormats?.Length ?? 0),
            ViewFormats = (WGPUTextureFormat*)scratch.Copy<TextureFormat>(descriptor.ViewFormats ?? []),
        };
        return new Texture(WGPU.wgpuDeviceCreateTexture(Native, &native).Handle);
    }

    public Sampler CreateSampler() => CreateSampler(new SamplerDescriptor());

    public Sampler CreateSampler(in SamplerDescriptor descriptor)
    {
        using var scratch = new GpuScratch();
        var native = new WGPUSamplerDescriptor
        {
            Label = scratch.Text(descriptor.Label),
            AddressModeU = (WGPUAddressMode)descriptor.AddressModeU,
            AddressModeV = (WGPUAddressMode)descriptor.AddressModeV,
            AddressModeW = (WGPUAddressMode)descriptor.AddressModeW,
            MagFilter = (WGPUFilterMode)descriptor.MagFilter,
            MinFilter = (WGPUFilterMode)descriptor.MinFilter,
            MipmapFilter = (WGPUMipmapFilterMode)descriptor.MipmapFilter,
            LodMinClamp = descriptor.LodMinClamp,
            LodMaxClamp = descriptor.LodMaxClamp,
            Compare = (WGPUCompareFunction)descriptor.Compare,
            MaxAnisotropy = descriptor.MaxAnisotropy,
        };
        return new Sampler(WGPU.wgpuDeviceCreateSampler(Native, &native).Handle);
    }

    public BindGroupLayout CreateBindGroupLayout(in BindGroupLayoutDescriptor descriptor)
    {
        using var scratch = new GpuScratch();
        BindGroupLayoutEntry[] entries = descriptor.Entries ?? [];
        WGPUBindGroupLayoutEntry* native = scratch.Alloc<WGPUBindGroupLayoutEntry>(entries.Length);
        for (int i = 0; i < entries.Length; i++)
        {
            BindGroupLayoutEntry entry = entries[i];
            native[i] = new WGPUBindGroupLayoutEntry
            {
                Binding = entry.Binding,
                Visibility = (WGPUShaderStage)entry.Visibility,
                BindingArraySize = entry.BindingArraySize,
                Buffer = new WGPUBufferBindingLayout
                {
                    Type = (WGPUBufferBindingType)entry.Buffer.Type,
                    HasDynamicOffset = entry.Buffer.HasDynamicOffset,
                    MinBindingSize = entry.Buffer.MinBindingSize,
                },
                Sampler = new WGPUSamplerBindingLayout { Type = (WGPUSamplerBindingType)entry.Sampler.Type },
                Texture = new WGPUTextureBindingLayout
                {
                    SampleType = (WGPUTextureSampleType)entry.Texture.SampleType,
                    ViewDimension = (WGPUTextureViewDimension)entry.Texture.ViewDimension,
                    Multisampled = entry.Texture.Multisampled,
                },
                StorageTexture = new WGPUStorageTextureBindingLayout
                {
                    Access = (WGPUStorageTextureAccess)entry.StorageTexture.Access,
                    Format = (WGPUTextureFormat)entry.StorageTexture.Format,
                    ViewDimension = (WGPUTextureViewDimension)entry.StorageTexture.ViewDimension,
                },
            };
        }
        var descriptorNative = new WGPUBindGroupLayoutDescriptor
        {
            Label = scratch.Text(descriptor.Label),
            EntryCount = (nuint)entries.Length,
            Entries = native,
        };
        return new BindGroupLayout(WGPU.wgpuDeviceCreateBindGroupLayout(Native, &descriptorNative).Handle);
    }

    public BindGroup CreateBindGroup(in BindGroupDescriptor descriptor)
    {
        using var scratch = new GpuScratch();
        BindGroupEntry[] entries = descriptor.Entries ?? [];
        WGPUBindGroupEntry* native = scratch.Alloc<WGPUBindGroupEntry>(entries.Length);
        for (int i = 0; i < entries.Length; i++)
        {
            BindGroupEntry entry = entries[i];
            native[i] = new WGPUBindGroupEntry
            {
                Binding = entry.Binding,
                Buffer = new WGPUBuffer(entry.Buffer?.Handle ?? 0),
                Offset = entry.Offset,
                Size = entry.Size ?? (entry.Buffer is null ? 0 : WGPU.WGPU_WHOLE_SIZE),
                Sampler = new WGPUSampler(entry.Sampler?.Handle ?? 0),
                TextureView = new WGPUTextureView(entry.TextureView?.Handle ?? 0),
            };
        }
        var descriptorNative = new WGPUBindGroupDescriptor
        {
            Label = scratch.Text(descriptor.Label),
            Layout = new WGPUBindGroupLayout(descriptor.Layout.Handle),
            EntryCount = (nuint)entries.Length,
            Entries = native,
        };
        BindGroup group = new(WGPU.wgpuDeviceCreateBindGroup(Native, &descriptorNative).Handle);
        GC.KeepAlive(descriptor.Layout);
        GC.KeepAlive(entries);
        return group;
    }

    public PipelineLayout CreatePipelineLayout(in PipelineLayoutDescriptor descriptor)
    {
        using var scratch = new GpuScratch();
        BindGroupLayout[] layouts = descriptor.BindGroupLayouts ?? [];
        WGPUBindGroupLayout* native = scratch.Alloc<WGPUBindGroupLayout>(layouts.Length);
        for (int i = 0; i < layouts.Length; i++)
            native[i] = new WGPUBindGroupLayout(layouts[i].Handle);
        var descriptorNative = new WGPUPipelineLayoutDescriptor
        {
            Label = scratch.Text(descriptor.Label),
            BindGroupLayoutCount = (nuint)layouts.Length,
            BindGroupLayouts = native,
            ImmediateSize = descriptor.ImmediateSize,
        };
        PipelineLayout layout = new(WGPU.wgpuDeviceCreatePipelineLayout(Native, &descriptorNative).Handle);
        GC.KeepAlive(layouts);
        return layout;
    }

    public ShaderModule CreateShaderModuleWGSL(in ShaderModuleWGSLDescriptor descriptor)
    {
        using var scratch = new GpuScratch();
        var source = new WGPUShaderSourceWGSL
        {
            Chain = new WGPUChainedStruct { SType = WGPUSType.ShaderSourceWGSL },
            Code = scratch.Text(descriptor.Code ?? ""),
        };
        var native = new WGPUShaderModuleDescriptor { NextInChain = &source.Chain, Label = scratch.Text(descriptor.Label) };
        return new ShaderModule(WGPU.wgpuDeviceCreateShaderModule(Native, &native).Handle);
    }

    public RenderPipeline CreateRenderPipelineSync(in RenderPipelineDescriptor descriptor)
    {
        using var scratch = new GpuScratch();
        WGPURenderPipelineDescriptor native = Marshal(descriptor, scratch);
        RenderPipeline pipeline = new(WGPU.wgpuDeviceCreateRenderPipeline(Native, &native).Handle);
        KeepAlive(descriptor);
        return pipeline;
    }

    /// <summary>A render pipeline made on a worker thread (wgpu makes pipelines from any thread).</summary>
    public Task<RenderPipeline> CreateRenderPipelineAsync(RenderPipelineDescriptor descriptor) =>
        Task.Run(() => CreateRenderPipelineSync(descriptor));

    public ComputePipeline CreateComputePipelineSync(in ComputePipelineDescriptor descriptor)
    {
        using var scratch = new GpuScratch();
        ComputeState compute = descriptor.Compute;
        var native = new WGPUComputePipelineDescriptor
        {
            Label = scratch.Text(descriptor.Label),
            Layout = new WGPUPipelineLayout(descriptor.Layout?.Handle ?? 0),
            Compute = new WGPUComputeState
            {
                Module = new WGPUShaderModule(compute.Module.Handle),
                EntryPoint = scratch.Text(compute.EntryPoint),
                ConstantCount = (nuint)(compute.Constants?.Length ?? 0),
                Constants = Constants(compute.Constants, scratch),
            },
        };
        ComputePipeline pipeline = new(WGPU.wgpuDeviceCreateComputePipeline(Native, &native).Handle);
        GC.KeepAlive(compute.Module);
        GC.KeepAlive(descriptor.Layout);
        return pipeline;
    }

    public CommandEncoder CreateCommandEncoder()
    {
        var native = new WGPUCommandEncoderDescriptor { Label = WGPUStringView.Null };
        return new CommandEncoder(WGPU.wgpuDeviceCreateCommandEncoder(Native, &native));
    }

    public RenderBundleEncoder CreateRenderBundleEncoder(in RenderBundleEncoderDescriptor descriptor)
    {
        using var scratch = new GpuScratch();
        TextureFormat[] formats = descriptor.ColorFormats ?? [];
        var native = new WGPURenderBundleEncoderDescriptor
        {
            Label = scratch.Text(descriptor.Label),
            ColorFormatCount = (nuint)formats.Length,
            ColorFormats = (WGPUTextureFormat*)scratch.Copy<TextureFormat>(formats),
            DepthStencilFormat = (WGPUTextureFormat)descriptor.DepthStencilFormat,
            SampleCount = descriptor.SampleCount,
            DepthReadOnly = descriptor.DepthReadOnly,
            StencilReadOnly = descriptor.StencilReadOnly,
        };
        return new RenderBundleEncoder(WGPU.wgpuDeviceCreateRenderBundleEncoder(Native, &native));
    }

    public void PushErrorScope(ErrorFilter filter) => WGPU.wgpuDevicePushErrorScope(Native, (WGPUErrorFilter)filter);

    /// <summary>The first error the innermost scope caught (NoError if none), waiting for the GPU as long as it takes.</summary>
    public (ErrorType Type, string Message) PopErrorScopeSync(ulong timeoutNanoseconds = ulong.MaxValue)
    {
        var result = new ScopeResult();
        GCHandle held = GCHandle.Alloc(result);
        try
        {
            WGPU.wgpuDevicePopErrorScope(Native, new WGPUPopErrorScopeCallbackInfo
            {
                Mode = WGPUCallbackMode.AllowSpontaneous,
                Callback = &ScopeResult.Popped,
                Userdata1 = (void*)GCHandle.ToIntPtr(held),
            });
            long until = Environment.TickCount64 + (long)Math.Min(timeoutNanoseconds / 1_000_000, int.MaxValue);
            while (!Volatile.Read(ref result.Done))
            {
                if (!Poll(wait: true) || Environment.TickCount64 > until)
                    break;
            }
            return Volatile.Read(ref result.Done) ? (result.Type, result.Message) : (ErrorType.Unknown, "The error scope never answered.");
        }
        finally
        {
            if (Volatile.Read(ref result.Done))
                held.Free();
        }
    }

    private sealed class ScopeResult
    {
        internal bool Done;
        internal ErrorType Type;
        internal string Message = "";

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        internal static void Popped(WGPUPopErrorScopeStatus status, WGPUErrorType type, WGPUStringView message, void* user1, void* user2)
        {
            if (GCHandle.FromIntPtr((nint)user1).Target is not ScopeResult result)
                return;
            result.Type = status == WGPUPopErrorScopeStatus.Success ? (ErrorType)type : ErrorType.Unknown;
            result.Message = status == WGPUPopErrorScopeStatus.Success ? message.ToString() : $"The error scope couldn't be popped ({status}).";
            Volatile.Write(ref result.Done, true);
        }
    }

    private static WGPUExtent3D Native3D(Extent3D size) => new() { Width = size.Width, Height = size.Height, DepthOrArrayLayers = size.DepthOrArrayLayers };

    private static WGPUConstantEntry* Constants(ConstantEntry[]? constants, GpuScratch scratch)
    {
        if (constants is not { Length: > 0 })
            return null;
        WGPUConstantEntry* native = scratch.Alloc<WGPUConstantEntry>(constants.Length);
        for (int i = 0; i < constants.Length; i++)
            native[i] = new WGPUConstantEntry { Key = scratch.Text(constants[i].Key), Value = constants[i].Value };
        return native;
    }

    private static WGPURenderPipelineDescriptor Marshal(in RenderPipelineDescriptor descriptor, GpuScratch scratch)
    {
        VertexState vertex = descriptor.Vertex;
        VertexBufferLayout[] buffers = vertex.Buffers ?? [];
        WGPUVertexBufferLayout* nativeBuffers = scratch.Alloc<WGPUVertexBufferLayout>(buffers.Length);
        for (int i = 0; i < buffers.Length; i++)
        {
            VertexAttribute[] attributes = buffers[i].Attributes ?? [];
            WGPUVertexAttribute* nativeAttributes = scratch.Alloc<WGPUVertexAttribute>(attributes.Length);
            for (int a = 0; a < attributes.Length; a++)
                nativeAttributes[a] = new WGPUVertexAttribute
                {
                    Format = (WGPUVertexFormat)attributes[a].Format,
                    Offset = attributes[a].Offset,
                    ShaderLocation = attributes[a].ShaderLocation,
                };
            nativeBuffers[i] = new WGPUVertexBufferLayout
            {
                StepMode = (WGPUVertexStepMode)buffers[i].StepMode,
                ArrayStride = buffers[i].ArrayStride,
                AttributeCount = (nuint)attributes.Length,
                Attributes = nativeAttributes,
            };
        }
        WGPUFragmentState* fragment = null;
        if (descriptor.Fragment is { } state)
        {
            ColorTargetState[] targets = state.Targets ?? [];
            WGPUColorTargetState* nativeTargets = scratch.Alloc<WGPUColorTargetState>(targets.Length);
            for (int i = 0; i < targets.Length; i++)
            {
                WGPUBlendState* blend = null;
                if (targets[i].Blend is { } b)
                    blend = scratch.One(new WGPUBlendState
                    {
                        Color = new WGPUBlendComponent { Operation = (WGPUBlendOperation)b.Color.Operation, SrcFactor = (WGPUBlendFactor)b.Color.SrcFactor, DstFactor = (WGPUBlendFactor)b.Color.DstFactor },
                        Alpha = new WGPUBlendComponent { Operation = (WGPUBlendOperation)b.Alpha.Operation, SrcFactor = (WGPUBlendFactor)b.Alpha.SrcFactor, DstFactor = (WGPUBlendFactor)b.Alpha.DstFactor },
                    });
                nativeTargets[i] = new WGPUColorTargetState
                {
                    Format = (WGPUTextureFormat)targets[i].Format,
                    Blend = blend,
                    WriteMask = (WGPUColorWriteMask)targets[i].WriteMask,
                };
            }
            fragment = scratch.One(new WGPUFragmentState
            {
                Module = new WGPUShaderModule(state.Module.Handle),
                EntryPoint = scratch.Text(state.EntryPoint),
                ConstantCount = (nuint)(state.Constants?.Length ?? 0),
                Constants = Constants(state.Constants, scratch),
                TargetCount = (nuint)targets.Length,
                Targets = nativeTargets,
            });
        }
        WGPUDepthStencilState* depth = null;
        if (descriptor.DepthStencil is { } d)
            depth = scratch.One(new WGPUDepthStencilState
            {
                Format = (WGPUTextureFormat)d.Format,
                DepthWriteEnabled = (WGPUOptionalBool)d.DepthWriteEnabled,
                DepthCompare = (WGPUCompareFunction)d.DepthCompare,
                StencilFront = Stencil(d.StencilFront),
                StencilBack = Stencil(d.StencilBack),
                StencilReadMask = d.StencilReadMask,
                StencilWriteMask = d.StencilWriteMask,
                DepthBias = d.DepthBias,
                DepthBiasSlopeScale = d.DepthBiasSlopeScale,
                DepthBiasClamp = d.DepthBiasClamp,
            });
        return new WGPURenderPipelineDescriptor
        {
            Label = scratch.Text(descriptor.Label),
            Layout = new WGPUPipelineLayout(descriptor.Layout?.Handle ?? 0),
            Vertex = new WGPUVertexState
            {
                Module = new WGPUShaderModule(vertex.Module.Handle),
                EntryPoint = scratch.Text(vertex.EntryPoint),
                ConstantCount = (nuint)(vertex.Constants?.Length ?? 0),
                Constants = Constants(vertex.Constants, scratch),
                BufferCount = (nuint)buffers.Length,
                Buffers = nativeBuffers,
            },
            Primitive = new WGPUPrimitiveState
            {
                Topology = (WGPUPrimitiveTopology)descriptor.Primitive.Topology,
                StripIndexFormat = (WGPUIndexFormat)descriptor.Primitive.StripIndexFormat,
                FrontFace = (WGPUFrontFace)descriptor.Primitive.FrontFace,
                CullMode = (WGPUCullMode)descriptor.Primitive.CullMode,
                UnclippedDepth = descriptor.Primitive.UnclippedDepth,
            },
            DepthStencil = depth,
            Multisample = new WGPUMultisampleState
            {
                Count = descriptor.Multisample.Count,
                Mask = descriptor.Multisample.Mask,
                AlphaToCoverageEnabled = descriptor.Multisample.AlphaToCoverageEnabled,
            },
            Fragment = fragment,
        };

        static WGPUStencilFaceState Stencil(StencilFaceState face) => new()
        {
            Compare = (WGPUCompareFunction)face.Compare,
            FailOp = (WGPUStencilOperation)face.FailOp,
            DepthFailOp = (WGPUStencilOperation)face.DepthFailOp,
            PassOp = (WGPUStencilOperation)face.PassOp,
        };
    }

    private static void KeepAlive(in RenderPipelineDescriptor descriptor)
    {
        GC.KeepAlive(descriptor.Layout);
        GC.KeepAlive(descriptor.Vertex.Module);
        GC.KeepAlive(descriptor.Fragment?.Module);
    }
}

public sealed unsafe class Queue : GpuObject
{
    private readonly Device device;

    internal Queue(nint handle, Device device) : base(handle) => this.device = device;

    private protected override void Release(nint handle) => WGPU.wgpuQueueRelease(new WGPUQueue(handle));

    internal WGPUQueue Native => new(Handle);

    /// <summary>Submits the command buffers, which are used up: each is let go once submitted.</summary>
    public void Submit(params ReadOnlySpan<CommandBuffer> commands)
    {
        WGPUCommandBuffer* native = stackalloc WGPUCommandBuffer[Math.Max(1, commands.Length)];
        for (int i = 0; i < commands.Length; i++)
            native[i] = new WGPUCommandBuffer(commands[i].Handle);
        WGPU.wgpuQueueSubmit(Native, (nuint)commands.Length, native);
        foreach (CommandBuffer command in commands)
            command.Dispose();
    }

    public void WriteBuffer<T>(Buffer buffer, ulong offset, ReadOnlySpan<T> data) where T : unmanaged
    {
        if (data.IsEmpty)
            return;
        fixed (T* pointer = data)
            WGPU.wgpuQueueWriteBuffer(Native, new WGPUBuffer(buffer.Handle), offset, pointer, (nuint)(data.Length * sizeof(T)));
        GC.KeepAlive(buffer);
    }

    public void WriteBuffer<T>(Buffer buffer, ulong offset, T[] data) where T : unmanaged => WriteBuffer(buffer, offset, (ReadOnlySpan<T>)data);

    public void WriteTexture<T>(in TexelCopyTextureInfo destination, ReadOnlySpan<T> data, in TexelCopyBufferLayout layout, in Extent3D size) where T : unmanaged
    {
        WGPUTexelCopyTextureInfo nativeDestination = Copies.Texture(destination);
        WGPUTexelCopyBufferLayout nativeLayout = Copies.Layout(layout);
        var extent = new WGPUExtent3D { Width = size.Width, Height = size.Height, DepthOrArrayLayers = size.DepthOrArrayLayers };
        fixed (T* pointer = data)
            WGPU.wgpuQueueWriteTexture(Native, &nativeDestination, pointer, (nuint)(data.Length * sizeof(T)), &nativeLayout, &extent);
        GC.KeepAlive(destination.Texture);
    }

    public void WriteTexture<T>(in TexelCopyTextureInfo destination, T[] data, in TexelCopyBufferLayout layout, in Extent3D size) where T : unmanaged =>
        WriteTexture(destination, (ReadOnlySpan<T>)data, layout, size);

    internal Device Device => device;
}

internal static class Copies
{
    internal static WGPUTexelCopyTextureInfo Texture(in TexelCopyTextureInfo info) => new()
    {
        Texture = new WGPUTexture(info.Texture.Handle),
        MipLevel = info.MipLevel,
        Origin = new WGPUOrigin3D { X = info.Origin.X, Y = info.Origin.Y, Z = info.Origin.Z },
        Aspect = (WGPUTextureAspect)info.Aspect,
    };

    internal static WGPUTexelCopyBufferLayout Layout(in TexelCopyBufferLayout layout) => new()
    {
        Offset = layout.Offset,
        BytesPerRow = layout.BytesPerRow,
        RowsPerImage = layout.RowsPerImage,
    };

    internal static WGPUTexelCopyBufferInfo Buffer(in TexelCopyBufferInfo info) => new()
    {
        Layout = Layout(info.Layout),
        Buffer = new WGPUBuffer(info.Buffer.Handle),
    };

    internal static WGPUExtent3D Extent(in Extent3D size) => new() { Width = size.Width, Height = size.Height, DepthOrArrayLayers = size.DepthOrArrayLayers };
}

public sealed unsafe class Buffer : GpuObject
{
    private readonly ulong size;
    private readonly Device device;

    internal Buffer(nint handle, ulong size, Device device) : base(handle)
    {
        this.size = size;
        this.device = device;
    }

    private protected override void Release(nint handle) => WGPU.wgpuBufferRelease(new WGPUBuffer(handle));

    internal WGPUBuffer Native => new(Handle);

    public ulong GetSize() => size;

    public void Destroy() => WGPU.wgpuBufferDestroy(Native);

    public void Unmap() => WGPU.wgpuBufferUnmap(Native);

    /// <summary>Maps the whole buffer, waiting for the GPU to be done with it.</summary>
    public void MapSync(MapMode mode) => MapSync(mode, 0, (nuint)size);

    public void MapSync(MapMode mode, nuint offset, nuint length)
    {
        var state = new MapState(null);
        GCHandle held = GCHandle.Alloc(state);
        WGPU.wgpuBufferMapAsync(Native, (WGPUMapMode)mode, offset, length, new WGPUBufferMapCallbackInfo
        {
            Mode = WGPUCallbackMode.AllowSpontaneous,
            Callback = &MapState.Mapped,
            Userdata1 = (void*)GCHandle.ToIntPtr(held),
        });
        while (!Volatile.Read(ref state.Done))
            if (!device.Poll(wait: true))
                break;
        if (state.Status != MapAsyncStatus.Success)
            throw new GpuException($"The buffer couldn't be mapped ({(Volatile.Read(ref state.Done) ? state.Status : "the device is lost")}): {state.Message}");
    }

    /// <summary>
    /// Maps the whole buffer without waiting: <paramref name="mapped"/> is called once it is (or it can't be), from a later
    /// device poll (each frame polls).
    /// </summary>
    public void Map(MapMode mode, Action<MapAsyncStatus, string> mapped)
    {
        var state = new MapState(mapped);
        GCHandle held = GCHandle.Alloc(state);
        WGPU.wgpuBufferMapAsync(Native, (WGPUMapMode)mode, 0, (nuint)size, new WGPUBufferMapCallbackInfo
        {
            Mode = WGPUCallbackMode.AllowSpontaneous,
            Callback = &MapState.Mapped,
            Userdata1 = (void*)GCHandle.ToIntPtr(held),
        });
    }

    /// <summary>The mapped range, <paramref name="count"/> values from value <paramref name="first"/>, read by <paramref name="read"/>.</summary>
    public TResult GetConstMappedRange<TResult, T>(nuint first, nuint count, Func<ReadOnlySpan<T>, TResult> read) where T : unmanaged
    {
        void* range = WGPU.wgpuBufferGetConstMappedRange(Native, first * (nuint)sizeof(T), count * (nuint)sizeof(T));
        if (range == null)
            throw new GpuException("The buffer isn't mapped for reading there.");
        return read(new ReadOnlySpan<T>(range, checked((int)count)));
    }

    /// <summary>The mapped range for writing, <paramref name="count"/> values from value <paramref name="first"/>.</summary>
    public Span<T> GetMappedRange<T>(nuint first, nuint count) where T : unmanaged
    {
        void* range = WGPU.wgpuBufferGetMappedRange(Native, first * (nuint)sizeof(T), count * (nuint)sizeof(T));
        if (range == null)
            throw new GpuException("The buffer isn't mapped for writing there.");
        return new Span<T>(range, checked((int)count));
    }

    private sealed class MapState(Action<MapAsyncStatus, string>? mapped)
    {
        private readonly Action<MapAsyncStatus, string>? mapped = mapped;
        internal bool Done;
        internal MapAsyncStatus Status;
        internal string Message = "";

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        internal static void Mapped(WGPUMapAsyncStatus status, WGPUStringView message, void* user1, void* user2)
        {
            GCHandle held = GCHandle.FromIntPtr((nint)user1);
            if (held.Target is not MapState state)
                return;
            held.Free();
            state.Status = (MapAsyncStatus)status;
            state.Message = message.ToString();
            Volatile.Write(ref state.Done, true);
            try
            {
                state.mapped?.Invoke(state.Status, state.Message);
            }
            catch (Exception error)
            {
                System.Diagnostics.Trace.WriteLine($"A buffer map's callback failed: {error}");
            }
        }
    }
}

public sealed unsafe class Texture : GpuObject
{
    internal Texture(nint handle) : base(handle)
    {
    }

    private protected override void Release(nint handle) => WGPU.wgpuTextureRelease(new WGPUTexture(handle));

    internal WGPUTexture Native => new(Handle);

    public void Destroy() => WGPU.wgpuTextureDestroy(Native);

    public uint GetWidth() => WGPU.wgpuTextureGetWidth(Native);

    public uint GetHeight() => WGPU.wgpuTextureGetHeight(Native);

    public uint GetDepthOrArrayLayers() => WGPU.wgpuTextureGetDepthOrArrayLayers(Native);

    public uint GetMipLevelCount() => WGPU.wgpuTextureGetMipLevelCount(Native);

    public TextureFormat GetFormat() => (TextureFormat)WGPU.wgpuTextureGetFormat(Native);

    public TextureUsage GetUsage() => (TextureUsage)WGPU.wgpuTextureGetUsage(Native);

    public TextureView CreateView() => new(WGPU.wgpuTextureCreateView(Native, null).Handle);

    public TextureView CreateView(in TextureViewDescriptor descriptor)
    {
        using var scratch = new GpuScratch();
        var native = new WGPUTextureViewDescriptor
        {
            Label = scratch.Text(descriptor.Label),
            Format = (WGPUTextureFormat)descriptor.Format,
            Dimension = (WGPUTextureViewDimension)descriptor.Dimension,
            BaseMipLevel = descriptor.BaseMipLevel,
            MipLevelCount = descriptor.MipLevelCount ?? WGPU.WGPU_MIP_LEVEL_COUNT_UNDEFINED,
            BaseArrayLayer = descriptor.BaseArrayLayer,
            ArrayLayerCount = descriptor.ArrayLayerCount ?? WGPU.WGPU_ARRAY_LAYER_COUNT_UNDEFINED,
            Aspect = (WGPUTextureAspect)descriptor.Aspect,
        };
        return new TextureView(WGPU.wgpuTextureCreateView(Native, &native).Handle);
    }
}

public sealed class TextureView : GpuObject
{
    internal TextureView(nint handle) : base(handle)
    {
    }

    private protected override void Release(nint handle) => WGPU.wgpuTextureViewRelease(new WGPUTextureView(handle));
}

public sealed class Sampler : GpuObject
{
    internal Sampler(nint handle) : base(handle)
    {
    }

    private protected override void Release(nint handle) => WGPU.wgpuSamplerRelease(new WGPUSampler(handle));
}

public sealed class BindGroup : GpuObject
{
    internal BindGroup(nint handle) : base(handle)
    {
    }

    private protected override void Release(nint handle) => WGPU.wgpuBindGroupRelease(new WGPUBindGroup(handle));
}

public sealed class BindGroupLayout : GpuObject
{
    internal BindGroupLayout(nint handle) : base(handle)
    {
    }

    private protected override void Release(nint handle) => WGPU.wgpuBindGroupLayoutRelease(new WGPUBindGroupLayout(handle));
}

public sealed class PipelineLayout : GpuObject
{
    internal PipelineLayout(nint handle) : base(handle)
    {
    }

    private protected override void Release(nint handle) => WGPU.wgpuPipelineLayoutRelease(new WGPUPipelineLayout(handle));
}

public sealed class ShaderModule : GpuObject
{
    internal ShaderModule(nint handle) : base(handle)
    {
    }

    private protected override void Release(nint handle) => WGPU.wgpuShaderModuleRelease(new WGPUShaderModule(handle));
}

public sealed class RenderPipeline : GpuObject
{
    internal RenderPipeline(nint handle) : base(handle)
    {
    }

    private protected override void Release(nint handle) => WGPU.wgpuRenderPipelineRelease(new WGPURenderPipeline(handle));

    public BindGroupLayout GetBindGroupLayout(uint index) => new(WGPU.wgpuRenderPipelineGetBindGroupLayout(new WGPURenderPipeline(Handle), index).Handle);
}

public sealed class ComputePipeline : GpuObject
{
    internal ComputePipeline(nint handle) : base(handle)
    {
    }

    private protected override void Release(nint handle) => WGPU.wgpuComputePipelineRelease(new WGPUComputePipeline(handle));

    public BindGroupLayout GetBindGroupLayout(uint index) => new(WGPU.wgpuComputePipelineGetBindGroupLayout(new WGPUComputePipeline(Handle), index).Handle);
}

public sealed class RenderBundle : GpuObject
{
    internal RenderBundle(nint handle) : base(handle)
    {
    }

    private protected override void Release(nint handle) => WGPU.wgpuRenderBundleRelease(new WGPURenderBundle(handle));
}

public sealed class CommandBuffer : GpuObject
{
    internal CommandBuffer(nint handle) : base(handle)
    {
    }

    private protected override void Release(nint handle) => WGPU.wgpuCommandBufferRelease(new WGPUCommandBuffer(handle));
}

public sealed unsafe class Surface : GpuObject
{
    internal Surface(nint handle) : base(handle)
    {
    }

    private protected override void Release(nint handle) => WGPU.wgpuSurfaceRelease(new WGPUSurface(handle));

    internal WGPUSurface Native => new(Handle);

    public SurfaceCapabilities? GetCapabilities(Adapter adapter)
    {
        var capabilities = new WGPUSurfaceCapabilities();
        if (WGPU.wgpuSurfaceGetCapabilities(Native, adapter.Native, &capabilities) != WGPUStatus.Success)
            return null;
        try
        {
            return new SurfaceCapabilities(
                [.. new ReadOnlySpan<TextureFormat>(capabilities.Formats, (int)capabilities.FormatCount)],
                [.. new ReadOnlySpan<PresentMode>(capabilities.PresentModes, (int)capabilities.PresentModeCount)],
                [.. new ReadOnlySpan<CompositeAlphaMode>(capabilities.AlphaModes, (int)capabilities.AlphaModeCount)]);
        }
        finally
        {
            capabilities.FreeMembers();
        }
    }

    public void Configure(in SurfaceConfiguration configuration)
    {
        using var scratch = new GpuScratch();
        TextureFormat[] views = configuration.ViewFormats ?? [];
        var native = new WGPUSurfaceConfiguration
        {
            Device = configuration.Device.Native,
            Format = (WGPUTextureFormat)configuration.Format,
            Usage = (WGPUTextureUsage)configuration.Usage,
            Width = configuration.Width,
            Height = configuration.Height,
            ViewFormatCount = (nuint)views.Length,
            ViewFormats = (WGPUTextureFormat*)scratch.Copy<TextureFormat>(views),
            AlphaMode = (WGPUCompositeAlphaMode)configuration.AlphaMode,
            PresentMode = (WGPUPresentMode)configuration.PresentMode,
        };
        WGPU.wgpuSurfaceConfigure(Native, &native);
        GC.KeepAlive(configuration.Device);
    }

    public void Unconfigure() => WGPU.wgpuSurfaceUnconfigure(Native);

    public SurfaceTexture GetCurrentTexture()
    {
        var native = new WGPUSurfaceTexture();
        WGPU.wgpuSurfaceGetCurrentTexture(Native, &native);
        return new SurfaceTexture((SurfaceGetCurrentTextureStatus)native.Status, native.Texture.IsNull ? null : new Texture(native.Texture.Handle));
    }

    public bool Present() => WGPU.wgpuSurfacePresent(Native) == WGPUStatus.Success;
}

/// <summary>Records a command buffer; used up by <see cref="Finish"/>.</summary>
public readonly unsafe struct CommandEncoder
{
    private readonly WGPUCommandEncoder native;

    internal CommandEncoder(WGPUCommandEncoder native)
    {
        if (native.IsNull)
            throw new GpuException("CommandEncoder couldn't be made.");
        this.native = native;
    }

    public RenderPassEncoder BeginRenderPass(in RenderPassDescriptor descriptor)
    {
        using var scratch = new GpuScratch();
        RenderPassColorAttachment[] colors = descriptor.ColorAttachments ?? [];
        WGPURenderPassColorAttachment* nativeColors = scratch.Alloc<WGPURenderPassColorAttachment>(colors.Length);
        for (int i = 0; i < colors.Length; i++)
        {
            RenderPassColorAttachment color = colors[i];
            nativeColors[i] = new WGPURenderPassColorAttachment
            {
                View = new WGPUTextureView(color.View?.Handle ?? 0),
                DepthSlice = color.DepthSlice ?? WGPU.WGPU_DEPTH_SLICE_UNDEFINED,
                ResolveTarget = new WGPUTextureView(color.ResolveTarget?.Handle ?? 0),
                LoadOp = (WGPULoadOp)color.LoadOp,
                StoreOp = (WGPUStoreOp)color.StoreOp,
                ClearValue = new WGPUColor { R = color.ClearValue.R, G = color.ClearValue.G, B = color.ClearValue.B, A = color.ClearValue.A },
            };
        }
        WGPURenderPassDepthStencilAttachment* depth = null;
        if (descriptor.DepthStencilAttachment is { } d)
            depth = scratch.One(new WGPURenderPassDepthStencilAttachment
            {
                View = new WGPUTextureView(d.View.Handle),
                DepthLoadOp = (WGPULoadOp)d.DepthLoadOp,
                DepthStoreOp = (WGPUStoreOp)d.DepthStoreOp,
                DepthClearValue = d.DepthClearValue,
                DepthReadOnly = d.DepthReadOnly,
                StencilLoadOp = (WGPULoadOp)d.StencilLoadOp,
                StencilStoreOp = (WGPUStoreOp)d.StencilStoreOp,
                StencilClearValue = d.StencilClearValue,
                StencilReadOnly = d.StencilReadOnly,
            });
        var native = new WGPURenderPassDescriptor
        {
            Label = scratch.Text(descriptor.Label),
            ColorAttachmentCount = (nuint)colors.Length,
            ColorAttachments = nativeColors,
            DepthStencilAttachment = depth,
        };
        RenderPassEncoder pass = new(WGPU.wgpuCommandEncoderBeginRenderPass(this.native, &native));
        GC.KeepAlive(colors);
        GC.KeepAlive(descriptor.DepthStencilAttachment?.View);
        return pass;
    }

    public ComputePassEncoder BeginComputePass()
    {
        var native = new WGPUComputePassDescriptor { Label = WGPUStringView.Null };
        return new ComputePassEncoder(WGPU.wgpuCommandEncoderBeginComputePass(this.native, &native));
    }

    public void CopyBufferToBuffer(Buffer source, ulong sourceOffset, Buffer destination, ulong destinationOffset, ulong size)
    {
        WGPU.wgpuCommandEncoderCopyBufferToBuffer(native, source.Native, sourceOffset, destination.Native, destinationOffset, size);
        GC.KeepAlive(source);
        GC.KeepAlive(destination);
    }

    public void CopyBufferToTexture(in TexelCopyBufferInfo source, in TexelCopyTextureInfo destination, in Extent3D size)
    {
        WGPUTexelCopyBufferInfo from = Copies.Buffer(source);
        WGPUTexelCopyTextureInfo to = Copies.Texture(destination);
        WGPUExtent3D extent = Copies.Extent(size);
        WGPU.wgpuCommandEncoderCopyBufferToTexture(native, &from, &to, &extent);
        GC.KeepAlive(source.Buffer);
        GC.KeepAlive(destination.Texture);
    }

    public void CopyTextureToBuffer(in TexelCopyTextureInfo source, in TexelCopyBufferInfo destination, in Extent3D size)
    {
        WGPUTexelCopyTextureInfo from = Copies.Texture(source);
        WGPUTexelCopyBufferInfo to = Copies.Buffer(destination);
        WGPUExtent3D extent = Copies.Extent(size);
        WGPU.wgpuCommandEncoderCopyTextureToBuffer(native, &from, &to, &extent);
        GC.KeepAlive(source.Texture);
        GC.KeepAlive(destination.Buffer);
    }

    public void CopyTextureToTexture(in TexelCopyTextureInfo source, in TexelCopyTextureInfo destination, in Extent3D size)
    {
        WGPUTexelCopyTextureInfo from = Copies.Texture(source);
        WGPUTexelCopyTextureInfo to = Copies.Texture(destination);
        WGPUExtent3D extent = Copies.Extent(size);
        WGPU.wgpuCommandEncoderCopyTextureToTexture(native, &from, &to, &extent);
        GC.KeepAlive(source.Texture);
        GC.KeepAlive(destination.Texture);
    }

    public void ClearBuffer(Buffer buffer, ulong offset = 0, ulong size = ulong.MaxValue)
    {
        WGPU.wgpuCommandEncoderClearBuffer(native, buffer.Native, offset, size);
        GC.KeepAlive(buffer);
    }

    public CommandBuffer Finish()
    {
        var descriptor = new WGPUCommandBufferDescriptor { Label = WGPUStringView.Null };
        WGPUCommandBuffer commands = WGPU.wgpuCommandEncoderFinish(native, &descriptor);
        WGPU.wgpuCommandEncoderRelease(native);
        return new CommandBuffer(commands.Handle);
    }
}

/// <summary>A render pass; used up by <see cref="End"/>.</summary>
public readonly unsafe struct RenderPassEncoder
{
    private readonly WGPURenderPassEncoder native;

    internal RenderPassEncoder(WGPURenderPassEncoder native)
    {
        if (native.IsNull)
            throw new GpuException("The render pass couldn't be begun.");
        this.native = native;
    }

    public void SetPipeline(RenderPipeline pipeline) => WGPU.wgpuRenderPassEncoderSetPipeline(native, new WGPURenderPipeline(pipeline.Handle));

    public void SetBindGroup(uint index, BindGroup? group) => WGPU.wgpuRenderPassEncoderSetBindGroup(native, index, new WGPUBindGroup(group?.Handle ?? 0), 0, null);

    public void SetBindGroup(uint index, BindGroup group, params ReadOnlySpan<uint> dynamicOffsets)
    {
        fixed (uint* offsets = dynamicOffsets)
            WGPU.wgpuRenderPassEncoderSetBindGroup(native, index, new WGPUBindGroup(group.Handle), (nuint)dynamicOffsets.Length, offsets);
    }

    public void SetVertexBuffer(uint slot, Buffer buffer, ulong offset = 0, ulong size = ulong.MaxValue) =>
        WGPU.wgpuRenderPassEncoderSetVertexBuffer(native, slot, buffer.Native, offset, size);

    public void SetIndexBuffer(Buffer buffer, IndexFormat format, ulong offset = 0, ulong size = ulong.MaxValue) =>
        WGPU.wgpuRenderPassEncoderSetIndexBuffer(native, buffer.Native, (WGPUIndexFormat)format, offset, size);

    public void SetViewport(float x, float y, float width, float height, float minDepth, float maxDepth) =>
        WGPU.wgpuRenderPassEncoderSetViewport(native, x, y, width, height, minDepth, maxDepth);

    public void SetScissorRect(uint x, uint y, uint width, uint height) => WGPU.wgpuRenderPassEncoderSetScissorRect(native, x, y, width, height);

    public void SetBlendConstant(in Color color)
    {
        var native = new WGPUColor { R = color.R, G = color.G, B = color.B, A = color.A };
        WGPU.wgpuRenderPassEncoderSetBlendConstant(this.native, &native);
    }

    public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0) =>
        WGPU.wgpuRenderPassEncoderDraw(native, vertexCount, instanceCount, firstVertex, firstInstance);

    public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0) =>
        WGPU.wgpuRenderPassEncoderDrawIndexed(native, indexCount, instanceCount, firstIndex, baseVertex, firstInstance);

    public void DrawIndexedIndirect(Buffer indirect, ulong offset) => WGPU.wgpuRenderPassEncoderDrawIndexedIndirect(native, indirect.Native, offset);

    public void ExecuteBundles(ReadOnlySpan<RenderBundle> bundles)
    {
        WGPURenderBundle* handles = stackalloc WGPURenderBundle[Math.Max(1, Math.Min(bundles.Length, 512))];
        // Many bundles a pass: in runs, without allocating.
        for (int start = 0; start < bundles.Length; start += 512)
        {
            int count = Math.Min(512, bundles.Length - start);
            for (int i = 0; i < count; i++)
                handles[i] = new WGPURenderBundle(bundles[start + i].Handle);
            WGPU.wgpuRenderPassEncoderExecuteBundles(native, (nuint)count, handles);
        }
    }

    public void End()
    {
        WGPU.wgpuRenderPassEncoderEnd(native);
        WGPU.wgpuRenderPassEncoderRelease(native);
    }
}

/// <summary>A compute pass; used up by <see cref="End"/>.</summary>
public readonly unsafe struct ComputePassEncoder
{
    private readonly WGPUComputePassEncoder native;

    internal ComputePassEncoder(WGPUComputePassEncoder native)
    {
        if (native.IsNull)
            throw new GpuException("The compute pass couldn't be begun.");
        this.native = native;
    }

    public void SetPipeline(ComputePipeline pipeline) => WGPU.wgpuComputePassEncoderSetPipeline(native, new WGPUComputePipeline(pipeline.Handle));

    public void SetBindGroup(uint index, BindGroup group) => WGPU.wgpuComputePassEncoderSetBindGroup(native, index, new WGPUBindGroup(group.Handle), 0, null);

    public void DispatchWorkgroups(uint x, uint y = 1, uint z = 1) => WGPU.wgpuComputePassEncoderDispatchWorkgroups(native, x, y, z);

    public void DispatchWorkgroupsIndirect(Buffer indirect, ulong offset) => WGPU.wgpuComputePassEncoderDispatchWorkgroupsIndirect(native, indirect.Native, offset);

    public void End()
    {
        WGPU.wgpuComputePassEncoderEnd(native);
        WGPU.wgpuComputePassEncoderRelease(native);
    }
}

/// <summary>Records a render bundle; used up by <see cref="Finish"/>.</summary>
public readonly unsafe struct RenderBundleEncoder
{
    private readonly WGPURenderBundleEncoder native;

    internal RenderBundleEncoder(WGPURenderBundleEncoder native)
    {
        if (native.IsNull)
            throw new GpuException("RenderBundleEncoder couldn't be made.");
        this.native = native;
    }

    public void SetPipeline(RenderPipeline pipeline) => WGPU.wgpuRenderBundleEncoderSetPipeline(native, new WGPURenderPipeline(pipeline.Handle));

    public void SetBindGroup(uint index, BindGroup group) => WGPU.wgpuRenderBundleEncoderSetBindGroup(native, index, new WGPUBindGroup(group.Handle), 0, null);

    public void SetVertexBuffer(uint slot, Buffer buffer, ulong offset = 0, ulong size = ulong.MaxValue) =>
        WGPU.wgpuRenderBundleEncoderSetVertexBuffer(native, slot, buffer.Native, offset, size);

    public void SetIndexBuffer(Buffer buffer, IndexFormat format, ulong offset = 0, ulong size = ulong.MaxValue) =>
        WGPU.wgpuRenderBundleEncoderSetIndexBuffer(native, buffer.Native, (WGPUIndexFormat)format, offset, size);

    public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0) =>
        WGPU.wgpuRenderBundleEncoderDraw(native, vertexCount, instanceCount, firstVertex, firstInstance);

    public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0) =>
        WGPU.wgpuRenderBundleEncoderDrawIndexed(native, indexCount, instanceCount, firstIndex, baseVertex, firstInstance);

    public void DrawIndexedIndirect(Buffer indirect, ulong offset) => WGPU.wgpuRenderBundleEncoderDrawIndexedIndirect(native, indirect.Native, offset);

    public RenderBundle Finish()
    {
        var descriptor = new WGPURenderBundleDescriptor { Label = WGPUStringView.Null };
        WGPURenderBundle bundle = WGPU.wgpuRenderBundleEncoderFinish(native, &descriptor);
        WGPU.wgpuRenderBundleEncoderRelease(native);
        return new RenderBundle(bundle.Handle);
    }
}
