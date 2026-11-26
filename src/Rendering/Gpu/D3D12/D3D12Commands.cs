using Vortice.Direct3D12;
using Vortice.Mathematics;
using D3DBox = Vortice.Mathematics.Box;

namespace HikariHopper.Gpu;

/// <summary>
/// A command list being recorded, and the states of what it uses.
/// <para>
/// Every list starts from the same known states, so recording needs nothing from the lists before it: buffers in the
/// common state (Direct3D 12 decays them back to it as each list's run ends, each list run on its own), and textures
/// in their resting state, which the list returns each texture it touched to as it closes. In between, a barrier goes
/// in before each use whose state differs from the last, batched until the next command needs them: the first use of
/// a buffer promotes it implicitly, reads combine into one state, and a write after a write of the same kind waits
/// for it (a UAV barrier, or a full one for copies).
/// </para>
/// </summary>
internal sealed class Recorder
{
    private const ResourceStates Writes = ResourceStates.RenderTarget | ResourceStates.UnorderedAccess | ResourceStates.DepthWrite
        | ResourceStates.CopyDest | ResourceStates.ResolveDest | ResourceStates.StreamOut;

    private readonly Device device;
    private readonly ID3D12CommandAllocator allocator;
    private readonly HashSet<D3D12Life> touched = [];
    private readonly Dictionary<TextureLife, ResourceStates[]> textures = [];
    private readonly Dictionary<BufferLife, (ResourceStates State, bool Written)> buffers = [];
    private readonly List<ResourceBarrier> barriers = [];
    private readonly List<(TextureView From, TextureView To, uint Slice)> resolves = [];
    private bool closed;
    private bool labelled;

    internal Recorder(Device device)
    {
        this.device = device;
        (allocator, List) = device.OpenList();
    }

    internal ID3D12GraphicsCommandList List { get; }

    internal Device Device => device;

    private void Touch(D3D12Life life)
    {
        if (touched.Add(life))
            Interlocked.Increment(ref life.Recording);
    }

    private static bool Reads(ResourceStates state) => state != ResourceStates.Common && (state & Writes) == 0;

    /// <summary>The buffer used in the given state (written, if <paramref name="write"/>) by the next command.</summary>
    internal void Use(BufferLife buffer, ResourceStates state, bool write = false)
    {
        Touch(buffer);
        // Memory the CPU writes or reads stays in the one state it was made in.
        if (buffer.Heap != HeapType.Default)
            return;
        if (!buffers.TryGetValue(buffer, out var now))
        {
            buffers[buffer] = (state, write);
            return;
        }
        if (now.State == state || (Reads(now.State) && (now.State & state) == state))
        {
            if (write && now.Written)
                barriers.Add(state == ResourceStates.UnorderedAccess ? ResourceBarrier.BarrierUnorderedAccessView(buffer.Resource) : ResourceBarrier.BarrierAliasing(null, null));
            buffers[buffer] = (now.State, now.Written | write);
            return;
        }
        ResourceStates after = Reads(now.State) && Reads(state) ? now.State | state : state;
        barriers.Add(ResourceBarrier.BarrierTransition(buffer.Resource, now.State, after));
        buffers[buffer] = (after, write);
    }

