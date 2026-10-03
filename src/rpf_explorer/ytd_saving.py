"""Local page-layout workaround for the FiveFury 0.5.1 YTD writer.

Only repacks freshly serialized models, never arbitrary source resources. The
library still owns descriptor encoding, page packing, relocation and compression.
"""

from __future__ import annotations

import struct
from typing import Any

_SYSTEM_BASE = 0x50000000
_GRAPHICS_BASE = 0x60000000
_MAX_PAGES = 128
_PAGES_INFO_SIZE = 16 + 8 * _MAX_PAGES
# Descriptor sizes and explicitly identified pointer fields of the upstream writer.
_DESCRIPTORS = {13: (0x90, (0x28, 0x70)), 5: (0x80, (0x28, 0x30, 0x38))}


def _pointer_offset(data: bytes | bytearray, field: int, base: int, size: int, limit: int) -> int:
    offset = struct.unpack_from('<Q', data, field)[0] - base
    if offset < 0 or offset + size > limit:
        raise ValueError(f'YTD pointer at 0x{field:X} exceeds its resource section')
    return offset


def _texture_layout(system: bytes | bytearray, graphics: bytes, version: int, textures: list[Any]):
    from fivefury.resource import ResourceBlockSpan

    descriptor_size, descriptor_pointers = _DESCRIPTORS[version]
    count = len(textures)
    if struct.unpack_from('<H', system, 0x28)[0] != count or struct.unpack_from('<H', system, 0x38)[0] != count:
        raise ValueError('Serialized YTD texture counts do not match the model')
    _pointer_offset(system, 0x20, _SYSTEM_BASE, 4 * count, len(system))
    array = _pointer_offset(system, 0x30, _SYSTEM_BASE, 8 * count, len(system))
    fields = [8, 0x20, 0x30]
    blocks = []
    for index, texture in enumerate(textures):
        field = array + 8 * index
        descriptor = _pointer_offset(system, field, _SYSTEM_BASE, descriptor_size, len(system))
        fields.append(field)
        fields.extend(descriptor + pointer for pointer in descriptor_pointers)
        name = texture.name.encode('utf-8') + b'\0'
        name_offset = _pointer_offset(system, descriptor + 0x28, _SYSTEM_BASE, len(name), len(system))
        if system[name_offset:name_offset + len(name)] != name:
            raise ValueError('Serialized YTD names do not match the model order')
        if version == 5:
            _pointer_offset(system, descriptor + 0x30, _SYSTEM_BASE, 0x28, len(system))
        offset = _pointer_offset(system, descriptor + descriptor_pointers[-1],
                                 _GRAPHICS_BASE, len(texture.data), len(graphics))
        if graphics[offset:offset + len(texture.data)] != texture.data:
            raise ValueError(f'Serialized texture payload changed: {texture.name}')
        blocks.append(ResourceBlockSpan(offset, len(texture.data), relocate_pointers=False))
    return tuple(fields), blocks


def serialize_ytd(dictionary: Any) -> bytes:
    """Build and verify page-contained YTD bytes before any destination is touched."""
    # AD HOC: Python 0.5.1 rejects empty models; use the .NET-validated templates.
    # Nonempty documents retain the full validation and page-layout path below.
    if not dictionary.textures:
        from .ytd_empty_ad_hoc import empty_ytd_bytes

        return empty_ytd_bytes(dictionary.game)

    from fivefury.hashing import jenk_hash
    from fivefury.resource import (
        ResourceBlockSpan, ResourceHeader, ResourceSections, find_resource_chunk,
        get_resource_total_page_count, layout_resource_sections,
    )
    from fivefury.ytd import read_ytd

    prepared = dictionary.prepare_sections()
    version = prepared.header.version
    if version not in _DESCRIPTORS:
        raise ValueError(f'Unsupported YTD resource version: {version}')
    textures = sorted(dictionary.textures, key=lambda texture: jenk_hash(texture.name))
    system = bytearray(prepared.system_data)
    body_size = len(system)
    fields, graphics_blocks = _texture_layout(system, prepared.graphics_data, version, textures)

    # The flat writer emits only a 16-byte pages-info stub. Replace it with a
    # zeroed, bounded descriptor reserve; stale stub bytes must not survive.
    old_info = _pointer_offset(system, 8, _SYSTEM_BASE, 16, body_size)
    system[old_info:old_info + 16] = bytes(16)
    struct.pack_into('<Q', system, 8, _SYSTEM_BASE + body_size)
    system.extend(bytes(_PAGES_INFO_SIZE))
    system_data, graphics_data, system_flags, graphics_flags = layout_resource_sections(
        bytes(system),
        [ResourceBlockSpan(0, body_size, pointer_offsets=fields),
         ResourceBlockSpan(body_size, _PAGES_INFO_SIZE, relocate_pointers=False)],
        prepared.graphics_data, graphics_blocks, version=version, max_page_count=_MAX_PAGES,
    )
    header = ResourceHeader(version, system_flags, graphics_flags)
    system_pages = get_resource_total_page_count(system_flags)
    graphics_pages = get_resource_total_page_count(graphics_flags)
    if system_pages + graphics_pages > _MAX_PAGES:
        raise ValueError('YTD exceeds the resource page capacity')
    info = _pointer_offset(system_data, 8, _SYSTEM_BASE, _PAGES_INFO_SIZE, len(system_data))
    system = bytearray(system_data)
    struct.pack_into('<BB', system, info + 8, system_pages, graphics_pages)

    # Keeping the dictionary/descriptor/name section indivisible also protects
    # long names and pointer arrays. Texture payloads are separate allocations.
    if not find_resource_chunk(header, _SYSTEM_BASE, body_size):
        raise ValueError('YTD system allocation crosses a resource page')
    if not find_resource_chunk(header, _SYSTEM_BASE + info, _PAGES_INFO_SIZE):
        raise ValueError('YTD pages-info allocation crosses a resource page')
    _, repaired_blocks = _texture_layout(system, graphics_data, version, textures)
    for block in repaired_blocks:
        if not find_resource_chunk(header, _GRAPHICS_BASE + block.offset, block.size):
            raise ValueError('YTD texture allocation crosses a resource page')

    result = ResourceSections(header, bytes(system), graphics_data).to_bytes()
    reread = read_ytd(result)
    reread.validate().raise_for_errors()
    if reread.game != dictionary.game or len(reread.textures) != len(textures):
        raise ValueError('YTD round-trip changed the target or texture count')
    for before, after in zip(textures, reread.textures, strict=True):
        if any(getattr(before, field) != getattr(after, field) for field in (
            'name', 'width', 'height', 'format', 'mip_count', 'usage', 'usage_flags', 'data',
        )):
            raise ValueError(f'YTD round-trip changed texture data: {before.name}')
    return result
