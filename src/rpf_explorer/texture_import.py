"""Image decoding policy shared by texture import and the replacement picker."""

from pathlib import Path

from .texture_dimensions import power_of_two_dimensions, texfury_mip_stop_size

IMAGE_SUFFIXES = frozenset({'.dds', '.png', '.jpg', '.jpeg', '.bmp', '.tga', '.webp'})
IMAGE_FILTER = "Texture images (*.dds *.png *.jpg *.jpeg *.bmp *.tga *.webp)"


def read_texture_image(path: Path):
    from texfury import BCFormat, Texture

    name = path.stem.lower()
    if path.suffix.lower() == '.dds':
        source = Texture.from_dds(path, name=name)
    else:
        # Inspect source pixels before lossy compression or resampling; an alpha
        # channel alone does not imply transparency. Keep this stage lossless.
        source = Texture.from_image(path, name=name, format=BCFormat.A8R8G8B8,
                                    quality=1.0, resize_to_pot=False, generate_mipmaps=False)
    target_format = BCFormat.BC3 if source.has_transparency() else BCFormat.BC1
    width, height = power_of_two_dimensions(source.width, source.height, 'nearest')
    width, height = max(4, width), max(4, height)
    if (width, height) != (source.width, source.height):
        if source.format != BCFormat.A8R8G8B8:
            source = source.to_format(BCFormat.A8R8G8B8, quality=1.0, generate_mipmaps=False)
        source = source.resize(width, height, quality=1.0, generate_mipmaps=False)
    return source.to_format(target_format, quality=1.0, generate_mipmaps=True,
                            min_mip_size=texfury_mip_stop_size(width, height, 4))
