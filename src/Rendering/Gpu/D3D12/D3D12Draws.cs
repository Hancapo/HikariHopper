using Vortice.Direct3D12;
using DxgiFormat = Vortice.DXGI.Format;

namespace HikariHopper.Gpu;

/// <summary>
/// What a pass or a render bundle binds and draws with. Either starts with nothing bound, as WebGPU's do. Root arguments
/// go in just before the draw or dispatch that needs them: a bind group's table and dynamic buffers once it's set (and
/// again if the root signature changes under it), vertex buffers once their pipeline's strides are known, and Naga's
/// special constants when they change. A bind group's buffers and textures are used in the states its shaders read them
/// in as it's set (a pass moves them there; a bundle lists them for the pass that runs it); a compute pass also waits
/// for each dispatch's writes before the next dispatch, as WebGPU's dispatches do.
/// </summary>
internal abstract unsafe class DrawRecorder
{
    private const int MaxGroups = 8;
    private const int MaxVertexBuffers = 16;

    private readonly BindGroup?[] groups = new BindGroup?[MaxGroups];
    private readonly uint[][] offsets = new uint[MaxGroups][];
    private readonly bool[] groupsDirty = new bool[MaxGroups];
    private readonly (BufferLife? Buffer, ulong Offset, ulong Size)[] vertexBuffers = new (BufferLife?, ulong, ulong)[MaxVertexBuffers];
    /// <summary>The command list drawn into: a pass's, or a bundle's.</summary>
    private protected abstract ID3D12GraphicsCommandList Commands { get; }

    private protected abstract Device Owner { get; }

    /// <summary>A buffer the next commands use in the given state.</summary>
    private protected abstract void UseBuffer(BufferLife buffer, ResourceStates state, bool write = false);

    /// <summary>A texture view the next commands use in the given state.</summary>
    private protected abstract void UseTexture(TextureView view, ResourceStates state);

    /// <summary>Something the GPU uses until the commands are done with it.</summary>
    private protected abstract void Hold(D3D12Life life);

    /// <summary>The barriers the next command waits for.</summary>
    private protected abstract void Barriers();

    private RenderPipeline? renderPipeline;
    private ComputePipeline? computePipeline;
    private ID3D12RootSignature? rootSignature;
    private bool vertexBuffersDirty;
    private protected bool heapsSet;
    private bool compute;
    private (int A, int B, int C)? constants;

    /// <summary>A pass begun: nothing bound.</summary>
    internal void ResetBindings(bool computing)
    {
        Array.Clear(groups);
        Array.Clear(groupsDirty);
        Array.Clear(vertexBuffers);
        renderPipeline = null;
        computePipeline = null;
        rootSignature = null;
        constants = null;
        vertexBuffersDirty = false;
        compute = computing;
    }

    private protected void SetHeaps()
    {
        if (heapsSet)
            return;
        ShaderHeaps heaps = Owner.Heaps;
        Hold(heaps.Indices);
        Commands.SetDescriptorHeaps(heaps.Both);
        heapsSet = true;
    }

    private void SetRoot(PipelineLayout layout)
    {
        Hold(layout.Life);
        if (rootSignature == layout.Native)
            return;
        rootSignature = layout.Native;
        if (compute)
            Commands.SetComputeRootSignature(layout.Native);
        else
            Commands.SetGraphicsRootSignature(layout.Native);
        // A new root signature forgets every root argument.
        Array.Fill(groupsDirty, true);
        constants = null;
        if (layout.SamplerTable >= 0)
        {
            if (compute)
                Commands.SetComputeRootDescriptorTable((uint)layout.SamplerTable, Owner.Heaps.SamplersOnGpu);
            else
                Commands.SetGraphicsRootDescriptorTable((uint)layout.SamplerTable, Owner.Heaps.SamplersOnGpu);
        }
    }

