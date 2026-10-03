import pytest
from PySide6.QtCore import QUrl
from PySide6.QtGui import QImage, QColor
from PySide6.QtTest import QSignalSpy
from fivefury import GameTarget

from rpf_explorer.ytd_empty_ad_hoc import empty_ytd_bytes, read_ytd


def image_file(path, width=8, height=4):
    image = QImage(width, height, QImage.Format_RGBA8888)
    image.fill(QColor(230, 40, 70, 100))
    assert image.save(str(path))
    return QUrl.fromLocalFile(str(path))


@pytest.mark.parametrize('game', list(GameTarget))
def test_drop_into_empty_ytd_imports_saves_and_undoes_one_batch(
    texture_sessions, settle_texture_sessions, tmp_path, game,
):
    viewer = texture_sessions.activeBridge.textureViewer
    path = tmp_path / 'test.ytd'
    path.write_bytes(empty_ytd_bytes(game))
    viewer.open_dictionary(path.name, str(path), path.read_bytes())
    settle_texture_sessions()
    urls = [image_file(tmp_path / 'paint.png', 7, 5), image_file(tmp_path / 'detail.bmp')]
    assert viewer.importDroppedImages(urls)
    assert viewer.operationBusy
    settle_texture_sessions()
    assert viewer.textureCount == viewer.selectedCount == 2, viewer.status
    assert viewer._dictionary.game == game
    assert viewer._dictionary.names() == ['paint', 'detail']
    assert (viewer._dictionary.textures[0].width, viewer._dictionary.textures[0].height) == (7, 5)
    assert len(viewer._undo_stack) == 1
    assert read_ytd(path.read_bytes()).textures == []  # Not autosaved.
    assert viewer.saveYtd()
    settle_texture_sessions()
    assert set(read_ytd(path.read_bytes()).names()) == {'paint', 'detail'}
    assert viewer.undo()
    settle_texture_sessions()
    assert viewer.textureCount == viewer.selectedCount == 0
    assert viewer.selectedIndex == -1 and not viewer.previewLoading


@pytest.mark.parametrize('accept', [False, True])
def test_name_collisions_require_one_global_confirmation(
    texture_sessions, texture_dictionary, settle_texture_sessions, tmp_path, accept,
):
    viewer = texture_sessions.activeBridge.textureViewer
    original = texture_dictionary('paint', 'detail', 'keep')
    viewer.open_dictionary('test.ytd', '', original.to_bytes())
    settle_texture_sessions()
    before = tuple(viewer._dictionary.textures)
    spy = QSignalSpy(viewer.imageImportConfirmationRequested)
    urls = [image_file(tmp_path / 'PAINT.png'), image_file(tmp_path / 'detail.png'),
            image_file(tmp_path / 'new.png')]
    assert viewer.importDroppedImages(urls)
    assert spy.count() == 1
    assert viewer.importConflictCount == 2
    assert tuple(viewer._dictionary.textures) == before
    assert not viewer.modified
    viewer.confirmImageImport(accept)
    settle_texture_sessions()
    assert spy.count() == 1
    if not accept:
        assert tuple(viewer._dictionary.textures) == before
        assert not viewer.modified and not viewer.canUndo
    else:
        result = {texture.name: texture for texture in viewer._dictionary.textures}
        assert set(result) == {'paint', 'detail', 'keep', 'new'}
        assert result['keep'] is next(texture for texture in before if texture.name == 'keep')
        assert result['paint'].width == result['detail'].width == 8
        assert viewer.undo()
        settle_texture_sessions()
        assert tuple(viewer._dictionary.textures) == before
        assert not viewer.modified


def test_bad_image_rolls_back_whole_import(texture_sessions, texture_dictionary, settle_texture_sessions, tmp_path):
    viewer = texture_sessions.activeBridge.textureViewer
    viewer.open_dictionary('test.ytd', '', texture_dictionary('keep').to_bytes())
    settle_texture_sessions()
    before = tuple(viewer._dictionary.textures)
    bad = tmp_path / 'bad.png'
    bad.write_bytes(b'not an image')
    assert viewer.importDroppedImages([image_file(tmp_path / 'good.png'), QUrl.fromLocalFile(str(bad))])
    settle_texture_sessions()
    assert tuple(viewer._dictionary.textures) == before
    assert not viewer.modified and not viewer.canUndo and not viewer.operationBusy
    assert 'bad.png' in viewer.status


