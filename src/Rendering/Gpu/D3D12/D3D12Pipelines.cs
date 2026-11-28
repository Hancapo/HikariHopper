using System.Text;
using SharpGen.Runtime;
using Vortice.D3D12MemoryAllocator;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;
using D3DBlend = Vortice.Direct3D12.Blend;
using D3DBlendOperation = Vortice.Direct3D12.BlendOperation;
using D3DCullMode = Vortice.Direct3D12.CullMode;
using D3DStencilOperation = Vortice.Direct3D12.StencilOperation;
using D3DTopology = Vortice.Direct3D.PrimitiveTopology;
using DxgiFormat = Vortice.DXGI.Format;

namespace HikariHopper.Gpu;

// How WebGPU's bindings are laid out on Direct3D 12, matching what tools/ShaderCompiler compiles the shaders for:
//
// - a bind group is one range of descriptors in a shader-visible heap, a descriptor table: its bindings in order, the
//   binding at @group(g) @binding(b) at register b of space g + 1, and, if it has samplers, the buffer of their sampler
//   heap indices last (register t1000), the sampler at binding b at index b;
// - samplers are made once each into one shader-visible sampler heap that every pipeline sees whole (s0 up, and s2048
//   up for comparison samplers);
// - uniform buffers with a dynamic offset are root descriptors, set at the offset when the group is;
// - Naga's special constants (first vertex and instance, workgroup counts) are three root constants at b0, space 0.

/// <summary>The shader-visible heaps bind groups' descriptors live in, and the samplers made into them once each.</summary>
internal sealed unsafe class ShaderHeaps
{
    internal const uint ViewCount = 1_000_000;
    internal const uint SamplerCount = 2048;
    internal const uint SamplerIndexRegister = 1000;
    private const uint IndexArenaCount = 1 << 20;

    private readonly ID3D12Device native;
    private readonly VirtualBlock views;
    private readonly Dictionary<SamplerDescription, uint> samplers = [];
    private readonly Dictionary<string, uint> indexLists = [];
    private readonly uint viewIncrement, samplerIncrement;
    private uint indexUsed;

    internal ShaderHeaps(Device device)
    {
        native = device.Native;
        Views = native.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView,
            ViewCount, DescriptorHeapFlags.ShaderVisible));
        Views.Name = "bind groups";
        Samplers = native.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.Sampler, SamplerCount, DescriptorHeapFlags.ShaderVisible));
        Samplers.Name = "samplers";
        viewIncrement = native.GetDescriptorHandleIncrementSize(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
        samplerIncrement = native.GetDescriptorHandleIncrementSize(DescriptorHeapType.Sampler);
        views = D3D12MA.CreateVirtualBlock(new VirtualBlockDescription(ViewCount));
        Indices = device.CreateUpload(IndexArenaCount * 4, "sampler indices", out byte* pointer);
        indexPointer = (uint*)pointer;
    }

    private readonly uint* indexPointer;

    internal ID3D12DescriptorHeap Views { get; }

    internal ID3D12DescriptorHeap Samplers { get; }

    /// <summary>Both heaps, as a list sets them.</summary>
    internal ID3D12DescriptorHeap[] Both => both ??= [Views, Samplers];

    private ID3D12DescriptorHeap[]? both;

    /// <summary>Every bind group's list of sampler heap indices, each list written once and shared by every group with it.</summary>
    internal BufferLife Indices { get; }

    internal CpuDescriptorHandle View(uint index) => new(Views.GetCPUDescriptorHandleForHeapStart(), (int)index, viewIncrement);

    internal GpuDescriptorHandle ViewOnGpu(uint index) => new(Views.GetGPUDescriptorHandleForHeapStart(), (int)index, viewIncrement);

    internal GpuDescriptorHandle SamplersOnGpu => Samplers.GetGPUDescriptorHandleForHeapStart();

    /// <summary>A range of <paramref name="count"/> descriptors for a bind group.</summary>
    internal (uint First, VirtualAllocation Range) Take(uint count)
    {
        lock (views)
        {
            if (views.Allocate(new VirtualAllocationDescription(count), out VirtualAllocation range, out ulong offset).Failure)
                throw new GpuException($"The bind groups' {ViewCount} descriptors are all in use.");
            return ((uint)offset, range);
        }
    }

    internal void Give(VirtualAllocation range)
    {
        lock (views)
            views.FreeAllocation(range);
    }

    /// <summary>The sampler's index in the sampler heap: made the first time one like it is asked for.</summary>
    internal uint Sampler(SamplerDescription description)
    {
        lock (samplers)
        {
            if (samplers.TryGetValue(description, out uint index))
                return index;
            if (samplers.Count == SamplerCount)
                throw new GpuException($"More than {SamplerCount} different samplers were asked for.");
            index = (uint)samplers.Count;
            native.CreateSampler(ref description, new CpuDescriptorHandle(Samplers.GetCPUDescriptorHandleForHeapStart(), (int)index, samplerIncrement));
            samplers[description] = index;
            return index;
        }
    }

    /// <summary>Where a list of sampler heap indices starts in <see cref="Indices"/>, written the first time it's asked for.</summary>
    internal uint IndexList(ReadOnlySpan<uint> indices)
    {
        string key = string.Join(',', indices.ToArray());
        lock (indexLists)
        {
            if (indexLists.TryGetValue(key, out uint first))
                return first;
            if (indexUsed + indices.Length > IndexArenaCount)
                throw new GpuException("The bind groups' sampler index lists are full.");
            first = indexUsed;
            indices.CopyTo(new Span<uint>(indexPointer + first, indices.Length));
            indexUsed += (uint)indices.Length;
            indexLists[key] = first;
            return first;
        }
    }
}

