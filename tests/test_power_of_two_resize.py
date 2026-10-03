import os
from pathlib import Path
import subprocess
import sys

import pytest
from fivefury import GameTarget
from fivefury.ytd import Texture, TextureFormat, Ytd, read_ytd

from rpf_explorer.texture_dimensions import power_of_two_dimensions


@pytest.mark.parametrize(('mode', 'expected'), [
    ('nearest', (256, 512)), ('down', (256, 256)), ('up', (512, 512)),
])
def test_rounding_modes(mode, expected):
    assert power_of_two_dimensions(300, 500, mode) == expected
    assert power_of_two_dimensions(1, 512, mode) == (1, 512)


@pytest.mark.parametrize(('width', 'height', 'mode'), [
    (0, 4, 'nearest'), (4, -1, 'up'), (65536, 4, 'down'), (3, 5, 'unknown'),
])
def test_rejects_invalid_dimensions_and_modes(width, height, mode):
    with pytest.raises(ValueError):
        power_of_two_dimensions(width, height, mode)


@pytest.mark.parametrize('game', [GameTarget.GTA5, GameTarget.GTA5_ENHANCED])
def test_resize_roundtrip_and_undo(texture_sessions, settle_texture_sessions, game):
    viewer = texture_sessions.activeBridge.textureViewer
    source = Texture.from_raw(bytes([0, 0, 255, 128]) * 30 * 50,
                              30, 50, TextureFormat.A8R8G8B8, 1, name='paint')
    dictionary = Ytd([source], game=game)
    viewer.open_dictionary('paint.ytd', '', dictionary.to_bytes())
    settle_texture_sessions()
    original = viewer._dictionary.textures[0]
    assert viewer.selectedNeedsPowerOfTwo
    size = viewer.powerOfTwoSize('nearest')
    assert size == {'width': 32, 'height': 64}
    assert viewer.resizeSelected(size['width'], size['height'], 'mitchell', 1, True)
    settle_texture_sessions()
    assert (viewer.selectedWidth, viewer.selectedHeight) == (32, 64)
    assert not viewer.selectedNeedsPowerOfTwo
    assert viewer.mipCount == 7
    assert viewer.modified and viewer.canUndo
    saved = read_ytd(viewer._dictionary.to_bytes()).textures[0]
    assert (saved.width, saved.height) == (32, 64)
    assert saved.format == original.format
    assert saved.usage == original.usage
    assert saved.name == original.name
    assert viewer.undo()
    settle_texture_sessions()
    assert (viewer.selectedWidth, viewer.selectedHeight) == (30, 50)
    assert viewer._dictionary.textures[0].data == original.data
    assert not viewer.modified


def test_qml_menu_opens_power_of_two_dialog():
    environment = os.environ.copy()
    environment['QT_QPA_PLATFORM'] = 'offscreen'
    environment['QT_QUICK_BACKEND'] = 'software'
    script = '''
from pathlib import Path
from PySide6.QtCore import QObject, QUrl, QMetaObject
from PySide6.QtWidgets import QApplication
from PySide6.QtQml import QQmlComponent, QQmlEngine, QQmlExpression
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
texture = Texture.from_raw(bytes([0, 0, 255, 255]) * 30 * 50, 30, 50,
                          TextureFormat.A8R8G8B8, 1, name='paint')
viewer.open_dictionary('paint.ytd', '', Ytd([texture]).to_bytes())
def settle():
    for _ in range(3):
        assert viewer._thread_pool.waitForDone(5000)
        app.processEvents()
settle()
def find_type(prefix):
    return next(obj for obj in window.findChildren(QObject)
                if obj.metaObject().className().startswith(prefix + '_'))
menu = find_type('TextureContextMenu')
action = next(obj for obj in menu.findChildren(QObject)
              if obj.property('text') == 'Resize to power of 2…')
assert action.property('enabled')
QMetaObject.invokeMethod(action, 'triggered')
settle()
dialog = find_type('TextureResizeDialog')
assert dialog.property('visible') and dialog.property('powerOfTwo')
assert (dialog.property('targetWidth'), dialog.property('targetHeight')) == (32, 64)
rounding = dialog.findChild(QObject, 'powerOfTwoRounding')
rounding.setProperty('currentIndex', 1)
app.processEvents()
assert (dialog.property('targetWidth'), dialog.property('targetHeight')) == (16, 32)
rounding.setProperty('currentIndex', 2)
app.processEvents()
assert (dialog.property('targetWidth'), dialog.property('targetHeight')) == (32, 64)
assert dialog.property('applyEnabled')
expression = QQmlExpression(QQmlEngine.contextForObject(dialog), dialog, 'applyAction()')
expression.evaluate()
assert not expression.hasError(), expression.error().toString()
settle()
assert (viewer.selectedWidth, viewer.selectedHeight) == (32, 64)
assert not action.property('enabled')
assert not dialog.property('applyEnabled')
QMetaObject.invokeMethod(dialog, 'reject')
settle()
assert viewer.undo()
settle()
assert action.property('enabled')
# Show an oversized result without allocating an oversized image.
record = viewer._textures_model.record_at(0)
record.texture.width = viewer.maximumDimension
viewer.selectionChanged.emit()
QMetaObject.invokeMethod(action, 'triggered')
settle()
dialog = find_type('TextureResizeDialog')
assert not dialog.property('applyEnabled')
rounding = dialog.findChild(QObject, 'powerOfTwoRounding')
rounding.setProperty('currentIndex', 1)
app.processEvents()
assert dialog.property('applyEnabled')
assert dialog.property('targetWidth') == 16384
viewer.shutdown()
window.setVisible(False)
engine.deleteLater()
app.processEvents()
'''
    result = subprocess.run([sys.executable, '-c', script], cwd=Path(__file__).parents[1],
                            env=environment, capture_output=True, text=True, timeout=30)
    assert result.returncode == 0, result.stdout + result.stderr
