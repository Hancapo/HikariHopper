from __future__ import annotations

from pathlib import Path


def test_start_page_opens_configured_paths_without_folder_picker() -> None:
    project_root = Path(__file__).parents[1]
    ui_path = project_root / "src" / "rpf_explorer" / "ui"
    start_page = (ui_path / "StartPage.qml").read_text(encoding="utf-8")
    menu = (ui_path / "MenuRow.qml").read_text(encoding="utf-8")

    assert "openConfiguredGame" in start_page
    assert "openConfiguredGame" in menu
    assert "openGameDialog" not in start_page
    assert "openGameDialog" not in menu
    assert 'text: qsTr("Open…")' not in start_page
    assert "Set this edition's installation path in Edit > Settings." in start_page
