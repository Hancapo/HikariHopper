import struct

import pytest
from fivefury import GameTarget, RpfArchive
from fivefury.resource import find_resource_chunk, split_rsc7_sections
from fivefury.ytd import Texture, TextureFormat, Ytd, read_ytd

from rpf_explorer.texture_viewer import _save_dictionary_to_archive, _save_dictionary_to_path
from rpf_explorer.backend import RpfProvider


def assert_page_safe(data):
    header, system, graphics = split_rsc7_sections(data)
    parsed = read_ytd(data)
    count = struct.unpack_from('<H', system, 0x38)[0]
    array = struct.unpack_from('<Q', system, 0x30)[0]
    assert find_resource_chunk(header, array, 8 * count) is not None
    by_name = {texture.name: texture for texture in parsed.textures}
    for index in range(count):
        pointer = struct.unpack_from('<Q', system, array - 0x50000000 + 8 * index)[0]
        offset = pointer - 0x50000000
        assert find_resource_chunk(header, pointer, 0x90 if header.version == 13 else 0x80)
        name_pointer = struct.unpack_from('<Q', system, offset + 0x28)[0]
        name_offset = name_pointer - 0x50000000
        name_end = system.index(0, name_offset)
        name = system[name_offset:name_end].decode('utf-8')
        assert find_resource_chunk(header, name_pointer, name_end - name_offset + 1), name
        data_pointer = struct.unpack_from('<Q', system, offset + (0x70 if header.version == 13 else 0x38))[0]
        assert find_resource_chunk(header, data_pointer, len(by_name[name].data)), name
    info_pointer = struct.unpack_from('<Q', system, 8)[0]
    info_offset = info_pointer - 0x50000000
    system_count, graphics_count = struct.unpack_from('<BB', system, info_offset + 8)
    assert system_count == sum(chunk.section == 'system' for chunk in header.chunks)
    assert graphics_count == sum(chunk.section == 'graphics' for chunk in header.chunks)
    assert find_resource_chunk(header, info_pointer, 16 + 8 * (system_count + graphics_count))
    return parsed


@pytest.fixture(params=[GameTarget.GTA5, GameTarget.GTA5_ENHANCED])
def dictionary(request):
    # Three independent 1 MiB allocations expose a page crossing in the flat writer.
    textures = [Texture.from_raw(bytes([index, 0, 0, 255]) * 512 * 512,
                                512, 512, TextureFormat.A8R8G8B8, 1,
                                name=f'texture_{index}') for index in range(3)]
    return Ytd(textures, game=request.param)


def test_loose_save_keeps_every_allocation_inside_a_page(dictionary, tmp_path):
    destination = tmp_path / 'saved.ytd'
    _save_dictionary_to_path(dictionary, destination)
    result = assert_page_safe(destination.read_bytes())
    assert result.game == dictionary.game
    assert {t.name: t.data for t in result.textures} == {t.name: t.data for t in dictionary.textures}


def test_archive_saver_receives_page_safe_bytes(dictionary):
    saved = []
    _save_dictionary_to_archive(dictionary, saved.append)
    assert len(saved) == 1
    assert_page_safe(saved[0])


def test_many_names_stay_within_pages(tmp_path):
    textures = [Texture.from_raw(bytes([0, 0, 255, 255]) * 4, 2, 2,
                                TextureFormat.A8R8G8B8, 1,
                                name=f'{index:03d}_' + 'x' * 97) for index in range(300)]
    dictionary = Ytd(textures)
    destination = tmp_path / 'names.ytd'
    _save_dictionary_to_path(dictionary, destination)
    assert len(assert_page_safe(destination.read_bytes()).textures) == 300


def test_nested_rpf_save_preserves_repaired_layout(dictionary, tmp_path):
    inner = RpfArchive.empty('inner.rpf')
    inner.file('test.ytd', dictionary.to_bytes())
    outer = RpfArchive.empty('outer.rpf')
    outer.file('inner.rpf', inner.to_bytes())
    outer.file('sibling.txt', b'untouched')
    path = tmp_path / 'outer.rpf'
    outer.save(path)
    provider = RpfProvider()
    provider.open_archive(path)
    try:
        entry = next(item for item in provider.archive_entries('.') if item.name == 'inner.rpf')
        provider.open_nested(entry)
        target = provider.archive_entry_target(provider.archive_entries('.')[0])
        _save_dictionary_to_archive(dictionary, target.save)
    finally:
        provider.close()
    with RpfArchive.from_path(path) as saved:
        assert_page_safe(saved.find_entry('inner.rpf/test.ytd').read_standalone())
        assert saved.find_entry('sibling.txt').read_standalone() == b'untouched'


def test_payload_bytes_that_look_like_pointers_are_not_relocated(tmp_path):
    pattern = struct.pack('<QQ', 0x50000040, 0x60000010)
    texture = Texture.from_raw(pattern * 4, 4, 4, TextureFormat.A8R8G8B8, 1, name='pointer_pixels')
    path = tmp_path / 'pointer_pixels.ytd'
    _save_dictionary_to_path(Ytd([texture]), path)
    assert assert_page_safe(path.read_bytes()).textures[0].data == pattern * 4


def test_verification_failure_never_replaces_destination(dictionary, tmp_path, monkeypatch):
    import fivefury.resource as resource

    pack = resource.layout_resource_sections

    def damaged_layout(*args, **kwargs):
        system, graphics, system_flags, graphics_flags = pack(*args, **kwargs)
        # Simulate a relocation bug pointing outside the generated system section.
        broken = bytearray(system)
        struct.pack_into('<Q', broken, 0x30, 0x50000000 + len(system) + 1)
        return bytes(broken), graphics, system_flags, graphics_flags

    path = tmp_path / 'existing.ytd'
    original = dictionary.to_bytes()
    path.write_bytes(original)
    monkeypatch.setattr(resource, 'layout_resource_sections', damaged_layout)
    with pytest.raises(ValueError):
        _save_dictionary_to_path(dictionary, path)
    assert path.read_bytes() == original
    calls = []
    with pytest.raises(ValueError):
        _save_dictionary_to_archive(dictionary, calls.append)
    assert calls == []
