using System.Collections.Concurrent;
using System.Text;
using HikariHopper.Gpu;

namespace HikariHopper.Rendering;

/// <summary>
/// The GPU every 3D viewport draws with, made once for the app: WebGPU's instance, a Direct3D 12 adapter and its
/// device, and the render and compute pipelines viewports compile, kept so the next viewport reuses them instead of
/// compiling its shaders again. Each viewport keeps its own swap chain, buffers and textures.
/// </summary>
/// <remarks>
/// Viewports are made, drawn and disposed on the UI thread; <see cref="WarmUp"/> fills the pipelines on a background
/// thread first. <see cref="Gate"/> keeps the two from using the device at once.
/// </remarks>
internal sealed class GpuDevice
{
    private static readonly Lock Creation = new();
    // Around the warm-up's task alone: the warm-up makes the device under Creation, which asking after it mustn't wait for.
    private static readonly Lock Warming = new();
    private static GpuDevice? shared;
    private static Task? warming;

    private readonly ConcurrentDictionary<string, object> pipelines = new(StringComparer.Ordinal);
    private readonly List<WeakReference<Action<string>>> listeners = [];
    private volatile bool lost;

    /// <summary>Held while the device is in use off the UI thread, and while a viewport is made.</summary>
    internal Lock Gate { get; } = new();

    internal Instance Instance { get; }
    internal Adapter Adapter { get; }
    internal Device Device { get; }
    internal Queue Queue { get; }

    /// <summary>Whether block-compressed (BC) textures can be sampled; game textures are drawn white without it.</summary>
    internal bool CompressedTextures { get; }

    /// <summary>
    /// Whether indirect draws can start past the first instance: the world's render bundles then read each frame's
    /// counts from one buffer instead of being recorded again (RedFury's indirect path).
    /// </summary>
    internal bool IndirectFirstInstance { get; }

    private GpuDevice()
    {
        // Direct3D 12 alone; Direct3D 12's removal data kept when WGPU_D3D12_DRED=1, for a lost device to say what the GPU
        // was doing.
        Instance = Instance.Create();
        // Any window can be drawn into from the Direct3D 12 adapter, so none is needed to pick it.
        Adapter = Instance.RequestAdapterSync(new RequestAdapterOptions
        {
            BackendType = BackendType.D3D12,
            PowerPreference = PowerPreference.HighPerformance,
        }) ?? throw new NotSupportedException("No Direct3D 12 GPU is available.");
        CompressedTextures = Adapter.HasFeature(FeatureName.TextureCompressionBC);
        IndirectFirstInstance = Adapter.HasFeature(FeatureName.IndirectFirstInstance);
        Device = RequestDevice(Adapter, CompressedTextures, IndirectFirstInstance, Report);
        Queue = Device.GetQueue();
    }

    /// <summary>The app's GPU, made on first use, and again if the device was lost (a driver reset, say).</summary>
    internal static GpuDevice Shared
    {
        get
        {
            lock (Creation)
            {
                if (shared is { lost: true })
                    shared = null;
                return shared ??= new GpuDevice();
            }
        }
    }

    /// <summary>
    /// Starts making the GPU and compiling every viewport pipeline in the background, once, so the first 3D viewer
    /// opens without waiting for them, and the worlds' pipelines compiling on the driver's threads, so the first world
    /// shown doesn't wait for them either. Failures are left for that viewer to report.
    /// </summary>
    internal static Task WarmUp()
    {
        lock (Warming)
            return warming ??= Task.Run(() =>
            {
                try
                {
                    GpuDevice gpu = Shared;
                    lock (gpu.Gate)
                    {
                        var renderer = new ViewportRenderer(gpu);
                        // The worlds' pipelines go on compiling on the driver's threads, for when a world is shown.
                        renderer.PrepareWorldFamilies(Enum.GetValues<WorldFamily>());
                        renderer.Dispose();
                    }
                }
                catch (Exception error) when (error is InvalidOperationException or NotSupportedException or DllNotFoundException
                    or EntryPointNotFoundException or TimeoutException or GpuException)
                {
                }
            });
    }

    /// <summary>
    /// Done once the warm-up has compiled the viewports' pipelines and let go of the device (<see cref="Gate"/>); done
    /// already when none was started. A viewport made before then waits for it on the UI thread.
    /// </summary>
    internal static Task WarmingUp
    {
        get
        {
            lock (Warming)
                return warming ?? Task.CompletedTask;
        }
    }