    internal void SetPipeline(RenderPipeline pipeline)
    {
        renderPipeline = pipeline;
        if (pipeline.State is null || pipeline.Life is null)
            return;
        SetHeaps();
        Hold(pipeline.Life);
        Commands.SetPipelineState(pipeline.State);
        SetRoot(pipeline.Layout);
        Commands.IASetPrimitiveTopology(pipeline.Topology);
        vertexBuffersDirty = true;
    }

    internal void SetPipeline(ComputePipeline pipeline)
    {
        computePipeline = pipeline;
        if (pipeline.State is null || pipeline.Life is null)
            return;
        SetHeaps();
        Hold(pipeline.Life);
        Commands.SetPipelineState(pipeline.State);
        SetRoot(pipeline.Layout);
    }

    internal void SetBindGroup(uint index, BindGroup? group, ReadOnlySpan<uint> dynamicOffsets)
    {
        groups[index] = group;
        offsets[index] = dynamicOffsets.IsEmpty ? [] : dynamicOffsets.ToArray();
        groupsDirty[index] = true;
        if (group is null)
            return;
        if (group.Life is not null)
            Hold(group.Life);
        // Into the states its shaders read and write them in; a render pass's, before its draws.
        foreach ((BufferLife buffer, ResourceStates state, bool write) in group.Buffers)
            UseBuffer(buffer, state, write);
        foreach ((TextureView view, ResourceStates state) in group.Textures)
            UseTexture(view, state);
    }

    internal void SetVertexBuffer(uint slot, BufferLife buffer, ulong offset, ulong size)
    {
        UseBuffer(buffer, ResourceStates.VertexAndConstantBuffer);
        vertexBuffers[slot] = (buffer, offset, size);
        vertexBuffersDirty = true;
    }

    internal void SetIndexBuffer(BufferLife buffer, IndexFormat format, ulong offset, ulong size)
    {
        UseBuffer(buffer, ResourceStates.IndexBuffer);
        Commands.IASetIndexBuffer(new IndexBufferView(buffer.Resource.GPUVirtualAddress + offset, (uint)size,
            format == IndexFormat.Uint32 ? DxgiFormat.R32_UInt : DxgiFormat.R16_UInt));
    }

    /// <summary>What the next draw or dispatch needs in its root arguments and barriers; false if its pipeline couldn't be made.</summary>
    private bool Prepare(PipelineLayout? layout, int first, int second, int third)
    {
        if (layout is null)
            return false;
        Barriers();
        for (int index = 0; index < layout.Groups.Length; index++)
        {
            if (!groupsDirty[index] || groups[index] is not { } group)
                continue;
            groupsDirty[index] = false;
            if (layout.Tables[index] is >= 0 and int table)
            {
                GpuDescriptorHandle start = Owner.Heaps.ViewOnGpu(group.First);
                if (compute)
                    Commands.SetComputeRootDescriptorTable((uint)table, start);
                else
                    Commands.SetGraphicsRootDescriptorTable((uint)table, start);
            }
            int[] roots = layout.DynamicRoots[index];
            uint[] dynamic = offsets[index];
            for (int i = 0; i < roots.Length && i < group.Dynamic.Length; i++)
            {
                (BufferLife buffer, ulong offset, _) = group.Dynamic[i];
                ulong address = buffer.Resource.GPUVirtualAddress + offset + (i < dynamic.Length ? dynamic[i] : 0);
                if (compute)
                    Commands.SetComputeRootConstantBufferView((uint)roots[i], address);
                else
                    Commands.SetGraphicsRootConstantBufferView((uint)roots[i], address);
            }
        }
        if (constants != (first, second, third))
        {
            constants = (first, second, third);
            int* values = stackalloc int[3] { first, second, third };
            if (compute)
                Commands.SetComputeRoot32BitConstants((uint)PipelineLayout.Constants, 3, values, 0);
            else
                Commands.SetGraphicsRoot32BitConstants((uint)PipelineLayout.Constants, 3, values, 0);
        }
        return true;
    }