def test_incoming_duplicates_use_one_confirmation_and_last_file(
    texture_sessions, settle_texture_sessions, tmp_path,
):
    viewer = texture_sessions.activeBridge.textureViewer
    viewer.open_dictionary('test.ytd', '', empty_ytd_bytes('gta5'))
    settle_texture_sessions()
    spy = QSignalSpy(viewer.imageImportConfirmationRequested)
    urls = [image_file(tmp_path / 'same.png', 8, 8), image_file(tmp_path / 'same.bmp', 16, 4)]
    assert viewer.importDroppedImages(urls)
    assert spy.count() == 1 and viewer.importConflictCount == 1
    assert viewer.confirmImageImport(True)
    settle_texture_sessions()
    assert viewer._dictionary.names() == ['same']
    assert viewer._dictionary.textures[0].width == 16


def test_stale_confirmation_cannot_modify_another_document(
    texture_sessions, texture_dictionary, settle_texture_sessions, tmp_path,
):
    viewer = texture_sessions.activeBridge.textureViewer
    viewer.open_dictionary('first.ytd', '', texture_dictionary('paint').to_bytes())
    settle_texture_sessions()
    assert viewer.importDroppedImages([image_file(tmp_path / 'paint.png')])
    viewer.open_dictionary('second.ytd', '', empty_ytd_bytes('gta5_enhanced'))
    settle_texture_sessions()
    assert not viewer.confirmImageImport(True)
    assert viewer.textureCount == 0 and not viewer.modified


def test_drop_rejects_unloaded_busy_remote_folder_and_unsupported(
    texture_sessions, settle_texture_sessions, tmp_path,
):
    viewer = texture_sessions.activeBridge.textureViewer
    url = image_file(tmp_path / 'test.png')
    assert not viewer.importDroppedImages([url])
    viewer.open_dictionary('test.ytd', '', empty_ytd_bytes('gta5'))
    settle_texture_sessions()
    for urls in ([], [QUrl('https://example.com/test.png')], [QUrl.fromLocalFile(str(tmp_path))],
                 [QUrl.fromLocalFile(str(tmp_path / 'missing.png'))],
                 [QUrl.fromLocalFile(str(tmp_path / 'unknown.txt'))]):
        assert not viewer.importDroppedImages(urls)
    viewer.set_external_write_busy(True)
    assert not viewer.importDroppedImages([url])
    assert not viewer.canAcceptImageDrop([url])
    viewer.set_external_write_busy(False)
    assert not viewer.modified and viewer.textureCount == 0


@pytest.mark.parametrize('extension', ['png', 'jpg', 'jpeg', 'bmp', 'tga', 'webp', 'dds'])
def test_supported_formats_import_real_encoded_files(
    texture_sessions, settle_texture_sessions, tmp_path, extension,
):
    from texfury import Texture, BCFormat

    path = tmp_path / f'input.{extension}'
    if extension == 'dds':
        source = Texture.from_raw(bytes([30, 60, 90, 100]) * 16, 4, 4,
                                  BCFormat.A8R8G8B8, 1, [0], [64], name='source')
        path.write_bytes(source.to_dds_bytes())
    elif extension == 'tga':
        # Synthetic uncompressed 4x4 32-bit TGA, top-left origin, eight alpha bits.
        path.write_bytes(bytes.fromhex('000002000000000000000000040004002028')
                         + bytes([30, 60, 90, 100]) * 16)
    else:
        image_file(path, 4, 4)
    viewer = texture_sessions.activeBridge.textureViewer
    viewer.open_dictionary('test.ytd', '', empty_ytd_bytes('gta5'))
    settle_texture_sessions()
    assert viewer.importDroppedImages([QUrl.fromLocalFile(str(path))])
    settle_texture_sessions()
    assert viewer.textureCount == 1, viewer.status
    result = viewer._dictionary.textures[0]
    assert result.name == 'input'
    assert (result.width, result.height) == (4, 4)
    if extension == 'dds':
        assert result.data == source.data
        assert result.format.value == source.format.value
        assert result.mip_count == source.mip_count
    else:
        from rpf_explorer.texture_viewer import decode_texture_image

        image = decode_texture_image(result)
        alpha = image.pixelColor(0, 0).alpha()
        assert alpha == 255 if extension in ('jpg', 'jpeg', 'bmp') else 80 <= alpha <= 120