    /// <summary>The texture's mips, layers and planes used in the given state by the next command.</summary>
    internal void Use(TextureLife texture, uint baseMip, uint mips, uint baseLayer, uint layers, TextureAspect aspect, ResourceStates state)
    {
        Touch(texture);
        if (!textures.TryGetValue(texture, out ResourceStates[]? states))
        {
            states = new ResourceStates[texture.Subresources];
            Array.Fill(states, texture.Rest);
            textures[texture] = states;
        }
        (uint firstPlane, uint planes) = Planes(texture, aspect);
        int first = barriers.Count;
        bool every = true, alike = true, uav = false;
        ResourceStates? before = null, after = null;
        for (uint plane = firstPlane; plane < firstPlane + planes; plane++)
            for (uint layer = baseLayer; layer < baseLayer + layers; layer++)
                for (uint mip = baseMip; mip < baseMip + mips; mip++)
                {
                    uint index = texture.Subresource(mip, layer, plane);
                    ResourceStates now = states[index];
                    if (now == state || (Reads(now) && (now & state) == state))
                    {
                        uav |= state == ResourceStates.UnorderedAccess;
                        every = false;
                        continue;
                    }
                    ResourceStates next = Reads(now) && Reads(state) ? now | state : state;
                    barriers.Add(ResourceBarrier.BarrierTransition(texture.Resource, now, next, index));
                    states[index] = next;
                    alike &= before is null || (before == now && after == next);
                    before = now;
                    after = next;
                }
        // Every subresource moved alike: one barrier for the whole texture.
        int added = barriers.Count - first;
        if (every && alike && added > 1 && added == texture.Subresources)
        {
            barriers.RemoveRange(first, added);
            barriers.Add(ResourceBarrier.BarrierTransition(texture.Resource, before!.Value, after!.Value));
        }
        if (uav)
            barriers.Add(ResourceBarrier.BarrierUnorderedAccessView(texture.Resource));
    }

    private static (uint First, uint Count) Planes(TextureLife texture, TextureAspect aspect) => texture.Planes == 1 ? (0, 1) : aspect switch
    {
        TextureAspect.DepthOnly => (0, 1),
        TextureAspect.StencilOnly => (1, 1),
        _ => (0, texture.Planes),
    };

    /// <summary>Puts in the barriers the next command waits for.</summary>
    internal void Flush()
    {
        if (barriers.Count == 0)
            return;
        List.ResourceBarrier(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(barriers));
        barriers.Clear();
    }

    /// <summary>Ends the list: every texture it touched back in its resting state.</summary>
    internal void Close()
    {
        if (closed)
            return;
        foreach ((TextureLife texture, ResourceStates[] states) in textures)
        {
            int first = barriers.Count;
            bool alike = true;
            ResourceStates? from = null;
            for (uint index = 0; index < states.Length; index++)
            {
                if (states[index] == texture.Rest)
                {
                    alike = false;
                    continue;
                }
                barriers.Add(ResourceBarrier.BarrierTransition(texture.Resource, states[index], texture.Rest, index));
                alike &= from is null || from == states[index];
                from = states[index];
            }
            int added = barriers.Count - first;
            if (alike && added > 1)
            {
                barriers.RemoveRange(first, added);
                barriers.Add(ResourceBarrier.BarrierTransition(texture.Resource, from!.Value, texture.Rest));
            }
        }
        textures.Clear();
        buffers.Clear();
        Flush();
        List.Close();
        closed = true;
    }

    /// <summary>Run by the submit of the given serial: what it used is the GPU's until that's done.</summary>
    internal void Submitted(ulong serial)
    {
        foreach (D3D12Life life in touched)
        {
            life.LastUse = Math.Max(life.LastUse, serial);
            Interlocked.Decrement(ref life.Recording);
        }
        touched.Clear();
        device.Recycle(allocator, List, serial);
    }

    /// <summary>Never submitted: what it used is free of it, and so is its allocator.</summary>
    internal void Discard()
    {
        if (!closed)
        {
            List.Close();
            closed = true;
        }
        foreach (D3D12Life life in touched)
            Interlocked.Decrement(ref life.Recording);
        touched.Clear();
        device.Recycle(allocator, List, 0);
    }

    // Copies.

    internal void CopyBuffer(BufferLife source, ulong sourceOffset, BufferLife destination, ulong destinationOffset, ulong size)
    {
        if (size == 0)
            return;
        Use(source, ResourceStates.CopySource);
        Use(destination, ResourceStates.CopyDest, write: true);
        Flush();
        List.CopyBufferRegion(destination.Resource, destinationOffset, source.Resource, sourceOffset, size);
    }

