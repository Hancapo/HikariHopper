import pytest
from PySide6.QtCore import Qt
from fivefury import GameTarget
from fivefury.ytd import read_ytd


@pytest.mark.parametrize('game', [GameTarget.GTA5, GameTarget.GTA5_ENHANCED])
def test_remove_duplicates_keeps_first_and_undo_restores_original(
    texture_sessions, texture_dictionary, settle_texture_sessions, tmp_path, game,
):
    viewer = texture_sessions.activeBridge.textureViewer
    dictionary = texture_dictionary('Paint', 'detail', 'paint', 'Paint', 'unique')
    dictionary.game = game
    originals = tuple(dictionary.textures)
    viewer._dictionary_loaded((viewer._generation, dictionary))
    settle_texture_sessions()
    viewer.selectTexture(2)
    viewer.selectTexture(4, Qt.KeyboardModifier.ControlModifier.value)
    assert viewer.duplicateTextureCount == 2
    assert viewer.removeDuplicatesByName()
    settle_texture_sessions()
    assert viewer._dictionary.names() == ['Paint', 'detail', 'unique']
    assert all(viewer._dictionary.textures[index] is originals[original_index]
               for index, original_index in enumerate((0, 1, 4)))
    assert viewer._selected_rows == {0, 2}
    assert viewer.selectedName == 'unique'
    assert viewer.duplicateTextureCount == 0
    assert len(viewer._undo_stack) == 1
    assert viewer.modified
    path = tmp_path / 'clean.ytd'
    viewer._dictionary.save(path)
    assert read_ytd(path.read_bytes()).game == game
    assert set(read_ytd(path.read_bytes()).names()) == {'Paint', 'detail', 'unique'}
    assert viewer.undo()
    settle_texture_sessions()
    assert viewer._dictionary.names() == ['Paint', 'detail', 'paint', 'Paint', 'unique']
    assert all(current is original for current, original in zip(viewer._dictionary.textures, originals))
    assert viewer._selected_rows == {2, 4}
    assert viewer.duplicateTextureCount == 2
    assert not viewer.modified
    with pytest.raises(ValueError, match='duplicated'):
        viewer._dictionary.to_bytes()
    assert viewer.removeDuplicatesByName()


def test_no_duplicates_is_noop(texture_sessions, texture_dictionary, settle_texture_sessions):
    viewer = texture_sessions.activeBridge.textureViewer
    assert viewer.duplicateTextureCount == 0
    assert not viewer.removeDuplicatesByName()
    viewer._dictionary_loaded((viewer._generation, texture_dictionary('one', 'two')))
    settle_texture_sessions()
    assert not viewer.removeDuplicatesByName()
    assert not viewer.modified and not viewer.canUndo


def test_duplicate_cleanup_ignores_selection_but_respects_busy_state(
    texture_sessions, texture_dictionary, settle_texture_sessions,
):
    viewer = texture_sessions.activeBridge.textureViewer
    viewer._dictionary_loaded((viewer._generation, texture_dictionary('same', 'SAME', 'other')))
    settle_texture_sessions()
    viewer.selectTexture(0, Qt.KeyboardModifier.ControlModifier.value)
    assert viewer.selectedCount == 0
    viewer.set_external_write_busy(True)
    assert not viewer.removeDuplicatesByName()
    assert viewer.textureCount == 3
    viewer.set_external_write_busy(False)
    assert viewer.removeDuplicatesByName()
    settle_texture_sessions()
    assert viewer.textureCount == 2
    assert viewer.selectedCount == 0
    assert viewer.selectedIndex == -1
    assert viewer.undo()
    settle_texture_sessions()
    assert viewer.selectedCount == 0


def test_edit_menu_cleanup_and_undo():
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
menu = next(obj for obj in window.findChildren(QObject)
            if obj.metaObject().className().startswith('TextureViewerMenuRow_'))
action = next(obj for obj in menu.findChildren(QObject)
              if obj.property('text') == 'Remove duplicates by name')
assert not action.property('enabled')
textures = [Texture.from_raw(bytes([0, 0, 255, 255]) * 4, 2, 2,
            TextureFormat.A8R8G8B8, 1, name=name) for name in ('Paint', 'paint')]
viewer._dictionary_loaded((viewer._generation, Ytd(textures)))
def settle():
    for _ in range(3):
        assert viewer._thread_pool.waitForDone(5000)
        app.processEvents()
settle()
assert action.property('enabled')
viewer.set_external_write_busy(True)
assert not action.property('enabled')
viewer.set_external_write_busy(False)
QMetaObject.invokeMethod(action, 'triggered')
settle()
assert viewer._dictionary.names() == ['Paint']
assert not action.property('enabled')
undo = next(obj for obj in menu.findChildren(QObject)
            if obj.property('text') == 'Undo Remove duplicates by name')
assert undo.property('enabled')
QMetaObject.invokeMethod(undo, 'triggered')
settle()
assert viewer._dictionary.names() == ['Paint', 'paint']
assert action.property('enabled')
viewer.shutdown()
engine.deleteLater()
app.processEvents()
'''
    result = subprocess.run([sys.executable, '-c', script], cwd=Path(__file__).parents[1],
                            env=environment, capture_output=True, text=True, timeout=30)
    assert result.returncode == 0, result.stdout + result.stderr
