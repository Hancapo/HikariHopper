from __future__ import annotations

from pathlib import Path


def test_texture_size_moves_to_rail_and_facts_bar_is_removed() -> None:
    ui_path = Path(__file__).parents[1] / "src" / "rpf_explorer" / "ui"

    rail = (ui_path / "TextureRail.qml").read_text()
    preview = (ui_path / "TexturePreview.qml").read_text()
    theme = (ui_path / "theme" / "Theme.qml").read_text()

    assert "required property string dataSizeLabel" in rail
    assert "text: textureDelegate.dataSizeLabel" in rail
    assert "TextureFactsBar" not in preview
    assert not (ui_path / "TextureFactsBar.qml").exists()
    assert "textureFactsHeight" not in theme
    assert "textureScrollBar.enabled ? Theme.Theme.scrollbarWidth : 0" in rail
    assert "anchors.rightMargin: textureScrollBar.enabled ? 6 : 12" in rail
