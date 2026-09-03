"""Application settings, migration, and game-installation preferences."""

from __future__ import annotations

from pathlib import Path

from PySide6.QtCore import Property, QObject, QSettings, Signal, Slot
from PySide6.QtWidgets import QFileDialog

APPLICATION_NAME = "HikariHopper"
ORGANIZATION_NAME = "HikariHopper"
_LEGACY_NAME = "Hikari Hopper"
_SETTINGS_SCHEMA_VERSION = 3

LAST_GAME_ROOT_KEY = "gameRoot"
LEGACY_GAME_ROOT_KEY = "gameRoots/legacy"
ENHANCED_GAME_ROOT_KEY = "gameRoots/enhanced"

LEGACY_EDITION = "legacy"
ENHANCED_EDITION = "enhanced"

_GAME_EDITIONS = {
    LEGACY_EDITION: (
        LEGACY_GAME_ROOT_KEY,
        "GTA5.exe",
        "Select GTA V Legacy Installation",
    ),
    ENHANCED_EDITION: (
        ENHANCED_GAME_ROOT_KEY,
        "GTA5_Enhanced.exe",
        "Select GTA V Enhanced Installation",
    ),
}


def app_settings() -> QSettings:
    return QSettings(ORGANIZATION_NAME, APPLICATION_NAME)


def migrate_legacy_settings() -> None:
    current = app_settings()
    schema_version = int(current.value("settingsSchemaVersion", 0))
    if schema_version >= _SETTINGS_SCHEMA_VERSION:
        return

    if schema_version < 1:
        legacy = QSettings(_LEGACY_NAME, _LEGACY_NAME)
        legacy_root = str(legacy.value(LAST_GAME_ROOT_KEY, ""))
        if game_edition_for_path(legacy_root):
            current.setValue(LAST_GAME_ROOT_KEY, legacy_root)

        recent_roots = _recent_game_roots(legacy)
        if not recent_roots:
            recent_roots = _recent_game_roots(current)
        current.setValue("recentGameRoots", recent_roots)

    if schema_version < 2:
        last_root = str(current.value(LAST_GAME_ROOT_KEY, ""))
        edition = game_edition_for_path(last_root)
        if edition:
            current.setValue(game_root_key(edition), last_root)

    if schema_version < 3:
        last_root = str(current.value(LAST_GAME_ROOT_KEY, ""))
        if not game_edition_for_path(last_root):
            for edition in (ENHANCED_EDITION, LEGACY_EDITION):
                configured_root = configured_game_root(current, edition)
                if is_game_root(configured_root, edition):
                    current.setValue(LAST_GAME_ROOT_KEY, configured_root)
                    break

    current.setValue("settingsSchemaVersion", _SETTINGS_SCHEMA_VERSION)
    current.sync()


def game_root_key(edition: str) -> str:
    return _GAME_EDITIONS[edition][0]


def game_executable(edition: str) -> str:
    return _GAME_EDITIONS[edition][1]


def game_edition_for_path(value: str) -> str:
    """Return the GTA V edition installed at *value*, preferring Enhanced."""
    if not value:
        return ""
    path = Path(value)
    if not path.is_dir():
        return ""
    if (path / game_executable(ENHANCED_EDITION)).is_file():
        return ENHANCED_EDITION
    if (path / game_executable(LEGACY_EDITION)).is_file():
        return LEGACY_EDITION
    return ""


def is_game_root(value: str, edition: str = "") -> bool:
    detected = game_edition_for_path(value)
    return detected == edition if edition else bool(detected)


def configured_game_root(settings: QSettings, edition: str) -> str:
    return str(settings.value(game_root_key(edition), ""))


def remember_game_root(settings: QSettings, value: str) -> None:
    """Remember the last opened game and its edition-specific installation."""
    edition = game_edition_for_path(value)
    if not edition:
        return
    settings.setValue(LAST_GAME_ROOT_KEY, value)
    settings.setValue(game_root_key(edition), value)


class GamePathSettings(QObject):
    """Editable GTA V installation paths exposed to the Settings QML."""

    pathsChanged = Signal()

    def __init__(self, parent: QObject | None = None) -> None:
        super().__init__(parent)
        self._paths = {
            edition: configured_game_root(app_settings(), edition)
            for edition in _GAME_EDITIONS
        }

    @Property(str, notify=pathsChanged)
    def legacyPath(self) -> str:
        return self._paths[LEGACY_EDITION]

    @Property(str, notify=pathsChanged)
    def enhancedPath(self) -> str:
        return self._paths[ENHANCED_EDITION]

    @Property(bool, notify=pathsChanged)
    def legacyPathValid(self) -> bool:
        return is_game_root(self.legacyPath, LEGACY_EDITION)

    @Property(bool, notify=pathsChanged)
    def enhancedPathValid(self) -> bool:
        return is_game_root(self.enhancedPath, ENHANCED_EDITION)

    @Slot(str)
    def setLegacyPath(self, value: str) -> None:
        self._set_path(LEGACY_EDITION, value)

    @Slot(str)
    def setEnhancedPath(self, value: str) -> None:
        self._set_path(ENHANCED_EDITION, value)

    @Slot()
    def browseLegacyPath(self) -> None:
        self._browse(LEGACY_EDITION)

    @Slot()
    def browseEnhancedPath(self) -> None:
        self._browse(ENHANCED_EDITION)

    @Slot()
    def refresh(self) -> None:
        refreshed = {
            edition: configured_game_root(app_settings(), edition)
            for edition in _GAME_EDITIONS
        }
        if refreshed == self._paths:
            return
        self._paths = refreshed
        self.pathsChanged.emit()

    def _set_path(self, edition: str, value: str) -> None:
        normalized = value.strip().strip('"')
        if normalized == self._paths[edition]:
            return
        previous = self._paths[edition]
        self._paths[edition] = normalized
        settings = app_settings()
        if normalized:
            settings.setValue(game_root_key(edition), normalized)
        else:
            settings.remove(game_root_key(edition))
        last_root = str(settings.value(LAST_GAME_ROOT_KEY, ""))
        last_edition = game_edition_for_path(last_root)
        if is_game_root(normalized, edition) and (
            not last_edition or last_edition == edition
        ):
            settings.setValue(LAST_GAME_ROOT_KEY, normalized)
        elif last_root == previous:
            settings.remove(LAST_GAME_ROOT_KEY)
        settings.sync()
        self.pathsChanged.emit()

    def _browse(self, edition: str) -> None:
        _key, _executable, title = _GAME_EDITIONS[edition]
        settings = app_settings()
        start_path = self._paths[edition]
        if not start_path or not Path(start_path).is_dir():
            start_path = str(settings.value(LAST_GAME_ROOT_KEY, ""))
        path = QFileDialog.getExistingDirectory(None, title, start_path)
        if path:
            self._set_path(edition, path)


def _recent_game_roots(settings: QSettings) -> list[str]:
    value = settings.value("recentGameRoots", [])
    candidates = [value] if isinstance(value, str) else value
    roots: list[str] = []
    for candidate in candidates:
        path = str(candidate)
        if is_game_root(path) and path not in roots:
            roots.append(path)
    return roots