def test_dropped_images_save_inside_rpf(texture_sessions, texture_dictionary, settle_texture_sessions, tmp_path):
    from fivefury import RpfArchive

    path = tmp_path / 'textures.rpf'
    archive = RpfArchive.empty(path.name)
    archive.file('test.ytd', empty_ytd_bytes('gta5_enhanced'))
    archive.file('keep.txt', b'untouched')
    archive.save(path)
    bridge = texture_sessions.activeBridge
    bridge.openArchive(str(path))
    row = next(row for row in range(bridge.entriesModel.rowCount())
               if bridge.entriesModel.entry_at(row).name == 'test.ytd')
    bridge.activateEntry(row)
    settle_texture_sessions()
    viewer = bridge.textureViewer
    assert viewer.importDroppedImages([image_file(tmp_path / 'paint.png')])
    settle_texture_sessions()
    assert viewer.modified
    assert viewer.saveYtd()
    settle_texture_sessions()
    assert not viewer.modified, viewer.status
    with RpfArchive.from_path(path) as saved:
        assert saved.find_entry('keep.txt').read_standalone() == b'untouched'
        result = read_ytd(saved.find_entry('test.ytd').read_standalone())
        assert result.names() == ['paint']
        assert result.game == GameTarget.GTA5_ENHANCED


def test_qml_drop_on_rail_and_preview_uses_one_global_dialog(tmp_path):
    import os
    import subprocess
    import sys

    path = tmp_path / 'paint.png'
    image_file(path)
    script = r'''
import sys
from pathlib import Path
from PySide6.QtCore import QUrl, Qt, QObject, QPoint, QPointF, QMimeData, QMetaObject
from PySide6.QtGui import QDragEnterEvent, QDropEvent
from PySide6.QtWidgets import QApplication
from PySide6.QtQml import QQmlEngine, QQmlComponent
from PySide6.QtTest import QTest, QSignalSpy
import rpf_explorer
from rpf_explorer.texture_viewer import TextureViewerBridge, TextureImageProvider
from rpf_explorer.ytd_empty_ad_hoc import empty_ytd_bytes

app = QApplication([])
engine = QQmlEngine()
images = TextureImageProvider()
engine.addImageProvider('textureviewer', images)
viewer = TextureViewerBridge(images)
component = QQmlComponent(engine, QUrl.fromLocalFile(str(Path(rpf_explorer.__file__).parent / 'ui/TextureDictionaryWindow.qml')))
window = component.createWithInitialProperties({'bridge': viewer})
assert window is not None, [error.toString() for error in component.errors()]

def settle():
    for _ in range(4):
        assert viewer._thread_pool.waitForDone(5000)
        app.processEvents()
    QTest.qWait(20)

def drop(x):
    mime = QMimeData()
    mime.setUrls([QUrl.fromLocalFile(sys.argv[1])])
    enter = QDragEnterEvent(QPoint(x, 220), Qt.CopyAction, mime, Qt.LeftButton, Qt.NoModifier)
    app.sendEvent(window, enter)
    assert enter.isAccepted(), 'Drag was not accepted'
    event = QDropEvent(QPointF(x, 220), Qt.CopyAction, mime, Qt.LeftButton, Qt.NoModifier)
    app.sendEvent(window, event)
    assert event.isAccepted(), 'Drop was not accepted'
    settle()

viewer.open_dictionary('empty.ytd', '', empty_ytd_bytes('gta5'))
settle()
window.show()
QTest.qWait(50)
drop(140)  # Texture rail.
assert viewer.textureCount == 1
assert viewer.undo()
settle()
assert viewer.textureCount == 0
drop(800)  # Preview.
assert viewer.textureCount == 1
spy = QSignalSpy(viewer.imageImportConfirmationRequested)
drop(800)
assert spy.count() == 1 and viewer.importConfirmationPending
dialog = window.findChild(QObject, 'textureImportConfirmation')
assert dialog is not None and dialog.property('visible')
QMetaObject.invokeMethod(dialog, 'reject')
settle()
assert not viewer.importConfirmationPending and len(viewer._undo_stack) == 1
drop(140)
assert spy.count() == 2
dialog = window.findChild(QObject, 'textureImportConfirmation')
button = next(obj for obj in dialog.findChildren(QObject) if obj.property('text') == 'Import')
QMetaObject.invokeMethod(button, 'clicked')
settle()
assert not viewer.importConfirmationPending
assert viewer.textureCount == 1 and len(viewer._undo_stack) == 2
viewer.shutdown()
window.setVisible(False)
engine.deleteLater()
app.processEvents()
'''
    env = dict(os.environ, QT_QPA_PLATFORM='offscreen', QT_QUICK_BACKEND='software')
    result = subprocess.run([sys.executable, '-c', script, str(path)], env=env,
                            capture_output=True, text=True, timeout=30)
    assert result.returncode == 0, result.stdout + result.stderr