public sealed class Sampler : GpuObject
{
    internal Sampler(Device device, in SamplerDescriptor descriptor)
    {
        bool comparison = descriptor.Compare != CompareFunction.Undefined;
        bool anisotropic = descriptor.MaxAnisotropy > 1;
        int filter = (descriptor.MipmapFilter == MipmapFilterMode.Linear ? 0x1 : 0) | (descriptor.MagFilter == FilterMode.Linear ? 0x4 : 0)
            | (descriptor.MinFilter == FilterMode.Linear ? 0x10 : 0) | (anisotropic ? 0x55 : 0) | (comparison ? 0x80 : 0);
        var description = new SamplerDescription((Filter)filter, Address(descriptor.AddressModeU), Address(descriptor.AddressModeV),
            Address(descriptor.AddressModeW), 0, anisotropic ? descriptor.MaxAnisotropy : 1u,
            comparison ? D3D12Pipeline.Compare(descriptor.Compare) : ComparisonFunction.Never, new Color4(0, 0, 0, 0), descriptor.LodMinClamp, descriptor.LodMaxClamp);
        Index = device.Heaps.Sampler(description);
        Comparison = comparison;
    }

    /// <summary>Where it is in the sampler heap.</summary>
    internal uint Index { get; }

    internal bool Comparison { get; }

    private static TextureAddressMode Address(AddressMode mode) => mode switch
    {
        AddressMode.Repeat => TextureAddressMode.Wrap,
        AddressMode.MirrorRepeat => TextureAddressMode.Mirror,
        _ => TextureAddressMode.Clamp,
    };

    // Kept in the sampler heap with every one like it for the device's life.
    private protected override void Release()
    {
    }
}

/// <summary>What kind of binding a layout entry is, from which of its four layouts is set.</summary>
internal enum BindingKind
{
    Uniform,
    DynamicUniform,
    ReadOnlyStorage,
    Storage,
    Sampler,
    Texture,
    StorageTexture,
}

public sealed class BindGroupLayout : GpuObject
{
    internal BindGroupLayout(in BindGroupLayoutDescriptor descriptor)
    {
        Entries = [.. (descriptor.Entries ?? []).OrderBy(entry => entry.Binding)];
        Kinds = [.. Entries.Select(Kind)];
        var slots = new List<(uint Binding, uint Offset, uint Count, BindingKind Kind)>();
        uint offset = 0;
        for (int i = 0; i < Entries.Length; i++)
        {
            BindingKind kind = Kinds[i];
            if (kind is BindingKind.DynamicUniform or BindingKind.Sampler)
                continue;
            uint count = Math.Max(1, Entries[i].BindingArraySize);
            slots.Add((Entries[i].Binding, offset, count, kind));
            offset += count;
        }
        Slots = [.. slots];
        uint[] samplers = [.. Entries.Where((_, i) => Kinds[i] == BindingKind.Sampler).Select(entry => entry.Binding)];
        SamplerSlots = samplers.Length == 0 ? 0 : samplers.Max() + 1;
        // The sampler index list's view last.
        Size = offset + (SamplerSlots > 0 ? 1u : 0u);
        DynamicUniforms = [.. Entries.Where((_, i) => Kinds[i] == BindingKind.DynamicUniform).Select(entry => entry.Binding)];
    }

    internal BindGroupLayoutEntry[] Entries { get; }

    internal BindingKind[] Kinds { get; }

    /// <summary>Each binding in the group's descriptor table: where it starts, how many descriptors it takes.</summary>
    internal (uint Binding, uint Offset, uint Count, BindingKind Kind)[] Slots { get; }

    /// <summary>How long the group's list of sampler heap indices is: one past its last sampler binding (none, zero).</summary>
    internal uint SamplerSlots { get; }

    /// <summary>The descriptors the group's table takes.</summary>
    internal uint Size { get; }

    /// <summary>The bindings of its uniform buffers with dynamic offsets, in the order their offsets are given.</summary>
    internal uint[] DynamicUniforms { get; }

    private static BindingKind Kind(BindGroupLayoutEntry entry)
    {
        if (entry.Buffer.Type != BufferBindingType.BindingNotUsed)
            return entry.Buffer.Type switch
            {
                BufferBindingType.Storage => BindingKind.Storage,
                BufferBindingType.ReadOnlyStorage => BindingKind.ReadOnlyStorage,
                _ => entry.Buffer.HasDynamicOffset ? BindingKind.DynamicUniform : BindingKind.Uniform,
            };
        if (entry.Sampler.Type != SamplerBindingType.BindingNotUsed)
            return BindingKind.Sampler;
        if (entry.Texture.SampleType != TextureSampleType.BindingNotUsed)
            return BindingKind.Texture;
        if (entry.StorageTexture.Access != StorageTextureAccess.BindingNotUsed)
            return BindingKind.StorageTexture;
        throw new GpuException($"Binding {entry.Binding} of a bind group layout binds nothing.");
    }

    private protected override void Release()
    {
    }
}

