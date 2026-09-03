from __future__ import annotations

from threading import Event

from fivefury.ytd import Texture, TextureFormat, Ytd

from rpf_explorer.bridge import ExplorerBridge
from rpf_explorer.texture_viewer import (
    TextureImageProvider,
    TextureListModel,
    TextureViewerBridge,
    _resize_texture,
    _to_fivefury_texture,
    _to_texfury_texture,
    decode_texture_image,
    mip_count_for_dimensions,
    mip_dimensions_for_minimum,
    texfury_mip_stop_size,
)


def _red_texture(name: str = "red") -> Texture:
    return Texture.from_raw(
        bytes([0, 0, 255, 255]) * 4,
        2,
        2,
        TextureFormat.A8R8G8B8,
        1,
        name=name,
    )


def _mipped_texture(name: str = "mipped") -> Texture:
    return Texture.from_raw(
        bytes([0, 0, 255, 255]) * 4 + bytes([0, 255, 0, 255]),
        2,
        2,
        TextureFormat.A8R8G8B8,
        2,
        name=name,
    )


def test_texfury_decodes_fivefury_texture_channels() -> None:
    texture = _red_texture()

    rgba = decode_texture_image(texture)
    red = decode_texture_image(texture, "r")
    green = decode_texture_image(texture, "g")
    alpha = decode_texture_image(texture, "a")

    assert rgba.size().toTuple() == (2, 2)
    assert rgba.pixelColor(0, 0).getRgb() == (255, 0, 0, 255)
    assert red.pixelColor(0, 0).getRgb() == (255, 255, 255, 255)
    assert green.pixelColor(0, 0).getRgb() == (0, 0, 0, 255)
    assert alpha.pixelColor(0, 0).getRgb() == (255, 255, 255, 255)


def test_texfury_decodes_requested_mip_level() -> None:
    mip = decode_texture_image(_mipped_texture(), mip=1)

    assert mip.size().toTuple() == (1, 1)
    assert mip.pixelColor(0, 0).getRgb() == (0, 255, 0, 255)


def test_texture_list_model_exposes_viewer_metadata() -> None:
    model = TextureListModel()
    model.set_textures([_red_texture("paint")])
    index = model.index(0, 0)

    assert model.data(index, model.NAME) == "paint"
    assert model.data(index, model.DIMENSIONS) == "2 × 2"
    assert model.data(index, model.FORMAT) == "ARGB 32-bit"
    assert model.data(index, model.MIP_COUNT) == 1
    assert model.data(index, model.DATA_SIZE_LABEL) == "16 B"
    assert model.data(index, model.THUMBNAIL_URL) == ""

    model.set_thumbnail(0, "image://textureviewer/session/thumbnail/0")

    assert model.data(index, model.THUMBNAIL_URL).endswith("/thumbnail/0")


def test_mip_chain_stops_when_short_edge_reaches_minimum() -> None:
    assert mip_count_for_dimensions(2048, 2048, 4) == 10
    assert mip_count_for_dimensions(1024, 512, 4) == 8
    assert mip_count_for_dimensions(256, 512, 4) == 7
    assert mip_dimensions_for_minimum(256, 512, 4)[-1] == (4, 8)
    assert mip_dimensions_for_minimum(512, 256, 4)[-1] == (8, 4)
    assert mip_count_for_dimensions(4, 4, 4) == 1


def test_mip_chain_does_not_cross_minimum_for_non_power_of_two_sizes() -> None:
    assert mip_dimensions_for_minimum(100, 200, 4)[-1] == (6, 12)


def test_mip_chain_terminates_at_one_pixel() -> None:
    expected = ((4, 4), (2, 2), (1, 1))

    assert mip_dimensions_for_minimum(4, 4, 1) == expected
    assert mip_dimensions_for_minimum(4, 4, 0) == expected
    assert mip_dimensions_for_minimum(1, 1, 1) == ((1, 1),)


def test_texfury_stop_size_produces_the_requested_short_edge() -> None:
    from PIL import Image
    from texfury import BCFormat, Texture

    texture = Texture.from_pil(
        Image.new("RGBA", (16, 32)),
        format=BCFormat.BC1,
        min_mip_size=texfury_mip_stop_size(16, 32, 4),
        resize_to_pot=False,
    )

    assert texture.mip_count == 3