    /// <summary>Zeros the range, copied from the device's megabyte of them.</summary>
    internal void ClearBuffer(BufferLife buffer, ulong offset, ulong size)
    {
        if (size == 0)
            return;
        BufferLife zeros = device.Zeros;
        Use(zeros, ResourceStates.CopySource);
        Use(buffer, ResourceStates.CopyDest, write: true);
        Flush();
        for (ulong done = 0; done < size; done += zeros.Size)
            List.CopyBufferRegion(buffer.Resource, offset + done, zeros.Resource, 0, Math.Min(zeros.Size, size - done));
    }

    /// <summary>Clears every mip and layer of a texture that's drawn into: colour to zero, depth to zero.</summary>
    internal void ClearTexture(TextureLife texture)
    {
        bool depth = D3D12Formats.IsDepth(texture.Format);
        Use(texture, 0, texture.Mips, 0, texture.Layers, TextureAspect.All, depth ? ResourceStates.DepthWrite : ResourceStates.RenderTarget);
        Flush();
        for (uint mip = 0; mip < texture.Mips; mip++)
            for (uint layer = 0; layer < texture.Layers; layer++)
            {
                if (depth)
                {
                    CpuDescriptorHandle handle = device.Dsv.Take();
                    device.Native.CreateDepthStencilView(texture.Resource, D3D12Views.Depth(texture, texture.Format, mip, layer, 1, DepthStencilViewFlags.None), handle);
                    List.ClearDepthStencilView(handle, D3D12Formats.HasStencil(texture.Format) ? ClearFlags.Depth | ClearFlags.Stencil : ClearFlags.Depth, 0, 0);
                    device.Dsv.Give(handle);
                }
                else
                {
                    CpuDescriptorHandle handle = device.Rtv.Take();
                    device.Native.CreateRenderTargetView(texture.Resource, D3D12Views.Target(texture, texture.Format, mip, layer, 1, 0, uint.MaxValue), handle);
                    List.ClearRenderTargetView(handle, new Color4(0, 0, 0, 0));
                    device.Rtv.Give(handle);
                }
            }
    }

    /// <summary>
    /// Copies between a buffer, at an offset Direct3D 12 places texels at (512-byte steps) with rows in 256-byte steps,
    /// and a texture's mip: one copy each layer or slice.
    /// </summary>
    internal void CopyTexels(BufferLife buffer, ulong offset, uint pitch, uint rowsPerImage, in TexelCopyTextureInfo texel, in Extent3D size, bool toTexture)
    {
        TextureLife texture = texel.Texture.Life;
        (uint block, _) = D3D12Formats.Layout(texture.Format);
        uint width = (size.Width + block - 1) / block * block, height = (size.Height + block - 1) / block * block;
        uint plane = texel.Aspect == TextureAspect.StencilOnly && texture.Planes > 1 ? 1u : 0u;
        bool volume = texture.Dimension == TextureDimension.D3;
        if (volume)
            Use(texture, texel.MipLevel, 1, 0, 1, texel.Aspect, toTexture ? ResourceStates.CopyDest : ResourceStates.CopySource);
        else
            Use(texture, texel.MipLevel, 1, texel.Origin.Z, size.DepthOrArrayLayers, texel.Aspect, toTexture ? ResourceStates.CopyDest : ResourceStates.CopySource);
        Use(buffer, toTexture ? ResourceStates.CopySource : ResourceStates.CopyDest, write: !toTexture);
        Flush();
        for (uint i = 0; i < size.DepthOrArrayLayers; i++)
        {
            uint subresource = texture.Subresource(texel.MipLevel, volume ? 0 : texel.Origin.Z + i, plane);
            uint z = volume ? texel.Origin.Z + i : 0;
            var footprint = new PlacedSubresourceFootPrint
            {
                Offset = offset + (ulong)i * pitch * rowsPerImage,
                Footprint = new SubresourceFootPrint { Format = texture.CopyFormats[plane], Width = width, Height = height, Depth = 1, RowPitch = pitch },
            };
            if (toTexture)
                List.CopyTextureRegion(new TextureCopyLocation(texture.Resource, subresource), texel.Origin.X, texel.Origin.Y, z,
                    new TextureCopyLocation(buffer.Resource, footprint), null);
            else
                List.CopyTextureRegion(new TextureCopyLocation(buffer.Resource, footprint), 0, 0, 0, new TextureCopyLocation(texture.Resource, subresource),
                    new D3DBox((int)texel.Origin.X, (int)texel.Origin.Y, (int)z, (int)(texel.Origin.X + width), (int)(texel.Origin.Y + height), (int)z + 1));
        }
    }