/// <summary>A bind group's descriptors, freed once the GPU is done with them.</summary>
internal sealed class BindGroupLife(ShaderHeaps heaps, VirtualAllocation range) : D3D12Life
{
    private protected override void Free() => heaps.Give(range);
}

public sealed class BindGroup : GpuObject
{
    private readonly Device device;
    private readonly BindGroupLife? life;

    internal BindGroup(Device device, in BindGroupDescriptor descriptor)
    {
        this.device = device;
        BindGroupLayout layout = Layout = descriptor.Layout;
        Dictionary<uint, BindGroupEntry> entries = [];
        foreach (BindGroupEntry entry in descriptor.Entries ?? [])
            entries[entry.Binding] = entry;
        ShaderHeaps heaps = device.Heaps;
        var buffers = new List<(BufferLife Buffer, ResourceStates State, bool Write)>();
        var textures = new List<(TextureView View, ResourceStates State)>();
        if (layout.Size > 0)
        {
            (First, VirtualAllocation range) = heaps.Take(layout.Size);
            life = new BindGroupLife(heaps, range);
        }
        foreach ((uint binding, uint offset, uint count, BindingKind kind) in layout.Slots)
        {
            if (!entries.TryGetValue(binding, out BindGroupEntry entry))
                throw new GpuException($"A bind group has nothing at binding {binding}.");
            CpuDescriptorHandle handle = heaps.View(First + offset);
            switch (kind)
            {
                case BindingKind.Uniform:
                {
                    Buffer buffer = entry.Buffer ?? throw new GpuException($"Binding {binding} takes a buffer.");
                    ulong size = entry.Size ?? buffer.GetSize() - entry.Offset;
                    device.Native.CreateConstantBufferView(new ConstantBufferViewDescription(buffer.Life.Resource.GPUVirtualAddress + entry.Offset,
                        (uint)Queue.Align(size, D3D12.ConstantBufferDataPlacementAlignment)), handle);
                    buffers.Add((buffer.Life, ResourceStates.VertexAndConstantBuffer, false));
                    break;
                }
                case BindingKind.ReadOnlyStorage or BindingKind.Storage:
                {
                    Buffer buffer = entry.Buffer ?? throw new GpuException($"Binding {binding} takes a buffer.");
                    ulong size = entry.Size ?? buffer.GetSize() - entry.Offset;
                    uint words = (uint)((size + 3) / 4);
                    if (kind == BindingKind.Storage)
                    {
                        device.Native.CreateUnorderedAccessView(buffer.Life.Resource, null, new UnorderedAccessViewDescription
                        {
                            Format = DxgiFormat.R32_Typeless,
                            ViewDimension = UnorderedAccessViewDimension.Buffer,
                            Buffer = new BufferUnorderedAccessView { FirstElement = entry.Offset / 4, NumElements = words, Flags = BufferUnorderedAccessViewFlags.Raw },
                        }, handle);
                        buffers.Add((buffer.Life, ResourceStates.UnorderedAccess, true));
                    }
                    else
                    {
                        device.Native.CreateShaderResourceView(buffer.Life.Resource, new ShaderResourceViewDescription
                        {
                            Format = DxgiFormat.R32_Typeless,
                            ViewDimension = ShaderResourceViewDimension.Buffer,
                            Shader4ComponentMapping = D3D12Views.DefaultMapping,
                            Buffer = new BufferShaderResourceView { FirstElement = entry.Offset / 4, NumElements = words, Flags = BufferShaderResourceViewFlags.Raw },
                        }, handle);
                        buffers.Add((buffer.Life, ResourceStates.NonPixelShaderResource | ResourceStates.PixelShaderResource, false));
                    }
                    break;
                }
                case BindingKind.Texture:
                {
                    TextureView view = entry.TextureView ?? throw new GpuException($"Binding {binding} takes a texture view.");
                    device.Native.CreateShaderResourceView(view.Texture.Life.Resource, D3D12Views.Read(view), handle);
                    textures.Add((view, ResourceStates.NonPixelShaderResource | ResourceStates.PixelShaderResource));
                    break;
                }
                case BindingKind.StorageTexture:
                {
                    TextureView view = entry.TextureView ?? throw new GpuException($"Binding {binding} takes a texture view.");
                    device.Native.CreateUnorderedAccessView(view.Texture.Life.Resource, null, D3D12Views.Write(view), handle);
                    textures.Add((view, ResourceStates.UnorderedAccess));
                    break;
                }
            }
        }
        if (layout.SamplerSlots > 0)
        {
            Span<uint> indices = stackalloc uint[(int)layout.SamplerSlots];
            for (int i = 0; i < layout.Entries.Length; i++)
                if (layout.Kinds[i] == BindingKind.Sampler)
                {
                    uint binding = layout.Entries[i].Binding;
                    Sampler sampler = entries.TryGetValue(binding, out BindGroupEntry entry) && entry.Sampler is { } given
                        ? given : throw new GpuException($"Binding {binding} takes a sampler.");
                    indices[(int)binding] = sampler.Index;
                }
            uint first = heaps.IndexList(indices);
            device.Native.CreateShaderResourceView(heaps.Indices.Resource, new ShaderResourceViewDescription
            {
                Format = DxgiFormat.Unknown,
                ViewDimension = ShaderResourceViewDimension.Buffer,
                Shader4ComponentMapping = D3D12Views.DefaultMapping,
                Buffer = new BufferShaderResourceView { FirstElement = first, NumElements = layout.SamplerSlots, StructureByteStride = 4 },
            }, heaps.View(First + layout.Size - 1));
        }
        Dynamic = new (BufferLife, ulong, ulong)[layout.DynamicUniforms.Length];
        for (int i = 0; i < Dynamic.Length; i++)
        {
            uint binding = layout.DynamicUniforms[i];
            if (!entries.TryGetValue(binding, out BindGroupEntry entry) || entry.Buffer is not { } buffer)
                throw new GpuException($"Binding {binding} takes a buffer.");
            Dynamic[i] = (buffer.Life, entry.Offset, entry.Size ?? buffer.GetSize() - entry.Offset);
            buffers.Add((buffer.Life, ResourceStates.VertexAndConstantBuffer, false));
        }
        Buffers = [.. buffers];
        Textures = [.. textures];
    }

