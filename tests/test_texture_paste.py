import os
import subprocess
import sys

import pytest


@pytest.mark.parametrize('scenario', ['add', 'opaque', 'replace', 'validation', 'snapshot', 'cancel', 'stale', 'failure', 'multiple', 'choice_stable'])
def test_paste_image_dialog(scenario):
    script = r'''
import sys
from pathlib import Path
from PySide6.QtCore import QObject, QMetaObject, QPoint, QUrl, Qt
from PySide6.QtGui import QImage, QColor, QFontDatabase
from PySide6.QtWidgets import QApplication
from PySide6.QtQml import QQmlEngine, QQmlComponent
from PySide6.QtTest import QTest
from PySide6.QtQuick import QQuickItem
from shiboken6 import getCppPointer, wrapInstance
from fivefury.ytd import Texture, TextureFormat, Ytd
import rpf_explorer
import rpf_explorer.texture_viewer as module
from rpf_explorer.ytd_empty_ad_hoc import empty_ytd_bytes
from rpf_explorer.ytd_saving import serialize_ytd

app = QApplication([])
font_dir = Path(rpf_explorer.__file__).parent / 'ui/fonts'
for font in font_dir.glob('*.ttf'):
    assert QFontDatabase.addApplicationFont(str(font)) >= 0
engine = QQmlEngine()
images = module.TextureImageProvider()
engine.addImageProvider('textureviewer', images)
viewer = module.TextureViewerBridge(images)
component = QQmlComponent(engine, QUrl.fromLocalFile(str(
    Path(rpf_explorer.__file__).parent / 'ui/TextureDictionaryWindow.qml')))
window = component.createWithInitialProperties({'bridge': viewer})
assert window is not None, [error.toString() for error in component.errors()]
paste_action = next((obj for obj in window.findChildren(QObject)
                     if obj.property('text') == 'Paste…'), None)
assert paste_action is not None, 'Paste action missing'
clipboard = app.clipboard()
clipboard.setText('not an image')
assert not paste_action.property('enabled') and not viewer.requestPasteImage()

def settle():
    for _ in range(4):
        assert viewer._thread_pool.waitForDone(5000)
        app.processEvents()

scenario = sys.argv[1]
original = Ytd([Texture.from_raw(bytes([0, 0, 255, 255]) * 16, 4, 4,
                                TextureFormat.A8R8G8B8, 1, name=name)
                for name in ('paint', 'keep')])
viewer.open_dictionary('textures.ytd', '', empty_ytd_bytes('gta5_enhanced')
                       if scenario in ('add', 'opaque', 'snapshot') else original.to_bytes())
settle()
if scenario == 'multiple':
    viewer.selectAllTextures()
before = tuple(viewer._dictionary.textures)
selection = set(viewer._selected_rows)
image = QImage(50, 19, QImage.Format_RGBA8888)
image.fill(QColor(220, 40, 70, 255 if scenario == 'opaque' else 100))
clipboard.setImage(image)
app.processEvents()
assert paste_action.property('enabled')
assert paste_action.property('shortcutText') == 'Ctrl+V'
window.requestActivate()
QTest.qWait(50)
if scenario == 'add':
    QTest.mouseClick(window, Qt.RightButton, Qt.NoModifier, QPoint(100, 180))
    app.processEvents()
    context = next(obj for obj in window.findChildren(QObject)
                   if obj.metaObject().className().startswith('TextureContextMenu_'))
    assert context.property('visible'), 'Empty texture rail has no context menu'
    QMetaObject.invokeMethod(context, 'close')
    QTest.keyClick(window, Qt.Key_V, Qt.ControlModifier)
else:
    assert QMetaObject.invokeMethod(paste_action, 'triggered')
app.processEvents()
dialog = window.findChild(QObject, 'texturePasteDialog')
assert dialog is not None and dialog.property('visible')
assert viewer.pastePending and not viewer.canPasteImage
assert tuple(viewer._dictionary.textures) == before
mode = dialog.findChild(QObject, 'pasteMode')
name = dialog.findChild(QObject, 'pasteName')
assert name.property('height') >= 28, 'Name field must have a usable hit target'
apply = next(obj for obj in dialog.findChildren(QObject) if obj.property('text') == 'Paste')
if scenario == 'replace':
    assert viewer.pasteCanReplace
    target = viewer._selected_index
    assert mode.property('currentValue') == 'replace'
    assert not name.property('enabled')
    assert QMetaObject.invokeMethod(apply, 'clicked')
    settle()
    after = viewer._dictionary.textures
    assert len(after) == 2 and after[target].name == before[target].name
    assert (after[target].width, after[target].height) == (64, 16)
    assert after[target].format.name == 'BC3' and after[target].mip_count == 3
    assert after[1-target] is before[1-target]
    assert after[target].usage == before[target].usage
    assert after[target].usage_flags == before[target].usage_flags
elif scenario in ('add', 'opaque', 'snapshot', 'multiple', 'choice_stable'):
    if scenario == 'choice_stable':
        wrapInstance(getCppPointer(mode)[0], QQuickItem).forceActiveFocus()
        QTest.keyClick(window, Qt.Key_Down)
        assert mode.property('currentValue') == 'add'
        wrapInstance(getCppPointer(name)[0], QQuickItem).forceActiveFocus()
        QTest.keyClick(window, Qt.Key_A, Qt.ControlModifier)
        for char in 'custom_name':
            QTest.keyClick(window, Qt.Key(ord(char.upper())))
    else:
        assert not viewer.pasteCanReplace and mode.property('currentValue') == 'add'
        name.setProperty('text', 'custom_name')
    app.processEvents()
    if scenario in ('snapshot', 'choice_stable'):
        clipboard.setText('changed after dialog opened')
        app.processEvents()
        assert mode.property('currentValue') == 'add'
        assert name.property('text') == 'custom_name'
    assert apply.property('enabled')
    assert QMetaObject.invokeMethod(apply, 'clicked')
    settle()
    assert viewer._dictionary.names() == [t.name for t in before] + ['custom_name'], viewer.status
    assert viewer._dictionary.game == ('gta5' if scenario in ('multiple', 'choice_stable') else 'gta5_enhanced')
    texture = viewer._dictionary.textures[-1]
    assert (texture.width, texture.height, texture.mip_count) == (64, 16, 3)
    assert texture.format.name == ('BC1' if scenario == 'opaque' else 'BC3')
    rgba, w, h = module._to_texfury_texture(texture).to_rgba(0)
    assert rgba[0] > 190 and rgba[2] < 100
    assert rgba[3] == 255 if scenario == 'opaque' else 80 <= rgba[3] <= 120
elif scenario == 'validation':
    mode.setProperty('currentIndex', 1)
    for value in ('', '   ', 'PAINT', 'bad\x00name', 'a'*256):
        assert viewer.pasteNameError(value)
        assert not viewer.pasteImage(False, value)
        assert viewer.pastePending and tuple(viewer._dictionary.textures) == before
    name.setProperty('text', 'paint')
    app.processEvents()
    assert not apply.property('enabled')
    QTest.keyClick(window, Qt.Key_Escape)
elif scenario == 'cancel':
    QTest.keyClick(window, Qt.Key_Escape)
elif scenario == 'stale':
    viewer.close_document()
    settle()
    assert not viewer.pasteImage(True, 'ignored')
    assert not viewer.hasDocument and not viewer.pastePending
elif scenario == 'failure':
    def fail(*args):
        raise ValueError('simulated paste failure')
    module.texture_from_clipboard_image = fail
    assert viewer.pasteImage(True, '')
    settle()
    assert tuple(viewer._dictionary.textures) == before and not viewer.modified
    assert 'simulated paste failure' in viewer.status
    assert not viewer.operationBusy and not viewer.canUndo
if scenario in ('add', 'opaque', 'replace', 'snapshot', 'multiple', 'choice_stable'):
    assert viewer.modified and len(viewer._undo_stack) == 1
    assert serialize_ytd(viewer._dictionary)
    assert viewer.undo()
    settle()
    assert tuple(viewer._dictionary.textures) == before
    assert viewer._selected_rows == selection and not viewer.modified
elif scenario in ('cancel', 'validation'):
    app.processEvents()
    assert not viewer.pastePending and not viewer.modified
    assert tuple(viewer._dictionary.textures) == before and not viewer.canUndo
viewer.shutdown()
window.setVisible(False)
engine.deleteLater()
app.processEvents()
'''
    environment = dict(os.environ, QT_QPA_PLATFORM='offscreen', QT_QUICK_BACKEND='software')
    result = subprocess.run([sys.executable, '-c', script, scenario], env=environment,
                            capture_output=True, text=True, timeout=30)
    assert result.returncode == 0, result.stdout + result.stderr
