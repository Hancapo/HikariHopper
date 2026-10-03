from threading import Event

import pytest
from PySide6.QtCore import Qt
from fivefury.ytd import Texture, TextureFormat, Ytd, read_ytd

from rpf_explorer.texture_viewer import TextureListModel, mip_count_for_dimensions


CTRL = Qt.KeyboardModifier.ControlModifier.value
SHIFT = Qt.KeyboardModifier.ShiftModifier.value


@pytest.fixture
def viewer(texture_sessions, settle_texture_sessions):
    bridge = texture_sessions.activeBridge.textureViewer
    textures = [Texture.from_raw(bytes([0, 0, 255, 128]) * width * height,
                                width, height, TextureFormat.A8R8G8B8, 1, name=f'texture_{index}')
                for index, (width, height) in enumerate([(30, 50), (16, 32), (100, 70), (8, 16), (16, 64)])]
    bridge._dictionary_loaded((bridge._generation, Ytd(textures)))
    settle_texture_sessions()
    return bridge


def selected(viewer):
    model = viewer.texturesModel
    return {row for row in range(model.rowCount())
            if model.data(model.index(row, 0), TextureListModel.SELECTED)}


def test_control_shift_and_context_selection(viewer):
    viewer.selectTexture(1)
    viewer.selectTexture(3, CTRL)
    assert selected(viewer) == {1, 3}
    viewer.selectContextTexture(1)
    assert selected(viewer) == {1, 3}
    assert viewer.selectedIndex == 1
    viewer.selectTexture(4, SHIFT)
    assert selected(viewer) == {3, 4}
    viewer.selectTexture(1, CTRL | SHIFT)
    assert selected(viewer) == {1, 2, 3, 4}
    viewer.selectContextTexture(0)
    assert selected(viewer) == {0}
    viewer.selectTexture(0, CTRL)
    assert viewer.selectedCount == 0
    assert viewer.selectedIndex == -1
    assert viewer.previewUrl == ''
    assert not viewer.removeSelected()
    viewer.selectAllTextures()
    assert selected(viewer) == set(range(5))
    assert viewer.canRemoveSelection
    assert viewer.removeSelected()
    assert viewer.textureCount == 0


@pytest.mark.parametrize('operation', ['resize', 'power', 'mips', 'format'])
def test_batch_operations_and_single_undo(viewer, settle_texture_sessions, operation, tmp_path):
    originals = tuple(viewer._dictionary.textures)
    viewer.selectTexture(0)
    viewer.selectTexture(2, CTRL)
    if operation == 'resize':
        assert viewer.resizeSelected(32, 64, 'mitchell', 1, True)
    elif operation == 'power':
        assert viewer.resizeSelectionToPowerOfTwo('nearest', 'mitchell', 1, True)
    elif operation == 'mips':
        assert viewer.recalculateSelectedMipmaps('mitchell', 1)
    elif operation == 'format':
        assert viewer.changeSelectedFormat('BC1', 0.7, 'mitchell', 1)
    settle_texture_sessions()
    assert viewer.modified, viewer.status
    assert len(viewer._undo_stack) == 1
    assert selected(viewer) == {0, 2}
    for row in (1, 3, 4):
        assert viewer._dictionary.textures[row] is originals[row]
    for row in (0, 2):
        result = viewer._dictionary.textures[row]
        if operation == 'resize':
            assert (result.width, result.height) == (32, 64)
        elif operation == 'power':
            assert (result.width, result.height) == ((32, 64) if row == 0 else (128, 64))
        elif operation == 'mips':
            assert result.mip_count == mip_count_for_dimensions(result.width, result.height, 1)
        elif operation == 'format':
            assert result.format == TextureFormat.BC1
    output = tmp_path / 'batch.ytd'
    viewer._dictionary.save(output)
    assert len(read_ytd(output.read_bytes()).textures) == 5
    assert viewer.undo()
    settle_texture_sessions()
    assert selected(viewer) == {0, 2}
    assert not viewer.modified
    assert all(current is original for current, original in zip(viewer._dictionary.textures, originals))


