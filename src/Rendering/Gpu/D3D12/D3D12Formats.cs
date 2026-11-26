using DxgiFormat = Vortice.DXGI.Format;

namespace HikariHopper.Gpu;

/// <summary>
/// WebGPU's texture formats as DXGI's: the format a texture is made with, the one its views read and write it as, and
/// how its texels are laid out in memory (blocks for the compressed formats).
/// </summary>
internal static class D3D12Formats
{
    /// <summary>The DXGI format a view reads or writes the texture as.</summary>
    internal static DxgiFormat View(TextureFormat format) => format switch
    {
        TextureFormat.R8Unorm => DxgiFormat.R8_UNorm,
        TextureFormat.R8Snorm => DxgiFormat.R8_SNorm,
        TextureFormat.R8Uint => DxgiFormat.R8_UInt,
        TextureFormat.R8Sint => DxgiFormat.R8_SInt,
        TextureFormat.R16Unorm => DxgiFormat.R16_UNorm,
        TextureFormat.R16Snorm => DxgiFormat.R16_SNorm,
        TextureFormat.R16Uint => DxgiFormat.R16_UInt,
        TextureFormat.R16Sint => DxgiFormat.R16_SInt,
        TextureFormat.R16Float => DxgiFormat.R16_Float,
        TextureFormat.RG8Unorm => DxgiFormat.R8G8_UNorm,
        TextureFormat.RG8Snorm => DxgiFormat.R8G8_SNorm,
        TextureFormat.RG8Uint => DxgiFormat.R8G8_UInt,
        TextureFormat.RG8Sint => DxgiFormat.R8G8_SInt,
        TextureFormat.R32Float => DxgiFormat.R32_Float,
        TextureFormat.R32Uint => DxgiFormat.R32_UInt,
        TextureFormat.R32Sint => DxgiFormat.R32_SInt,
        TextureFormat.RG16Unorm => DxgiFormat.R16G16_UNorm,
        TextureFormat.RG16Snorm => DxgiFormat.R16G16_SNorm,
        TextureFormat.RG16Uint => DxgiFormat.R16G16_UInt,
        TextureFormat.RG16Sint => DxgiFormat.R16G16_SInt,
        TextureFormat.RG16Float => DxgiFormat.R16G16_Float,
        TextureFormat.RGBA8Unorm => DxgiFormat.R8G8B8A8_UNorm,
        TextureFormat.RGBA8UnormSrgb => DxgiFormat.R8G8B8A8_UNorm_SRgb,
        TextureFormat.RGBA8Snorm => DxgiFormat.R8G8B8A8_SNorm,
        TextureFormat.RGBA8Uint => DxgiFormat.R8G8B8A8_UInt,
        TextureFormat.RGBA8Sint => DxgiFormat.R8G8B8A8_SInt,
        TextureFormat.BGRA8Unorm => DxgiFormat.B8G8R8A8_UNorm,
        TextureFormat.BGRA8UnormSrgb => DxgiFormat.B8G8R8A8_UNorm_SRgb,
        TextureFormat.RGB10A2Uint => DxgiFormat.R10G10B10A2_UInt,
        TextureFormat.RGB10A2Unorm => DxgiFormat.R10G10B10A2_UNorm,
        TextureFormat.RG11B10Ufloat => DxgiFormat.R11G11B10_Float,
        TextureFormat.RGB9E5Ufloat => DxgiFormat.R9G9B9E5_SharedExp,
        TextureFormat.RG32Float => DxgiFormat.R32G32_Float,
        TextureFormat.RG32Uint => DxgiFormat.R32G32_UInt,
        TextureFormat.RG32Sint => DxgiFormat.R32G32_SInt,
        TextureFormat.RGBA16Unorm => DxgiFormat.R16G16B16A16_UNorm,
        TextureFormat.RGBA16Snorm => DxgiFormat.R16G16B16A16_SNorm,
        TextureFormat.RGBA16Uint => DxgiFormat.R16G16B16A16_UInt,
        TextureFormat.RGBA16Sint => DxgiFormat.R16G16B16A16_SInt,
        TextureFormat.RGBA16Float => DxgiFormat.R16G16B16A16_Float,
        TextureFormat.RGBA32Float => DxgiFormat.R32G32B32A32_Float,
        TextureFormat.RGBA32Uint => DxgiFormat.R32G32B32A32_UInt,
        TextureFormat.RGBA32Sint => DxgiFormat.R32G32B32A32_SInt,
        TextureFormat.Depth16Unorm => DxgiFormat.D16_UNorm,
        TextureFormat.Depth24Plus => DxgiFormat.D32_Float,
        TextureFormat.Depth24PlusStencil8 => DxgiFormat.D24_UNorm_S8_UInt,
        TextureFormat.Depth32Float => DxgiFormat.D32_Float,
        TextureFormat.Depth32FloatStencil8 => DxgiFormat.D32_Float_S8X24_UInt,
        TextureFormat.Stencil8 => DxgiFormat.D24_UNorm_S8_UInt,
        TextureFormat.BC1RGBAUnorm => DxgiFormat.BC1_UNorm,
        TextureFormat.BC1RGBAUnormSrgb => DxgiFormat.BC1_UNorm_SRgb,
        TextureFormat.BC2RGBAUnorm => DxgiFormat.BC2_UNorm,
        TextureFormat.BC2RGBAUnormSrgb => DxgiFormat.BC2_UNorm_SRgb,
        TextureFormat.BC3RGBAUnorm => DxgiFormat.BC3_UNorm,
        TextureFormat.BC3RGBAUnormSrgb => DxgiFormat.BC3_UNorm_SRgb,
        TextureFormat.BC4RUnorm => DxgiFormat.BC4_UNorm,
        TextureFormat.BC4RSnorm => DxgiFormat.BC4_SNorm,
        TextureFormat.BC5RGUnorm => DxgiFormat.BC5_UNorm,
        TextureFormat.BC5RGSnorm => DxgiFormat.BC5_SNorm,
        TextureFormat.BC6HRGBUfloat => DxgiFormat.BC6H_Uf16,
        TextureFormat.BC6HRGBFloat => DxgiFormat.BC6H_Sf16,
        TextureFormat.BC7RGBAUnorm => DxgiFormat.BC7_UNorm,
        TextureFormat.BC7RGBAUnormSrgb => DxgiFormat.BC7_UNorm_SRgb,
        _ => throw new NotSupportedException($"Direct3D 12 has no format for {format}."),
    };

