"""Image decoding policy shared by texture import and the replacement picker."""

from pathlib import Path

IMAGE_SUFFIXES = frozenset({'.dds', '.png', '.jpg', '.jpeg', '.bmp', '.tga', '.webp'})
IMAGE_FILTER = "Texture images (*.dds *.png *.jpg *.jpeg *.bmp *.tga *.webp)"


def read_texture_image(path: Path):
    from texfury import BCFormat, Texture

    name = path.stem.lower()
    if path.suffix.lower() == '.dds':
        return Texture.from_dds(path, name=name)
    # BC3 preserves alpha in both editions; keep the source dimensions exactly.
    return Texture.from_image(path, name=name, format=BCFormat.BC3,
                              resize_to_pot=False, generate_mipmaps=True, min_mip_size=1)