def test_power_of_two_skips_valid_textures(viewer, settle_texture_sessions):
    viewer.selectTexture(0)
    viewer.selectTexture(1, CTRL)
    unchanged = viewer._dictionary.textures[1]
    assert viewer.resizeSelectionToPowerOfTwo('down', 'mitchell', 1, True)
    settle_texture_sessions()
    assert viewer._dictionary.textures[1] is unchanged
    assert (viewer._dictionary.textures[0].width, viewer._dictionary.textures[0].height) == (16, 32)


def test_failed_batch_does_not_commit_partial_results(viewer, settle_texture_sessions):
    originals = tuple(viewer._dictionary.textures)
    viewer.selectTexture(0)
    viewer.selectTexture(2, CTRL)

    def transform(texture):
        if texture.name == 'texture_2':
            raise ValueError('simulated conversion failure')
        return texture.resize(16, 16, generate_mipmaps=False)

    assert viewer._start_transform('Resizing', transform)
    settle_texture_sessions()
    assert not viewer.modified
    assert not viewer.canUndo
    assert 'texture_2' in viewer.status and 'simulated conversion failure' in viewer.status
    assert all(current is original for current, original in zip(viewer._dictionary.textures, originals))


def test_selection_is_stable_during_batch(viewer, settle_texture_sessions):
    release = Event()
    started = Event()
    viewer.selectTexture(1)
    viewer.selectTexture(3, CTRL)

    def transform(texture):
        started.set()
        assert release.wait(5)
        return texture.resize(4, 4, generate_mipmaps=False)

    assert viewer._start_transform('Resizing', transform)
    assert started.wait(5)
    try:
        viewer.selectTexture(0)
        viewer.selectAllTextures()
        assert selected(viewer) == {1, 3}
        assert not viewer.removeSelected()
    finally:
        release.set()
    settle_texture_sessions()
    assert selected(viewer) == {1, 3}


def test_remove_selection_and_restore_in_one_undo(viewer, settle_texture_sessions):
    viewer.selectTexture(1)
    viewer.selectTexture(3, CTRL)
    assert not viewer.renameSelected('same_name')
    assert not viewer.replaceSelectedFromImage()
    assert viewer.removeSelected()
    settle_texture_sessions()
    assert viewer._dictionary.names() == ['texture_0', 'texture_2', 'texture_4']
    assert len(viewer._undo_stack) == 1
    assert viewer.undo()
    settle_texture_sessions()
    assert viewer._dictionary.names() == [f'texture_{index}' for index in range(5)]
    assert selected(viewer) == {1, 3}
    assert not viewer.modified


def test_extract_only_selected_textures(viewer, tmp_path, monkeypatch):
    viewer.selectTexture(1)
    viewer.selectTexture(3, CTRL)
    monkeypatch.setattr('rpf_explorer.texture_viewer.QFileDialog.getExistingDirectory', lambda *args: str(tmp_path))
    viewer.extractSelected()
    assert {path.name for path in tmp_path.glob('*.dds')} == {'texture_1.dds', 'texture_3.dds'}
    assert not viewer.modified


def test_alpha_repair_checks_each_selected_texture(viewer, settle_texture_sessions, monkeypatch):
    import rpf_explorer.texture_viewer as module

    original = module._repair_alpha_edges
    checked = []

    def repair(texture, **kwargs):
        checked.append(texture.name)
        return original(texture, **kwargs)

    monkeypatch.setattr(module, '_repair_alpha_edges', repair)
    viewer.selectTexture(1)
    viewer.selectTexture(3, CTRL)
    assert viewer.repairSelectedAlphaEdges(2)
    settle_texture_sessions()
    assert checked == ['texture_1', 'texture_3']
    assert not viewer.modified  # Clean textures are a no-op, with no unnecessary undo entry.


