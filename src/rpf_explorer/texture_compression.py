"""Conservative format optimization for conventional DXT textures only."""

DXT_FORMATS = frozenset({'BC1', 'BC1A', 'BC2', 'BC3'})


def optimize_texture_compression(texture):
    from texfury import BCFormat, Texture

    if texture.format.name not in DXT_FORMATS:
        return texture
    transparent = False
    # A lower authored mip may contain alpha even when the base level is opaque.
    for mip in range(texture.mip_count):
        rgba, _, _ = texture.to_rgba(mip)
        alpha = rgba[3::4]
        if alpha.count(255) != len(alpha):
            transparent = True
            break
    target = BCFormat.BC3 if transparent else BCFormat.BC1
    if texture.format == target:
        return texture

    data = bytearray()
    offsets, sizes = [], []
    for mip in range(texture.mip_count):
        rgba, width, height = texture.to_rgba(mip)
        source = Texture.from_raw(rgba, width, height, BCFormat.R8G8B8A8,
                                  1, [0], [len(rgba)], texture.name)
        # Re-encode each stored level independently; never regenerate or resize.
        encoded = source.to_format(target, quality=1.0, generate_mipmaps=False)
        offsets.append(len(data))
        sizes.append(len(encoded.data))
        data.extend(encoded.data)
    return Texture.from_raw(bytes(data), texture.width, texture.height, target,
                            texture.mip_count, offsets, sizes, texture.name)
