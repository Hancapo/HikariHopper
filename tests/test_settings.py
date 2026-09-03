from __future__ import annotations

from pathlib import Path

from PySide6.QtCore import QSettings
from PySide6.QtTest import QSignalSpy

from rpf_explorer.settings import (
    ENHANCED_EDITION,
    ENHANCED_GAME_ROOT_KEY,
    LEGACY_EDITION,
    LEGACY_GAME_ROOT_KEY,
    GamePathSettings,
    game_edition_for_path,
    migrate_legacy_settings,
    remember_game_root,
)


def _installation(parent: Path, name: str, executable: str) -> Path:
    root = parent / name
    root.mkdir()
    (root / executable).write_bytes(b"game")
    return root


def test_game_edition_detection_prefers_enhanced(tmp_path: Path) -> None:
    installation = _installation(tmp_path, "Both", "GTA5.exe")
    (installation / "GTA5_Enhanced.exe").write_bytes(b"enhanced")

    assert game_edition_for_path(str(installation)) == ENHANCED_EDITION
    assert game_edition_for_path(str(tmp_path / "missing")) == ""


def test_remember_game_root_tracks_each_edition(tmp_path: Path) -> None:
    settings = QSettings(str(tmp_path / "game-roots.ini"), QSettings.Format.IniFormat)
    legacy = _installation(tmp_path, "Legacy", "GTA5.exe")
    enhanced = _installation(tmp_path, "Enhanced", "GTA5_Enhanced.exe")

    remember_game_root(settings, str(legacy))
    remember_game_root(settings, str(enhanced))

    assert settings.value(LEGACY_GAME_ROOT_KEY) == str(legacy)
    assert settings.value(ENHANCED_GAME_ROOT_KEY) == str(enhanced)
    assert settings.value("gameRoot") == str(enhanced)


def test_schema_three_migrates_the_last_game_to_its_edition(
    tmp_path: Path,
) -> None:
    from rpf_explorer import settings as settings_module

    settings = settings_module.app_settings()
    legacy = _installation(tmp_path, "Legacy", "GTA5.exe")
    settings.setValue("settingsSchemaVersion", 1)
    settings.setValue("gameRoot", str(legacy))

    migrate_legacy_settings()

    assert settings.value("settingsSchemaVersion") == 3
    assert settings.value(LEGACY_GAME_ROOT_KEY) == str(legacy)


def test_schema_three_uses_a_configured_path_when_no_last_game_exists(
    tmp_path: Path,
) -> None:
    from rpf_explorer import settings as settings_module

    settings = settings_module.app_settings()
    enhanced = _installation(tmp_path, "Enhanced", "GTA5_Enhanced.exe")
    settings.setValue("settingsSchemaVersion", 2)
    settings.setValue(ENHANCED_GAME_ROOT_KEY, str(enhanced))

    migrate_legacy_settings()

    assert settings.value("settingsSchemaVersion") == 3
    assert settings.value("gameRoot") == str(enhanced)


def test_game_path_settings_persists_and_validates_paths(tmp_path: Path) -> None:
    from rpf_explorer import settings as settings_module

    settings = settings_module.app_settings()
    legacy = _installation(tmp_path, "Legacy", "GTA5.exe")
    invalid = tmp_path / "NotAGame"
    invalid.mkdir()
    controller = GamePathSettings()
    changes = QSignalSpy(controller.pathsChanged)

    controller.setLegacyPath(f'  "{legacy}"  ')
    controller.setEnhancedPath(str(invalid))

    assert controller.legacyPath == str(legacy)
    assert controller.legacyPathValid
    assert settings.value(LEGACY_GAME_ROOT_KEY) == str(legacy)
    assert settings.value("gameRoot") == str(legacy)
    assert controller.enhancedPath == str(invalid)
    assert not controller.enhancedPathValid
    assert settings.value(ENHANCED_GAME_ROOT_KEY) == str(invalid)
    assert changes.count() == 2

    controller.setEnhancedPath("")

    assert settings.value(ENHANCED_GAME_ROOT_KEY) is None
    assert changes.count() == 3

    controller.setLegacyPath("")

    assert settings.value(LEGACY_GAME_ROOT_KEY) is None
    assert settings.value("gameRoot") is None
    assert changes.count() == 4


def test_configured_game_opens_without_a_folder_dialog(
    tmp_path: Path, monkeypatch
) -> None:
    import fivefury

    from rpf_explorer import settings as settings_module
    from rpf_explorer.bridge import ExplorerBridge

    monkeypatch.setattr(fivefury, "load_game_keys", lambda *args, **kwargs: None)
    legacy = _installation(tmp_path, "Legacy", "GTA5.exe")
    settings_module.app_settings().setValue(LEGACY_GAME_ROOT_KEY, str(legacy))
    bridge = ExplorerBridge()

    bridge.openConfiguredGame(LEGACY_EDITION)

    assert bridge.hasGame
    assert bridge.gamePath == str(legacy)


def test_missing_configured_game_points_to_settings() -> None:
    from rpf_explorer.bridge import ExplorerBridge

    bridge = ExplorerBridge()

    bridge.openConfiguredGame(ENHANCED_EDITION)

    assert bridge.status == "Configure the GTA V Enhanced path in Settings"


def test_tabs_refresh_game_path_settings_after_opening_game(
    tmp_path: Path, monkeypatch
) -> None:
    import fivefury

    from rpf_explorer.tabs import ExplorerTabs

    monkeypatch.setattr(fivefury, "load_game_keys", lambda *args, **kwargs: None)
    enhanced = _installation(tmp_path, "Enhanced", "GTA5_Enhanced.exe")
    tabs = ExplorerTabs()
    changes = QSignalSpy(tabs.gamePathSettings.pathsChanged)

    tabs.activeBridge.openGame(str(enhanced))

    assert tabs.gamePathSettings.enhancedPath == str(enhanced)
    assert tabs.gamePathSettings.enhancedPathValid
    assert changes.count() == 1
