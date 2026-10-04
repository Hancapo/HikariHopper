import pytest
from fivefury import GameTarget
from fivefury.ytd import Texture as YtdTexture, TextureFormat, Ytd
from texfury import Texture, BCFormat

from rpf_explorer.texture_viewer import _to_texfury_texture


def make_texture(format, *, name='paint', alpha=255, last_alpha=None):
    chunks = []
    for index, (width, height, rgb) in enumerate(((16, 8, (0, 0, 255)),
                                                (8, 4, (0, 255, 0)),
                                                (4, 2, (255, 0, 0)))):
        level_alpha = last_alpha if index == 2 and last_alpha is not None else alpha
        if format == BCFormat.BC2:
            from texfury.formats import mip_data_size
            # Synthetic DXT3 blocks: explicit 4-bit alpha + a solid RGB565 color.
            # TexFury reads DXT3 but does not encode it through to_format().
            endpoint = { (0, 0, 255): b'\x00\xf8', (0, 255, 0): b'\xe0\x07', (255, 0, 0): b'\x1f\x00' }[rgb]
            block = bytes([(level_alpha // 17) * 17]) * 8 + endpoint * 2 + bytes(4)
            chunks.append(block * (mip_data_size(width, height, format) // 16))
            continue
        pixels = bytes((*rgb, level_alpha)) * width * height
        source = Texture.from_raw(pixels, width, height, BCFormat.A8R8G8B8, 1, [0], [len(pixels)])
        chunks.append(source.to_format(format, quality=1.0, generate_mipmaps=False).data)
    return YtdTexture.from_raw(b''.join(chunks), 16, 8, TextureFormat(format.value), 3, name=name)


@pytest.mark.parametrize('game', list(GameTarget))
@pytest.mark.parametrize('format', [BCFormat.BC2, BCFormat.BC3])
def test_optimization_preserves_authored_mips_and_undo(
    texture_sessions, settle_texture_sessions, game, format,
):
    viewer = texture_sessions.activeBridge.textureViewer
    source = make_texture(format)
    viewer._dictionary_loaded((viewer._generation, Ytd([source], game=game)))
    settle_texture_sessions()
    assert viewer.optimizeSelectedCompression()
    settle_texture_sessions()
    result = viewer._dictionary.textures[0]
    assert result.format == TextureFormat.BC1
    assert (result.width, result.height, result.mip_count) == (16, 8, 3)
    assert result.usage == source.usage and result.usage_flags == source.usage_flags
    assert len(result.data) == len(source.data) // 2
    decoder = _to_texfury_texture(result)
    for mip, (size, rgb) in enumerate((((16, 8), (255, 0, 0)), ((8, 4), (0, 255, 0)), ((4, 2), (0, 0, 255)))):
        rgba, width, height = decoder.to_rgba(mip)
        assert (width, height) == size
        assert tuple(rgba[:3]) == rgb  # Stored lower mips, not a regenerated red chain.
    assert viewer._dictionary.game == game
    assert viewer.modified and viewer.undo()
    settle_texture_sessions()
    assert viewer._dictionary.textures[0] is source and not viewer.modified


@pytest.mark.parametrize('format,alpha,last_alpha,expected', [
    (BCFormat.BC1, 255, None, BCFormat.BC1),
    (BCFormat.BC3, 100, None, BCFormat.BC3),
    (BCFormat.BC3, 255, 100, BCFormat.BC3),
    (BCFormat.BC2, 255, 100, BCFormat.BC3),
])
def test_checks_alpha_in_all_mips_and_leaves_correct_formats_untouched(
    texture_sessions, settle_texture_sessions, format, alpha, last_alpha, expected,
):
    viewer = texture_sessions.activeBridge.textureViewer
    source = make_texture(format, alpha=alpha, last_alpha=last_alpha)
    viewer._dictionary_loaded((viewer._generation, Ytd([source])))
    settle_texture_sessions()
    assert viewer.optimizeSelectedCompression()
    settle_texture_sessions()
    result = viewer._dictionary.textures[0]
    assert result.format.value == expected.value
    if format == expected:
        assert result is source and not viewer.modified and not viewer.canUndo
    if last_alpha is not None:
        rgba, _, _ = _to_texfury_texture(result).to_rgba(2)
        assert rgba[3] < 255


def test_batch_skips_nonconventional_formats_and_preserves_selection(
    texture_sessions, settle_texture_sessions,
):
    viewer = texture_sessions.activeBridge.textureViewer
    sources = [make_texture(format, name=f'tex_{index}') for index, format in enumerate(
        (BCFormat.BC3, BCFormat.A8R8G8B8, BCFormat.BC5, BCFormat.BC4, BCFormat.BC7))]
    viewer._dictionary_loaded((viewer._generation, Ytd(sources, game='gta5_enhanced')))
    settle_texture_sessions()
    viewer.selectAllTextures()
    assert viewer.optimizeSelectedCompression()
    settle_texture_sessions()
    assert viewer._dictionary.textures[0].format == TextureFormat.BC1
    assert all(viewer._dictionary.textures[i] is sources[i] for i in range(1, 5))
    assert viewer._selected_rows == set(range(5)) and len(viewer._undo_stack) == 1
    assert viewer.undo()
    settle_texture_sessions()
    viewer.selectTexture(1)
    assert not viewer.canOptimizeCompression
    assert not viewer.optimizeSelectedCompression()
    assert not viewer.modified and not viewer.canUndo


def test_optimization_error_keeps_entire_batch_unchanged(
    texture_sessions, settle_texture_sessions, monkeypatch,
):
    import rpf_explorer.texture_viewer as module

    viewer = texture_sessions.activeBridge.textureViewer
    sources = [make_texture(BCFormat.BC3, name=name) for name in ('good', 'bad')]
    viewer._dictionary_loaded((viewer._generation, Ytd(sources)))
    settle_texture_sessions()
    viewer.selectAllTextures()
    original = module.optimize_texture_compression
    def fail_second(texture):
        if texture.name == 'bad':
            raise ValueError('simulated optimization failure')
        return original(texture)
    monkeypatch.setattr(module, 'optimize_texture_compression', fail_second)
    assert viewer.optimizeSelectedCompression()
    settle_texture_sessions()
    assert all(a is b for a, b in zip(sources, viewer._dictionary.textures))
    assert not viewer.modified and not viewer.canUndo and not viewer.operationBusy
    assert 'simulated optimization failure' in viewer.status


def test_dxt1_punch_through_alpha_is_not_treated_as_opaque():
    from rpf_explorer.texture_compression import optimize_texture_compression

    # Endpoint ordering enables DXT1 punch-through; all sixteen indices select alpha.
    source = Texture.from_raw(bytes.fromhex('0000ffffffffffff'), 4, 4,
                              BCFormat.BC1A, 1, [0], [8])
    result = optimize_texture_compression(source)
    assert result.format == BCFormat.BC3
    assert result.to_rgba()[0][3::4] == bytes(16)


def test_every_existing_mip_is_encoded_at_maximum_quality(tmp_path, monkeypatch):
    from texfury import _native
    from rpf_explorer.texture_compression import optimize_texture_compression

    source = _to_texfury_texture(make_texture(BCFormat.BC3))
    calls = []
    original = _native.compress
    def encode(image, format, mipmaps, minimum, quality, filter):
        calls.append((mipmaps, quality))
        return original(image, format, mipmaps, minimum, quality, filter)
    monkeypatch.setattr(_native, 'compress', encode)
    result = optimize_texture_compression(source)
    assert result.format == BCFormat.BC1
    assert calls == [(False, 1.0)] * 3


def test_context_menu_optimizes_selection_and_disables_excluded_formats():
    import os
    import subprocess
    import sys

    script = r'''
from pathlib import Path
from PySide6.QtCore import QObject, QMetaObject, QUrl
from PySide6.QtWidgets import QApplication
from PySide6.QtQml import QQmlEngine, QQmlComponent
from texfury import Texture, BCFormat
from fivefury.ytd import Ytd
import rpf_explorer
from rpf_explorer.texture_viewer import TextureViewerBridge, TextureImageProvider, _to_fivefury_texture

app = QApplication([])
engine = QQmlEngine()
images = TextureImageProvider()
engine.addImageProvider('textureviewer', images)
viewer = TextureViewerBridge(images)
component = QQmlComponent(engine, QUrl.fromLocalFile(str(Path(rpf_explorer.__file__).parent / 'ui/TextureDictionaryWindow.qml')))
window = component.createWithInitialProperties({'bridge': viewer})
assert window is not None, [error.toString() for error in component.errors()]
action = next((obj for obj in window.findChildren(QObject) if obj.property('text') == 'Optimize compression'), None)
assert action is not None, 'Optimize compression missing from context menu'
assert not action.property('enabled')
raw = Texture.from_raw(bytes([0, 0, 255, 255]) * 16, 4, 4, BCFormat.A8R8G8B8, 1, [0], [64], 'paint')
source = _to_fivefury_texture(raw.to_format(BCFormat.BC3, generate_mipmaps=False))
viewer._dictionary_loaded((viewer._generation, Ytd([source])))
def settle():
    for _ in range(4):
        assert viewer._thread_pool.waitForDone(5000)
        app.processEvents()
settle()
assert action.property('enabled')
assert QMetaObject.invokeMethod(action, 'triggered')
assert not action.property('enabled') and viewer.operationBusy
settle()
assert viewer._dictionary.textures[0].format.name == 'BC1'
assert viewer.undo()
settle()
assert viewer._dictionary.textures[0] is source
viewer._dictionary_loaded((viewer._generation, Ytd([_to_fivefury_texture(raw)])))
settle()
assert not action.property('enabled')
viewer.shutdown()
window.setVisible(False)
engine.deleteLater()
app.processEvents()
'''
    env = dict(os.environ, QT_QPA_PLATFORM='offscreen', QT_QUICK_BACKEND='software')
    result = subprocess.run([sys.executable, '-c', script], env=env, capture_output=True, text=True, timeout=30)
    assert result.returncode == 0, result.stdout + result.stderr