    private bool PrepareDraw(int firstVertex, uint firstInstance)
    {
        if (renderPipeline is not { State: not null } pipeline || !Prepare(pipeline.Layout, firstVertex, (int)firstInstance, 0))
            return false;
        if (vertexBuffersDirty)
        {
            vertexBuffersDirty = false;
            uint[] strides = pipeline.Strides;
            for (int slot = 0; slot < strides.Length; slot++)
                if (vertexBuffers[slot] is { Buffer: { } buffer } bound)
                    Commands.IASetVertexBuffers((uint)slot, new VertexBufferView(buffer.Resource.GPUVirtualAddress + bound.Offset, (uint)bound.Size, strides[slot]));
        }
        return true;
    }

    internal void Draw(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
    {
        if (PrepareDraw((int)firstVertex, firstInstance))
            Commands.DrawInstanced(vertexCount, instanceCount, firstVertex, firstInstance);
    }

    internal void DrawIndexed(uint indexCount, uint instanceCount, uint firstIndex, int baseVertex, uint firstInstance)
    {
        if (PrepareDraw(baseVertex, firstInstance))
            Commands.DrawIndexedInstanced(indexCount, instanceCount, firstIndex, baseVertex, firstInstance);
    }

    /// <summary>
    /// A draw whose arguments the GPU reads from the buffer. Its shaders see its first vertex and instance as zero: the
    /// pipelines drawn this way take their instances from vertex buffers, which Direct3D 12 offsets itself.
    /// </summary>
    internal void DrawIndexedIndirect(BufferLife arguments, ulong offset)
    {
        UseBuffer(arguments, ResourceStates.IndirectArgument);
        if (PrepareDraw(0, 0))
            Commands.ExecuteIndirect(Owner.Signatures.DrawIndexed, 1, arguments.Resource, offset, null, 0);
    }

    // Compute.

    private void PrepareDispatch()
    {
        // WebGPU's dispatches see each other's writes: what the groups write is waited for again before each one.
        for (int index = 0; index < MaxGroups; index++)
            if (groups[index] is { } group)
            {
                foreach ((BufferLife buffer, ResourceStates state, bool write) in group.Buffers)
                    UseBuffer(buffer, state, write);
                foreach ((TextureView view, ResourceStates state) in group.Textures)
                    UseTexture(view, state);
            }
    }

    internal void Dispatch(uint x, uint y, uint z)
    {
        PrepareDispatch();
        if (computePipeline is { State: not null } pipeline && Prepare(pipeline.Layout, (int)x, (int)y, (int)z))
            Commands.Dispatch(x, y, z);
    }

    /// <summary>A dispatch whose workgroup counts the GPU reads from the buffer (its shaders see them as zero).</summary>
    internal void DispatchIndirect(BufferLife arguments, ulong offset)
    {
        PrepareDispatch();
        UseBuffer(arguments, ResourceStates.IndirectArgument);
        if (computePipeline is { State: not null } pipeline && Prepare(pipeline.Layout, 0, 0, 0))
            Commands.ExecuteIndirect(Owner.Signatures.Dispatch, 1, arguments.Resource, offset, null, 0);
    }
}

/// <summary>How the GPU reads WebGPU's indirect arguments: an indexed draw's five values, a dispatch's three.</summary>
internal sealed class CommandSignatures(ID3D12Device device)
{
    internal ID3D12CommandSignature DrawIndexed { get; } = device.CreateCommandSignature<ID3D12CommandSignature>(
        new CommandSignatureDescription(20, [new IndirectArgumentDescription { Type = IndirectArgumentType.DrawIndexed }]), null);

    internal ID3D12CommandSignature Dispatch { get; } = device.CreateCommandSignature<ID3D12CommandSignature>(
        new CommandSignatureDescription(12, [new IndirectArgumentDescription { Type = IndirectArgumentType.Dispatch }]), null);
}