    internal BindGroupLayout Layout { get; }

    /// <summary>Where its descriptor table starts in the shader-visible heap.</summary>
    internal uint First { get; }

    internal BindGroupLife? Life => life;

    /// <summary>The buffers and textures its shaders use, and the states they use them in.</summary>
    internal (BufferLife Buffer, ResourceStates State, bool Write)[] Buffers { get; }

    internal (TextureView View, ResourceStates State)[] Textures { get; }

    /// <summary>Its uniform buffers with dynamic offsets: where each is bound before the offset is added, and how much of it.</summary>
    internal (BufferLife Buffer, ulong Offset, ulong Size)[] Dynamic { get; }

    private protected override void Release()
    {
        if (life is not null)
            device.Retire(life);
    }
}

public sealed class PipelineLayout : GpuObject
{
    private readonly Device device;

    internal PipelineLayout(Device device, in PipelineLayoutDescriptor descriptor)
    {
        this.device = device;
        Groups = descriptor.BindGroupLayouts ?? [];
        var parameters = new List<RootParameter1>
        {
            // Naga's special constants.
            new(new RootConstants(0, 0, 3), ShaderVisibility.All),
        };
        Tables = new int[Groups.Length];
        DynamicRoots = new int[Groups.Length][];
        bool sampled = false;
        for (int group = 0; group < Groups.Length; group++)
        {
            BindGroupLayout layout = Groups[group];
            uint space = (uint)group + 1;
            Tables[group] = -1;
            if (layout.Size > 0)
            {
                var ranges = new List<DescriptorRange1>();
                foreach ((uint binding, uint offset, uint count, BindingKind kind) in layout.Slots)
                    ranges.Add(new DescriptorRange1(kind switch
                    {
                        BindingKind.Uniform => DescriptorRangeType.ConstantBufferView,
                        BindingKind.Storage or BindingKind.StorageTexture => DescriptorRangeType.UnorderedAccessView,
                        _ => DescriptorRangeType.ShaderResourceView,
                    }, count, binding, space, offset, DescriptorRangeFlags.DescriptorsVolatile | DescriptorRangeFlags.DataVolatile));
                if (layout.SamplerSlots > 0)
                {
                    ranges.Add(new DescriptorRange1(DescriptorRangeType.ShaderResourceView, 1, ShaderHeaps.SamplerIndexRegister, space, layout.Size - 1,
                        DescriptorRangeFlags.DescriptorsVolatile | DescriptorRangeFlags.DataVolatile));
                    sampled = true;
                }
                Tables[group] = parameters.Count;
                parameters.Add(new RootParameter1(new RootDescriptorTable1([.. ranges]), ShaderVisibility.All));
            }
            DynamicRoots[group] = new int[layout.DynamicUniforms.Length];
            for (int i = 0; i < layout.DynamicUniforms.Length; i++)
            {
                DynamicRoots[group][i] = parameters.Count;
                parameters.Add(new RootParameter1(RootParameterType.ConstantBufferView,
                    new RootDescriptor1(layout.DynamicUniforms[i], space, RootDescriptorFlags.DataVolatile), ShaderVisibility.All));
            }
        }
        SamplerTable = -1;
        if (sampled)
        {
            // The whole sampler heap, twice: as samplers and as comparison samplers.
            SamplerTable = parameters.Count;
            parameters.Add(new RootParameter1(new RootDescriptorTable1(
                new DescriptorRange1(DescriptorRangeType.Sampler, ShaderHeaps.SamplerCount, 0, 0, 0, DescriptorRangeFlags.DescriptorsVolatile),
                new DescriptorRange1(DescriptorRangeType.Sampler, ShaderHeaps.SamplerCount, ShaderHeaps.SamplerCount, 0, 0, DescriptorRangeFlags.DescriptorsVolatile)),
                ShaderVisibility.All));
        }
        Native = device.Native.CreateRootSignature(new RootSignatureDescription1(RootSignatureFlags.AllowInputAssemblerInputLayout, [.. parameters]));
        if (descriptor.Label is { } label)
            Native.Name = label;
        Life = new ComLife(Native);
    }

    /// <summary>The root signature, for command lists to hold until the GPU is done with it.</summary>
    internal ComLife Life { get; }

    internal ID3D12RootSignature Native { get; }

    internal BindGroupLayout[] Groups { get; }

    /// <summary>Each group's descriptor table's root parameter (-1 for none).</summary>
    internal int[] Tables { get; }

    /// <summary>Each group's dynamic uniform buffers' root parameters.</summary>
    internal int[][] DynamicRoots { get; }

