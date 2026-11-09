using System.Collections.Concurrent;
using System.Text;
using WebGpuSharp;

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

    private GpuDevice()
    {
        Instance = WebGPU.CreateInstance() ?? throw new InvalidOperationException("WebGPU is unavailable.");
        // Any window can be drawn into from the Direct3D 12 adapter, so none is needed to pick it.
        Adapter = Instance.RequestAdapterSync(new RequestAdapterOptions
        {
            CompatibleSurface = null!,
            BackendType = BackendType.D3D12,
            PowerPreference = PowerPreference.HighPerformance,
        }) ?? throw new NotSupportedException("No Direct3D 12 GPU is available.");
        CompressedTextures = Adapter.HasFeature(FeatureName.TextureCompressionBC);
        Device = RequestDevice(Adapter, CompressedTextures, Report);
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
    /// opens without waiting for them. Failures are left for that viewer to report.
    /// </summary>
    internal static Task WarmUp()
    {
        lock (Creation)
            return warming ??= Task.Run(() =>
            {
                try
                {
                    GpuDevice gpu = Shared;
                    lock (gpu.Gate)
                        new ViewportRenderer(gpu).Dispose();
                }
                catch (Exception error) when (error is InvalidOperationException or NotSupportedException or DllNotFoundException
                    or EntryPointNotFoundException or TimeoutException or WebGPUException)
                {
                }
            });
    }

    /// <summary>A render pipeline compiled once per key: a viewport's pipelines are the same for every viewport.</summary>
    internal RenderPipeline Pipeline(string key, Func<RenderPipeline> create) => (RenderPipeline)pipelines.GetOrAdd(key, _ => create());

    /// <summary>A compute pipeline compiled once per key.</summary>
    internal ComputePipeline Compute(string key, Func<ComputePipeline> create) => (ComputePipeline)pipelines.GetOrAdd(key, _ => create());

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

    private static Device RequestDevice(Adapter adapter, bool compressedTextures, Action<string> failed)
    {
        // The descriptor is a ref struct, so it is built where the request is made, off the caller's context.
        return Task.Run(() => adapter.RequestDeviceAsync(new DeviceDescriptor
        {
            RequiredFeatures = compressedTextures ? new[] { FeatureName.TextureCompressionBC } : [],
            UncapturedErrorCallback = (type, message) => failed($"WebGPU {type}: {Encoding.UTF8.GetString(message)}"),
            DeviceLostCallback = (reason, message) =>
            {
                if (reason != DeviceLostReason.Destroyed)
                    failed($"{LostPrefix} ({reason}): {Encoding.UTF8.GetString(message)}");
            },
        })).GetAwaiter().GetResult();
    }
}
