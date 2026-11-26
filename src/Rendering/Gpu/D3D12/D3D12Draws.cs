using Vortice.Direct3D12;
using DxgiFormat = Vortice.DXGI.Format;

namespace HikariHopper.Gpu;

/// <summary>
/// What a pass binds and draws with. A pass starts with nothing bound, as WebGPU's do. Root arguments go in just before
/// the draw or dispatch that needs them: a bind group's table and dynamic buffers once it's set (and again if the root
/// signature changes under it), vertex buffers once their pipeline's strides are known, and Naga's special constants
/// when they change. A bind group's buffers and textures move into the states its shaders read them in as it's set;
/// a compute pass also waits for each dispatch's writes before the next dispatch, as WebGPU's dispatches do.
/// </summary>
internal sealed unsafe partial class Recorder
{
    private const int MaxGroups = 8;
    private const int MaxVertexBuffers = 16;

    private readonly BindGroup?[] groups = new BindGroup?[MaxGroups];
    private readonly uint[][] offsets = new uint[MaxGroups][];
    private readonly bool[] groupsDirty = new bool[MaxGroups];
    private readonly (BufferLife? Buffer, ulong Offset, ulong Size)[] vertexBuffers = new (BufferLife?, ulong, ulong)[MaxVertexBuffers];
    private RenderPipeline? renderPipeline;
    private ComputePipeline? computePipeline;
    private ID3D12RootSignature? rootSignature;
    private bool vertexBuffersDirty;
    private bool heapsSet;
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

    private void SetHeaps()
    {
        if (heapsSet)
            return;
        ShaderHeaps heaps = device.Heaps;
        Touch(heaps.Indices);
        List.SetDescriptorHeaps([heaps.Views, heaps.Samplers]);
        heapsSet = true;
    }

    private void SetRoot(PipelineLayout layout)
    {
        Touch(layout.Life);
        if (rootSignature == layout.Native)
            return;
        rootSignature = layout.Native;
        if (compute)
            List.SetComputeRootSignature(layout.Native);
        else
            List.SetGraphicsRootSignature(layout.Native);
        // A new root signature forgets every root argument.
        Array.Fill(groupsDirty, true);
        constants = null;
        if (layout.SamplerTable >= 0)
        {
            if (compute)
                List.SetComputeRootDescriptorTable((uint)layout.SamplerTable, device.Heaps.SamplersOnGpu);
            else
                List.SetGraphicsRootDescriptorTable((uint)layout.SamplerTable, device.Heaps.SamplersOnGpu);
        }
    }

    internal void SetPipeline(RenderPipeline pipeline)
    {
        renderPipeline = pipeline;
        if (pipeline.State is null || pipeline.Life is null)
            return;
        SetHeaps();
        Touch(pipeline.Life);
        List.SetPipelineState(pipeline.State);
        SetRoot(pipeline.Layout);
        List.IASetPrimitiveTopology(pipeline.Topology);
        vertexBuffersDirty = true;
    }

    internal void SetPipeline(ComputePipeline pipeline)
    {
        computePipeline = pipeline;
        if (pipeline.State is null || pipeline.Life is null)
            return;
        SetHeaps();
        Touch(pipeline.Life);
        List.SetPipelineState(pipeline.State);
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
            Touch(group.Life);
        // Into the states its shaders read and write them in; a render pass's, before its draws.
        foreach ((BufferLife buffer, ResourceStates state, bool write) in group.Buffers)
            Use(buffer, state, write);
        foreach ((TextureView view, ResourceStates state) in group.Textures)
            UseView(view, state);
    }

    private void UseView(TextureView view, ResourceStates state)
    {
        TextureLife texture = view.Texture.Life;
        bool volume = texture.Dimension == TextureDimension.D3;
        uint mips = state == ResourceStates.UnorderedAccess ? 1 : view.Mips;
        Use(texture, view.BaseMip, mips, volume ? 0 : view.BaseLayer, volume ? 1 : view.Layers, view.Aspect, state);
    }

    internal void SetVertexBuffer(uint slot, BufferLife buffer, ulong offset, ulong size)
    {
        Use(buffer, ResourceStates.VertexAndConstantBuffer);
        vertexBuffers[slot] = (buffer, offset, size);
        vertexBuffersDirty = true;
    }

    internal void SetIndexBuffer(BufferLife buffer, IndexFormat format, ulong offset, ulong size)
    {
        Use(buffer, ResourceStates.IndexBuffer);
        List.IASetIndexBuffer(new IndexBufferView(buffer.Resource.GPUVirtualAddress + offset, (uint)size,
            format == IndexFormat.Uint32 ? DxgiFormat.R32_UInt : DxgiFormat.R16_UInt));
    }

