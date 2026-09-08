from threading import Event

import pytest
from PySide6.QtCore import QUrl
from fivefury import RpfArchive
from fivefury.ytd import read_ytd


def row_for(bridge, name):
    return next(row for row in range(bridge.entriesModel.rowCount())
                if bridge.entriesModel.entry_at(row).name == name)


def entry_names(bridge):
    return [bridge.entriesModel.entry_at(row).name
            for row in range(bridge.entriesModel.rowCount())]


@pytest.fixture
def shared_archive(texture_sessions, texture_dictionary, tmp_path):
    path = tmp_path / 'shared.rpf'
    archive = RpfArchive.empty(path.name)
    archive.file('textures.ytd', texture_dictionary('paint', 'detail').to_bytes())
    archive.file('sibling.txt', b'untouched')
    archive.save(path)
    first = texture_sessions.activeBridge
    first.openArchive(str(path))
    texture_sessions.newTab()
    peer = texture_sessions.activeBridge
    peer.openArchive(str(path))
    return first, peer, path


@pytest.mark.parametrize('peer_count', [0, 1])
@pytest.mark.parametrize('nested', [False, True])
def test_repeated_ytd_saves_reload_current_handles_and_peer_contents(
    texture_sessions, texture_dictionary, settle_texture_sessions, tmp_path, peer_count, nested,
):
    path = tmp_path / 'shared.rpf'
    inner = RpfArchive.empty('inner.rpf')
    inner.file('textures.ytd', texture_dictionary('paint').to_bytes())
    if nested:
        root = RpfArchive.empty(path.name)
        root.file('inner.rpf', inner.to_bytes())
    else:
        root = inner
    root.file('sibling.txt', b'untouched')
    root.save(path)
    for index in range(peer_count + 1):
        if index:
            texture_sessions.newTab()
        bridge = texture_sessions.activeBridge
        bridge.openArchive(str(path))
        if nested:
            bridge.activateEntry(row_for(bridge, 'inner.rpf'))
    first = texture_sessions._tabs[0]
    first.activateEntry(row_for(first, 'textures.ytd'))
    settle_texture_sessions()
    for name in ('first_edit', 'second_edit'):
        assert first.textureViewer.renameSelected(name)
        assert first.textureViewer.saveYtd()
        settle_texture_sessions()
        assert not first.textureViewer.modified, first.textureViewer.status
        for bridge in texture_sessions._tabs:
            entry = bridge.entriesModel.entry_at(row_for(bridge, 'textures.ytd'))
            assert read_ytd(bridge.provider.read_entry_bytes(entry)).names() == [name]
    with RpfArchive.from_path(path) as saved:
        assert saved.find_entry('sibling.txt').read_standalone() == b'untouched'


def test_deleting_nested_rpf_returns_peer_to_root(
    texture_sessions, settle_texture_sessions, tmp_path,
):
    path = tmp_path / 'outer.rpf'
    inner = RpfArchive.empty('inner.rpf')
    inner.file('payload.txt', b'example')
    outer = RpfArchive.empty(path.name)
    outer.file('inner.rpf', inner.to_bytes())
    outer.save(path)
    first = texture_sessions.activeBridge
    first.openArchive(str(path))
    texture_sessions.newTab()
    peer = texture_sessions.activeBridge
    peer.openArchive(str(path))
    peer.activateEntry(row_for(peer, 'inner.rpf'))
    first.selectEntry(row_for(first, 'inner.rpf'))
    assert first.deleteSelectedFiles()
    settle_texture_sessions()
    with RpfArchive.from_path(path) as saved:
        assert saved.find_entry('inner.rpf') is None
    assert peer.archiveName == 'outer.rpf'
    assert peer.provider.archive_prefix == ''
    assert peer.currentPath == '.'
    assert entry_names(peer) == []
    assert peer.createEmptyRpf('replacement')
    settle_texture_sessions()
    assert entry_names(first) == ['replacement.rpf']


