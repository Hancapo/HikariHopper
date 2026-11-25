namespace HikariHopper.Gpu;

// What the renderer asks the GPU for, in managed form: arrays and strings, the defaults webgpu.h gives. Marshalled to
// wgpu's structs in one place (GpuObjects.cs) as each call is made.

public record struct Extent3D(uint Width, uint Height = 1, uint DepthOrArrayLayers = 1);

public record struct Origin3D(uint X = 0, uint Y = 0, uint Z = 0);

public record struct Color(double R, double G, double B, double A);

public struct BufferDescriptor
{
    public string? Label;
    public BufferUsage Usage;
    public ulong Size;
    public bool MappedAtCreation;
}

public struct TextureDescriptor
{
    public TextureDescriptor()
    {
    }

    public string? Label;
    public TextureUsage Usage;
    public TextureDimension Dimension = TextureDimension.D2;
    public Extent3D Size;
    public TextureFormat Format;
    public uint MipLevelCount = 1;
    public uint SampleCount = 1;
    public TextureFormat[]? ViewFormats;
}

public struct TextureViewDescriptor
{
    public TextureViewDescriptor()
    {
    }

    public string? Label;
    public TextureFormat Format;
    public TextureViewDimension Dimension;
    public uint BaseMipLevel;
    public uint? MipLevelCount;
    public uint BaseArrayLayer;
    public uint? ArrayLayerCount;
    public TextureAspect Aspect = TextureAspect.All;
}

public struct SamplerDescriptor
{
    public SamplerDescriptor()
    {
    }

    public string? Label;
    public AddressMode AddressModeU = AddressMode.ClampToEdge;
    public AddressMode AddressModeV = AddressMode.ClampToEdge;
    public AddressMode AddressModeW = AddressMode.ClampToEdge;
    public FilterMode MagFilter = FilterMode.Nearest;
    public FilterMode MinFilter = FilterMode.Nearest;
    public MipmapFilterMode MipmapFilter = MipmapFilterMode.Nearest;
    public float LodMinClamp;
    public float LodMaxClamp = 32;
    public CompareFunction Compare;
    public ushort MaxAnisotropy = 1;
}

public struct BindGroupEntry
{
    public uint Binding;
    public Buffer? Buffer;
    public ulong Offset;
    /// <summary>The bytes bound; the rest of the buffer from the offset when null.</summary>
    public ulong? Size;
    public Sampler? Sampler;
    public TextureView? TextureView;
}

public struct BindGroupDescriptor
{
    public string? Label;
    public BindGroupLayout Layout;
    public BindGroupEntry[] Entries;
}

public struct BufferBindingLayout
{
    public BufferBindingLayout()
    {
    }

    public BufferBindingType Type = BufferBindingType.Uniform;
    public bool HasDynamicOffset;
    public ulong MinBindingSize;
}

public struct SamplerBindingLayout
{
    public SamplerBindingLayout()
    {
    }

    public SamplerBindingType Type = SamplerBindingType.Filtering;
}

public struct TextureBindingLayout
{
    public TextureBindingLayout()
    {
    }

    public TextureSampleType SampleType = TextureSampleType.Float;
    public TextureViewDimension ViewDimension = TextureViewDimension.D2;
    public bool Multisampled;
}

public struct StorageTextureBindingLayout
{
    public StorageTextureBindingLayout()
    {
    }

    public StorageTextureAccess Access = StorageTextureAccess.WriteOnly;
    public TextureFormat Format;
    public TextureViewDimension ViewDimension = TextureViewDimension.D2;
}

/// <summary>One binding of a layout: the kind of binding is the one of the four layouts given (the others left default).</summary>
public struct BindGroupLayoutEntry
{
    public uint Binding;
    public ShaderStage Visibility;
    public uint BindingArraySize;
    public BufferBindingLayout Buffer;
    public SamplerBindingLayout Sampler;
    public TextureBindingLayout Texture;
    public StorageTextureBindingLayout StorageTexture;
}