    /// <summary>The queue's texture write, from texels staged where Direct3D 12 places them.</summary>
    internal void CopyToTexture(BufferLife staging, ulong offset, uint pitch, uint rowsPerImage, in TexelCopyTextureInfo destination, in Extent3D size) =>
        CopyTexels(staging, offset, pitch, rowsPerImage, destination, size, toTexture: true);

    /// <summary>
    /// A copy between a buffer laid out as WebGPU lays it and a texture. Rows are already in 256-byte steps; texels at
    /// an offset that isn't in 512-byte steps go through a buffer of their own that starts them at zero.
    /// </summary>
    internal void CopyBufferTexture(in TexelCopyBufferInfo buffer, in TexelCopyTextureInfo texture, in Extent3D size, bool toTexture)
    {
        if (size.Width == 0 || size.Height == 0 || size.DepthOrArrayLayers == 0)
            return;
        (uint block, uint blockBytes) = D3D12Formats.Layout(texture.Texture.GetFormat());
        uint blocksWide = (size.Width + block - 1) / block, blocksHigh = (size.Height + block - 1) / block;
        uint rowBytes = blocksWide * blockBytes;
        uint pitch = buffer.Layout.BytesPerRow == uint.MaxValue ? (uint)Queue.Align(rowBytes, D3D12.TextureDataPitchAlignment) : buffer.Layout.BytesPerRow;
        uint rows = buffer.Layout.RowsPerImage == uint.MaxValue ? blocksHigh : buffer.Layout.RowsPerImage;
        if (pitch % D3D12.TextureDataPitchAlignment != 0)
        {
            device.Error(ErrorType.Validation, $"A texture copy's rows are {pitch} bytes apart, not a multiple of 256.");
            return;
        }
        BufferLife life = buffer.Buffer.Life;
        ulong offset = buffer.Layout.Offset;
        if (offset % D3D12.TextureDataPlacementAlignment == 0)
        {
            CopyTexels(life, offset, pitch, rows, texture, size, toTexture);
            return;
        }
        ulong span = (ulong)pitch * rows * (size.DepthOrArrayLayers - 1) + (ulong)pitch * (blocksHigh - 1) + rowBytes;
        BufferLife bounce = device.CreateScratch(Queue.Align(span, 4));
        if (toTexture)
        {
            CopyBuffer(life, offset, bounce, 0, span);
            CopyTexels(bounce, 0, pitch, rows, texture, size, toTexture: true);
        }
        else
        {
            CopyTexels(bounce, 0, pitch, rows, texture, size, toTexture: false);
            CopyBuffer(bounce, 0, life, offset, span);
        }
        device.Retire(bounce);
    }

