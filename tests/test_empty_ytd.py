"""Regression contracts for the explicitly temporary empty-YTD compatibility path."""

import struct

import pytest
from fivefury import GameTarget
from fivefury.resource import ResourceSections, split_rsc7_sections
from fivefury.ytd import Ytd

from rpf_explorer.ytd_empty_ad_hoc import empty_ytd_bytes, read_ytd
from rpf_explorer.ytd_saving import serialize_ytd


@pytest.mark.parametrize('game', list(GameTarget))
def test_empty_template_has_zero_arrays_and_no_graphics(game):
    data = serialize_ytd(Ytd([], game=game))
    header, system, graphics = split_rsc7_sections(data)
    assert header.version == (13 if game == GameTarget.GTA5 else 5)
    assert not graphics
    assert struct.unpack_from('<QHH', system, 0x20) == (0, 0, 0)
    assert struct.unpack_from('<QHH', system, 0x30) == (0, 0, 0)
    parsed = read_ytd(data)
    assert parsed.game == game
    assert parsed.textures == []
    # RPF extraction can recompress an equivalent resource.
    recompressed = ResourceSections(header, system, graphics).to_bytes()
    assert read_ytd(recompressed).game == game
    assert read_ytd(recompressed).textures == []


def test_empty_workaround_rejects_unknown_target_and_corrupt_template():
    with pytest.raises(ValueError):
        empty_ytd_bytes('unsupported')
    data = empty_ytd_bytes(GameTarget.GTA5)
    header, system, graphics = split_rsc7_sections(data)
    corrupted = bytearray(system)
    struct.pack_into('<H', corrupted, 0x28, 1)
    with pytest.raises(ValueError):
        read_ytd(ResourceSections(header, bytes(corrupted), graphics).to_bytes())
    with pytest.raises(ValueError):
        read_ytd(data[:24])


@pytest.mark.parametrize('game', list(GameTarget))
@pytest.mark.parametrize('count', [1, 3])
@pytest.mark.parametrize('in_archive', [False, True])
def test_remove_every_texture_save_reopen_and_undo(
    texture_sessions, texture_dictionary, settle_texture_sessions, tmp_path,
    game, count, in_archive,
):
    from fivefury import RpfArchive

    original = Ytd(texture_dictionary(*[f'tex_{i}' for i in range(count)]).textures, game=game)
    viewer = texture_sessions.activeBridge.textureViewer
    path = tmp_path / 'empty.ytd'
    bridge = texture_sessions.activeBridge
    provider = bridge.provider
    try:
        if in_archive:
            archive_path = tmp_path / 'test.rpf'
            archive = RpfArchive.empty(archive_path.name)
            archive.file('empty.ytd', serialize_ytd(original))
            archive.save(archive_path)
            bridge.openArchive(str(archive_path))
            bridge.activateEntry(0)
            settle_texture_sessions()
            saver = viewer._source_saver
            source = str(archive_path) + '::empty.ytd'
        else:
            path.write_bytes(serialize_ytd(original))
            saver = None
            source = str(path)
            viewer.open_dictionary('empty.ytd', source, serialize_ytd(original))
        settle_texture_sessions()
        loaded_textures = tuple(viewer._dictionary.textures)
        viewer.selectAllTextures()
        assert viewer.canRemoveSelection
        assert viewer.removeSelected()
        settle_texture_sessions()
        assert viewer.textureCount == viewer.selectedCount == 0
        assert viewer.selectedIndex == -1
        assert viewer.previewUrl == ''
        assert not viewer.previewLoading
        assert viewer.status == 'Texture dictionary is empty'
        assert viewer.modified and viewer.hasDocument
        assert not viewer.canRemoveSelection
        assert viewer.saveYtd()
        settle_texture_sessions()
        assert not viewer.modified, viewer.status
        if in_archive:
            with RpfArchive.from_path(archive_path) as saved:
                data = saved.find_entry('empty.ytd').read_standalone()
        else:
            data = path.read_bytes()
        assert read_ytd(data).textures == []
        assert read_ytd(data).game == game
        assert viewer.undo()
        settle_texture_sessions()
        assert tuple(viewer._dictionary.textures) == loaded_textures
        assert viewer.modified
        assert viewer.selectedCount == count
        assert viewer._dictionary.game == game
        # Reopen the actual saved empty file, without depending on injected models.
        viewer.open_dictionary('empty.ytd', source, data, source_saver=saver,
                               source_prepare=viewer._source_prepare,
                               source_root_path=viewer._source_root_path)
        viewer.resolvePendingChanges('discard')
        settle_texture_sessions()
        assert not viewer.error
        assert viewer.hasDocument and viewer.textureCount == 0
        assert viewer.saveYtd()
        settle_texture_sessions()
        assert not viewer.saving and not viewer.operationBusy
    finally:
        provider.close()


def test_nonempty_ytd_still_uses_normal_validation(texture_dictionary):
    with pytest.raises(ValueError):
        serialize_ytd(texture_dictionary('duplicate', 'duplicate'))


@pytest.mark.parametrize('game', list(GameTarget))
def test_save_empty_ytd_as_preserves_target(
    texture_sessions, settle_texture_sessions, tmp_path, monkeypatch, game,
):
    viewer = texture_sessions.activeBridge.textureViewer
    viewer.open_dictionary('empty.ytd', '', empty_ytd_bytes(game))
    settle_texture_sessions()
    assert viewer.hasDocument and not viewer.canSaveSource
    destination = tmp_path / 'exported.ytd'
    monkeypatch.setattr('rpf_explorer.texture_viewer.QFileDialog.getSaveFileName',
                        lambda *args: (str(destination), ''))
    assert viewer.saveYtdAs()
    settle_texture_sessions()
    assert viewer.canSaveSource
    assert viewer.sourcePath == str(destination)
    assert read_ytd(destination.read_bytes()).game == game
    assert read_ytd(destination.read_bytes()).textures == []


@pytest.mark.parametrize('game', list(GameTarget))
def test_undo_last_removal_preserves_an_imported_unnamed_texture(
    texture_sessions, texture_dictionary, settle_texture_sessions, game,
):
    dictionary = Ytd(texture_dictionary('named').textures, game=game)
    header, system, graphics = split_rsc7_sections(serialize_ytd(dictionary))
    system = bytearray(system)
    array = struct.unpack_from('<Q', system, 0x30)[0] - 0x50000000
    descriptor = struct.unpack_from('<Q', system, array)[0] - 0x50000000
    name = struct.unpack_from('<Q', system, descriptor + 0x28)[0] - 0x50000000
    system[name] = 0
    viewer = texture_sessions.activeBridge.textureViewer
    viewer.open_dictionary('unnamed.ytd', '', ResourceSections(header, bytes(system), graphics).to_bytes())
    settle_texture_sessions()
    assert not viewer.error and viewer.textureCount == 1
    original = viewer._dictionary.textures[0]
    assert original.name == ''
    assert viewer.removeSelected()
    assert viewer.textureCount == 0
    assert viewer.undo()
    settle_texture_sessions()
    assert viewer._dictionary.textures == [original]
    assert not viewer.modified
    with pytest.raises(ValueError):
        serialize_ytd(viewer._dictionary)  # Undo is not an export-validation bypass.
