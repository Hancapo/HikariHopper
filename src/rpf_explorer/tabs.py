"""Independent explorer sessions exposed as browser-style tabs."""

from __future__ import annotations

from pathlib import Path
from typing import Any

from PySide6.QtCore import (
    Property,
    QAbstractListModel,
    QByteArray,
    QModelIndex,
    QObject,
    Qt,
    Signal,
    Slot,
)

from .bridge import ExplorerBridge
from .settings import GamePathSettings, app_settings

_INVALID_INDEX = QModelIndex()
_MAX_RECENT_GAMES = 6


class ExplorerTabs(QAbstractListModel):
    """Owns isolated explorer bridges and publishes the active session to QML."""

    TITLE = Qt.ItemDataRole.UserRole + 1
    ACTIVE = Qt.ItemDataRole.UserRole + 2
    TEXTURE_VIEWER = Qt.ItemDataRole.UserRole + 3

    activeTabChanged = Signal()
    recentGamesChanged = Signal()

    def __init__(
        self,
        parent: QObject | None = None,
        image_provider: Any = None,
    ) -> None:
        super().__init__(parent)
        self._tabs: list[ExplorerBridge] = []
        self._active_index = -1
        self._image_provider = image_provider
        self._game_path_settings = GamePathSettings(self)
        self.newTab()

    def roleNames(self) -> dict[int, QByteArray]:
        return {
            self.TITLE: QByteArray(b"tabTitle"),
            self.ACTIVE: QByteArray(b"tabActive"),
            self.TEXTURE_VIEWER: QByteArray(b"textureViewer"),
        }

    def rowCount(self, parent: QModelIndex = _INVALID_INDEX) -> int:
        return 0 if parent.isValid() else len(self._tabs)

    def data(self, index: QModelIndex, role: int = Qt.ItemDataRole.DisplayRole) -> Any:
        if not index.isValid() or not 0 <= index.row() < len(self._tabs):
            return None
        bridge = self._tabs[index.row()]
        return {
            self.TITLE: bridge.tabTitle,
            self.ACTIVE: index.row() == self._active_index,
            self.TEXTURE_VIEWER: bridge.textureViewer,
        }.get(role)

    @Property(int, notify=activeTabChanged)
    def activeIndex(self) -> int:
        return self._active_index

    @Property(QObject, notify=activeTabChanged)
    def activeBridge(self) -> QObject:
        return self._tabs[self._active_index]

    @Property(QObject, constant=True)
    def gamePathSettings(self) -> QObject:
        return self._game_path_settings

    @Property(QObject, notify=activeTabChanged)
    def activeEntryModel(self) -> QObject:
        return self._tabs[self._active_index].entriesModel

    @Property(QObject, notify=activeTabChanged)
    def activeTreeModel(self) -> QObject:
        return self._tabs[self._active_index].treeModel

    @Property("QVariantList", notify=recentGamesChanged)
    def recentGames(self) -> list[dict[str, str]]:
        games: list[dict[str, str]] = []
        for path_string in self._recent_game_paths():
            path = Path(path_string)
            if not path.is_dir():
                continue
            if (path / "GTA5_Enhanced.exe").is_file():
                edition = "GTA V Enhanced"
            elif (path / "GTA5.exe").is_file():
                edition = "GTA V Legacy"
            else:
                continue
            games.append({"name": path.name, "path": str(path), "edition": edition})
        return games

    def restore_last_game(self) -> None:
        self.activeBridge.restore_last_game()

    @Slot(result=int)
    def newTab(self) -> int:
        row = len(self._tabs)
        self.beginInsertRows(_INVALID_INDEX, row, row)
        bridge = ExplorerBridge(self, image_provider=self._image_provider)
        bridge.workspaceChanged.connect(lambda bridge=bridge: self._workspace_changed(bridge))
        bridge.gameOpened.connect(self._remember_game)
        bridge.gameOpened.connect(lambda _path: self._game_path_settings.refresh())
        self._tabs.append(bridge)
        self.endInsertRows()
        self.activateTab(row)
        return row

    @Slot(int)
    def activateTab(self, row: int) -> None:
        if not 0 <= row < len(self._tabs) or row == self._active_index:
            return
        previous = self._active_index
        self._active_index = row
        changed_rows = [value for value in (previous, row) if value >= 0]
        for changed_row in changed_rows:
            model_index = self.index(changed_row, 0)
            self.dataChanged.emit(model_index, model_index, [self.ACTIVE])
        self.activeTabChanged.emit()

    @Slot(int)
    def closeTab(self, row: int) -> None:
        if not 0 <= row < len(self._tabs):
            return
        if len(self._tabs) == 1:
            self._tabs[0].closeWorkspace()
            return
        self.beginRemoveRows(_INVALID_INDEX, row, row)
        bridge = self._tabs.pop(row)
        self.endRemoveRows()
        bridge.textureViewer.shutdown()
        bridge.provider.close()
        bridge.deleteLater()
        if self._active_index > row:
            self._active_index -= 1
        elif self._active_index == row:
            self._active_index = min(row, len(self._tabs) - 1)
        self.activeTabChanged.emit()
        active_index = self.index(self._active_index, 0)
        self.dataChanged.emit(active_index, active_index, [self.ACTIVE])

    @Slot()
    def closeActiveTab(self) -> None:
        self.closeTab(self._active_index)

    @Slot(str)
    def openRecentGame(self, path: str) -> None:
        self.activeBridge.openGame(path)

    def _workspace_changed(self, bridge: ExplorerBridge) -> None:
        try:
            row = self._tabs.index(bridge)
        except ValueError:
            return
        model_index = self.index(row, 0)
        self.dataChanged.emit(model_index, model_index, [self.TITLE])
        if row == self._active_index:
            self.activeTabChanged.emit()

    @Slot(str)
    def _remember_game(self, path: str) -> None:
        recent = [item for item in self._recent_game_paths() if item != path]
        recent.insert(0, path)
        app_settings().setValue("recentGameRoots", recent[:_MAX_RECENT_GAMES])
        self.recentGamesChanged.emit()

    @staticmethod
    def _recent_game_paths() -> list[str]:
        value = app_settings().value("recentGameRoots", [])
        if isinstance(value, str):
            return [value] if value else []
        return [str(item) for item in value]
