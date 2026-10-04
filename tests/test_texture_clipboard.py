import os
import subprocess
import sys

import pytest


@pytest.mark.parametrize('scenario', ['copy', 'shortcut', 'guards', 'failure', 'stale'])
def test_copy_image_context_action(scenario):
    # The offscreen clipboard is process-local: never overwrite the user's clipboard.
    script = r'''
import sys
from pathlib import Path
from threading import Event
from PySide6.QtCore import QObject, QMetaObject, QThread, QTimer, QUrl, Qt
from PySide6.QtWidgets import QApplication
from PySide6.QtQml import QQmlEngine, QQmlComponent
from PySide6.QtTest import QTest
from PySide6.QtQuick import QQuickItem
from shiboken6 import getCppPointer, wrapInstance
from fivefury.ytd import Texture, TextureFormat, Ytd
import rpf_explorer
import rpf_explorer.texture_viewer as module

app = QApplication([])
engine = QQmlEngine()
images = module.TextureImageProvider()
engine.addImageProvider('textureviewer', images)
viewer = module.TextureViewerBridge(images)
component = QQmlComponent(engine, QUrl.fromLocalFile(str(
    Path(rpf_explorer.__file__).parent / 'ui/TextureDictionaryWindow.qml')))
window = component.createWithInitialProperties({'bridge': viewer})
assert window is not None, [error.toString() for error in component.errors()]
copy_action = next((obj for obj in window.findChildren(QObject)
                   if obj.property('text') == 'Copy image'), None)
assert copy_action is not None, 'Copy image is missing from the texture context menu'
clipboard = app.clipboard()
clipboard.setText('untouched')
assert not copy_action.property('enabled')
assert not viewer.copySelectedImage()
data = bytes([10, 20, 30, 64]) * 32 + bytes([50, 60, 70, 255]) * 8
textures = [Texture.from_raw(data, 8, 4, TextureFormat.A8R8G8B8, 2, name=name)
            for name in ('paint', 'detail')]

def settle():
    for _ in range(4):
        assert viewer._thread_pool.waitForDone(5000)
        app.processEvents()

viewer.open_dictionary('textures.ytd', '', Ytd(textures).to_bytes())
settle()
assert copy_action.property('enabled')
scenario = sys.argv[1]
if scenario in ('copy', 'shortcut'):
    viewer.setChannel('r')
    viewer.setMipLevel(1)
    settle()
    preview = viewer.previewUrl
    original = module.decode_texture_image
    def decode(*args):
        assert QThread.currentThread() != app.thread(), 'Decode blocked the GUI thread'
        return original(*args)
    module.decode_texture_image = decode
    if scenario == 'shortcut':
        assert copy_action.property('shortcutText') == 'Ctrl+C'
        window.requestActivate()
        QTest.qWait(50)
        list_object = window.findChild(QObject, 'textureSelectionList')
        wrapInstance(getCppPointer(list_object)[0], QQuickItem).forceActiveFocus()
        QTest.keyClick(window, Qt.Key_C, Qt.ControlModifier)
    else:
        assert QMetaObject.invokeMethod(copy_action, 'triggered')
    assert viewer.operationBusy and not copy_action.property('enabled')
    settle()
    assert clipboard.mimeData().hasImage()
    image = clipboard.image()
    assert (image.width(), image.height()) == (8, 4)
    assert image.pixelColor(0, 0).getRgb() == (30, 20, 10, 64)
    assert viewer.previewUrl == preview
    assert viewer.channel == 'r' and viewer.mipLevel == 1
    assert not viewer.modified and not viewer.canUndo and not viewer.operationBusy
    if scenario == 'shortcut':
        clipboard.setText('untouched')
        viewer.selectAllTextures()
        QTest.keyClick(window, Qt.Key_C, Qt.ControlModifier)
        settle()
        assert clipboard.text() == 'untouched'
        viewer.selectTexture(0)
        viewer.set_external_write_busy(True)
        QTest.keyClick(window, Qt.Key_C, Qt.ControlModifier)
        settle()
        assert clipboard.text() == 'untouched'
        viewer.set_external_write_busy(False)
        QTest.keyClick(window, Qt.Key_F2)
        app.processEvents()
        rename_dialog = next(obj for obj in window.findChildren(QObject)
                             if obj.metaObject().className().startswith('TextureRenameDialog_'))
        assert rename_dialog.property('visible')
        QTest.keyClick(window, Qt.Key_C, Qt.ControlModifier)
        settle()
        assert not clipboard.mimeData().hasImage()  # Leave text-copy to the dialog.
        QTest.keyClick(window, Qt.Key_Escape)
        app.processEvents()
elif scenario == 'guards':
    viewer.selectAllTextures()
    assert not copy_action.property('enabled')
    assert not viewer.copySelectedImage()
    viewer.selectTexture(0)
    viewer.set_external_write_busy(True)
    assert not copy_action.property('enabled') and not viewer.copySelectedImage()
    viewer.set_external_write_busy(False)
    viewer.selectTexture(0, Qt.ControlModifier.value)
    assert viewer.selectedCount == 0
    assert not copy_action.property('enabled') and not viewer.copySelectedImage()
    assert clipboard.text() == 'untouched'
elif scenario == 'failure':
    def fail(*args):
        raise ValueError('simulated image decode failure')
    module.decode_texture_image = fail
    assert viewer.copySelectedImage()
    settle()
    assert not viewer.operationBusy and copy_action.property('enabled')
    assert clipboard.text() == 'untouched'
    assert 'simulated image decode failure' in viewer.status
    assert not viewer.modified
else:
    started, release = Event(), Event()
    original = module.decode_texture_image
    def delayed(*args):
        started.set()
        assert release.wait(5)
        return original(*args)
    module.decode_texture_image = delayed
    assert viewer.copySelectedImage()
    assert started.wait(5)
    try:
        assert not viewer.copySelectedImage()
        responsive = []
        QTimer.singleShot(0, lambda: responsive.append(True))
        app.processEvents()
        assert responsive
        viewer.close_document()
    finally:
        release.set()
    settle()
    assert clipboard.text() == 'untouched'
    assert not viewer.operationBusy and not viewer.hasDocument
viewer.shutdown()
window.setVisible(False)
engine.deleteLater()
app.processEvents()
'''
    environment = dict(os.environ, QT_QPA_PLATFORM='offscreen', QT_QUICK_BACKEND='software')
    result = subprocess.run([sys.executable, '-c', script, scenario], env=environment,
                            capture_output=True, text=True, timeout=30)
    assert result.returncode == 0, result.stdout + result.stderr