    internal void CopyTextureToTexture(in TexelCopyTextureInfo source, in TexelCopyTextureInfo destination, in Extent3D size)
    {
        TextureLife from = source.Texture.Life, to = destination.Texture.Life;
        (uint block, _) = D3D12Formats.Layout(from.Format);
        uint width = (size.Width + block - 1) / block * block, height = (size.Height + block - 1) / block * block;
        bool volume = from.Dimension == TextureDimension.D3;
        Use(from, source.MipLevel, 1, volume ? 0 : source.Origin.Z, volume ? 1 : size.DepthOrArrayLayers, source.Aspect, ResourceStates.CopySource);
        Use(to, destination.MipLevel, 1, volume ? 0 : destination.Origin.Z, volume ? 1 : size.DepthOrArrayLayers, destination.Aspect, ResourceStates.CopyDest);
        Flush();
        uint fromPlane = source.Aspect == TextureAspect.StencilOnly && from.Planes > 1 ? 1u : 0u;
        uint toPlane = destination.Aspect == TextureAspect.StencilOnly && to.Planes > 1 ? 1u : 0u;
        uint copies = volume ? 1 : size.DepthOrArrayLayers, depth = volume ? size.DepthOrArrayLayers : 1;
        for (uint i = 0; i < copies; i++)
        {
            uint sz = volume ? source.Origin.Z : 0;
            List.CopyTextureRegion(new TextureCopyLocation(to.Resource, to.Subresource(destination.MipLevel, volume ? 0 : destination.Origin.Z + i, toPlane)),
                destination.Origin.X, destination.Origin.Y, volume ? destination.Origin.Z : 0,
                new TextureCopyLocation(from.Resource, from.Subresource(source.MipLevel, volume ? 0 : source.Origin.Z + i, fromPlane)),
                new D3DBox((int)source.Origin.X, (int)source.Origin.Y, (int)sz, (int)(source.Origin.X + width), (int)(source.Origin.Y + height), (int)(sz + depth)));
        }
    }

    // Render passes.

    internal void BeginPass(in RenderPassDescriptor descriptor)
    {
        RenderPassColorAttachment[] colors = descriptor.ColorAttachments ?? [];
        Span<CpuDescriptorHandle> targets = stackalloc CpuDescriptorHandle[8];
        int count = 0;
        uint width = 0, height = 0;
        foreach (RenderPassColorAttachment color in colors)
        {
            if (color.View is not { } view)
                continue;
            TextureLife texture = view.Texture.Life;
            uint slice = color.DepthSlice ?? 0;
            bool volume = texture.Dimension == TextureDimension.D3;
            Use(texture, view.BaseMip, 1, volume ? 0 : view.BaseLayer, volume ? 1 : view.Layers, TextureAspect.All, ResourceStates.RenderTarget);
            targets[count++] = view.Target(slice);
            if (color.ResolveTarget is { } resolve)
                resolves.Add((view, resolve, slice));
            (width, height, _) = texture.Extent(view.BaseMip);
        }
        CpuDescriptorHandle? depthTarget = null;
        if (descriptor.DepthStencilAttachment is { } depth)
        {
            TextureView view = depth.View;
            TextureLife texture = view.Texture.Life;
            bool stencil = D3D12Formats.HasStencil(texture.Format);
            // Only read: still sampled in the pass, if it's a texture shaders sample.
            ResourceStates read = ResourceStates.DepthRead | ((texture.Usage & TextureUsage.TextureBinding) != 0 ? ResourceStates.AllShaderResource : 0);
            Use(texture, view.BaseMip, 1, view.BaseLayer, view.Layers, stencil ? TextureAspect.DepthOnly : TextureAspect.All,
                depth.DepthReadOnly ? read : ResourceStates.DepthWrite);
            if (stencil)
                Use(texture, view.BaseMip, 1, view.BaseLayer, view.Layers, TextureAspect.StencilOnly, depth.StencilReadOnly ? read : ResourceStates.DepthWrite);
            DepthStencilViewFlags flags = (depth.DepthReadOnly ? DepthStencilViewFlags.ReadOnlyDepth : 0)
                | (stencil && depth.StencilReadOnly ? DepthStencilViewFlags.ReadOnlyStencil : 0);
            depthTarget = view.DepthTarget(flags);
            if (count == 0)
                (width, height, _) = texture.Extent(view.BaseMip);
        }
        Flush();
        if (descriptor.Label is { } label)
        {
            List.BeginEvent(label);
            labelled = true;
        }
        List.OMSetRenderTargets((uint)count, targets[..count], false, depthTarget);
        int target = 0;
        foreach (RenderPassColorAttachment color in colors)
        {
            if (color.View is null)
                continue;
            if (color.LoadOp == LoadOp.Clear)
                List.ClearRenderTargetView(targets[target], new Color4((float)color.ClearValue.R, (float)color.ClearValue.G, (float)color.ClearValue.B, (float)color.ClearValue.A));
            target++;
        }
        if (descriptor.DepthStencilAttachment is { } cleared && depthTarget is { } handle)
        {
            ClearFlags clear = (!cleared.DepthReadOnly && cleared.DepthLoadOp == LoadOp.Clear ? ClearFlags.Depth : 0)
                | (D3D12Formats.HasStencil(cleared.View.Texture.Life.Format) && !cleared.StencilReadOnly && cleared.StencilLoadOp == LoadOp.Clear ? ClearFlags.Stencil : 0);
            if (clear != 0)
                List.ClearDepthStencilView(handle, clear, cleared.DepthClearValue, (byte)cleared.StencilClearValue);
        }
        List.RSSetViewport(0, 0, width, height, 0, 1);
        List.RSSetScissorRect(new Vortice.RawRect(0, 0, (int)width, (int)height));
    }