def test_shared_archive_creation_import_and_deletion(
    shared_archive, settle_texture_sessions, tmp_path,
):
    first, peer, path = shared_archive
    assert first.createEmptyRpf('new')
    settle_texture_sessions()
    assert 'new.rpf' in entry_names(peer)
    source = tmp_path / 'import.txt'
    source.write_bytes(b'imported')
    assert peer.importDroppedFiles([QUrl.fromLocalFile(str(source))])
    settle_texture_sessions()
    assert 'import.txt' in entry_names(first)
    first.selectEntry(row_for(first, 'new.rpf'))
    assert first.deleteSelectedFiles()
    settle_texture_sessions()
    assert 'new.rpf' not in entry_names(peer)
    with RpfArchive.from_path(path) as saved:
        assert saved.find_entry('sibling.txt').read_standalone() == b'untouched'
        assert saved.find_entry('import.txt').read_standalone() == b'imported'


def test_failed_ytd_write_restores_sessions_and_can_retry(
    shared_archive, settle_texture_sessions, monkeypatch,
):
    first, peer, _ = shared_archive
    first.activateEntry(row_for(first, 'textures.ytd'))
    settle_texture_sessions()
    assert first.textureViewer.renameSelected('edited')

    def fail(*args):
        raise OSError('simulated write failure')

    with monkeypatch.context() as patch:
        patch.setattr('rpf_explorer.texture_viewer._save_dictionary_to_archive', fail)
        assert first.textureViewer.saveYtd()
        settle_texture_sessions()
    assert first.textureViewer.modified
    for bridge in (first, peer):
        assert not bridge.entryOperationBusy
        assert not bridge.textureViewer.operationBusy
        entry = bridge.entriesModel.entry_at(row_for(bridge, 'textures.ytd'))
        assert set(read_ytd(bridge.provider.read_entry_bytes(entry)).names()) == {'paint', 'detail'}
    assert first.textureViewer.saveYtd()
    settle_texture_sessions()
    assert not first.textureViewer.modified, first.textureViewer.status


def test_in_flight_save_blocks_competing_writes_and_reopening_handles(
    shared_archive, settle_texture_sessions, monkeypatch,
):
    import rpf_explorer.texture_viewer as viewer_module

    first, peer, path = shared_archive
    for bridge in (first, peer):
        bridge.activateEntry(row_for(bridge, 'textures.ytd'))
    settle_texture_sessions()
    assert first.textureViewer.renameSelected('edited')
    assert peer.textureViewer.renameSelected('peer_edit')
    started = Event()
    release = Event()
    save = viewer_module._save_dictionary_to_archive

    def paused_save(*args):
        started.set()
        assert release.wait(5)
        save(*args)

    monkeypatch.setattr(viewer_module, '_save_dictionary_to_archive', paused_save)
    assert first.textureViewer.saveYtd()
    assert started.wait(5)
    try:
        assert first.entryOperationBusy and peer.entryOperationBusy
        assert not peer.textureViewer.saveYtd()
        assert not peer.createEmptyRpf('blocked')
        first.selectEntry(row_for(first, 'sibling.txt'))
        assert not first.deleteSelectedFiles()
        first_root = first.provider._root_archive()
        peer_root = peer.provider._root_archive()
        first.openArchive(str(path))
        peer.openArchive(str(path))
        assert first.provider._root_archive() is first_root
        assert peer.provider._root_archive() is peer_root
    finally:
        release.set()
    settle_texture_sessions()
    assert not first.textureViewer.modified, first.textureViewer.status
    assert peer.textureViewer.modified  # Reloading explorer data must preserve editor changes.
    assert not peer.entryOperationBusy


def test_failed_entry_write_restores_peers(shared_archive, settle_texture_sessions, monkeypatch):
    first, peer, _ = shared_archive

    def fail(*args):
        raise OSError('simulated create failure')

    with monkeypatch.context() as patch:
        patch.setattr('rpf_explorer.bridge.create_empty_rpf_at', fail)
        assert first.createEmptyRpf('failed')
        settle_texture_sessions()
    assert 'simulated create failure' in first.status
    assert not peer.entryOperationBusy
    assert first.createEmptyRpf('retry')
    settle_texture_sessions()
    assert 'retry.rpf' in entry_names(peer)
