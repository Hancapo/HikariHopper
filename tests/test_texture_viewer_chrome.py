from __future__ import annotations

from pathlib import Path


def test_marked_texture_viewer_chrome_is_removed() -> None:
    ui_path = Path(__file__).parents[1] / "src" / "rpf_explorer" / "ui"

    menu = (ui_path / "TextureViewerMenuRow.qml").read_text()
    window = (ui_path / "TextureDictionaryWindow.qml").read_text()
    rail = (ui_path / "TextureRail.qml").read_text()
    toolbar = (ui_path / "TexturePreviewToolbar.qml").read_text()
    status = (ui_path / "TextureViewerStatusBar.qml").read_text()

    assert 'qsTr("Help")' not in menu
    assert "TextureContextBar" not in window
    assert 'qsTr("TEXTURES' not in rail
    assert "selectedName" not in toolbar
    assert "selectedName" not in status
    assert "selectedDataSize" not in status
    assert 'qsTr("Selected")' not in status
