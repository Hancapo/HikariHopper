"""QObject bridge between the QML presentation and the FiveFury adapter."""

from __future__ import annotations

from dataclasses import dataclass
from functools import partial
from pathlib import Path
from tempfile import TemporaryDirectory
from typing import Any, Callable

from PySide6.QtCore import (
    Property,
    QAbstractListModel,
    QByteArray,
    QMimeData,
    QModelIndex,
    QObject,
    QRunnable,
    QThreadPool,
    Qt,
    QUrl,
    Signal,
    Slot,
)
from PySide6.QtGui import QDrag, QGuiApplication
from PySide6.QtWidgets import QFileDialog

from .backend import (
    EntryCreationTarget,
    RpfProvider,
    create_empty_rpf_at,
    create_folder_at,
    create_rpf_from_folder_at,
    create_rpf_from_zip_at,
    normalize_entry_name,
    normalize_rpf_name,
)
from .formatting import format_size
from .models import EntryRecord
from .settings import (
    ENHANCED_EDITION,
    LEGACY_EDITION,
    app_settings,
    configured_game_root,
    is_game_root,
    remember_game_root,
)
from .texture_viewer import TextureImageProvider, TextureViewerBridge

_INVALID_INDEX = QModelIndex()


class EntryListModel(QAbstractListModel):
    NAME = Qt.ItemDataRole.UserRole + 1
    PATH = Qt.ItemDataRole.UserRole + 2
    KIND = Qt.ItemDataRole.UserRole + 3
    SIZE = Qt.ItemDataRole.UserRole + 4
    SIZE_LABEL = Qt.ItemDataRole.UserRole + 5
    CHILD_COUNT = Qt.ItemDataRole.UserRole + 6
    IS_DIRECTORY = Qt.ItemDataRole.UserRole + 7
    SELECTED = Qt.ItemDataRole.UserRole + 8

    def __init__(self, parent: QObject | None = None) -> None:
        super().__init__(parent)
        self._entries: list[EntryRecord] = []
        self._selected_rows: set[int] = set()

    def roleNames(self) -> dict[int, QByteArray]:
        return {
            self.NAME: QByteArray(b"name"),
            self.PATH: QByteArray(b"path"),
            self.KIND: QByteArray(b"kind"),
            self.SIZE: QByteArray(b"size"),
            self.SIZE_LABEL: QByteArray(b"sizeLabel"),
            self.CHILD_COUNT: QByteArray(b"childCount"),
            self.IS_DIRECTORY: QByteArray(b"isDirectory"),
            self.SELECTED: QByteArray(b"selected"),
        }

    def rowCount(self, parent: QModelIndex = _INVALID_INDEX) -> int:
        return 0 if parent.isValid() else len(self._entries)

    def data(self, index: QModelIndex, role: int = Qt.ItemDataRole.DisplayRole) -> Any:
        if not index.isValid() or not 0 <= index.row() < len(self._entries):
            return None
        entry = self._entries[index.row()]
        return {
            self.NAME: entry.name,
            self.PATH: entry.path,
            self.KIND: entry.kind,
            self.SIZE: entry.size,
            self.SIZE_LABEL: format_size(entry.size),
            self.CHILD_COUNT: entry.children,
            self.IS_DIRECTORY: entry.is_directory,
            self.SELECTED: index.row() in self._selected_rows,
        }.get(role)

    def set_entries(self, entries: list[EntryRecord]) -> None:
        self.beginResetModel()
        self._entries = entries
        self._selected_rows.clear()
        self.endResetModel()

    def set_selected_rows(self, rows: set[int]) -> None:
        selected = {row for row in rows if 0 <= row < len(self._entries)}
        changed = self._selected_rows.symmetric_difference(selected)
        self._selected_rows = selected
        if not changed:
            return
        first = min(changed)
        last = max(changed)
        self.dataChanged.emit(
            self.index(first, 0),
            self.index(last, 0),
            [self.SELECTED],
        )

    def entry_at(self, row: int) -> EntryRecord | None:
        return self._entries[row] if 0 <= row < len(self._entries) else None


@dataclass(frozen=True, slots=True)
class TreeRecord:
    label: str
    path: str
    depth: int
    kind: str
    expanded: bool = False
    has_children: bool = False
    selected: bool = False


class TreeListModel(QAbstractListModel):
    LABEL = Qt.ItemDataRole.UserRole + 1
    PATH = Qt.ItemDataRole.UserRole + 2
    DEPTH = Qt.ItemDataRole.UserRole + 3
    KIND = Qt.ItemDataRole.UserRole + 4
    EXPANDED = Qt.ItemDataRole.UserRole + 5
    HAS_CHILDREN = Qt.ItemDataRole.UserRole + 6
    SELECTED = Qt.ItemDataRole.UserRole + 7

    def __init__(self, parent: QObject | None = None) -> None:
        super().__init__(parent)
        self._rows: list[TreeRecord] = []

    def roleNames(self) -> dict[int, QByteArray]:
        return {
            self.LABEL: QByteArray(b"label"),
            self.PATH: QByteArray(b"path"),
            self.DEPTH: QByteArray(b"depth"),
            self.KIND: QByteArray(b"kind"),
            self.EXPANDED: QByteArray(b"expanded"),
            self.HAS_CHILDREN: QByteArray(b"hasChildren"),
            self.SELECTED: QByteArray(b"selected"),
        }

    def rowCount(self, parent: QModelIndex = _INVALID_INDEX) -> int:
        return 0 if parent.isValid() else len(self._rows)

    def data(self, index: QModelIndex, role: int = Qt.ItemDataRole.DisplayRole) -> Any:
        if not index.isValid() or not 0 <= index.row() < len(self._rows):
            return None
        row = self._rows[index.row()]
        return {
            self.LABEL: row.label,
            self.PATH: row.path,
            self.DEPTH: row.depth,
            self.KIND: row.kind,
            self.EXPANDED: row.expanded,
            self.HAS_CHILDREN: row.has_children,
            self.SELECTED: row.selected,
        }.get(role)

    def set_rows(self, rows: list[TreeRecord]) -> None:
        old_count = len(self._rows)
        new_count = len(rows)
        if new_count == old_count and all(
            current.path == replacement.path
            for current, replacement in zip(self._rows, rows, strict=True)
        ):
            if rows == self._rows:
                return
            self._rows = rows
            self.dataChanged.emit(
                self.index(0, 0),
                self.index(len(rows) - 1, 0),
                [],
            )
            return

        prefix = 0
        while (
            prefix < old_count
            and prefix < new_count
            and self._rows[prefix].path == rows[prefix].path
        ):
            prefix += 1

        suffix = 0
        while (
            suffix < old_count - prefix
            and suffix < new_count - prefix
            and self._rows[old_count - suffix - 1].path == rows[new_count - suffix - 1].path
        ):
            suffix += 1

        removed_count = old_count - prefix - suffix
        if removed_count:
            self.beginRemoveRows(_INVALID_INDEX, prefix, prefix + removed_count - 1)
            del self._rows[prefix : prefix + removed_count]
            self.endRemoveRows()

        inserted = rows[prefix : new_count - suffix]
        if inserted:
            self.beginInsertRows(_INVALID_INDEX, prefix, prefix + len(inserted) - 1)
            self._rows[prefix:prefix] = inserted
            self.endInsertRows()

        self._rows = rows
        if rows:
            self.dataChanged.emit(self.index(0, 0), self.index(new_count - 1, 0), [])

    def row_at(self, row: int) -> TreeRecord | None:
        return self._rows[row] if 0 <= row < len(self._rows) else None

    def row_for_path(self, path: str) -> int:
        return next(
            (index for index, row in enumerate(self._rows) if row.path == path),
            -1,
        )

    def selected_row(self) -> int:
        return next(
            (index for index, row in enumerate(self._rows) if row.selected),
            -1,
        )


