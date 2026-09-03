"""Application settings identity and legacy-key migration."""

from __future__ import annotations

from pathlib import Path

from PySide6.QtCore import QSettings

APPLICATION_NAME = "HikariHopper"
ORGANIZATION_NAME = "HikariHopper"
_LEGACY_NAME = "Hikari Hopper"
_SETTINGS_SCHEMA_VERSION = 1


def app_settings() -> QSettings:
    return QSettings(ORGANIZATION_NAME, APPLICATION_NAME)


def migrate_legacy_settings() -> None:
    current = app_settings()
    if int(current.value("settingsSchemaVersion", 0)) >= _SETTINGS_SCHEMA_VERSION:
        return

    legacy = QSettings(_LEGACY_NAME, _LEGACY_NAME)
    legacy_root = str(legacy.value("gameRoot", ""))
    if _is_game_root(legacy_root):
        current.setValue("gameRoot", legacy_root)

    recent_roots = _recent_game_roots(legacy)
    if not recent_roots:
        recent_roots = _recent_game_roots(current)
    current.setValue("recentGameRoots", recent_roots)
    current.setValue("settingsSchemaVersion", _SETTINGS_SCHEMA_VERSION)
    current.sync()


def _is_game_root(value: str) -> bool:
    if not value:
        return False
    path = Path(value)
    return path.is_dir() and any(
        (path / executable).is_file()
        for executable in ("GTA5.exe", "GTA5_Enhanced.exe")
    )


def _recent_game_roots(settings: QSettings) -> list[str]:
    value = settings.value("recentGameRoots", [])
    candidates = [value] if isinstance(value, str) else value
    roots: list[str] = []
    for candidate in candidates:
        path = str(candidate)
        if _is_game_root(path) and path not in roots:
            roots.append(path)
    return roots