public struct BindGroupLayoutDescriptor
{
    public string? Label;
    public BindGroupLayoutEntry[] Entries;
}

public struct PipelineLayoutDescriptor
{
    public string? Label;
    public BindGroupLayout[] BindGroupLayouts;
    public uint ImmediateSize;
}

public struct ShaderModuleWGSLDescriptor
{
    public string? Label;
    public string Code;
}

public struct ConstantEntry
{
    public string Key;
    public double Value;
}

public struct VertexAttribute
{
    public VertexFormat Format;
    public ulong Offset;
    public uint ShaderLocation;
}

public struct VertexBufferLayout
{
    public VertexBufferLayout()
    {
    }

    public ulong ArrayStride;
    public VertexStepMode StepMode = VertexStepMode.Vertex;
    public VertexAttribute[] Attributes = [];
}

public struct VertexState
{
    public ShaderModule Module;
    public string? EntryPoint;
    public ConstantEntry[]? Constants;
    public VertexBufferLayout[]? Buffers;
}

public struct BlendComponent
{
    public BlendComponent()
    {
    }

    public BlendOperation Operation = BlendOperation.Add;
    public BlendFactor SrcFactor = BlendFactor.One;
    public BlendFactor DstFactor = BlendFactor.Zero;
}

public struct BlendState
{
    public BlendComponent Color;
    public BlendComponent Alpha;
}

public struct ColorTargetState
{
    public ColorTargetState()
    {
    }

    public TextureFormat Format;
    public BlendState? Blend;
    public ColorWriteMask WriteMask = ColorWriteMask.All;
}

public struct FragmentState
{
    public ShaderModule Module;
    public string? EntryPoint;
    public ConstantEntry[]? Constants;
    public ColorTargetState[] Targets;
}

public struct PrimitiveState
{
    public PrimitiveState()
    {
    }

    public PrimitiveTopology Topology = PrimitiveTopology.TriangleList;
    public IndexFormat StripIndexFormat;
    public FrontFace FrontFace = FrontFace.CCW;
    public CullMode CullMode = CullMode.None;
    public bool UnclippedDepth;
}

public struct StencilFaceState
{
    public StencilFaceState()
    {
    }

    public CompareFunction Compare = CompareFunction.Always;
    public StencilOperation FailOp = StencilOperation.Keep;
    public StencilOperation DepthFailOp = StencilOperation.Keep;
    public StencilOperation PassOp = StencilOperation.Keep;
}

public struct DepthStencilState
{
    public DepthStencilState()
    {
    }

    public TextureFormat Format;
    public OptionalBool DepthWriteEnabled = OptionalBool.False;
    public CompareFunction DepthCompare;
    public StencilFaceState StencilFront = new();
    public StencilFaceState StencilBack = new();
    public uint StencilReadMask = uint.MaxValue;
    public uint StencilWriteMask = uint.MaxValue;
    public int DepthBias;
    public float DepthBiasSlopeScale;
    public float DepthBiasClamp;
}

public struct MultisampleState
{
    public MultisampleState()
    {
    }

    public uint Count = 1;
    public uint Mask = uint.MaxValue;
    public bool AlphaToCoverageEnabled;
}

public struct RenderPipelineDescriptor
{
    public RenderPipelineDescriptor()
    {
    }

    public string? Label;
    public PipelineLayout? Layout;
    public VertexState Vertex;
    public PrimitiveState Primitive = new();
    public DepthStencilState? DepthStencil;
    public MultisampleState Multisample = new();
    public FragmentState? Fragment;
}

public struct ComputeState
{
    public ShaderModule Module;
    public string? EntryPoint;
    public ConstantEntry[]? Constants;
}

public struct ComputePipelineDescriptor
{
    public string? Label;
    public PipelineLayout? Layout;
    public ComputeState Compute;
}