    /// <summary>
    /// The format the texture is made with: its view format, but typeless when its views read it as more than one format
    /// (an sRGB view of a linear texture) or when a depth texture is also read by shaders.
    /// </summary>
    internal static DxgiFormat Resource(TextureFormat format, bool sampledDepth, bool castable) => View(format) switch
    {
        DxgiFormat.D32_Float when sampledDepth => DxgiFormat.R32_Typeless,
        DxgiFormat.D16_UNorm when sampledDepth => DxgiFormat.R16_Typeless,
        DxgiFormat.D24_UNorm_S8_UInt when sampledDepth => DxgiFormat.R24G8_Typeless,
        DxgiFormat.D32_Float_S8X24_UInt when sampledDepth => DxgiFormat.R32G8X24_Typeless,
        DxgiFormat.R8G8B8A8_UNorm or DxgiFormat.R8G8B8A8_UNorm_SRgb when castable => DxgiFormat.R8G8B8A8_Typeless,
        DxgiFormat.B8G8R8A8_UNorm or DxgiFormat.B8G8R8A8_UNorm_SRgb when castable => DxgiFormat.B8G8R8A8_Typeless,
        DxgiFormat.BC1_UNorm or DxgiFormat.BC1_UNorm_SRgb when castable => DxgiFormat.BC1_Typeless,
        DxgiFormat.BC2_UNorm or DxgiFormat.BC2_UNorm_SRgb when castable => DxgiFormat.BC2_Typeless,
        DxgiFormat.BC3_UNorm or DxgiFormat.BC3_UNorm_SRgb when castable => DxgiFormat.BC3_Typeless,
        DxgiFormat.BC7_UNorm or DxgiFormat.BC7_UNorm_SRgb when castable => DxgiFormat.BC7_Typeless,
        var view => view,
    };