    internal void EndPass()
    {
        foreach ((TextureView from, TextureView to, uint slice) in resolves)
        {
            TextureLife source = from.Texture.Life, destination = to.Texture.Life;
            Use(source, from.BaseMip, 1, from.BaseLayer, 1, TextureAspect.All, ResourceStates.ResolveSource);
            Use(destination, to.BaseMip, 1, to.BaseLayer, 1, TextureAspect.All, ResourceStates.ResolveDest);
            Flush();
            List.ResolveSubresource(destination.Resource, destination.Subresource(to.BaseMip, to.BaseLayer, 0), source.Resource,
                source.Subresource(from.BaseMip, from.BaseLayer, 0), D3D12Formats.View(to.Format));
        }
        resolves.Clear();
        if (labelled)
        {
            List.EndEvent();
            labelled = false;
        }
    }
}

/// <summary>Records a command buffer; used up by <see cref="Finish"/>.</summary>
public readonly struct CommandEncoder
{
    private readonly Recorder recorder;

    internal CommandEncoder(Recorder recorder) => this.recorder = recorder;

    public RenderPassEncoder BeginRenderPass(in RenderPassDescriptor descriptor)
    {
        recorder.BeginPass(descriptor);
        return new RenderPassEncoder(recorder);
    }

    public ComputePassEncoder BeginComputePass() => new(recorder);

    public void CopyBufferToBuffer(Buffer source, ulong sourceOffset, Buffer destination, ulong destinationOffset, ulong size) =>
        recorder.CopyBuffer(source.Life, sourceOffset, destination.Life, destinationOffset, size);

    public void CopyBufferToTexture(in TexelCopyBufferInfo source, in TexelCopyTextureInfo destination, in Extent3D size) =>
        recorder.CopyBufferTexture(source, destination, size, toTexture: true);

    public void CopyTextureToBuffer(in TexelCopyTextureInfo source, in TexelCopyBufferInfo destination, in Extent3D size) =>
        recorder.CopyBufferTexture(destination, source, size, toTexture: false);

    public void CopyTextureToTexture(in TexelCopyTextureInfo source, in TexelCopyTextureInfo destination, in Extent3D size) =>
        recorder.CopyTextureToTexture(source, destination, size);

    public void ClearBuffer(Buffer buffer, ulong offset = 0, ulong size = ulong.MaxValue) =>
        recorder.ClearBuffer(buffer.Life, offset, size == ulong.MaxValue ? buffer.GetSize() - offset : size);

    public CommandBuffer Finish()
    {
        recorder.Close();
        return new CommandBuffer(recorder);
    }
}