class _EntryCreationSignals(QObject):
    completed = Signal(object)
    failed = Signal(object)


class _EntryCreationTask(QRunnable):
    def __init__(
        self,
        target: EntryCreationTarget,
        label: str,
        operation: Callable[[], str],
    ) -> None:
        super().__init__()
        self.signals = _EntryCreationSignals()
        self._target = target
        self._label = label
        self._operation = operation

    @Slot()
    def run(self) -> None:
        try:
            name = self._operation()
        except (
            ImportError,
            MemoryError,
            OSError,
            RuntimeError,
            TypeError,
            ValueError,
        ) as error:
            self.signals.failed.emit((self._label, str(error)))
            return
        self.signals.completed.emit((self._target, name))


class ExplorerBridge(QObject):
    workspaceChanged = Signal()
    currentPathChanged = Signal()
    pathPartsChanged = Signal()
    statusChanged = Signal()
    countsChanged = Signal()
    selectionChanged = Signal()
    searchChanged = Signal()
    sortChanged = Signal()
    viewModeChanged = Signal()
    treeFocusChanged = Signal()
    gameOpened = Signal(str)
    uiStateChanged = Signal()
    creationStateChanged = Signal()
    searchFocusRequested = Signal()

    def __init__(
        self,
        parent: QObject | None = None,
        image_provider: TextureImageProvider | None = None,
    ) -> None:
        super().__init__(parent)
        self.provider = RpfProvider()
        self.entriesModel = EntryListModel(self)
        self.treeModel = TreeListModel(self)
        self._texture_viewer = TextureViewerBridge(
            image_provider or TextureImageProvider(),
            self,
        )
        self._texture_viewer.sourceSaved.connect(self._texture_source_saved)
        self._creation_pool = QThreadPool(self)
        self._creation_pool.setMaxThreadCount(1)
        self._creation_busy = False
        self._current_path = "."
        self._history: list[str] = []
        self._history_index = -1
        self._search = ""
        self._sort_column = "name"
        self._sort_ascending = True
        self._status = "No game loaded  ·  Configure game paths in Settings"
        self._total_count = 0
        self._visible_count = 0
        self._selected_name = ""
        self._selected_size = ""
        self._selected_path = ""
        self._selected_index = -1
        self._selected_rows: set[int] = set()
        self._selection_anchor = -1
        self._marquee_base_selection: set[int] = set()
        self._marquee_modifiers = 0
        self._drag_staging: TemporaryDirectory[str] | None = None
        self._expanded_nodes: set[str] = set()
        self._tree_focus_path = ""
        self._tree_focus_index = -1
        self._folders_visible = True
        self._view_mode = "list"

    @Property(QObject, constant=True)
    def textureViewer(self) -> QObject:
        return self._texture_viewer

    @Property(str, notify=workspaceChanged)
    def archiveName(self) -> str:
        return self.provider.info.name

    @Property(str, notify=workspaceChanged)
    def archivePath(self) -> str:
        return self.provider.info.source_path

    @Property(str, notify=workspaceChanged)
    def gameName(self) -> str:
        return self.provider.game_name

    @Property(str, notify=workspaceChanged)
    def gamePath(self) -> str:
        return self.provider.game_path

    @Property(str, notify=workspaceChanged)
    def archiveTabName(self) -> str:
        return self.provider.root_archive_name

    @Property(str, notify=workspaceChanged)
    def archiveRootName(self) -> str:
        return self.provider.root_archive_name

    @Property(str, notify=workspaceChanged)
    def tabTitle(self) -> str:
        if self.provider.has_game:
            return self.provider.game_name
        if self.provider.has_archive:
            return self.provider.root_archive_name
        return "New tab"

    @Property(bool, notify=workspaceChanged)
    def hasWorkspace(self) -> bool:
        return self.provider.is_open

    @Property(bool, notify=workspaceChanged)
    def hasGame(self) -> bool:
        return self.provider.has_game

    @Property(bool, notify=workspaceChanged)
    def hasArchive(self) -> bool:
        return self.provider.has_archive

    @Property(bool, notify=workspaceChanged)
    def inArchive(self) -> bool:
        return self.provider.in_archive

    @Property(str, notify=currentPathChanged)
    def currentPath(self) -> str:
        return self._current_path

    @Property("QVariantList", notify=pathPartsChanged)
    def pathParts(self) -> list[str]:
        return [] if self._current_path == "." else self._current_path.split("/")

    @Property("QVariantList", notify=pathPartsChanged)
    def archiveGamePathParts(self) -> list[str]:
        parts = self.provider.archive_game_path.split("/") if self.provider.archive_game_path else []
        if self.provider.archive_prefix:
            parts.extend(self.provider.archive_prefix.split("/"))
        return parts

    @Property("QVariantList", notify=pathPartsChanged)
    def navigationPathSegments(self) -> list[dict[str, Any]]:
        segments: list[dict[str, Any]] = []
        if not self.provider.is_open:
            return segments

        if self.provider.in_archive:
            game_parts = (
                self.provider.archive_game_path.split("/")
                if self.provider.archive_game_path
                else []
            )
            for index, label in enumerate(game_parts):
                path = "/".join(game_parts[: index + 1])
                is_archive = index == len(game_parts) - 1
                segments.append(
                    {
                        "label": label,
                        "target": f"rpf://{path}" if is_archive else f"game://{path}",
                        "archive": is_archive,
                    }
                )

            nested_prefixes = set(self.provider.archive_chain_prefixes[1:])
            prefix_parts = self.provider.archive_prefix.split("/") if self.provider.archive_prefix else []
            for index, label in enumerate(prefix_parts):
                full_path = "/".join(prefix_parts[: index + 1])
                is_archive = full_path in nested_prefixes
                segments.append(
                    {
                        "label": label,
                        "target": (
                            self._nested_archive_key(full_path)
                            if is_archive
                            else self._archive_directory_key(full_path)
                        ),
                        "archive": is_archive,
                    }
                )

            current_parts = [] if self._current_path == "." else self._current_path.split("/")
            for index, label in enumerate(current_parts):
                local_path = "/".join(current_parts[: index + 1])
                full_path = "/".join(
                    part
                    for part in (self.provider.archive_prefix, local_path)
                    if part
                )
                segments.append(
                    {
                        "label": label,
                        "target": self._archive_directory_key(full_path),
                        "archive": False,
                    }
                )
            return segments

        current_parts = [] if self._current_path == "." else self._current_path.split("/")
        for index, label in enumerate(current_parts):
            path = "/".join(current_parts[: index + 1])
            segments.append(
                {"label": label, "target": f"game://{path}", "archive": False}
            )
        return segments

    @Property(str, notify=searchChanged)
    def searchQuery(self) -> str:
        """The live filter text, so the table can mark the matched characters."""
        return self._search

    @Property(str, notify=sortChanged)
    def sortColumn(self) -> str:
        return self._sort_column

    @Property(bool, notify=sortChanged)
    def sortAscending(self) -> bool:
        return self._sort_ascending

    @Property(str, notify=statusChanged)
    def status(self) -> str:
        return self._status

    @Property(int, notify=countsChanged)
    def totalCount(self) -> int:
        return self._total_count

    @Property(int, notify=countsChanged)
    def visibleCount(self) -> int:
        return self._visible_count

    @Property(bool, notify=countsChanged)
    def canGoBack(self) -> bool:
        return self._history_index > 0

    @Property(bool, notify=countsChanged)
    def canGoForward(self) -> bool:
        return self._history_index + 1 < len(self._history)

    @Property(bool, notify=currentPathChanged)
    def canGoUp(self) -> bool:
        return self._current_path not in ("", ".")

    @Property(str, notify=selectionChanged)
    def selectedName(self) -> str:
        return self._selected_name

    @Property(str, notify=selectionChanged)
    def selectedSize(self) -> str:
        return self._selected_size

    @Property(int, notify=selectionChanged)
    def selectedIndex(self) -> int:
        return self._selected_index

    @Property(bool, notify=selectionChanged)
    def hasSelection(self) -> bool:
        return bool(self._selected_rows)

    @Property(int, notify=selectionChanged)
    def selectionCount(self) -> int:
        return len(self._selected_rows)

    @Property(bool, notify=uiStateChanged)
    def foldersVisible(self) -> bool:
        return self._folders_visible

    @Property(bool, notify=creationStateChanged)
    def creationBusy(self) -> bool:
        return self._creation_busy

    @Property(str, notify=viewModeChanged)
    def viewMode(self) -> str:
        return self._view_mode

    @Property(int, notify=treeFocusChanged)
    def treeFocusIndex(self) -> int:
        return self._tree_focus_index

    @Property(str, notify=treeFocusChanged)
    def treeFocusPath(self) -> str:
        return self._tree_focus_path

    def _set_status(self, value: str) -> None:
        self._status = value
        self.statusChanged.emit()

    def _reset_navigation(self) -> None:
        self._history = ["."]
        self._history_index = 0
        self._search = ""
        self._current_path = "."
        self.searchChanged.emit()

    def _refresh(self) -> None:
        entries = self.provider.entries(self._current_path)
        self._total_count = len(entries)
        search = self._search.casefold().strip()
        visible = [entry for entry in entries if not search or search in entry.name.casefold()]
        visible = self._sorted_entries(visible)
        self.entriesModel.set_entries(visible)
        self._refresh_tree()
        self._visible_count = len(visible)
        self.countsChanged.emit()

    def _sorted_entries(self, entries: list[EntryRecord]) -> list[EntryRecord]:
        def sort_key(entry: EntryRecord) -> tuple[Any, ...]:
            name = entry.name.casefold()
            if self._sort_column == "type":
                return (entry.kind.casefold(), name)
            if self._sort_column == "size":
                return (entry.size, name)
            return (name,)

        folders = [entry for entry in entries if entry.is_directory]
        files = [entry for entry in entries if not entry.is_directory]
        reverse = not self._sort_ascending
        folder_key = sort_key if self._sort_column == "name" else lambda entry: (entry.name.casefold(),)
        return sorted(folders, key=folder_key, reverse=reverse) + sorted(
            files,
            key=sort_key,
            reverse=reverse,
        )

    def _refresh_tree(self) -> None:
        if not self.provider.is_open:
            self.treeModel.set_rows([])
            self._update_tree_focus("", -1)
            return
        self.treeModel.set_rows(self._persistent_tree_rows())
        self._sync_tree_focus()

    def _sync_tree_focus(self) -> None:
        index = self.treeModel.row_for_path(self._tree_focus_path)
        if index < 0:
            index = self.treeModel.selected_row()
        if index < 0 and self.treeModel.rowCount() > 0:
            index = min(max(self._tree_focus_index, 0), self.treeModel.rowCount() - 1)
        row = self.treeModel.row_at(index)
        self._update_tree_focus(row.path if row is not None else "", index)

    def _update_tree_focus(self, path: str, index: int) -> None:
        if path == self._tree_focus_path and index == self._tree_focus_index:
            return
        self._tree_focus_path = path
        self._tree_focus_index = index
        self.treeFocusChanged.emit()

    def _persistent_tree_rows(self) -> list[TreeRecord]:
        rows: list[TreeRecord] = []
        if self.provider.has_game:
            root_key = "game://."
            rows.append(
                TreeRecord(
                    self.provider.game_name,
                    root_key,
                    0,
                    "root",
                    root_key in self._expanded_nodes,
                    True,
                    not self.provider.in_archive and self._current_path == ".",
                )
            )
            if root_key in self._expanded_nodes:
                self._append_game_children(rows, ".", 1)
        if self.provider.has_archive and not self.provider.archive_game_path:
            archive_key = "archive://."
            has_children = self.provider.archive_has_tree_children("")
            rows.append(
                TreeRecord(
                    self.provider.root_archive_name,
                    archive_key,
                    0,
                    "archive",
                    has_children and archive_key in self._expanded_nodes,
                    has_children,
                    (
                        self.provider.in_archive
                        and not self.provider.archive_prefix
                        and self._current_path == "."
                    ),
                )
            )
            if has_children and archive_key in self._expanded_nodes:
                self._append_archive_children(rows, "", ".", 1)
        return rows

    def _append_game_children(self, rows: list[TreeRecord], directory_path: str, depth: int) -> None:
        for entry in self.provider.game_entries(directory_path):
            is_archive = not entry.is_directory and entry.name.casefold().endswith(".rpf")
            if not entry.is_directory and not is_archive:
                continue
            key = f"rpf://{entry.path}" if is_archive else f"game://{entry.path}"
            is_loaded_archive = is_archive and entry.path == self.provider.archive_game_path
            has_children = (
                self.provider.archive_has_tree_children("")
                if is_loaded_archive
                else True if is_archive else bool(entry.navigable_children)
            )
            expanded = has_children and key in self._expanded_nodes
            rows.append(
                TreeRecord(
                    entry.name,
                    key,
                    depth,
                    "archive" if is_archive else "folder",
                    expanded,
                    has_children,
                    (
                        is_loaded_archive
                        and self.provider.in_archive
                        and not self.provider.archive_prefix
                        and self._current_path == "."
                    )
                    or (
                        entry.is_directory
                        and not self.provider.in_archive
                        and self._current_path == entry.path
                    ),
                )
            )
            if not expanded:
                continue
            if entry.is_directory:
                self._append_game_children(rows, entry.path, depth + 1)
            elif is_loaded_archive:
                self._append_archive_children(rows, "", ".", depth + 1)

    def _append_archive_children(
        self,
        rows: list[TreeRecord],
        archive_prefix: str,
        directory_path: str,
        depth: int,
    ) -> None:
        for entry in self.provider.archive_entries_at(archive_prefix, directory_path):
            is_nested_archive = entry.name.casefold().endswith(".rpf")
            if not entry.is_directory and not is_nested_archive:
                continue
            key = (
                self._archive_directory_key(entry.path)
                if entry.is_directory
                else self._nested_archive_key(entry.path)
            )
            child_prefix = self.provider.loaded_nested_prefix(entry)
            has_children = (
                bool(entry.navigable_children)
                if entry.is_directory
                else (
                    self.provider.archive_has_tree_children(child_prefix)
                    if child_prefix
                    else True
                )
            )
            expanded = key in self._expanded_nodes and (
                entry.is_directory or bool(child_prefix)
            )
            entry_prefix = self.provider.entry_archive_prefix(entry)
            local_path = self.provider.entry_local_path(entry)
            selected = (
                entry.is_directory
                and self.provider.in_archive
                and self.provider.archive_prefix == entry_prefix
                and self._current_path == local_path
            ) or (
                is_nested_archive
                and bool(child_prefix)
                and self.provider.in_archive
                and self.provider.archive_prefix == child_prefix
                and self._current_path == "."
            )
            rows.append(
                TreeRecord(
                    entry.name,
                    key,
                    depth,
                    "folder" if entry.is_directory else "archive",
                    expanded,
                    has_children,
                    selected,
                )
            )
            if not expanded:
                continue
            if entry.is_directory:
                self._append_archive_children(
                    rows,
                    archive_prefix,
                    local_path,
                    depth + 1,
                )
            else:
                self._append_archive_children(rows, child_prefix, ".", depth + 1)

    def _expand_current_branch(self) -> None:
        if self.provider.in_archive:
            archive_game_path = self.provider.archive_game_path
            root_has_children = self.provider.archive_has_tree_children("")
            if archive_game_path:
                self._expand_game_ancestors(archive_game_path)
                root_key = f"rpf://{archive_game_path}"
            else:
                root_key = "archive://."
            if root_has_children:
                self._expanded_nodes.add(root_key)
            else:
                self._expanded_nodes.discard(root_key)
            self._expand_archive_ancestors()
        else:
            self._expand_path_ancestors(self._current_path, "game://")

    def _expand_archive_ancestors(self) -> None:
        for prefix in self.provider.archive_chain_prefixes[1:]:
            parts = prefix.split("/")
            for index in range(1, len(parts)):
                candidate = "/".join(parts[:index])
                if not candidate.casefold().endswith(".rpf"):
                    self._expanded_nodes.add(self._archive_directory_key(candidate))
            key = self._nested_archive_key(prefix)
            if self.provider.archive_has_tree_children(prefix):
                self._expanded_nodes.add(key)
            else:
                self._expanded_nodes.discard(key)

        current = self._current_path
        if current == ".":
            return
        full_path = "/".join(
            part for part in (self.provider.archive_prefix, current) if part
        )
        parts = full_path.split("/")
        for index in range(1, len(parts)):
            candidate = "/".join(parts[:index])
            if not candidate.casefold().endswith(".rpf"):
                self._expanded_nodes.add(self._archive_directory_key(candidate))

    def _expand_game_ancestors(self, path: str) -> None:
        self._expanded_nodes.add("game://.")
        parent_parts = path.split("/")[:-1]
        for index in range(1, len(parent_parts) + 1):
            self._expanded_nodes.add(f"game://{'/'.join(parent_parts[:index])}")

    def _expand_path_ancestors(self, path: str, prefix: str) -> None:
        if path == ".":
            return
        parts = path.split("/")
        for index in range(1, len(parts)):
            self._expanded_nodes.add(f"{prefix}{'/'.join(parts[:index])}")

    def _clear_archive_expansion(self) -> None:
        self._expanded_nodes = {
            key
            for key in self._expanded_nodes
            if not key.startswith(("rpf://", "archive://", "nested://"))
        }

    def _archive_directory_key(self, full_path: str) -> str:
        return f"archive://{self.provider.archive_tree_id}::{full_path}"

    def _nested_archive_key(self, full_path: str) -> str:
        return f"nested://{self.provider.archive_tree_id}::{full_path}"

    def _archive_key_path(self, key: str, scheme: str) -> str:
        prefix = f"{scheme}://"
        if not key.startswith(prefix):
            return ""
        scope, separator, full_path = key.removeprefix(prefix).partition("::")
        if not separator or scope != self.provider.archive_tree_id:
            return ""
        return full_path

    def _publish_navigation(self) -> None:
        self._clear_selection()
        self.currentPathChanged.emit()
        self.pathPartsChanged.emit()
        self.countsChanged.emit()
        self._refresh()

    def _publish_workspace(self) -> None:
        self.workspaceChanged.emit()
        self._publish_navigation()

    def restore_last_game(self) -> None:
        path = self._configured_game_path()
        if path:
            self.openGame(path)

    @Slot(str)
    def openConfiguredGame(self, edition: str = "") -> None:
        path = self._configured_game_path(edition)
        if not path:
            edition_name = {
                ENHANCED_EDITION: "GTA V Enhanced",
                LEGACY_EDITION: "GTA V Legacy",
            }.get(edition, "game")
            self._set_status(f"Configure the {edition_name} path in Settings")
            return
        self.openGame(path)

    @staticmethod
    def _configured_game_path(edition: str = "") -> str:
        settings = app_settings()
        if edition:
            path = configured_game_root(settings, edition)
            return path if is_game_root(path, edition) else ""

        last_root = str(settings.value("gameRoot", ""))
        if is_game_root(last_root):
            return last_root
        for candidate_edition in (ENHANCED_EDITION, LEGACY_EDITION):
            path = configured_game_root(settings, candidate_edition)
            if is_game_root(path, candidate_edition):
                return path
        return ""

    @Slot(str)
    def openGame(self, path: str) -> None:
        self._set_status("Loading GTA V keys…")
        try:
            self.provider.open_game(path)
        except (OSError, ValueError, RuntimeError) as error:
            self._set_status(f"Could not load game: {error}")
            return
        remember_game_root(app_settings(), self.provider.game_path)
        self.gameOpened.emit(self.provider.game_path)
        self._expanded_nodes = {"game://."}
        self._reset_navigation()
        self._publish_workspace()
        self._set_status(f"Loaded {self.provider.info.version}  ·  FiveFury")

    @Slot()
    def openArchiveDialog(self) -> None:
        start_path = self.provider.game_path
        path, _ = QFileDialog.getOpenFileName(
            None,
            "Open RPF Archive",
            start_path,
            "RPF archives (*.rpf)",
        )
        if path:
            self.openArchive(path)

    @Slot(result="QVariantMap")
    def chooseRpfFolderSource(self) -> dict[str, str]:
        path = QFileDialog.getExistingDirectory(
            None,
            "Choose a folder to pack as RPF",
            self.provider.game_path,
        )
        return self._rpf_source_result(path, folder=True)

    @Slot(result="QVariantMap")
    def chooseRpfZipSource(self) -> dict[str, str]:
        path, _ = QFileDialog.getOpenFileName(
            None,
            "Choose a ZIP archive to pack as RPF",
            self.provider.game_path,
            "ZIP archives (*.zip)",
        )
        return self._rpf_source_result(path, folder=False)

    @staticmethod
    def _rpf_source_result(path: str, *, folder: bool) -> dict[str, str]:
        if not path:
            return {"path": "", "suggestedName": ""}
        source = Path(path)
        stem = source.name if folder else source.stem
        return {"path": str(source), "suggestedName": f"{stem}.rpf"}

    @Slot(str, result=bool)
    def createFolder(self, name: str) -> bool:
        normalized = self._creation_name(name, rpf=False)
        if not normalized:
            return False
        return self._start_entry_creation(
            normalized,
            lambda target: partial(create_folder_at, target, normalized),
        )

    @Slot(str, result=bool)
    def createEmptyRpf(self, name: str) -> bool:
        normalized = self._creation_name(name, rpf=True)
        if not normalized:
            return False
        return self._start_entry_creation(
            normalized,
            lambda target: partial(create_empty_rpf_at, target, normalized),
        )

    @Slot(str, str, result=bool)
    def createRpfFromFolder(self, name: str, source: str) -> bool:
        normalized = self._creation_name(name, rpf=True)
        if not normalized:
            return False
        return self._start_entry_creation(
            normalized,
            lambda target: partial(
                create_rpf_from_folder_at,
                target,
                normalized,
                source,
            ),
        )

    @Slot(str, str, result=bool)
    def createRpfFromZip(self, name: str, source: str) -> bool:
        normalized = self._creation_name(name, rpf=True)
        if not normalized:
            return False
        return self._start_entry_creation(
            normalized,
            lambda target: partial(
                create_rpf_from_zip_at,
                target,
                normalized,
                source,
            ),
        )

    def _creation_name(self, name: str, *, rpf: bool) -> str:
        try:
            return normalize_rpf_name(name) if rpf else normalize_entry_name(name)
        except ValueError as error:
            self._set_status(str(error))
            return ""

    def _start_entry_creation(
        self,
        label: str,
        operation_factory: Callable[[EntryCreationTarget], Callable[[], str]],
    ) -> bool:
        if self._creation_busy:
            return False
        try:
            target = self.provider.creation_target(self._current_path)
        except (OSError, ValueError, RuntimeError) as error:
            self._set_status(f"Could not create entry: {error}")
            return False

        task = _EntryCreationTask(target, label.strip(), operation_factory(target))
        task.signals.completed.connect(self._entry_creation_completed)
        task.signals.failed.connect(self._entry_creation_failed)
        self._creation_busy = True
        self.creationStateChanged.emit()
        self._set_status(f"Creating {label.strip() or 'entry'}…")
        self._creation_pool.start(task)
        return True

    @Slot(object)
    def _entry_creation_completed(self, payload: object) -> None:
        target, name = payload
        self._creation_busy = False
        self.creationStateChanged.emit()

        refreshed = False
        try:
            if target.archive_path is not None:
                refreshed = self.provider.reload_archive(target.archive_path)
            else:
                current = self.provider.creation_target(self._current_path)
                refreshed = current.directory == target.directory
        except (OSError, ValueError, RuntimeError):
            refreshed = False

        if refreshed:
            self._clear_selection()
            self._refresh()
            self._select_created_entry(name)
        self._set_status(f"Created {name}")

    @Slot(object)
    def _entry_creation_failed(self, payload: object) -> None:
        label, message = payload
        self._creation_busy = False
        self.creationStateChanged.emit()
        self._set_status(f"Could not create {label or 'entry'}: {message}")

    def _select_created_entry(self, name: str) -> None:
        for row in range(self.entriesModel.rowCount()):
            entry = self.entriesModel.entry_at(row)
            if entry is not None and entry.name.casefold() == name.casefold():
                self._set_selection({row}, row, row, force=True)
                return

    @Slot(str)
    def openArchive(self, path: str) -> None:
        try:
            self.provider.open_archive(path)
        except (OSError, ValueError, RuntimeError) as error:
            self._set_status(f"Could not open archive: {error}")
            return
        self._clear_archive_expansion()
        self._reset_navigation()
        self._expand_current_branch()
        self._publish_workspace()
        self._set_status(f"Opened {self.archiveName}  ·  FiveFury")

    @Slot()
    def showGame(self) -> None:
        try:
            self.provider.show_game()
        except ValueError as error:
            self._set_status(str(error))
            return
        self._reset_navigation()
        self._expand_current_branch()
        self._publish_workspace()
        self._set_status(f"Browsing {self.provider.game_name}")

    @Slot()
    def showArchive(self) -> None:
        try:
            self.provider.show_archive()
        except ValueError as error:
            self._set_status(str(error))
            return
        self._reset_navigation()
        self._expand_current_branch()
        self._publish_workspace()
        self._set_status(f"Browsing {self.provider.archive_name}")

    @Slot()
    def closeArchive(self) -> None:
        self.provider.close_archive()
        self._clear_archive_expansion()
        if self.provider.has_game:
            self._reset_navigation()
            self._publish_workspace()
            self._set_status(f"Browsing {self.provider.game_name}")
        else:
            self.closeWorkspace()

    @Slot()
    def closeWorkspace(self) -> None:
        self.provider.close()
        self.entriesModel.set_entries([])
        self.treeModel.set_rows([])
        self._history = []
        self._history_index = -1
        self._search = ""
        self._current_path = "."
        self._expanded_nodes.clear()
        self._update_tree_focus("", -1)
        self.workspaceChanged.emit()
        self.currentPathChanged.emit()
        self.pathPartsChanged.emit()
        self._clear_selection()
        self._total_count = 0
        self._visible_count = 0
        self.countsChanged.emit()
        self._set_status("No game loaded  ·  Configure game paths in Settings")

    @Slot(str)
    def navigate(self, path: str) -> None:
        normalized = path or "."
        if normalized == self._current_path:
            return
        self._history = self._history[: self._history_index + 1]
        self._history.append(normalized)
        self._history_index += 1
        self._current_path = normalized
        self._expand_current_branch()
        self._publish_navigation()

    @Slot()
    def goBack(self) -> None:
        if self._history_index > 0:
            self._history_index -= 1
            self._current_path = self._history[self._history_index]
            self._expand_current_branch()
            self._publish_navigation()

    @Slot()
    def goForward(self) -> None:
        if self._history_index + 1 < len(self._history):
            self._history_index += 1
            self._current_path = self._history[self._history_index]
            self._expand_current_branch()
            self._publish_navigation()

    @Slot()
    def goUp(self) -> None:
        if self.canGoUp:
            parent = str(Path(self._current_path).parent).replace("\\", "/")
            self.navigate("." if parent == "." else parent)

    @Slot(str)
    def setSearch(self, value: str) -> None:
        if value == self._search:
            return
        self._search = value
        self.searchChanged.emit()
        self._clear_selection()
        self._refresh()

    @Slot(str)
    def sortEntries(self, column: str) -> None:
        normalized = column.casefold()
        if normalized not in {"name", "type", "size"}:
            return

        selected_paths = {entry.path for entry in self._selected_entries()}
        current = self.entriesModel.entry_at(self._selected_index)
        anchor = self.entriesModel.entry_at(self._selection_anchor)
        current_path = current.path if current is not None else ""
        anchor_path = anchor.path if anchor is not None else ""

        if normalized == self._sort_column:
            self._sort_ascending = not self._sort_ascending
        else:
            self._sort_column = normalized
            self._sort_ascending = True
        self.sortChanged.emit()
        self._refresh()

        rows_by_path = {
            entry.path: row
            for row in range(self.entriesModel.rowCount())
            if (entry := self.entriesModel.entry_at(row)) is not None
        }
        selected_rows = {
            rows_by_path[path]
            for path in selected_paths
            if path in rows_by_path
        }
        if selected_rows:
            self._set_selection(
                selected_rows,
                rows_by_path.get(current_path, min(selected_rows)),
                rows_by_path.get(anchor_path, min(selected_rows)),
                force=True,
            )

    @Slot()
    def refresh(self) -> None:
        if self.provider.is_open:
            self._clear_selection()
            self._refresh()
            self._set_status(f"Refreshed {self.archiveName}")

    @Slot(bool)
    def setFoldersVisible(self, visible: bool) -> None:
        if visible == self._folders_visible:
            return
        self._folders_visible = visible
        self.uiStateChanged.emit()

    @Slot(str)
    def setViewMode(self, mode: str) -> None:
        normalized = mode.casefold()
        if normalized not in {"list", "grid"} or normalized == self._view_mode:
            return
        self._view_mode = normalized
        self.viewModeChanged.emit()

    @Slot()
    def requestSearchFocus(self) -> None:
        self.searchFocusRequested.emit()

    @Slot(str)
    def focusTreePath(self, path: str) -> None:
        index = self.treeModel.row_for_path(path)
        if index >= 0:
            self._update_tree_focus(path, index)

    @Slot(int)
    def focusTreeRow(self, row: int) -> None:
        item = self.treeModel.row_at(row)
        if item is not None:
            self._update_tree_focus(item.path, row)

    @Slot(str)
    def navigateTree(self, path: str) -> None:
        if self._creation_busy:
            return
        if path.startswith("game://"):
            target = path.removeprefix("game://")
            if self.provider.in_archive:
                self.provider.show_game()
                self._reset_navigation()
                self._publish_workspace()
            self.navigate(target)
        elif path == "archive://.":
            self._show_root_archive(path)
        elif path.startswith("archive://"):
            target = self._archive_key_path(path, "archive")
            entry = self.provider.archive_entry_from_root(target)
            if entry is None or not entry.is_directory:
                self._set_status(f"Could not find archive folder: {target}")
                return
            try:
                self.provider.activate_entry_archive(entry)
            except ValueError as error:
                self._set_status(str(error))
                return
            local_path = self.provider.entry_local_path(entry)
            self._reset_navigation()
            self._history.append(local_path)
            self._history_index = 1
            self._current_path = local_path
            self._expand_current_branch()
            self._update_tree_focus(path, self.treeModel.row_for_path(path))
            self._publish_workspace()
        elif path.startswith("rpf://"):
            relative = path.removeprefix("rpf://")
            if self.provider.archive_game_path == relative:
                self._show_root_archive(path)
            else:
                self.openArchive(str(Path(self.provider.game_path) / relative))
        elif path.startswith("nested://"):
            target = self._archive_key_path(path, "nested")
            entry = self.provider.archive_entry_from_root(target)
            if entry is None:
                self._set_status(f"Could not find nested archive: {target}")
                return
            self._open_archive_entry(entry)

    def _show_root_archive(self, focus_path: str) -> None:
        try:
            self.provider.show_root_archive()
        except ValueError as error:
            self._set_status(str(error))
            return
        self._reset_navigation()
        self._expand_current_branch()
        self._update_tree_focus(focus_path, -1)
        self._publish_workspace()
        self._set_status(f"Browsing {self.provider.archive_name}")

    @Slot(int)
    def activateTreeRow(self, row: int) -> None:
        item = self.treeModel.row_at(row)
        if item is None:
            return
        self.navigateTree(item.path)
        if item.kind != "archive" and item.has_children:
            self.toggleTreeNode(item.path)

    @Slot(str)
    def toggleTreeNode(self, path: str) -> None:
        if self._creation_busy:
            return
        if path in self._expanded_nodes:
            self._expanded_nodes.remove(path)
            self.focusTreePath(path)
            self._refresh_tree()
            return
        if path.startswith("rpf://"):
            relative = path.removeprefix("rpf://")
            if self.provider.archive_game_path != relative:
                self.openArchive(str(Path(self.provider.game_path) / relative))
                return
        if path.startswith("nested://"):
            target = self._archive_key_path(path, "nested")
            entry = self.provider.archive_entry_from_root(target)
            if entry is None:
                self._set_status(f"Could not find nested archive: {target}")
                return
            self._open_archive_entry(entry)
            return
        self._expanded_nodes.add(path)
        self.focusTreePath(path)
        self._refresh_tree()

    @Slot(int)
    @Slot(int, int)
    def selectEntry(self, row: int, modifiers: int = 0) -> None:
        entry = self.entriesModel.entry_at(row)
        if entry is None:
            self._clear_selection()
            return

        control = bool(modifiers & Qt.KeyboardModifier.ControlModifier.value)
        shift = bool(modifiers & Qt.KeyboardModifier.ShiftModifier.value)
        if shift:
            anchor = self._selection_anchor if self._selection_anchor >= 0 else row
            interval = set(range(min(anchor, row), max(anchor, row) + 1))
            selected = self._selected_rows | interval if control else interval
            self._set_selection(selected, row, anchor)
            return
        if control:
            selected = set(self._selected_rows)
            if row in selected:
                selected.remove(row)
            else:
                selected.add(row)
            current = row if selected else -1
            self._set_selection(selected, current, row)
            return
        self._set_selection({row}, row, row)

    @Slot()
    def selectAllEntries(self) -> None:
        if self.entriesModel.rowCount() == 0:
            return
        current = max(self._selected_index, 0)
        self._set_selection(set(range(self.entriesModel.rowCount())), current, 0)

    @Slot()
    def clearEntrySelection(self) -> None:
        self._clear_selection()

    @Slot(int)
    def beginMarqueeSelection(self, modifiers: int) -> None:
        self._marquee_base_selection = set(self._selected_rows)
        self._marquee_modifiers = modifiers

    @Slot(int, int)
    def updateMarqueeSelection(self, first: int, last: int) -> None:
        interval = (
            set(range(min(first, last), max(first, last) + 1))
            if first >= 0 and last >= 0
            else set()
        )
        self._apply_marquee_selection(interval, last, first)

    @Slot("QVariantList")
    def updateMarqueeRows(self, rows: list[Any]) -> None:
        selected = {
            int(row)
            for row in rows
            if isinstance(row, (int, float))
            and 0 <= int(row) < self.entriesModel.rowCount()
        }
        self._apply_marquee_selection(
            selected,
            max(selected) if selected else -1,
            min(selected) if selected else -1,
        )

    def _apply_marquee_selection(
        self,
        interval: set[int],
        current_hint: int,
        anchor_hint: int,
    ) -> None:
        control = bool(
            self._marquee_modifiers & Qt.KeyboardModifier.ControlModifier.value
        )
        shift = bool(self._marquee_modifiers & Qt.KeyboardModifier.ShiftModifier.value)
        if control:
            selected = self._marquee_base_selection.symmetric_difference(interval)
        elif shift:
            selected = self._marquee_base_selection | interval
        else:
            selected = interval
        current = (
            current_hint
            if current_hint in selected
            else (max(selected) if selected else -1)
        )
        anchor = (
            anchor_hint
            if anchor_hint in interval
            else (min(interval) if interval else -1)
        )
        self._set_selection(selected, current, anchor)

    @Slot()
    def endMarqueeSelection(self) -> None:
        self._marquee_base_selection.clear()
        self._marquee_modifiers = 0

    def _set_selection(
        self,
        rows: set[int],
        current: int,
        anchor: int,
        *,
        force: bool = False,
    ) -> None:
        selected = {
            row
            for row in rows
            if self.entriesModel.entry_at(row) is not None
        }
        resolved_current = current if self.entriesModel.entry_at(current) is not None else -1
        if not force and (
            selected == self._selected_rows
            and resolved_current == self._selected_index
            and anchor == self._selection_anchor
        ):
            return
        self._selected_rows = selected
        self.entriesModel.set_selected_rows(selected)
        self._selected_index = resolved_current
        self._selection_anchor = anchor
        entries = self._selected_entries()
        if len(entries) == 1:
            entry = entries[0]
            self._selected_name = entry.name
            self._selected_path = entry.path
        elif entries:
            self._selected_name = f"{len(entries)} items"
            self._selected_path = ""
        else:
            self._selected_name = ""
            self._selected_path = ""
        self._selected_size = format_size(sum(entry.size for entry in entries)) if entries else ""
        self.selectionChanged.emit()

    def _selected_entries(self) -> list[EntryRecord]:
        return [
            entry
            for row in sorted(self._selected_rows)
            if (entry := self.entriesModel.entry_at(row)) is not None
        ]

    def _clear_selection(self) -> None:
        self._marquee_base_selection.clear()
        self._marquee_modifiers = 0
        self._selected_rows.clear()
        self.entriesModel.set_selected_rows(set())
        self._selected_index = -1
        self._selection_anchor = -1
        self._selected_name = ""
        self._selected_size = ""
        self._selected_path = ""
        self.selectionChanged.emit()

    @Slot()
    def copySelectedName(self) -> None:
        entries = self._selected_entries()
        if entries:
            QGuiApplication.clipboard().setText("\n".join(entry.name for entry in entries))
            noun = "name" if len(entries) == 1 else f"{len(entries)} names"
            self._set_status(f"Copied {noun}")

    @Slot()
    def copySelectedPath(self) -> None:
        entries = self._selected_entries()
        if not entries:
            return
        if self.provider.in_archive:
            values = [
                f"{self.provider.info.source_path}::{entry.path}"
                for entry in entries
            ]
        else:
            values = [str(Path(self.provider.game_path) / entry.path) for entry in entries]
        QGuiApplication.clipboard().setText("\n".join(values))
        noun = "path" if len(values) == 1 else f"{len(values)} paths"
        self._set_status(f"Copied {noun}")

    @Slot(int)
    def startEntryDrag(self, row: int) -> None:
        if self._creation_busy:
            return
        if row not in self._selected_rows:
            self.selectEntry(row)
        entries = self._selected_entries()
        if not entries:
            return
        try:
            if self._drag_staging is not None:
                self._drag_staging.cleanup()
                self._drag_staging = None
            if self.provider.in_archive:
                self._drag_staging = TemporaryDirectory(
                    prefix="HikariHopper-",
                    ignore_cleanup_errors=True,
                )
                paths = self.provider.export_entries(entries, self._drag_staging.name)
            else:
                paths = self.provider.export_entries(entries, "")
        except (OSError, ValueError, RuntimeError) as error:
            self._set_status(f"Could not extract selection: {error}")
            return

        mime_data = QMimeData()
        mime_data.setUrls([QUrl.fromLocalFile(str(path)) for path in paths])
        drag = QDrag(self)
        drag.setMimeData(mime_data)
        self._set_status(
            "Dragging one item as a copy"
            if len(paths) == 1
            else f"Dragging {len(paths)} items as copies"
        )
        drag.exec(Qt.DropAction.CopyAction, Qt.DropAction.CopyAction)

    @Slot(int)
    def activateEntry(self, row: int) -> None:
        if self._creation_busy:
            return
        entry = self.entriesModel.entry_at(row)
        if entry is None:
            return
        self.selectEntry(row)
        if entry.is_directory:
            path = (
                self.provider.entry_local_path(entry)
                if self.provider.in_archive
                else entry.path
            )
            self.navigate(path)
            return
        if entry.name.casefold().endswith(".ytd"):
            self._open_texture_dictionary(entry)
            return
        if not entry.name.casefold().endswith(".rpf"):
            return
        self._open_archive_entry(entry)

    def _open_texture_dictionary(self, entry: EntryRecord) -> None:
        try:
            data = self.provider.read_entry_bytes(entry)
            source_path = self.provider.entry_source_path(entry)
            source_target = (
                self.provider.archive_entry_target(entry)
                if self.provider.in_archive
                else None
            )
        except (OSError, ValueError, RuntimeError) as error:
            self._set_status(f"Could not open texture dictionary: {error}")
            return
        self._texture_viewer.open_dictionary(
            entry.name,
            source_path,
            data,
            source_saver=source_target.save if source_target is not None else None,
            source_prepare=(
                source_target.prepare if source_target is not None else None
            ),
            source_root_path=(
                str(source_target.root_path) if source_target is not None else ""
            ),
        )
        self._set_status(f"Opening {entry.name}  ·  FiveFury")

    @Slot(str)
    def _texture_source_saved(self, root_path: str) -> None:
        try:
            reloaded = self.provider.reload_archive(root_path)
        except (OSError, ValueError, RuntimeError) as error:
            self._set_status(f"Saved YTD, but could not refresh the RPF: {error}")
            return
        if not reloaded:
            return
        self._clear_selection()
        self._refresh()
        self._set_status("Saved YTD inside RPF  ·  FiveFury")

    def _open_archive_entry(self, entry: EntryRecord) -> None:
        is_nested = getattr(entry.native_entry, "_archive", None) is not None
        focus_path = self._nested_archive_key(entry.path) if is_nested else ""
        try:
            if is_nested:
                self.provider.open_nested(entry)
                status = "Opened nested archive  ·  FiveFury"
            else:
                self.provider.open_archive(entry.native_entry)
                status = f"Opened {entry.name}  ·  FiveFury"
        except (ValueError, RuntimeError, OSError) as error:
            self._set_status(f"Could not open archive: {error}")
            return
        if is_nested:
            self._expanded_nodes.add(focus_path)
        else:
            self._clear_archive_expansion()
        self._reset_navigation()
        self._expand_current_branch()
        self._update_tree_focus(focus_path, -1)
        self._publish_workspace()
        self._set_status(status)