def test_qml_mouse_keyboard_and_context_menu_multiselection():
    import os
    from pathlib import Path
    import subprocess
    import sys

    environment = os.environ.copy()
    environment['QT_QPA_PLATFORM'] = 'offscreen'
    environment['QT_QUICK_BACKEND'] = 'software'
    script = '''
from pathlib import Path
from PySide6.QtCore import QObject, QUrl, Qt, QPointF, QMetaObject
from PySide6.QtWidgets import QApplication
from PySide6.QtQml import QQmlComponent, QQmlEngine
from PySide6.QtQuick import QQuickItem
from PySide6.QtTest import QTest
from shiboken6 import getCppPointer, wrapInstance
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
textures = [Texture.from_raw(bytes([0, 0, 255, 255]) * 16, 4, 4,
            TextureFormat.A8R8G8B8, 1, name=f'texture_{i}') for i in range(5)]
viewer.open_dictionary('test.ytd', '', Ytd(textures).to_bytes())
for _ in range(3):
    assert viewer._thread_pool.waitForDone(5000)
    app.processEvents()
QTest.qWait(50)
rail = next(obj for obj in window.findChildren(QObject)
            if obj.metaObject().className().startswith('TextureRail_'))
list_obj = rail.findChild(QObject, 'textureSelectionList')
view = wrapInstance(getCppPointer(list_obj)[0], QQuickItem)
def click(row, modifier=Qt.NoModifier, button=Qt.LeftButton):
    point = view.mapToScene(QPointF(80, row * 62 + 20)).toPoint()
    QTest.mouseClick(window, button, modifier, point)
    app.processEvents()
click(0)
click(2, Qt.ControlModifier)
assert viewer._selected_rows == {0, 2}, viewer._selected_rows
click(4, Qt.ShiftModifier)
assert viewer._selected_rows == {2, 3, 4}, viewer._selected_rows
click(3, button=Qt.RightButton)
assert viewer._selected_rows == {2, 3, 4}
menu = next(obj for obj in rail.findChildren(QObject)
            if obj.metaObject().className().startswith('TextureContextMenu_'))
actions = {obj.property('text'): obj for obj in menu.findChildren(QObject)
           if obj.metaObject().className().startswith('RetroMenuItem_')}
assert not actions['Rename…'].property('enabled')
assert not actions['Replace from image…'].property('enabled')
assert actions['Remove'].property('enabled')
QMetaObject.invokeMethod(menu, 'close')
view.forceActiveFocus()
QTest.keyClick(window, Qt.Key_A, Qt.ControlModifier)
assert viewer.selectedCount == 5
assert actions['Remove'].property('enabled')
click(1)
QTest.keyClick(window, Qt.Key_Down, Qt.ShiftModifier)
assert viewer._selected_rows == {1, 2}
QTest.keyClick(window, Qt.Key_End, Qt.ShiftModifier)
assert viewer._selected_rows == {1, 2, 3, 4}
viewer.selectAllTextures()
assert viewer.removeSelected()
app.processEvents()
assert viewer.hasDocument and viewer.textureCount == 0
file_actions = {obj.property('text'): obj for obj in window.findChildren(QObject)
                if obj.metaObject().className().startswith('RetroMenuItem_')}
assert file_actions['Save YTD as…'].property('enabled')
assert not file_actions['Save YTD'].property('enabled')  # No source path.
assert not file_actions['Extract all textures…'].property('enabled')
viewer._source_path = 'empty.ytd'
viewer.stateChanged.emit()
assert file_actions['Save YTD'].property('enabled')
viewer._operation_busy = True
viewer.stateChanged.emit()
assert not file_actions['Save YTD'].property('enabled')
assert not file_actions['Save YTD as…'].property('enabled')
viewer._operation_busy = False
viewer._reset_document()
assert not viewer.hasDocument
assert not file_actions['Save YTD as…'].property('enabled')
viewer.shutdown()
window.setVisible(False)
engine.deleteLater()
app.processEvents()
'''
    result = subprocess.run([sys.executable, '-c', script], cwd=Path(__file__).parents[1],
                            env=environment, capture_output=True, text=True, timeout=30)
    assert result.returncode == 0, result.stdout + result.stderr