    /// <summary>The sampler heap's root parameter (-1 when no group has samplers).</summary>
    internal int SamplerTable { get; }

    /// <summary>The special constants' root parameter.</summary>
    internal static int Constants => 0;

    private protected override void Release() => device.Retire(Life);
}

/// <summary>The shaders compiled when the app was built (tools/ShaderCompiler), found by a hash of their WGSL.</summary>
internal static class ShaderPack
{
    private static readonly Lazy<Dictionary<ulong, PackedModule>> Modules = new(Load);

    internal sealed class PackedModule(string name)
    {
        internal string Name { get; } = name;

        /// <summary>Each entry point's DXIL: by its name and, for a vertex one, the fragment one it was compiled with ("" alone).</summary>
        internal Dictionary<(string Entry, string Fragment), byte[]> Entries { get; } = [];

        /// <summary>Each entry point's stage: 0 vertex, 1 fragment, 2 compute.</summary>
        internal Dictionary<string, byte> Stages { get; } = [];
    }

    internal static PackedModule? Find(string code)
    {
        ulong hash = 0xcbf2_9ce4_8422_2325;
        foreach (byte value in Encoding.UTF8.GetBytes(code))
        {
            hash ^= value;
            hash *= 0x0100_0000_01b3;
        }
        return Modules.Value.GetValueOrDefault(hash);
    }

    private static Dictionary<ulong, PackedModule> Load()
    {
        var modules = new Dictionary<ulong, PackedModule>();
        // The app's, and those of the assemblies loaded with it (the tests' own modules).
        foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic)
                continue;
            foreach (string name in assembly.GetManifestResourceNames())
                if (name.EndsWith(".Shaders.dxil.pack", StringComparison.Ordinal))
                {
                    using Stream stream = assembly.GetManifestResourceStream(name)!;
                    Read(stream, modules);
                }
        }
        return modules;
    }

    private static void Read(Stream stream, Dictionary<ulong, PackedModule> modules)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8);
        if (reader.ReadUInt32() != 0x58444848 || reader.ReadUInt32() != 1)
            throw new GpuException("The app's compiled shaders are of another version.");
        uint count = reader.ReadUInt32();
        for (uint i = 0; i < count; i++)
        {
            ulong hash = reader.ReadUInt64();
            var module = new PackedModule(Text(reader));
            uint entries = reader.ReadUInt32();
            for (uint e = 0; e < entries; e++)
            {
                byte stage = reader.ReadByte();
                string entry = Text(reader), fragment = Text(reader);
                module.Stages[entry] = stage;
                module.Entries[(entry, fragment)] = reader.ReadBytes(reader.ReadInt32());
            }
            modules[hash] = module;
        }

        static string Text(BinaryReader reader) => Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadUInt16()));
    }
}

public sealed class ShaderModule : GpuObject
{
    internal ShaderModule(Device device, in ShaderModuleWGSLDescriptor descriptor)
    {
        Label = descriptor.Label ?? "a shader module";
        Packed = ShaderPack.Find(descriptor.Code ?? "");
        if (Packed is null)
            device.Error(ErrorType.Validation, $"{Label} wasn't compiled when the app was built: Direct3D 12 draws only with the modules in Shaders/modules.txt.");
    }

    internal string Label { get; }

    internal ShaderPack.PackedModule? Packed { get; }

    /// <summary>An entry point's DXIL: for a vertex one, the build made with the given fragment entry point if there is one.</summary>
    internal byte[]? Find(byte stage, string? entry, string? fragment = null)
    {
        if (Packed is null)
            return null;
        if (entry is null)
        {
            // WebGPU's default: the module's one entry point of the stage.
            string[] ofStage = [.. Packed.Stages.Where(pair => pair.Value == stage).Select(pair => pair.Key)];
            if (ofStage.Length != 1)
                return null;
            entry = ofStage[0];
        }
        return fragment is not null && Packed.Entries.TryGetValue((entry, fragment), out byte[]? paired) ? paired
            : Packed.Entries.GetValueOrDefault((entry, ""));
    }

    private protected override void Release()
    {
    }
}

/// <summary>How WebGPU's pipeline state reads in Direct3D 12's terms.</summary>
internal static class D3D12Pipeline
{
    internal static ComparisonFunction Compare(CompareFunction compare) => compare switch
    {
        CompareFunction.Never => ComparisonFunction.Never,
        CompareFunction.Less => ComparisonFunction.Less,
        CompareFunction.Equal => ComparisonFunction.Equal,
        CompareFunction.LessEqual => ComparisonFunction.LessEqual,
        CompareFunction.Greater => ComparisonFunction.Greater,
        CompareFunction.NotEqual => ComparisonFunction.NotEqual,
        CompareFunction.GreaterEqual => ComparisonFunction.GreaterEqual,
        _ => ComparisonFunction.Always,
    };