    /// <summary>
    /// Done once the warm-up has compiled the viewports' pipelines and the worlds' have compiled on the driver's threads
    /// too; done already when no warm-up was started. A viewport made before then waits for them on the UI thread (the
    /// warm-up holds <see cref="Gate"/>, and a world's first frames wait for its pipelines): ten seconds and more after
    /// the app starts. It never fails: a pipeline that didn't compile is compiled again when it's used.
    /// </summary>
    internal static Task Ready
    {
        get
        {
            Task? started;
            lock (Warming)
                started = warming;
            if (started is null)
                return Task.CompletedTask;
            return started.ContinueWith(_ => shared is { } gpu
                    ? Task.WhenAll(gpu.pipelines.Values.OfType<Task<RenderPipeline>>())
                    : Task.CompletedTask, TaskScheduler.Default)
                .Unwrap()
                .ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }

    /// <summary>
    /// A render pipeline compiled once per key: a viewport's pipelines are the same for every viewport. One compiling in
    /// the background (<see cref="Prepare"/>) is waited for; one that failed there is compiled here.
    /// </summary>
    internal RenderPipeline Pipeline(string key, Func<RenderPipeline> create)
    {
        object value = pipelines.GetOrAdd(key, _ => create());
        if (value is not Task<RenderPipeline> compiling)
            return (RenderPipeline)value;
        RenderPipeline made;
        try
        {
            made = compiling.GetAwaiter().GetResult();
        }
        catch (Exception error) when (error is GpuException or InvalidOperationException)
        {
            made = create();
        }
        pipelines[key] = made;
        return made;
    }

    /// <summary>
    /// Starts compiling a render pipeline on the driver's threads, unless it's compiled or compiling already: the
    /// viewport that asks for it later (<see cref="Pipeline"/>) gets it without compiling it then.
    /// </summary>
    internal void Prepare(string key, Func<Task<RenderPipeline>> start)
    {
        if (!pipelines.ContainsKey(key))
            pipelines.TryAdd(key, start());
    }

    /// <summary>A compute pipeline compiled once per key.</summary>
    internal ComputePipeline Compute(string key, Func<ComputePipeline> create) => (ComputePipeline)pipelines.GetOrAdd(key, _ => create());

    /// <summary>Forgets the compiled pipelines, so the next viewport compiles them again; for tests.</summary>
    internal void ForgetPipelines() => pipelines.Clear();

    /// <summary>Whether a pipeline is compiled already, for tests.</summary>
    internal bool Compiled(string key) => pipelines.ContainsKey(key);

    /// <summary>Tells a viewport about the device's errors while it is alive.</summary>
    internal void Listen(Action<string> failed)
    {
        lock (listeners)
        {
            listeners.RemoveAll(listener => !listener.TryGetTarget(out _));
            listeners.Add(new WeakReference<Action<string>>(failed));
        }
    }

    private void Report(string message)
    {
        if (message.StartsWith(LostPrefix, StringComparison.Ordinal))
            lost = true;
        lock (listeners)
            foreach (WeakReference<Action<string>> listener in listeners)
                if (listener.TryGetTarget(out Action<string>? failed))
                    failed(message);
    }

    private const string LostPrefix = "WebGPU device lost";

    /// <summary>What a viewport says when the GPU device is lost.</summary>
    internal const string LostMessage = "The GPU device was lost. Restart HikariHopper to see the 3D view again.";

    /// <summary>
    /// Whether an error is the device being lost: reported by the device's lost callback, or met first as a call on it
    /// fails (a swap chain that gives errors, a map that can't be made).
    /// </summary>
    internal static bool IsLost(Exception error) => error.Message.StartsWith(LostPrefix, StringComparison.Ordinal)
        || error.Message.Contains("is lost", StringComparison.Ordinal) || error.Message == LostMessage;

    /// <summary>
    /// Marks the device lost when a viewport finds it so (its swap chain failing), for every viewport on it to stop with
    /// why, and the next to be made on a new one.
    /// </summary>
    internal void Lose(string why)
    {
        if (!lost)
            Report($"{LostPrefix} ({why})");
    }

    private static Device RequestDevice(Adapter adapter, bool compressedTextures, bool indirectFirstInstance, Action<string> failed)
    {
        // What the adapter allows, asked for whole: Red Dead Redemption 2's material reads more textures at once than
        // WebGPU's defaults let a shader (Rdr2/surface.wgsl, Rdr2TextureSlots).
        return adapter.RequestDeviceSync(new DeviceDescriptor
        {
            AdapterLimits = true,
            RequiredFeatures = [.. compressedTextures ? [FeatureName.TextureCompressionBC] : Array.Empty<FeatureName>(),
                .. indirectFirstInstance ? [FeatureName.IndirectFirstInstance] : Array.Empty<FeatureName>()],
            UncapturedErrorCallback = (type, message) => failed($"WebGPU {type}: {message}"),
            // Its reason with it: Direct3D 12's removal reason, and with WGPU_D3D12_DRED=1 what the GPU was doing. A device
            // let go of, or destroyed, isn't lost.
            DeviceLostCallback = (reason, message) =>
            {
                if (reason is not (DeviceLostReason.Destroyed or DeviceLostReason.CallbackCancelled))
                    failed($"{LostPrefix} ({reason}): {message}");
            },
        }) ?? throw new NotSupportedException("The GPU refused a device.");
    }
}