def test_texfury_resize_returns_valid_fivefury_texture() -> None:
    from texfury import MipFilter

    source = _red_texture("paint")
    editable = _to_texfury_texture(source)
    resized = _resize_texture(
        editable,
        width=4,
        height=4,
        mip_filter=MipFilter.MITCHELL,
        min_mip_size=1,
        generate_mipmaps=True,
    )
    result = _to_fivefury_texture(resized, source)

    assert result.name == "paint"
    assert (result.width, result.height) == (4, 4)
    assert result.mip_count == 3
    assert result.usage == source.usage
    assert result.validate().valid


def test_texture_bridge_undoes_rename_and_remove_operations() -> None:
    bridge = TextureViewerBridge(TextureImageProvider())
    dictionary = Ytd([_red_texture("paint"), _red_texture("detail")])
    bridge._dictionary_loaded((0, dictionary))

    assert bridge.renameSelected("body")
    assert bridge.selectedName == "body"
    assert bridge.modified
    assert bridge.canUndo
    assert bridge.undoLabel == "Rename texture"
    assert not bridge.renameSelected("detail")
    assert bridge.removeSelected()
    assert bridge.textureCount == 1
    assert bridge.selectedName == "detail"
    assert bridge.undoLabel == "Remove texture"

    assert bridge.undo()
    assert bridge.textureCount == 2
    assert bridge.selectedName == "body"
    assert bridge.modified

    assert bridge.undo()
    assert bridge.selectedName == "paint"
    assert not bridge.modified
    assert not bridge.canUndo

    bridge.shutdown()


def test_undo_after_save_creates_pending_changes_again() -> None:
    bridge = TextureViewerBridge(TextureImageProvider())
    bridge._dictionary_loaded((0, Ytd([_red_texture("paint")])))

    assert bridge.renameSelected("body")
    bridge._save_completed((0, "textures.ytd", None))
    assert not bridge.modified

    assert bridge.undo()
    assert bridge.selectedName == "paint"
    assert bridge.modified

    bridge.shutdown()


def test_saving_is_distinct_from_pending_changes() -> None:
    bridge = TextureViewerBridge(TextureImageProvider())
    bridge._dictionary_loaded((0, Ytd([_red_texture("paint")])))
    assert bridge.renameSelected("body")

    started = Event()
    release = Event()

    def save_action() -> None:
        started.set()
        release.wait(1)

    assert bridge._start_save("textures.ytd", save_action)
    assert started.wait(1)
    assert bridge.saving
    assert bridge.modified

    release.set()
    bridge._thread_pool.waitForDone(1000)
    bridge._save_completed((0, "textures.ytd", None))
    assert not bridge.saving
    assert not bridge.modified

    bridge.shutdown()


def test_activating_ytd_opens_texture_viewer(tmp_path, monkeypatch) -> None:
    import fivefury

    monkeypatch.setattr(fivefury, "load_game_keys", lambda *args, **kwargs: None)
    (tmp_path / "GTA5.exe").write_bytes(b"test executable")
    ytd_path = tmp_path / "textures.ytd"
    Ytd([_red_texture()]).save(ytd_path)
    opened: list[tuple[str, str, bytes, object, object, str]] = []

    def capture_open(
        self: TextureViewerBridge,
        name: str,
        source_path: str,
        data: bytes,
        *,
        source_saver=None,
        source_prepare=None,
        source_root_path: str = "",
    ) -> None:
        del self
        opened.append(
            (
                name,
                source_path,
                data,
                source_saver,
                source_prepare,
                source_root_path,
            )
        )

    monkeypatch.setattr(TextureViewerBridge, "open_dictionary", capture_open)
    bridge = ExplorerBridge(image_provider=TextureImageProvider())
    bridge.openGame(str(tmp_path))
    row = next(
        index
        for index in range(bridge.entriesModel.rowCount())
        if bridge.entriesModel.entry_at(index).name == "textures.ytd"
    )

    bridge.activateEntry(row)

    assert opened == [
        (
            "textures.ytd",
            str(ytd_path),
            ytd_path.read_bytes(),
            None,
            None,
            "",
        )
    ]
    assert bridge.currentPath == "."