    internal static D3DBlend Blend(BlendFactor factor, bool alpha) => factor switch
    {
        BlendFactor.Zero => D3DBlend.Zero,
        BlendFactor.Src => alpha ? D3DBlend.SourceAlpha : D3DBlend.SourceColor,
        BlendFactor.OneMinusSrc => alpha ? D3DBlend.InverseSourceAlpha : D3DBlend.InverseSourceColor,
        BlendFactor.SrcAlpha => D3DBlend.SourceAlpha,
        BlendFactor.OneMinusSrcAlpha => D3DBlend.InverseSourceAlpha,
        BlendFactor.Dst => alpha ? D3DBlend.DestinationAlpha : D3DBlend.DestinationColor,
        BlendFactor.OneMinusDst => alpha ? D3DBlend.InverseDestinationAlpha : D3DBlend.InverseDestinationColor,
        BlendFactor.DstAlpha => D3DBlend.DestinationAlpha,
        BlendFactor.OneMinusDstAlpha => D3DBlend.InverseDestinationAlpha,
        BlendFactor.SrcAlphaSaturated => D3DBlend.SourceAlphaSaturate,
        BlendFactor.Constant => alpha ? D3DBlend.AlphaFactor : D3DBlend.BlendFactor,
        BlendFactor.OneMinusConstant => alpha ? D3DBlend.InverseAlphaFactor : D3DBlend.InverseBlendFactor,
        BlendFactor.Src1 => alpha ? D3DBlend.Source1Alpha : D3DBlend.Source1Color,
        BlendFactor.OneMinusSrc1 => alpha ? D3DBlend.InverseSource1Alpha : D3DBlend.InverseSource1Color,
        BlendFactor.Src1Alpha => D3DBlend.Source1Alpha,
        BlendFactor.OneMinusSrc1Alpha => D3DBlend.InverseSource1Alpha,
        _ => D3DBlend.One,
    };

    internal static D3DBlendOperation Operation(BlendOperation operation) => operation switch
    {
        BlendOperation.Subtract => D3DBlendOperation.Subtract,
        BlendOperation.ReverseSubtract => D3DBlendOperation.RevSubtract,
        BlendOperation.Min => D3DBlendOperation.Min,
        BlendOperation.Max => D3DBlendOperation.Max,
        _ => D3DBlendOperation.Add,
    };

    internal static D3DStencilOperation Stencil(StencilOperation operation) => operation switch
    {
        StencilOperation.Zero => D3DStencilOperation.Zero,
        StencilOperation.Replace => D3DStencilOperation.Replace,
        StencilOperation.Invert => D3DStencilOperation.Invert,
        StencilOperation.IncrementClamp => D3DStencilOperation.IncrementSaturate,
        StencilOperation.DecrementClamp => D3DStencilOperation.DecrementSaturate,
        StencilOperation.IncrementWrap => D3DStencilOperation.Increment,
        StencilOperation.DecrementWrap => D3DStencilOperation.Decrement,
        _ => D3DStencilOperation.Keep,
    };

    internal static DxgiFormat Vertex(VertexFormat format) => format switch
    {
        VertexFormat.Uint8 => DxgiFormat.R8_UInt,
        VertexFormat.Uint8x2 => DxgiFormat.R8G8_UInt,
        VertexFormat.Uint8x4 => DxgiFormat.R8G8B8A8_UInt,
        VertexFormat.Sint8 => DxgiFormat.R8_SInt,
        VertexFormat.Sint8x2 => DxgiFormat.R8G8_SInt,
        VertexFormat.Sint8x4 => DxgiFormat.R8G8B8A8_SInt,
        VertexFormat.Unorm8 => DxgiFormat.R8_UNorm,
        VertexFormat.Unorm8x2 => DxgiFormat.R8G8_UNorm,
        VertexFormat.Unorm8x4 => DxgiFormat.R8G8B8A8_UNorm,
        VertexFormat.Snorm8 => DxgiFormat.R8_SNorm,
        VertexFormat.Snorm8x2 => DxgiFormat.R8G8_SNorm,
        VertexFormat.Snorm8x4 => DxgiFormat.R8G8B8A8_SNorm,
        VertexFormat.Uint16 => DxgiFormat.R16_UInt,
        VertexFormat.Uint16x2 => DxgiFormat.R16G16_UInt,
        VertexFormat.Uint16x4 => DxgiFormat.R16G16B16A16_UInt,
        VertexFormat.Sint16 => DxgiFormat.R16_SInt,
        VertexFormat.Sint16x2 => DxgiFormat.R16G16_SInt,
        VertexFormat.Sint16x4 => DxgiFormat.R16G16B16A16_SInt,
        VertexFormat.Unorm16 => DxgiFormat.R16_UNorm,
        VertexFormat.Unorm16x2 => DxgiFormat.R16G16_UNorm,
        VertexFormat.Unorm16x4 => DxgiFormat.R16G16B16A16_UNorm,
        VertexFormat.Snorm16 => DxgiFormat.R16_SNorm,
        VertexFormat.Snorm16x2 => DxgiFormat.R16G16_SNorm,
        VertexFormat.Snorm16x4 => DxgiFormat.R16G16B16A16_SNorm,
        VertexFormat.Float16 => DxgiFormat.R16_Float,
        VertexFormat.Float16x2 => DxgiFormat.R16G16_Float,
        VertexFormat.Float16x4 => DxgiFormat.R16G16B16A16_Float,
        VertexFormat.Float32 => DxgiFormat.R32_Float,
        VertexFormat.Float32x2 => DxgiFormat.R32G32_Float,
        VertexFormat.Float32x3 => DxgiFormat.R32G32B32_Float,
        VertexFormat.Float32x4 => DxgiFormat.R32G32B32A32_Float,
        VertexFormat.Uint32 => DxgiFormat.R32_UInt,
        VertexFormat.Uint32x2 => DxgiFormat.R32G32_UInt,
        VertexFormat.Uint32x3 => DxgiFormat.R32G32B32_UInt,
        VertexFormat.Uint32x4 => DxgiFormat.R32G32B32A32_UInt,
        VertexFormat.Sint32 => DxgiFormat.R32_SInt,
        VertexFormat.Sint32x2 => DxgiFormat.R32G32_SInt,
        VertexFormat.Sint32x3 => DxgiFormat.R32G32B32_SInt,
        VertexFormat.Sint32x4 => DxgiFormat.R32G32B32A32_SInt,
        VertexFormat.Unorm10_10_10_2 => DxgiFormat.R10G10B10A2_UNorm,
        VertexFormat.Unorm8x4BGRA => DxgiFormat.B8G8R8A8_UNorm,
        _ => throw new NotSupportedException($"Direct3D 12 has no vertex format for {format}."),
    };
}

