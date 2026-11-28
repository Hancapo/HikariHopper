using Vortice.Direct3D12;

namespace HikariHopper.Gpu;

/// <summary>
/// Command allocators render bundles are recorded from, each shared by many bundles: freed once full and every bundle
/// recorded from it is freed.
/// </summary>
internal sealed class BundleArena(ID3D12CommandAllocator allocator) : D3D12Life
{
    internal const int Bundles = 128;

    internal ID3D12CommandAllocator Allocator { get; } = allocator;

    /// <summary>Bundles recorded from it.</summary>
    internal int Recorded;

    /// <summary>Of those, the ones not freed yet.</summary>
    internal int Alive;

    private protected override void Free() => Allocator.Dispose();
}

/// <summary>A render bundle's command list, freed once the GPU is done with it.</summary>
internal sealed class BundleLife(Device device, BundleArena arena, ID3D12GraphicsCommandList list) : D3D12Life
{
    internal ID3D12GraphicsCommandList List { get; } = list;

    private protected override void Free()
    {
        List.Dispose();
        device.BundleFreed(arena);
    }
}

/// <summary>
/// Records a render bundle into a Direct3D 12 bundle: its draws once, run by a pass with one call. A bundle can't hold
/// barriers, so what it uses is listed instead, each buffer and texture view once, for the pass that runs it to move
/// into the states it's used in.
/// </summary>
internal sealed class BundleRecorder : DrawRecorder
{
    private readonly Device device;
    private readonly BundleArena arena;
    private readonly ID3D12GraphicsCommandList list;
    private readonly Dictionary<BufferLife, (ResourceStates State, bool Write)> buffers = [];
    private readonly HashSet<(TextureView View, ResourceStates State)> textures = [];
    private readonly HashSet<D3D12Life> held = [];

    internal BundleRecorder(Device device)
    {
        this.device = device;
        (arena, list) = device.OpenBundle();
        ResetBindings(computing: false);
    }

    private protected override ID3D12GraphicsCommandList Commands => list;

    private protected override Device Owner => device;

    private protected override void UseBuffer(BufferLife buffer, ResourceStates state, bool write = false)
    {
        held.Add(buffer);
        // Memory the CPU writes or reads stays in the one state it was made in.
        if (buffer.Heap != HeapType.Default)
            return;
        buffers[buffer] = buffers.TryGetValue(buffer, out var used) ? (used.State | state, used.Write | write) : (state, write);
    }

    private protected override void UseTexture(TextureView view, ResourceStates state)
    {
        held.Add(view.Texture.Life);
        textures.Add((view, state));
    }

    private protected override void Hold(D3D12Life life) => held.Add(life);

    private protected override void Barriers()
    {
    }

    internal RenderBundle Finish(Instance instance)
    {
        list.Close();
        device.BundleClosed(arena);
        var life = new BundleLife(device, arena, list);
        return new RenderBundle(device, instance, life, [.. buffers.Select(pair => (pair.Key, pair.Value.State, pair.Value.Write))], [.. textures], [.. held]);
    }
}

/// <summary>Records a render bundle; used up by <see cref="Finish"/>.</summary>
public readonly struct RenderBundleEncoder
{
    private readonly BundleRecorder recorder;
    private readonly Instance instance;

    internal RenderBundleEncoder(BundleRecorder recorder, Instance instance)
    {
        this.recorder = recorder;
        this.instance = instance;
    }

    public void SetPipeline(RenderPipeline pipeline) => recorder.SetPipeline(pipeline);

    public void SetBindGroup(uint index, BindGroup group) => recorder.SetBindGroup(index, group, []);

    public void SetVertexBuffer(uint slot, Buffer buffer, ulong offset = 0, ulong size = ulong.MaxValue) =>
        recorder.SetVertexBuffer(slot, buffer.Life, offset, size == ulong.MaxValue ? buffer.GetSize() - offset : size);

    public void SetIndexBuffer(Buffer buffer, IndexFormat format, ulong offset = 0, ulong size = ulong.MaxValue) =>
        recorder.SetIndexBuffer(buffer.Life, format, offset, size == ulong.MaxValue ? buffer.GetSize() - offset : size);

    public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0) =>
        recorder.Draw(vertexCount, instanceCount, firstVertex, firstInstance);

    public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0) =>
        recorder.DrawIndexed(indexCount, instanceCount, firstIndex, baseVertex, firstInstance);

    public void DrawIndexedIndirect(Buffer indirect, ulong offset) => recorder.DrawIndexedIndirect(indirect.Life, offset);

    public RenderBundle Finish() => recorder.Finish(instance);
}

public sealed class RenderBundle : GpuObject
{
    private readonly Device device;
    private readonly Instance instance;

    internal RenderBundle(Device device, Instance instance, BundleLife life, (BufferLife Buffer, ResourceStates State, bool Write)[] buffers,
        (TextureView View, ResourceStates State)[] textures, D3D12Life[] held)
    {
        this.device = device;
        this.instance = instance;
        Life = life;
        Buffers = buffers;
        Textures = textures;
        Held = held;
        instance.Count("render bundles", 1);
    }

    internal BundleLife Life { get; }

    /// <summary>The buffers and texture views its draws use, each once, and the states they use them in.</summary>
    internal (BufferLife Buffer, ResourceStates State, bool Write)[] Buffers { get; }

    internal (TextureView View, ResourceStates State)[] Textures { get; }

    /// <summary>What the GPU uses while it runs: pipelines, layouts, bind groups, buffers and textures.</summary>
    internal D3D12Life[] Held { get; }

    private protected override void Release()
    {
        instance.Count("render bundles", -1);
        device.Retire(Life);
    }
}

internal sealed partial class Recorder : DrawRecorder
{
    private protected override ID3D12GraphicsCommandList Commands => List;

    private protected override Device Owner => device;

    private protected override void UseBuffer(BufferLife buffer, ResourceStates state, bool write = false) => Use(buffer, state, write);

    private protected override void UseTexture(TextureView view, ResourceStates state)
    {
        TextureLife texture = view.Texture.Life;
        bool volume = texture.Dimension == TextureDimension.D3;
        uint mips = state == ResourceStates.UnorderedAccess ? 1 : view.Mips;
        Use(texture, view.BaseMip, mips, volume ? 0 : view.BaseLayer, volume ? 1 : view.Layers, view.Aspect, state);
    }

    private protected override void Hold(D3D12Life life) => Touch(life);

    private protected override void Barriers() => Flush();

    /// <summary>
    /// Runs the bundles in the pass: what each uses moved into its states first, then each with one call. The pass has
    /// nothing bound after them, as WebGPU's passes don't.
    /// </summary>
    internal void ExecuteBundles(ReadOnlySpan<RenderBundle> bundles)
    {
        // The bundles' descriptor tables index the heaps the pass has.
        SetHeaps();
        foreach (RenderBundle bundle in bundles)
        {
            Touch(bundle.Life);
            foreach (D3D12Life life in bundle.Held)
                Touch(life);
            foreach ((BufferLife buffer, ResourceStates state, bool write) in bundle.Buffers)
                Use(buffer, state, write);
            foreach ((TextureView view, ResourceStates state) in bundle.Textures)
                UseTexture(view, state);
        }
        Flush();
        foreach (RenderBundle bundle in bundles)
            List.ExecuteBundle(bundle.Life.List);
        ResetBindings(computing: false);
    }
}
