from __future__ import annotations

from pathlib import Path


def test_texture_tool_dialog_creation_is_deferred_until_the_menu_releases() -> None:
    source = (
        Path(__file__).parents[1]
        / "src"
        / "rpf_explorer"
        / "ui"
        / "TextureDictionaryWindow.qml"
    ).read_text(encoding="utf-8")
    open_dialog = source.index("function openToolDialog(component)")
    present_dialog = source.index("function presentPendingToolDialog()")
    deferred = source.index("Qt.callLater(window.presentPendingToolDialog)")
    activated = source.index("toolDialogLoader.active = true", present_dialog)

    assert open_dialog < deferred < present_dialog < activated
    assert "onLoaded: item.open()" in source