public sealed class RenderPipeline : GpuObject
{
    private readonly Device device;

    internal RenderPipeline(Device device, in RenderPipelineDescriptor descriptor)
    {
        this.device = device;
        Layout = descriptor.Layout ?? throw new GpuException("A render pipeline needs a layout.");
        VertexState vertex = descriptor.Vertex;
        FragmentState? fragment = descriptor.Fragment;
        // The vertex stage as compiled with its fragment stage, when both are of one module.
        string? pairedWith = fragment is { } paired && paired.Module == vertex.Module ? paired.EntryPoint : null;
        byte[]? vertexCode = vertex.Module.Find(0, vertex.EntryPoint, pairedWith);
        byte[]? fragmentCode = fragment is { } f ? f.Module.Find(1, f.EntryPoint) : null;
        string label = descriptor.Label ?? $"{vertex.Module.Label} {vertex.EntryPoint}";
        if (vertexCode is null || (fragment is not null && fragmentCode is null))
        {
            device.Error(ErrorType.Validation, $"The pipeline {label} has no compiled shader for {(vertexCode is null ? vertex.EntryPoint : fragment?.EntryPoint)}.");
            return;
        }
        VertexBufferLayout[] buffers = vertex.Buffers ?? [];
        Strides = [.. buffers.Select(buffer => (uint)buffer.ArrayStride)];
        var elements = new List<InputElementDescription>();
        for (int slot = 0; slot < buffers.Length; slot++)
        {
            bool instanced = buffers[slot].StepMode == VertexStepMode.Instance;
            foreach (VertexAttribute attribute in buffers[slot].Attributes ?? [])
                elements.Add(new InputElementDescription("LOC", attribute.ShaderLocation, D3D12Pipeline.Vertex(attribute.Format), (uint)attribute.Offset,
                    (uint)slot, instanced ? InputClassification.PerInstanceData : InputClassification.PerVertexData, instanced ? 1u : 0u));
        }
        ColorTargetState[] targets = fragment?.Targets ?? [];
        var blend = new BlendDescription { AlphaToCoverageEnable = descriptor.Multisample.AlphaToCoverageEnabled, IndependentBlendEnable = true };
        for (int i = 0; i < targets.Length && i < 8; i++)
        {
            BlendState? state = targets[i].Blend;
            blend.RenderTarget[i] = new RenderTargetBlendDescription
            {
                BlendEnable = state is not null,
                SourceBlend = state is { } s1 ? D3D12Pipeline.Blend(s1.Color.SrcFactor, false) : D3DBlend.One,
                DestinationBlend = state is { } s2 ? D3D12Pipeline.Blend(s2.Color.DstFactor, false) : D3DBlend.Zero,
                BlendOperation = state is { } s3 ? D3D12Pipeline.Operation(s3.Color.Operation) : D3DBlendOperation.Add,
                SourceBlendAlpha = state is { } s4 ? D3D12Pipeline.Blend(s4.Alpha.SrcFactor, true) : D3DBlend.One,
                DestinationBlendAlpha = state is { } s5 ? D3D12Pipeline.Blend(s5.Alpha.DstFactor, true) : D3DBlend.Zero,
                BlendOperationAlpha = state is { } s6 ? D3D12Pipeline.Operation(s6.Alpha.Operation) : D3DBlendOperation.Add,
                LogicOp = LogicOp.Noop,
                RenderTargetWriteMask = (ColorWriteEnable)(int)targets[i].WriteMask,
            };
        }
        DepthStencilState? depth = descriptor.DepthStencil;
        PrimitiveState primitive = descriptor.Primitive;
        var raster = new RasterizerDescription(primitive.CullMode switch
        {
            CullMode.Front => D3DCullMode.Front,
            CullMode.Back => D3DCullMode.Back,
            _ => D3DCullMode.None,
        }, FillMode.Solid, primitive.FrontFace != FrontFace.CW, depth?.DepthBias ?? 0, depth?.DepthBiasClamp ?? 0, depth?.DepthBiasSlopeScale ?? 0,
            !primitive.UnclippedDepth);
        DepthStencilDescription depthState = new() { DepthEnable = false, DepthWriteMask = DepthWriteMask.Zero, DepthFunc = ComparisonFunction.Always };
        if (depth is { } d)
        {
            static DepthStencilOperationDescription Face(StencilFaceState face) => new(D3D12Pipeline.Stencil(face.FailOp),
                D3D12Pipeline.Stencil(face.DepthFailOp), D3D12Pipeline.Stencil(face.PassOp), D3D12Pipeline.Compare(face.Compare));
            static bool Idle(StencilFaceState face) => face.Compare is CompareFunction.Always or CompareFunction.Undefined
                && face.FailOp is StencilOperation.Keep or StencilOperation.Undefined && face.DepthFailOp is StencilOperation.Keep or StencilOperation.Undefined
                && face.PassOp is StencilOperation.Keep or StencilOperation.Undefined;
            depthState = new DepthStencilDescription
            {
                DepthEnable = true,
                DepthWriteMask = d.DepthWriteEnabled == OptionalBool.True ? DepthWriteMask.All : DepthWriteMask.Zero,
                DepthFunc = D3D12Pipeline.Compare(d.DepthCompare),
                StencilEnable = D3D12Formats.HasStencil(d.Format) && !(Idle(d.StencilFront) && Idle(d.StencilBack)),
                StencilReadMask = (byte)d.StencilReadMask,
                StencilWriteMask = (byte)d.StencilWriteMask,
                FrontFace = Face(d.StencilFront),
                BackFace = Face(d.StencilBack),
            };
        }
        Topology = primitive.Topology switch
        {
            PrimitiveTopology.PointList => D3DTopology.PointList,
            PrimitiveTopology.LineList => D3DTopology.LineList,
            PrimitiveTopology.LineStrip => D3DTopology.LineStrip,
            PrimitiveTopology.TriangleStrip => D3DTopology.TriangleStrip,
            _ => D3DTopology.TriangleList,
        };
        bool strip = primitive.Topology is PrimitiveTopology.LineStrip or PrimitiveTopology.TriangleStrip;
        var description = new GraphicsPipelineStateDescription
        {
            RootSignature = Layout.Native,
            VertexShader = vertexCode,
            PixelShader = fragmentCode ?? ReadOnlyMemory<byte>.Empty,
            BlendState = blend,
            SampleMask = descriptor.Multisample.Mask,
            RasterizerState = raster,
            DepthStencilState = depthState,
            InputLayout = elements.Count > 0 ? new InputLayoutDescription([.. elements]) : null,
            IndexBufferStripCutValue = !strip ? IndexBufferStripCutValue.Disabled
                : primitive.StripIndexFormat == IndexFormat.Uint16 ? IndexBufferStripCutValue.Value0xFFFF
                : primitive.StripIndexFormat == IndexFormat.Uint32 ? IndexBufferStripCutValue.Value0xFFFFFFFF
                : IndexBufferStripCutValue.Disabled,
            PrimitiveTopologyType = primitive.Topology switch
            {
                PrimitiveTopology.PointList => PrimitiveTopologyType.Point,
                PrimitiveTopology.LineList or PrimitiveTopology.LineStrip => PrimitiveTopologyType.Line,
                _ => PrimitiveTopologyType.Triangle,
            },
            RenderTargetFormats = [.. targets.Select(target => D3D12Formats.View(target.Format))],
            DepthStencilFormat = depth is { } ds ? D3D12Formats.View(ds.Format) : DxgiFormat.Unknown,
            SampleDescription = new SampleDescription(descriptor.Multisample.Count, 0),
        };
        try
        {
            State = device.Native.CreateGraphicsPipelineState(description);
            State.Name = label;
            Life = new ComLife(State);
        }
        catch (SharpGenException error)
        {
            device.Error(ErrorType.Validation, $"The pipeline {label} couldn't be made: {error.Message}");
        }
    }