public struct RenderPassColorAttachment
{
    public TextureView? View;
    public uint? DepthSlice;
    public TextureView? ResolveTarget;
    public Color ClearValue;
    public LoadOp LoadOp;
    public StoreOp StoreOp;
}

public struct RenderPassDepthStencilAttachment
{
    public TextureView View;
    public LoadOp DepthLoadOp;
    public StoreOp DepthStoreOp;
    public float DepthClearValue;
    public bool DepthReadOnly;
    public LoadOp StencilLoadOp;
    public StoreOp StencilStoreOp;
    public uint StencilClearValue;
    public bool StencilReadOnly;
}

public struct RenderPassDescriptor
{
    public string? Label;
    public RenderPassColorAttachment[] ColorAttachments;
    public RenderPassDepthStencilAttachment? DepthStencilAttachment;
}

public struct ComputePassDescriptor
{
    public string? Label;
}

public struct RenderBundleEncoderDescriptor
{
    public RenderBundleEncoderDescriptor()
    {
    }

    public string? Label;
    public TextureFormat[] ColorFormats = [];
    public TextureFormat DepthStencilFormat;
    public uint SampleCount = 1;
    public bool DepthReadOnly;
    public bool StencilReadOnly;
}

public struct TexelCopyBufferLayout
{
    public TexelCopyBufferLayout()
    {
    }

    public ulong Offset;
    public uint BytesPerRow = uint.MaxValue;
    public uint RowsPerImage = uint.MaxValue;
}

public struct TexelCopyBufferInfo
{
    public TexelCopyBufferLayout Layout;
    public Buffer Buffer;
}

public struct TexelCopyTextureInfo
{
    public TexelCopyTextureInfo()
    {
    }

    public Texture Texture = null!;
    public uint MipLevel;
    public Origin3D Origin;
    public TextureAspect Aspect = TextureAspect.All;
}

public struct SurfaceConfiguration
{
    public SurfaceConfiguration()
    {
    }

    public Device Device = null!;
    public TextureFormat Format;
    public TextureUsage Usage = TextureUsage.RenderAttachment;
    public TextureFormat[]? ViewFormats;
    public CompositeAlphaMode AlphaMode = CompositeAlphaMode.Auto;
    public uint Width;
    public uint Height;
    public PresentMode PresentMode = PresentMode.Fifo;
}

public struct RequestAdapterOptions
{
    public PowerPreference PowerPreference;
    public BackendType BackendType;
    public bool ForceFallbackAdapter;
    public Surface? CompatibleSurface;
}

public struct DeviceDescriptor
{
    public string? Label;
    public FeatureName[]? RequiredFeatures;
    /// <summary>Asks for everything the adapter allows, not WebGPU's defaults.</summary>
    public bool AdapterLimits;
    /// <summary>Called once if the device is lost, with why; not when it's released.</summary>
    public Action<DeviceLostReason, string>? DeviceLostCallback;
    /// <summary>An error no error scope caught.</summary>
    public Action<ErrorType, string>? UncapturedErrorCallback;
}

/// <summary>What the window's swap chain gave for a frame.</summary>
public readonly record struct SurfaceTexture(SurfaceGetCurrentTextureStatus Status, Texture? Texture);

public sealed record SurfaceCapabilities(TextureFormat[] Formats, PresentMode[] PresentModes, CompositeAlphaMode[] AlphaModes);

/// <summary>What the GPU allows, as the renderer reads it.</summary>
public struct Limits
{
    public uint MaxSampledTexturesPerShaderStage;
    public uint MaxSamplersPerShaderStage;
    public uint MaxStorageBuffersPerShaderStage;
    public uint MaxBindGroups;
    public ulong MaxBufferSize;
    public uint MaxTextureDimension2D;
}

public sealed record AdapterInfo(string Vendor, string Architecture, string Device, string Description, BackendType BackendType, uint VendorId, uint DeviceId);

/// <summary>A GPU call that failed, or a GPU object that couldn't be made.</summary>
public sealed class GpuException(string message) : Exception(message);