    /// <summary>What the next draw or dispatch needs in its root arguments and barriers; false if its pipeline couldn't be made.</summary>
    private bool Prepare(PipelineLayout? layout, int first, int second, int third)
    {
        if (layout is null)
            return false;
        Flush();
        for (int index = 0; index < layout.Groups.Length; index++)
        {
            if (!groupsDirty[index] || groups[index] is not { } group)
                continue;
            groupsDirty[index] = false;
            if (layout.Tables[index] is >= 0 and int table)
            {
                GpuDescriptorHandle start = device.Heaps.ViewOnGpu(group.First);
                if (compute)
                    List.SetComputeRootDescriptorTable((uint)table, start);
                else
                    List.SetGraphicsRootDescriptorTable((uint)table, start);
            }
            int[] roots = layout.DynamicRoots[index];
            uint[] dynamic = offsets[index];
            for (int i = 0; i < roots.Length && i < group.Dynamic.Length; i++)
            {
                (BufferLife buffer, ulong offset, _) = group.Dynamic[i];
                ulong address = buffer.Resource.GPUVirtualAddress + offset + (i < dynamic.Length ? dynamic[i] : 0);
                if (compute)
                    List.SetComputeRootConstantBufferView((uint)roots[i], address);
                else
                    List.SetGraphicsRootConstantBufferView((uint)roots[i], address);
            }
        }
        if (constants != (first, second, third))
        {
            constants = (first, second, third);
            int* values = stackalloc int[3] { first, second, third };
            if (compute)
                List.SetComputeRoot32BitConstants((uint)PipelineLayout.Constants, 3, values, 0);
            else
                List.SetGraphicsRoot32BitConstants((uint)PipelineLayout.Constants, 3, values, 0);
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
                    List.IASetVertexBuffers((uint)slot, new VertexBufferView(buffer.Resource.GPUVirtualAddress + bound.Offset, (uint)bound.Size, strides[slot]));
        }
        return true;
    }

    internal void Draw(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
    {
        if (PrepareDraw((int)firstVertex, firstInstance))
            List.DrawInstanced(vertexCount, instanceCount, firstVertex, firstInstance);
    }

    internal void DrawIndexed(uint indexCount, uint instanceCount, uint firstIndex, int baseVertex, uint firstInstance)
    {
        if (PrepareDraw(baseVertex, firstInstance))
            List.DrawIndexedInstanced(indexCount, instanceCount, firstIndex, baseVertex, firstInstance);
    }

    /// <summary>
    /// A draw whose arguments the GPU reads from the buffer. Its shaders see its first vertex and instance as zero: the
    /// pipelines drawn this way take their instances from vertex buffers, which Direct3D 12 offsets itself.
    /// </summary>
    internal void DrawIndexedIndirect(BufferLife arguments, ulong offset)
    {
        Use(arguments, ResourceStates.IndirectArgument);
        if (PrepareDraw(0, 0))
            List.ExecuteIndirect(device.Signatures.DrawIndexed, 1, arguments.Resource, offset, null, 0);
    }

    // Compute.

    private void PrepareDispatch()
    {
        // WebGPU's dispatches see each other's writes: what the groups write is waited for again before each one.
        for (int index = 0; index < MaxGroups; index++)
            if (groups[index] is { } group)
            {
                foreach ((BufferLife buffer, ResourceStates state, bool write) in group.Buffers)
                    Use(buffer, state, write);
                foreach ((TextureView view, ResourceStates state) in group.Textures)
                    UseView(view, state);
            }
    }

    internal void Dispatch(uint x, uint y, uint z)
    {
        PrepareDispatch();
        if (computePipeline is { State: not null } pipeline && Prepare(pipeline.Layout, (int)x, (int)y, (int)z))
            List.Dispatch(x, y, z);
    }

    /// <summary>A dispatch whose workgroup counts the GPU reads from the buffer (its shaders see them as zero).</summary>
    internal void DispatchIndirect(BufferLife arguments, ulong offset)
    {
        PrepareDispatch();
        Use(arguments, ResourceStates.IndirectArgument);
        if (computePipeline is { State: not null } pipeline && Prepare(pipeline.Layout, 0, 0, 0))
            List.ExecuteIndirect(device.Signatures.Dispatch, 1, arguments.Resource, offset, null, 0);
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