/// <summary>A render pass; used up by <see cref="End"/>.</summary>
public readonly struct RenderPassEncoder
{
    private readonly Recorder recorder;

    internal RenderPassEncoder(Recorder recorder) => this.recorder = recorder;

    public void SetPipeline(RenderPipeline pipeline) => throw D3D12Pending.Draws();

    public void SetBindGroup(uint index, BindGroup? group) => throw D3D12Pending.Draws();

    public void SetBindGroup(uint index, BindGroup group, params ReadOnlySpan<uint> dynamicOffsets) => throw D3D12Pending.Draws();

    public void SetVertexBuffer(uint slot, Buffer buffer, ulong offset = 0, ulong size = ulong.MaxValue) => throw D3D12Pending.Draws();

    public void SetIndexBuffer(Buffer buffer, IndexFormat format, ulong offset = 0, ulong size = ulong.MaxValue) => throw D3D12Pending.Draws();

    public void SetViewport(float x, float y, float width, float height, float minDepth, float maxDepth) =>
        recorder.List.RSSetViewport(x, y, width, height, minDepth, maxDepth);

    public void SetScissorRect(uint x, uint y, uint width, uint height) =>
        recorder.List.RSSetScissorRect(new Vortice.RawRect((int)x, (int)y, (int)(x + width), (int)(y + height)));

    public void SetBlendConstant(in Color color) => recorder.List.OMSetBlendFactor(new Color4((float)color.R, (float)color.G, (float)color.B, (float)color.A));

    public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0) => throw D3D12Pending.Draws();

    public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0) => throw D3D12Pending.Draws();

    public void DrawIndexedIndirect(Buffer indirect, ulong offset) => throw D3D12Pending.Draws();

    public void ExecuteBundles(ReadOnlySpan<RenderBundle> bundles) => throw D3D12Pending.Bundles();

    public void End() => recorder.EndPass();
}

/// <summary>A compute pass; used up by <see cref="End"/>.</summary>
public readonly struct ComputePassEncoder
{
    private readonly Recorder recorder;

    internal ComputePassEncoder(Recorder recorder) => this.recorder = recorder;

    public void SetPipeline(ComputePipeline pipeline) => throw D3D12Pending.Draws();

    public void SetBindGroup(uint index, BindGroup group) => throw D3D12Pending.Draws();

    public void DispatchWorkgroups(uint x, uint y = 1, uint z = 1) => throw D3D12Pending.Draws();

    public void DispatchWorkgroupsIndirect(Buffer indirect, ulong offset) => throw D3D12Pending.Draws();

    public void End()
    {
    }
}

/// <summary>Records a render bundle; used up by <see cref="Finish"/>.</summary>
public readonly struct RenderBundleEncoder
{
    public void SetPipeline(RenderPipeline pipeline) => throw D3D12Pending.Bundles();

    public void SetBindGroup(uint index, BindGroup group) => throw D3D12Pending.Bundles();

    public void SetVertexBuffer(uint slot, Buffer buffer, ulong offset = 0, ulong size = ulong.MaxValue) => throw D3D12Pending.Bundles();

    public void SetIndexBuffer(Buffer buffer, IndexFormat format, ulong offset = 0, ulong size = ulong.MaxValue) => throw D3D12Pending.Bundles();

    public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0) => throw D3D12Pending.Bundles();

    public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0) => throw D3D12Pending.Bundles();

    public void DrawIndexedIndirect(Buffer indirect, ulong offset) => throw D3D12Pending.Bundles();

    public RenderBundle Finish() => throw D3D12Pending.Bundles();
}

/// <summary>A recorded command list, waiting to be submitted; let go of unsubmitted, it's thrown away.</summary>
public sealed class CommandBuffer : GpuObject
{
    private Recorder? recorder;

    internal CommandBuffer(Recorder recorder) => this.recorder = recorder;

    internal Recorder Take() => Interlocked.Exchange(ref recorder, null) ?? throw new GpuException("The command buffer was already submitted.");

    private protected override void Release() => Interlocked.Exchange(ref recorder, null)?.Discard();
}