    internal PipelineLayout Layout { get; }

    /// <summary>Its state; null if it couldn't be made (an error was reported).</summary>
    internal ID3D12PipelineState? State { get; }

    internal D3DTopology Topology { get; }

    /// <summary>Each vertex buffer slot's stride.</summary>
    internal uint[] Strides { get; } = [];

    public BindGroupLayout GetBindGroupLayout(uint index) => Layout.Groups[index];

    /// <summary>The pipeline state, for command lists to hold until the GPU is done with it.</summary>
    internal ComLife? Life { get; }

    private protected override void Release()
    {
        if (Life is not null)
            device.Retire(Life);
    }
}

public sealed class ComputePipeline : GpuObject
{
    private readonly Device device;

    internal ComputePipeline(Device device, in ComputePipelineDescriptor descriptor)
    {
        this.device = device;
        Layout = descriptor.Layout ?? throw new GpuException("A compute pipeline needs a layout.");
        ComputeState compute = descriptor.Compute;
        string label = descriptor.Label ?? $"{compute.Module.Label} {compute.EntryPoint}";
        byte[]? code = compute.Module.Find(2, compute.EntryPoint);
        if (code is null)
        {
            device.Error(ErrorType.Validation, $"The pipeline {label} has no compiled shader for {compute.EntryPoint}.");
            return;
        }
        try
        {
            State = device.Native.CreateComputePipelineState(new ComputePipelineStateDescription { RootSignature = Layout.Native, ComputeShader = code });
            State.Name = label;
            Life = new ComLife(State);
        }
        catch (SharpGenException error)
        {
            device.Error(ErrorType.Validation, $"The pipeline {label} couldn't be made: {error.Message}");
        }
    }

    internal PipelineLayout Layout { get; }

    internal ID3D12PipelineState? State { get; }

    public BindGroupLayout GetBindGroupLayout(uint index) => Layout.Groups[index];

    /// <summary>The pipeline state, for command lists to hold until the GPU is done with it.</summary>
    internal ComLife? Life { get; }

    private protected override void Release()
    {
        if (Life is not null)
            device.Retire(Life);
    }
}
