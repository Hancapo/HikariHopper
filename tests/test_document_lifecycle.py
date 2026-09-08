from threading import Event

import pytest
from fivefury.ytd import read_ytd
from PySide6.QtTest import QSignalSpy


@pytest.fixture
def edited_document(texture_sessions, texture_dictionary, settle_texture_sessions, tmp_path):
    viewer = texture_sessions.activeBridge.textureViewer
    path = tmp_path / 'first.ytd'
    texture_dictionary('paint').save(path)
    viewer.open_dictionary(path.name, str(path), path.read_bytes())
    settle_texture_sessions()
    assert viewer.renameSelected('edited')
    return viewer, path


@pytest.mark.parametrize('decision', ['cancel', 'discard', 'save'])
def test_opening_another_ytd_requires_a_decision(
    edited_document, texture_dictionary, settle_texture_sessions, decision,
):
    viewer, path = edited_document
    prompt = QSignalSpy(viewer.unsavedChangesRequested)
    viewer.open_dictionary('second.ytd', '', texture_dictionary('other').to_bytes())
    assert prompt.count() == 1
    assert viewer.sourceName == 'first.ytd'
    assert viewer.modified
    viewer.resolvePendingChanges(decision)
    settle_texture_sessions()
    assert viewer.sourceName == ('first.ytd' if decision == 'cancel' else 'second.ytd')
    assert read_ytd(path.read_bytes()).names() == (['edited'] if decision == 'save' else ['paint'])
    if decision == 'cancel':
        assert viewer.modified and viewer.canUndo


@pytest.mark.parametrize('decision', ['cancel', 'discard', 'save'])
def test_close_tab_resolves_pending_edits(
    texture_sessions, edited_document, settle_texture_sessions, decision,
):
    viewer, path = edited_document
    texture_sessions.newTab()
    texture_sessions.closeTab(0)
    assert texture_sessions.rowCount() == 2
    viewer.resolvePendingChanges(decision)
    settle_texture_sessions()
    assert texture_sessions.rowCount() == (2 if decision == 'cancel' else 1)
    assert read_ytd(path.read_bytes()).names() == (['edited'] if decision == 'save' else ['paint'])


def test_last_tab_clears_its_texture_document_after_discard(
    texture_sessions, edited_document, settle_texture_sessions,
):
    viewer, _ = edited_document
    closed = QSignalSpy(viewer.closeRequested)
    texture_sessions.closeTab(0)
    viewer.resolvePendingChanges('discard')
    settle_texture_sessions()
    assert texture_sessions.rowCount() == 1
    assert closed.count() == 1
    assert viewer.textureCount == 0
    assert not viewer.modified


def test_failed_save_cancels_document_switch(
    edited_document, texture_dictionary, settle_texture_sessions, monkeypatch,
):
    viewer, _ = edited_document

    def fail_save(*args):
        raise OSError('simulated write failure')

    monkeypatch.setattr('rpf_explorer.texture_viewer._save_dictionary_to_path', fail_save)
    viewer.open_dictionary('second.ytd', '', texture_dictionary('other').to_bytes())
    viewer.resolvePendingChanges('save')
    settle_texture_sessions()
    assert viewer.sourceName == 'first.ytd'
    assert viewer.modified
    assert 'simulated write failure' in viewer.status
    assert viewer._pending_change is None


def test_cancelled_save_as_preserves_document(
    edited_document, texture_dictionary, monkeypatch,
):
    viewer, _ = edited_document
    viewer._source_path = ''
    monkeypatch.setattr('rpf_explorer.texture_viewer.QFileDialog.getSaveFileName', lambda *args: ('', ''))
    viewer.open_dictionary('second.ytd', '', texture_dictionary('other').to_bytes())
    viewer.resolvePendingChanges('save')
    assert viewer.sourceName == 'first.ytd'
    assert viewer.modified
    assert viewer._pending_change is None


def test_tab_close_waits_for_in_flight_save(
    texture_sessions, edited_document, settle_texture_sessions,
):
    viewer, _ = edited_document
    texture_sessions.newTab()
    release = Event()
    assert viewer._start_save('first.ytd', lambda: release.wait(5))
    try:
        texture_sessions.closeTab(0)
        assert texture_sessions.rowCount() == 2
        assert viewer.saving
    finally:
        release.set()
    settle_texture_sessions()
    assert texture_sessions.rowCount() == 1


def test_tab_close_uses_identity_after_another_tab_closes(
    texture_sessions, texture_dictionary, settle_texture_sessions,
):
    texture_sessions.newTab()
    edited_bridge = texture_sessions.activeBridge
    viewer = edited_bridge.textureViewer
    viewer.open_dictionary('first.ytd', '', texture_dictionary('paint').to_bytes())
    settle_texture_sessions()
    assert viewer.renameSelected('edited')
    texture_sessions.newTab()
    survivor = texture_sessions.activeBridge
    texture_sessions.closeTab(1)
    texture_sessions.closeTab(0)
    viewer.resolvePendingChanges('discard')
    settle_texture_sessions()
    assert texture_sessions._tabs == [survivor]


def test_qml_window_prompts_before_closing():
    import os
    from pathlib import Path
    import subprocess
    import sys

    environment = os.environ.copy()
    environment['QT_QPA_PLATFORM'] = 'offscreen'
    environment['QT_QUICK_BACKEND'] = 'software'
    script = '''
from pathlib import Path
from PySide6.QtCore import QObject, QUrl, QMetaObject
from PySide6.QtWidgets import QApplication
from PySide6.QtQml import QQmlComponent, QQmlEngine
from fivefury.ytd import Texture, TextureFormat, Ytd
from rpf_explorer.texture_viewer import TextureImageProvider, TextureViewerBridge
import rpf_explorer

app = QApplication([])
engine = QQmlEngine()
images = TextureImageProvider()
engine.addImageProvider('textureviewer', images)
viewer = TextureViewerBridge(images)
component = QQmlComponent(engine, QUrl.fromLocalFile(str(
    Path(rpf_explorer.__file__).parent / 'ui' / 'TextureDictionaryWindow.qml')))
window = component.createWithInitialProperties({'bridge': viewer})
assert window is not None, [error.toString() for error in component.errors()]
texture = Texture.from_raw(bytes([0, 0, 255, 255]) * 4, 2, 2,
                          TextureFormat.A8R8G8B8, 1, name='paint')
viewer.open_dictionary('first.ytd', '', Ytd([texture]).to_bytes())
for _ in range(3):
    assert viewer._thread_pool.waitForDone(5000)
    app.processEvents()
assert viewer.renameSelected('edited')
window.close()
for _ in range(4):
    app.processEvents()
assert window.isVisible()
dialogs = [obj for obj in window.findChildren(QObject)
           if obj.metaObject().className().startswith('TextureUnsavedChangesDialog_')]
assert len(dialogs) == 1
assert dialogs[0].property('visible')
QMetaObject.invokeMethod(dialogs[0], 'reject')
app.processEvents()
assert viewer._pending_change is None
assert viewer.modified
window.close()
for _ in range(4):
    app.processEvents()
viewer.resolvePendingChanges('discard')
for _ in range(4):
    app.processEvents()
assert not window.isVisible()
assert not viewer.modified
viewer.shutdown()
engine.deleteLater()
app.processEvents()
'''
    result = subprocess.run([sys.executable, '-c', script], cwd=Path(__file__).parents[1],
                            env=environment, capture_output=True, text=True, timeout=30)
    assert result.returncode == 0, result.stdout + result.stderr