    /// <summary>The format a shader reads a depth texture as (its depth, as a float); the view format otherwise.</summary>
    internal static DxgiFormat ShaderRead(TextureFormat format) => View(format) switch
    {
        DxgiFormat.D32_Float => DxgiFormat.R32_Float,
        DxgiFormat.D16_UNorm => DxgiFormat.R16_UNorm,
        DxgiFormat.D24_UNorm_S8_UInt => DxgiFormat.R24_UNorm_X8_Typeless,
        DxgiFormat.D32_Float_S8X24_UInt => DxgiFormat.R32_Float_X8X24_Typeless,
        var view => view,
    };

    /// <summary>The format a copy reads or writes a texture's texels as (a depth texture's as its plain depth).</summary>
    internal static DxgiFormat Copy(TextureFormat format) => View(format) switch
    {
        DxgiFormat.D32_Float => DxgiFormat.R32_Float,
        DxgiFormat.D16_UNorm => DxgiFormat.R16_UNorm,
        var view => view,
    };

    internal static bool IsDepth(TextureFormat format) =>
        format is TextureFormat.Depth16Unorm or TextureFormat.Depth24Plus or TextureFormat.Depth24PlusStencil8 or TextureFormat.Depth32Float
            or TextureFormat.Depth32FloatStencil8 or TextureFormat.Stencil8;

    internal static bool HasStencil(TextureFormat format) =>
        format is TextureFormat.Depth24PlusStencil8 or TextureFormat.Depth32FloatStencil8 or TextureFormat.Stencil8;

    /// <summary>Texels a block spans across (4 for the BC formats, 1 otherwise), and the bytes a block takes.</summary>
    internal static (uint Block, uint Bytes) Layout(TextureFormat format) => format switch
    {
        TextureFormat.BC1RGBAUnorm or TextureFormat.BC1RGBAUnormSrgb or TextureFormat.BC4RUnorm or TextureFormat.BC4RSnorm => (4, 8),
        TextureFormat.BC2RGBAUnorm or TextureFormat.BC2RGBAUnormSrgb or TextureFormat.BC3RGBAUnorm or TextureFormat.BC3RGBAUnormSrgb
            or TextureFormat.BC5RGUnorm or TextureFormat.BC5RGSnorm or TextureFormat.BC6HRGBUfloat or TextureFormat.BC6HRGBFloat
            or TextureFormat.BC7RGBAUnorm or TextureFormat.BC7RGBAUnormSrgb => (4, 16),
        TextureFormat.R8Unorm or TextureFormat.R8Snorm or TextureFormat.R8Uint or TextureFormat.R8Sint or TextureFormat.Stencil8 => (1, 1),
        TextureFormat.R16Unorm or TextureFormat.R16Snorm or TextureFormat.R16Uint or TextureFormat.R16Sint or TextureFormat.R16Float
            or TextureFormat.RG8Unorm or TextureFormat.RG8Snorm or TextureFormat.RG8Uint or TextureFormat.RG8Sint or TextureFormat.Depth16Unorm => (1, 2),
        TextureFormat.RG32Float or TextureFormat.RG32Uint or TextureFormat.RG32Sint or TextureFormat.RGBA16Unorm or TextureFormat.RGBA16Snorm
            or TextureFormat.RGBA16Uint or TextureFormat.RGBA16Sint or TextureFormat.RGBA16Float or TextureFormat.Depth32FloatStencil8 => (1, 8),
        TextureFormat.RGBA32Float or TextureFormat.RGBA32Uint or TextureFormat.RGBA32Sint => (1, 16),
        _ => (1, 4),
    };
}
